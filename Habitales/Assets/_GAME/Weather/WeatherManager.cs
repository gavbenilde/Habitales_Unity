using System;
using System.Collections.Generic;
using UnityEngine;

public enum WeatherState { Sunny, Cloudy, Rainy, Stormy }

/// <summary>
/// One weather type's tunable effects. This is the flexibility seam: add a field HERE and
/// EVERY weather automatically gains the lever (S2 — one concept, one place). Designers tune
/// these per-state in the Inspector; no code change needed to rebalance.
/// </summary>
[Serializable]
public class WeatherProfile
{
    public WeatherState state;

    [Header("Simulation effects (read by other systems via Law-1 getters)")]
    [Tooltip("Applied to action length: days = ceil(baseDays × this). Lower = fewer days.")]
    public float workSpeedMultiplier = 1f;

    [Tooltip("Fatigue severity exponent: severity = random^k. HIGHER k = LESS fatigue " +
             "(harsh weather uses a LOWER k → more fatigue). Read by ResourceManager.")]
    public float fatigueK = 3f;

    [Tooltip("Multiplier on fire spread chance. Read by the Fire behaviour hook via TickContext.")]
    public float fireSpreadMultiplier = 1f;

    [Tooltip("Extra per-day vegetation damage on burning tiles.")]
    public float fireBonusDamage = 0f;

    // ── Add new levers below as the design grows (visibilityRange, cropGrowthMultiplier,
    //    pestChance, soilEvaporation…). Add the field here, expose a getter on WeatherManager,
    //    and the consumer reads it via Law 1. That is the whole extension story. ──
}

/// <summary>
/// Owns the weather STATE and its SIMULATION effects (data-driven via WeatherProfile).
/// Does NOT own visuals — the cosmetic layer (rain, godrays, thunder) is WeatherVFXController,
/// which reacts to OnWeatherChanged (arch S3: cosmetics live in their own component).
///
/// Calendar-driven daily weighted roll (see GameCalendar): a 21-day ACTUAL weather window is
/// pre-generated and advanced one day at a time as ResourceManager advances totalDays. The
/// weather-APP FORECAST is a separate, deliberately-lying view over the same window: near-term
/// days (0-2) are always truthful, and accuracy decays the further out you look, converging on
/// the truth as "today" approaches (see BuildForecast).
/// </summary>
[DefaultExecutionOrder(-200)] // core service — initializes before consumers (arch §4 init order)
public class WeatherManager : MonoBehaviour
{
    public static WeatherManager Instance { get; private set; }

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;

    [Header("Weather Tuning (data-driven — one profile per state)")]
    [Tooltip("Author one WeatherProfile per WeatherState. Any state you leave out falls back to " +
             "the coded default (and warns), so the sim never reads a null profile.")]
    [SerializeField] private List<WeatherProfile> profiles = new List<WeatherProfile>();

    [Header("Seasonal Weighting")]
    [Tooltip("Days on EACH side of a season boundary (Dec 1 / Jun 1) over which the two seasons' " +
             "weight tables crossfade, so weather shifts gradually instead of snapping. The crossfade " +
             "midpoint naturally passes near the designer's 'normal' blended weights.")]
    [SerializeField] private int transitionDays = 15;

    [Tooltip("Chance that a rolled day simply repeats the previous day's weather instead of drawing " +
             "from the seasonal table — creates multi-day fronts (sunny spells, rain streaks) so the " +
             "streak mechanics actually happen. 0 = fully independent daily rolls.")]
    [Range(0f, 0.9f)]
    [SerializeField] private float persistenceBias = 0.25f;

    [Header("Streaks")]
    [Tooltip("Dry/wet spell level required before Drought/Deluge is considered active.")]
    [SerializeField] private int streakThreshold = 3;

    [Tooltip("How much a Cloudy day relieves the dry spell. Cloudy is 'safe' weather — no sun, but " +
             "no water either, so it only winds a drought down slowly (never raises either spell).")]
    [SerializeField] private int cloudyDrySpellRelief = 1;

    [Tooltip("How much a Rainy/Stormy day relieves the dry spell. Deliberately less than a full reset — " +
             "one shower does not erase a long drought.")]
    [SerializeField] private int rainDrySpellRelief = 3;

