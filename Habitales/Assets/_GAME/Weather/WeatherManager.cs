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

    public WeatherState CurrentWeather { get; private set; } = WeatherState.Cloudy;

    public event Action<WeatherState> OnWeatherChanged;

    // Dry season: days 1–182   |   Wet season: days 183–365
    // Weights order: Sunny, Cloudy, Rainy, Stormy
    private static readonly float[] DryWeights = { 40f, 35f, 20f,  5f };
    private static readonly float[] WetWeights = { 15f, 30f, 40f, 15f };

    private Dictionary<WeatherState, WeatherProfile> _byState;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildProfileTable();
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
        WeatherState.Rainy  => new WeatherProfile { state = s, workSpeedMultiplier = 0.85f, fatigueK = 3.0f, fireSpreadMultiplier = 0.5f, fireBonusDamage = 0f },
        WeatherState.Stormy => new WeatherProfile { state = s, workSpeedMultiplier = 0.50f, fatigueK = 1.5f, fireSpreadMultiplier = 0.5f, fireBonusDamage = 0f },
        _                   => new WeatherProfile { state = s },
    };

    /// <summary>The active weather's full profile. Falls back to a coded default if unbuilt/missing.</summary>
    public WeatherProfile Current =>
        (_byState != null && _byState.TryGetValue(CurrentWeather, out var p)) ? p : DefaultProfile(CurrentWeather);

    // ── Called by ResourceManager.AdvanceTime once per action ─────────────────
    public void RollWeather(int currentTotalDay)
    {
        int dayOfYear = currentTotalDay % 365;
        bool isDry    = dayOfYear < 182;
        float[] weights = isDry ? DryWeights : WetWeights;

        WeatherState next = WeightedRoll(weights);
        if (next == CurrentWeather) return;

        CurrentWeather = next;
        OnWeatherChanged?.Invoke(CurrentWeather);   // sim consumers read getters; VFX layer reacts here

        if (showDebugInfo)
            Debug.Log($"Weather changed → {CurrentWeather} ({(isDry ? "Dry" : "Wet")} Season, Day {dayOfYear})");
    }

    private WeatherState WeightedRoll(float[] weights)
    {
        float total = 0f;
        foreach (float w in weights) total += w;
        float roll = UnityEngine.Random.Range(0f, total);
        float cumulative = 0f;
        for (int i = 0; i < weights.Length; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative) return (WeatherState)i;
        }
        return WeatherState.Cloudy;
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