    [Header("Forecast (the weather app deliberately lies)")]
    [Tooltip("Per-offset accuracy decay base: accuracy(k) = max(accuracyFloor, forecastDecay^k). " +
             "0.94^20 ≈ 0.29, i.e. ~30% accurate 21 days out.")]
    [SerializeField] private float forecastDecay = 0.94f;
    [Tooltip("Forecast accuracy never drops below this floor, however far out you look.")]
    [SerializeField] private float accuracyFloor = 0.3f;

    public const int ForecastLength = 21;

    public WeatherState CurrentWeather { get; private set; } = WeatherState.Cloudy;

    public event Action<WeatherState> OnWeatherChanged;

    /// <summary>Fired once per RollWeather call, after the actual window and forecast have both advanced.</summary>
    public event Action OnForecastUpdated;

    /// <summary>Fired when the calendar season flips (Dry ↔ Wet), after the day's weather has resolved.</summary>
    public event Action<Season> OnSeasonChanged;

    /// <summary>Fired the day a drought becomes active (dry spell reaches threshold on a Sunny day).</summary>
    public event Action OnDroughtStarted;
    /// <summary>Fired the day an active drought stops being active (rain/cloud relief or spell decay).</summary>
    public event Action OnDroughtEnded;
    /// <summary>Fired the day a deluge becomes active (rain streak reaches threshold).</summary>
    public event Action OnDelugeStarted;
    /// <summary>Fired the day an active deluge stops being active (the rain broke).</summary>
    public event Action OnDelugeEnded;

    /// <summary>Dry-spell pressure, in days. Only a Sunny day raises it (+1). Cloudy is safe weather:
    /// it never raises either spell and relieves this one slowly (−cloudyDrySpellRelief). A rain day
    /// knocks it down by rainDrySpellRelief without erasing it — long droughts outlast one shower.</summary>
    public int DrySpellDays { get; private set; }

    /// <summary>Consecutive days (including today) that were Rainy or Stormy. Resets to 0 on any
    /// non-rain day — rain damage is acute; the moment it stops, the danger passes.</summary>
    public int WetSpellDays { get; private set; }

    /// <summary>Days a dry/wet streak must run before Drought/Deluge activates. Read by TileManager's
    /// severity ramp so the stress math stays in lockstep with this threshold (Law 1).</summary>
    public int StreakThreshold => streakThreshold;

    /// <summary>True when it's currently Sunny and the dry spell has built up enough to count as a drought.
    /// A Cloudy day mid-spell pauses the drought (safe weather) without unwinding it much.</summary>
    public bool IsDroughtActive => CurrentWeather == WeatherState.Sunny && DrySpellDays >= streakThreshold;

    /// <summary>True when it's currently Rainy/Stormy and the rain streak has run long enough to count as a deluge.</summary>
    public bool IsDelugeActive =>
        (CurrentWeather == WeatherState.Rainy || CurrentWeather == WeatherState.Stormy) &&
        WetSpellDays >= streakThreshold;

    // Designer weight order [Cloudy, Sunny, Rainy, Stormy] mapped to enum order
    // (Sunny, Cloudy, Rainy, Stormy) below. Dry season: [50, 30, 15, 5]. Wet season: [15, 25, 50, 10].
    private static readonly float[] DrySeasonWeights = { 50f, 30f, 15f, 5f };
    private static readonly float[] WetSeasonWeights = { 15f, 25f, 50f, 10f };

    private Dictionary<WeatherState, WeatherProfile> _byState;

    // ── 21-day actual weather window. Index 0 = today. ──────────────────────────
    private List<WeatherState> _actualWindow = new List<WeatherState>();
    // Absolute totalDay each _actualWindow entry corresponds to (parallel list; [0] = today's totalDay).
    private List<int> _windowTotalDays = new List<int>();

    private int _lastProcessedDay;
    private int _runSeed;
    private bool _initialized;

    // Previous-day snapshots for the state-change events (season / drought / deluge edges).
    private Season _lastSeason;
    private bool _wasDroughtActive;
    private bool _wasDelugeActive;

    /// <summary>21-entry forecast as the weather app displays it (lies beyond offset 2). [0] = today.</summary>
    public IReadOnlyList<WeatherState> Forecast => _forecast;
    private List<WeatherState> _forecast = new List<WeatherState>();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildProfileTable();
    }

    void Start()
    {
        // All singleton Awakes have run by Start() — ResourceManager's StartDayOfYear/TotalDays
        // are ready here (arch §4 init order; ResourceManager is also DefaultExecutionOrder -200,
        // but Awake-vs-Awake ordering between two -200 scripts isn't guaranteed, Start() is safe).
        InitializeWindow();
    }

    private void BuildProfileTable()
    {
        _byState = new Dictionary<WeatherState, WeatherProfile>();
        foreach (var p in profiles)
            if (p != null) _byState[p.state] = p;

        // Seed any unauthored state with the coded default (Law 3 — warn, never silently break).
        foreach (WeatherState s in Enum.GetValues(typeof(WeatherState)))
            if (!_byState.ContainsKey(s))
            {
                _byState[s] = DefaultProfile(s);
                if (showDebugInfo)
                    Debug.LogWarning($"WeatherManager: no profile authored for {s} — using coded default. " +
                                     "Add one in the Inspector to tune it.", this);
            }
    }

    // Coded defaults preserve the exact pre-refactor hardcoded values.
    private static WeatherProfile DefaultProfile(WeatherState s) => s switch
    {
        WeatherState.Sunny  => new WeatherProfile { state = s, workSpeedMultiplier = 1.00f, fatigueK = 1.5f, fireSpreadMultiplier = 1.5f, fireBonusDamage = 4f },
        WeatherState.Cloudy => new WeatherProfile { state = s, workSpeedMultiplier = 1.00f, fatigueK = 3.0f, fireSpreadMultiplier = 1.0f, fireBonusDamage = 0f },
        WeatherState.Rainy  => new WeatherProfile { state = s, workSpeedMultiplier = 1.30f, fatigueK = 3.0f, fireSpreadMultiplier = 0.5f, fireBonusDamage = 0f },
        WeatherState.Stormy => new WeatherProfile { state = s, workSpeedMultiplier = 2.00f, fatigueK = 1.5f, fireSpreadMultiplier = 0.5f, fireBonusDamage = 0f },
        _                   => new WeatherProfile { state = s },
    };

    /// <summary>The active weather's full profile. Falls back to a coded default if unbuilt/missing.</summary>
    public WeatherProfile Current =>
        (_byState != null && _byState.TryGetValue(CurrentWeather, out var p)) ? p : DefaultProfile(CurrentWeather);

    // ── Window init ──────────────────────────────────────────────────────────

    private void InitializeWindow()
    {
        if (_initialized) return;

        var rm = ResourceManager.Instance;
        int startTotalDay;
        int startDayOfYear;

        if (rm == null)
        {
            Debug.LogWarning("WeatherManager: ResourceManager.Instance is null at Start() — " +
                              "falling back to StartDayOfYear=0. Check singleton init order.", this);
            startTotalDay = 0;
            startDayOfYear = 0;
        }
        else
        {
            startTotalDay = rm.TotalDays;
            startDayOfYear = rm.DayOfYearFor(startTotalDay);
        }

        _runSeed = new System.Random().Next();
        _lastProcessedDay = startTotalDay;

        _actualWindow.Clear();
        _windowTotalDays.Clear();
        for (int k = 0; k < ForecastLength; k++)
        {
            int totalDay = startTotalDay + k;
            int dayOfYear = rm != null ? rm.DayOfYearFor(totalDay) : Normalize(startDayOfYear + k);
            var weights = GetWeightsForDayOfYear(dayOfYear);
            WeatherState? previous = k > 0 ? _actualWindow[k - 1] : (WeatherState?)null;
            _actualWindow.Add(RollWithPersistence(weights, previous));
            _windowTotalDays.Add(totalDay);
        }

        CurrentWeather = _actualWindow[0];
        RecomputeStreaksFromScratch();
        BuildForecast();

        // Seed the edge-event snapshots silently — no Started/Changed events on run start.
        _lastSeason = rm != null ? rm.CurrentSeason : GameCalendar.GetSeason(startDayOfYear);
        _wasDroughtActive = IsDroughtActive;
        _wasDelugeActive = IsDelugeActive;

        _initialized = true;

        if (showDebugInfo)
            Debug.Log($"WeatherManager: window initialized → {CurrentWeather} " +
                      $"({(rm != null ? rm.CurrentSeason : GameCalendar.GetSeason(startDayOfYear))} Season, " +
                      $"{(rm != null ? rm.GetCalendarDisplay() : GameCalendar.GetShortDate(startDayOfYear))})");
    }

    private static int Normalize(int dayOfYear)
    {
        int d = dayOfYear % GameCalendar.DaysPerYear;
        if (d < 0) d += GameCalendar.DaysPerYear;
        return d;
    }

    // ── Called by ResourceManager.AdvanceTime / AdvanceOneDay ──────────────────
    public void RollWeather(int currentTotalDay)
    {
        if (!_initialized)
            InitializeWindow();

        int steps = currentTotalDay - _lastProcessedDay;
        if (steps <= 0) return; // nothing to advance (or we're already ahead, e.g. re-entrant call)

        WeatherState previous = CurrentWeather;

        for (int i = 0; i < steps; i++)
        {
            AdvanceWindowOneDay();
        }

        _lastProcessedDay = currentTotalDay;

        if (CurrentWeather != previous)
        {
            OnWeatherChanged?.Invoke(CurrentWeather);   // sim consumers read getters; VFX layer reacts here

            if (showDebugInfo)
            {
                var rm = ResourceManager.Instance;
                string dateStr = rm != null ? rm.GetCalendarDisplay() : GameCalendar.GetShortDate(Normalize(currentTotalDay));
                Debug.Log($"Weather changed → {CurrentWeather} ({CurrentSeasonLabel()} Season, {dateStr})");
            }
        }

        BuildForecast();
        OnForecastUpdated?.Invoke();
        FireEdgeEvents();
    }

    // Edge-triggered meaning events (Law 2): season flips and drought/deluge start/end fire once,
    // on the day the state changes — after the day's weather, streaks, and forecast have resolved.
    private void FireEdgeEvents()
    {
        Season season = ResourceManager.Instance != null
            ? ResourceManager.Instance.CurrentSeason
            : GameCalendar.GetSeason(Normalize(_lastProcessedDay));
        if (season != _lastSeason)
        {
            _lastSeason = season;
            OnSeasonChanged?.Invoke(season);
            if (showDebugInfo) Debug.Log($"Season changed → {season}");
        }

        bool drought = IsDroughtActive;
        if (drought != _wasDroughtActive)
        {
            _wasDroughtActive = drought;
            if (drought) OnDroughtStarted?.Invoke(); else OnDroughtEnded?.Invoke();
            if (showDebugInfo) Debug.Log(drought ? $"Drought STARTED (dry spell {DrySpellDays}d)" : "Drought ended.");
        }

        bool deluge = IsDelugeActive;
        if (deluge != _wasDelugeActive)
        {
            _wasDelugeActive = deluge;
            if (deluge) OnDelugeStarted?.Invoke(); else OnDelugeEnded?.Invoke();
            if (showDebugInfo) Debug.Log(deluge ? $"Deluge STARTED (rain streak {WetSpellDays}d)" : "Deluge ended.");
        }
    }

    private string CurrentSeasonLabel()
    {
        var rm = ResourceManager.Instance;
        return (rm != null ? rm.CurrentSeason : GameCalendar.GetSeason(Normalize(_lastProcessedDay))).ToString();
    }

    // Pops the front (today) of the actual window, appends one freshly-rolled day at the back.
    private void AdvanceWindowOneDay()
    {
        if (_actualWindow.Count == 0)
        {
            // Defensive — should never happen once initialized, but never let a bad state throw.
            // Must drop the flag first: InitializeWindow() no-ops while _initialized is true.
            _initialized = false;
            InitializeWindow();
            return;
        }

        _actualWindow.RemoveAt(0);
        _windowTotalDays.RemoveAt(0);

        int nextTotalDay = _windowTotalDays.Count > 0
            ? _windowTotalDays[_windowTotalDays.Count - 1] + 1
            : _lastProcessedDay + 1;

        var rm = ResourceManager.Instance;
        int nextDayOfYear = rm != null ? rm.DayOfYearFor(nextTotalDay) : Normalize(nextTotalDay);
        var weights = GetWeightsForDayOfYear(nextDayOfYear);

        WeatherState? previous = _actualWindow.Count > 0
            ? _actualWindow[_actualWindow.Count - 1]
            : (WeatherState?)null;
        _actualWindow.Add(RollWithPersistence(weights, previous));
        _windowTotalDays.Add(nextTotalDay);

        CurrentWeather = _actualWindow[0];
        UpdateStreaksForNewToday(CurrentWeather);
    }

    // Asymmetric by design: drought is CHRONIC pressure (builds on Sunny, winds down slowly, only
    // rain relieves it meaningfully — and even then by rainDrySpellRelief, not a reset), while a
    // deluge is ACUTE (strictly consecutive rain; any dry day breaks it completely).
    private void UpdateStreaksForNewToday(WeatherState today)
    {
        switch (today)
        {
            case WeatherState.Sunny:
                DrySpellDays++;
                WetSpellDays = 0;
                break;

            case WeatherState.Cloudy: // safe weather — breaks a wet spell, gently relieves a dry one
                DrySpellDays = Mathf.Max(0, DrySpellDays - cloudyDrySpellRelief);
                WetSpellDays = 0;
                break;

            default: // Rainy / Stormy
                WetSpellDays++;
                DrySpellDays = Mathf.Max(0, DrySpellDays - rainDrySpellRelief);
                break;
        }
    }

    // Used once at init (and defensively on recovery) to seed spells from the window's first day
    // rather than assuming a fresh run — cheap since the window is only 21 days.
    private void RecomputeStreaksFromScratch()
    {
        DrySpellDays = 0;
        WetSpellDays = 0;
        if (_actualWindow.Count == 0) return;

        UpdateStreaksForNewToday(_actualWindow[0]);
    }

    // ── Forecast (the app's lying view) ─────────────────────────────────────────

    private void BuildForecast()
    {
        _forecast.Clear();
        for (int k = 0; k < ForecastLength; k++)
        {
            if (k < _actualWindow.Count)
                _forecast.Add(ForecastForOffset(k));
            else
                _forecast.Add(WeatherState.Cloudy); // guard — window should always be full length
        }
    }

    // Per absolute day D, a FIXED deterministic roll decides whether the forecast for D at
    // offset k shows the truth or a plausible lie. accuracy(k) rises as k shrinks (day approaches),
    // so each day's forecast "corrects" itself toward the truth over time — the desired feel.
    private WeatherState ForecastForOffset(int offsetK)
    {
        int absoluteDay = _windowTotalDays[offsetK];
        WeatherState truth = _actualWindow[offsetK];

        float accuracy = GetForecastAccuracy(offsetK);
        float truthRoll = Hash01(_runSeed, absoluteDay, 0);

        if (truthRoll < accuracy)
            return truth;

        // Lie: sample from D's OWN blended weight table (deterministic, so the lie is stable
        // across rebuilds — it may coincidentally equal the truth, which is fine).
        var rm = ResourceManager.Instance;
        int dayOfYear = rm != null ? rm.DayOfYearFor(absoluteDay) : Normalize(absoluteDay);
        var weights = GetWeightsForDayOfYear(dayOfYear);
        float lieRoll = Hash01(_runSeed, absoluteDay, 1);
        return WeightedRollDeterministic(weights, lieRoll);
    }

    /// <summary>Forecast accuracy at a given look-ahead offset: 100% truthful for 0-2 days out, then decays.</summary>
    public float GetForecastAccuracy(int daysAhead)
    {
        if (daysAhead <= 2) return 1f;
        return Mathf.Max(accuracyFloor, Mathf.Pow(forecastDecay, daysAhead));
    }

    /// <summary>Truth within the 21-day actual window for an absolute totalDay. Clamped + warned outside it.</summary>
    public WeatherState GetActualWeatherForDay(int totalDay)
    {
        if (_windowTotalDays.Count == 0)
        {
            Debug.LogWarning("WeatherManager: GetActualWeatherForDay called before the window was initialized.", this);
            return CurrentWeather;
        }

        int first = _windowTotalDays[0];
        int last = _windowTotalDays[_windowTotalDays.Count - 1];

        if (totalDay < first || totalDay > last)
        {
            Debug.LogWarning($"WeatherManager: GetActualWeatherForDay({totalDay}) is outside the " +
                              $"{first}-{last} actual window — clamping. Only {ForecastLength} days ahead are pre-generated.", this);
            totalDay = Mathf.Clamp(totalDay, first, last);
        }

        return _actualWindow[totalDay - first];
    }

    // ── Weighting (calendar-driven, crossfaded across season boundaries) ───────

    /// <summary>Blended seasonal weights (enum order: Sunny, Cloudy, Rainy, Stormy) for a day-of-year,
    /// crossfading Dry/Wet tables over transitionDays on each side of a season boundary.</summary>
    public float[] GetWeightsForDayOfYear(int dayOfYear)
    {
        int d = Normalize(dayOfYear);
        Season season = GameCalendar.GetSeason(d);

        int intoSeason = GameCalendar.DaysIntoSeason(d);
        int untilEnd = GameCalendar.DaysUntilSeasonEnd(d);

        float[] own = season == Season.Dry ? DrySeasonWeights : WetSeasonWeights;
        float[] other = season == Season.Dry ? WetSeasonWeights : DrySeasonWeights;

        int span = Mathf.Max(1, transitionDays);
        float blendTowardOther = 0f;

        // The crossfade meets at 50/50 exactly on the boundary: each side ramps its blend from
        // 0 (span days away) up to ~0.5 (at the boundary), so the last day of one season and the
        // first day of the next land on near-identical effective weights — no seasonal snap.
        if (intoSeason < span)
        {
            // Just entered this season — still crossfading in from the previous season's table.
            blendTowardOther = 0.5f * (1f - (intoSeason + 0.5f) / span);
        }
        else if (untilEnd < span)
        {
            // Approaching the next season — crossfading out toward its table.
            blendTowardOther = 0.5f * (1f - (untilEnd + 0.5f) / span);
        }

        blendTowardOther = Mathf.Clamp01(blendTowardOther);

        var result = new float[own.Length];
        for (int i = 0; i < own.Length; i++)
            result[i] = Mathf.Lerp(own[i], other[i], blendTowardOther);

        return result;
    }

    // ── Weighted sampling ────────────────────────────────────────────────────

    private static WeatherState WeightedRollUnity(float[] weights)
    {
        return WeightedRollDeterministic(weights, UnityEngine.Random.value);
    }

    // Mixture roll for the ACTUAL weather: with probability persistenceBias the new day simply
    // repeats the previous one (weather arrives in fronts — sunny spells, multi-day rain), otherwise
    // it's an independent draw from the seasonal table. Forecast lies never use this — they sample
    // the pure seasonal blend, which keeps them plausible without leaking streak information.
    private WeatherState RollWithPersistence(float[] weights, WeatherState? previousDay)
    {
        if (previousDay.HasValue && UnityEngine.Random.value < persistenceBias)
            return previousDay.Value;
        return WeightedRollUnity(weights);
    }

    // roll01 in [0,1) selects a bucket from the weight table — shared by both the UnityEngine.Random
    // actual rolls and the deterministic-hash forecast lies.
    private static WeatherState WeightedRollDeterministic(float[] weights, float roll01)
    {
        float total = 0f;
        foreach (float w in weights) total += w;
        if (total <= 0f) return WeatherState.Cloudy;

        float roll = roll01 * total;
        float cumulative = 0f;
        for (int i = 0; i < weights.Length; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative) return (WeatherState)i;
        }
        return (WeatherState)(weights.Length - 1);
    }

    // Deterministic float in [0,1) from a seed/day/salt combination — stable within a run.
    // Simple integer scramble (xorshift-ish mix), not cryptographic, just needs to be
    // well-distributed and stable given the same inputs.
    private static float Hash01(int seed, int day, int salt)
    {
        unchecked
        {
            uint x = (uint)(seed * 486187739 + day * 1000003 + salt * 2654435761);
            x ^= x >> 16;
            x *= 0x7feb352dU;
            x ^= x >> 15;
            x *= 0x846ca68bU;
            x ^= x >> 16;
            return x / (float)uint.MaxValue;
        }
    }

    // ── Law-1 getters — read the CURRENT profile. A new lever = one new getter here. ──────────

    /// <summary>Work-speed multiplier applied to action days.</summary>
    public float WorkSpeedMultiplier  => Current.workSpeedMultiplier;

    /// <summary>Fatigue severity exponent. Read by ResourceManager (replaces its mirrored weatherK).</summary>
    public float FatigueK             => Current.fatigueK;

    /// <summary>Fire spread chance multiplier — threaded to entities via TickContext.</summary>
    public float FireSpreadMultiplier => Current.fireSpreadMultiplier;

    /// <summary>Extra vegetation damage per day on burning tiles.</summary>
    public float FireBonusDamage      => Current.fireBonusDamage;

    // Legacy method wrappers — kept so existing callers compile unchanged.
    public float GetWorkSpeedMultiplier()   => WorkSpeedMultiplier;
    public float GetFireSpreadMultiplier()  => FireSpreadMultiplier;
    public float GetFireBonusDamage()       => FireBonusDamage;
}
