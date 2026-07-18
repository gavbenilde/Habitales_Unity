using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DefaultExecutionOrder(-200)] // core service — initializes before consumers (arch §4 init order)
public class ResourceManager : MonoBehaviour
{
    public static ResourceManager Instance { get; private set; }

    // ── Time ─────────────────────────────────────────────────────────────────
    [Header("Time")]
    [SerializeField] private int totalDays = 0;

    // Canonical run length — the single source of truth for when a run ends.
    // When the player picked a season count in the main menu (RunConfig.SelectedSeasons),
    // Awake() overwrites this with seasons × GameCalendar.DaysPerSeason; otherwise the
    // serialized value is the scene's default (direct scene loads / tests).
    // NOTE: reaching runLengthDays no longer stops the clock — an in-flight action always
    // plays out its remaining days, and RunManager evaluates game-over once no action is
    // running ("the actions dictate the length of the season at the end").
    [SerializeField] private int runLengthDays = 100;

    [Header("Calendar")]
    [Tooltip("If true, the run's starting day-of-year is randomized in Awake() (never a season's " +
             "first day, so weather doesn't open on a hard transition). Turn off for deterministic testing.")]
    [SerializeField] private bool randomizeStartDate = true;
    [Tooltip("Used only when Randomize Start Date is OFF and this is >= 0 — pins the starting " +
             "day-of-year (0 = Jan 1) for repeatable test runs.")]
    [SerializeField] private int startDayOfYearOverride = -1;

    /// <summary>The 0-based day-of-year (Jan 1 = 0) the run started on. Chosen once in Awake().</summary>
    public int StartDayOfYear { get; private set; }

    // Year length is owned by GameCalendar (12 × 30 = 360) — derived here rather than mirrored
    // in a serialized field so the calendar and the year math can never drift apart.
    public int DaysPerYear => GameCalendar.DaysPerYear;
    public int CurrentYear => (totalDays / GameCalendar.DaysPerYear) + 1;

    /// <summary>The current 0-based calendar day-of-year, accounting for the random start date.</summary>
    public int DayOfYear => DayOfYearFor(totalDays);

    /// <summary>The 0-based calendar day-of-year for an arbitrary total-day count (past or future) —
    /// used by the weather forecast / calendar UI to resolve dates ahead of "today".</summary>
    public int DayOfYearFor(int totalDay)
    {
        int d = (StartDayOfYear + totalDay) % GameCalendar.DaysPerYear;
        if (d < 0) d += GameCalendar.DaysPerYear;
        return d;
    }

    /// <summary>The active season for the current calendar day (Law 1 getter — WeatherManager reads this).</summary>
    public Season CurrentSeason => GameCalendar.GetSeason(DayOfYear);

    // Run-length accessors (Law 1: getters, not setters).
    public int RunLengthDays => runLengthDays;
    public int DaysRemaining => Mathf.Max(0, runLengthDays - totalDays);

    // ── Workers ───────────────────────────────────────────────────────────────
    [Header("Workers")]
    [SerializeField] private int startingWorkerCount = 12;
    private List<Worker> allWorkers = new List<Worker>();

    // ── Events ────────────────────────────────────────────────────────────────
    // OnGameOver was DELETED (2026-07-08 run-end rework): the clock never declares game
    // over any more. RunManager owns the decision (RunManager.OnGameOverTriggered) and
    // evaluates it only when no action is running, so a multi-day action that crosses
    // the final day always finishes first.
    public event Action<int>      OnTimeAdvanced;
    public event Action<int, int> OnPeopleFatigued;  // (count, latestReturnDay)
    public event Action<int>      OnPeopleRecovered;

    // ── Worker meaning-event seams (arch §6.1 HOOK) ─────────────────────────────
    // Law 2: a SPECIFIC named worker recovering / having a birthday is meaning — the
    // narrative layer (chat app, story weaver) wants the worker, not just a count.
    // OnPeopleRecovered (count) stays for the UI; these carry the individual.
    /// <summary>Fires per worker the day they return from fatigue. arg: the recovered worker.</summary>
    public event Action<Worker>   OnWorkerRecovered;
    /// <summary>Fires on a worker's birthday. SEAM ONLY — not yet raised (birthday system dormant, CLAUDE.md §7).</summary>
    public event Action<Worker>   OnWorkerBirthday;

    // ── Public Accessors ──────────────────────────────────────────────────────
    public int TotalDays => totalDays;
    public int TotalPeople => allWorkers.Count;
    public int AvailablePeople => allWorkers.Count(w => !w.isFatigued);
    public int RecoveringPeopleCount => allWorkers.Count(w => w.isFatigued);

    public List<Worker> AllWorkers => allWorkers;
    public List<Worker> AvailableWorkers => allWorkers.Where(w => !w.isFatigued).ToList();
    public List<Worker> FatiguedWorkers => allWorkers.Where(w => w.isFatigued).ToList();

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Apply the main-menu season selection (if any) before anything reads RunLengthDays.
        // RunConfig is a cross-scene static: 0/unset means "no selection was made" (direct
        // scene load, tests) and the serialized default above stands.
        if (Habitales.Core.RunConfig.HasSelection)
            runLengthDays = Habitales.Core.RunConfig.RunLengthDaysForSelection();

        // Pick the run's starting calendar day here (before any Start() runs) so every other
        // system's Start() can already read StartDayOfYear/DayOfYear (e.g. WeatherManager's
        // 21-day window init).
        PickStartDayOfYear();
    }

    // Chooses StartDayOfYear. Random runs never begin exactly on a season boundary (Dec 1 / Jun 1)
    // so weather doesn't open on a hard seasonal snap — reroll while IsSeasonStart.
    private void PickStartDayOfYear()
    {
        if (!randomizeStartDate)
        {
            StartDayOfYear = startDayOfYearOverride >= 0
                ? startDayOfYearOverride % GameCalendar.DaysPerYear
                : 0;
            return;
        }

        int day;
        do
        {
            day = UnityEngine.Random.Range(0, GameCalendar.DaysPerYear);
        } while (GameCalendar.IsSeasonStart(day));

        StartDayOfYear = day;
    }

    void Start()
    {
        InitializeWorkers(startingWorkerCount);
        // Fatigue severity now READS WeatherManager.FatigueK on demand (Law 1) — no event
        // subscription and no mirrored weatherK field. See ApplyFatigue.
    }

    public void InitializeWorkers(int count)
    {
        allWorkers = WorkerFactory.GenerateBatch(count);
        Debug.Log($"ResourceManager: {count} workers initialized.");
    }

    // ── Time ──────────────────────────────────────────────────────────────────

    // Original batch advance — used by debug tools (DebugAdvanceOneDay, TestYearComplete).
    // Does NOT wait for the day/night cycle; fires OnTimeAdvanced(days) once in bulk.
    // The run-length cap is deliberately NOT checked here — RunManager's heartbeat evaluates
    // game-over per resolved day and stops its own loop, so days past the cap simply resolve.
    public void AdvanceTime(int days)
    {
        totalDays += days;
        CheckWorkerRecovery();

        if (WeatherManager.Instance != null)
            WeatherManager.Instance.RollWeather(totalDays);

        OnTimeAdvanced?.Invoke(days);
        Debug.Log($"Day {totalDays} ({GetFullTimeDisplay()}) | Available: {AvailablePeople}/{TotalPeople}");
    }

    // Single-day primitive — advances simulation state by exactly one day.
    // Fires OnTimeAdvanced(1) but does NOT wait for DayNightCycleHandler.
    // The heartbeat system (and AdvanceTimeStepped below) call this directly.
    public void AdvanceOneDay()
    {
        totalDays++;
        CheckWorkerRecovery();

        if (WeatherManager.Instance != null)
            WeatherManager.Instance.RollWeather(totalDays);

        OnTimeAdvanced?.Invoke(1); // triggers DayNightCycleHandler.StartCycle(1)
        Debug.Log($"Day {totalDays} ({GetFullTimeDisplay()}) | Available: {AvailablePeople}/{TotalPeople}");
    }

    // Stepped advance — used by ActionManager after real player actions.
    // Advances one day at a time and waits for DayNightCycleHandler to finish each cycle.
    // No run-length peek here (2026-07-08 run-end rework): an action that crosses the final
    // day plays ALL its remaining days — the season ends when the action does, evaluated by
    // RunManager on OnActionCompleted.
    public IEnumerator AdvanceTimeStepped(int days)
    {
        for (int i = 0; i < days; i++)
        {
            AdvanceOneDay();
            yield return new WaitUntil(() => DayNightCycleHandler.IsIdle);
        }
    }

    public string GetFullTimeDisplay()
    {
        int year = totalDays / GameCalendar.DaysPerYear + 1;
        int day = totalDays % GameCalendar.DaysPerYear + 1;
        return $"Year {year}, Day {day}";
    }

    /// <summary>Calendar-flavored date display for the weather app / UI, e.g. "Jun 14, Year 1".</summary>
    public string GetCalendarDisplay()
    {
        return $"{GameCalendar.GetShortDate(DayOfYear)}, Year {CurrentYear}";
    }

    // ── Fatigue ───────────────────────────────────────────────────────────────
    // Inside ResourceManager.cs

    public void ApplyFatigue(int workerCount, int duration, float multiplier, float exertion) 
{
    // Ensure exertion is clamped. If exertion is below 0.2f, the job was so overstaffed it's negligible.
    exertion = Mathf.Clamp(exertion, 0.0f, 1.0f); 

    int fatiguedCount = 0;
    int maxReturnDay = 0;

    var candidates = allWorkers.Where(w => !w.isFatigued).ToList();
    Shuffle(candidates);

    int workersToProcess = Mathf.Min(workerCount, candidates.Count);

    // Hard cap: Never fatigue more than a certain percentage of the assigned team in one go.
    // Example: Even at 100% exertion, only a max of 40% of the team will actually need recovery.
    float maxFatigueProportion = 0.40f; 
    int maxWorkersToFatigue = Mathf.RoundToInt(workersToProcess * maxFatigueProportion * exertion);

    for (int i = 0; i < workersToProcess; i++)
    {
        // Stop if we've hit our fatigue cap for this action
        if (fatiguedCount >= maxWorkersToFatigue) break;

        Worker w = candidates[i];

        // The probability of getting fatigued scales with exertion, but it's never a 100% guarantee.
        float fatigueChance = exertion * 0.5f; // Max 50% chance per person at highest exertion
        
        if (UnityEngine.Random.value > fatigueChance) 
            continue;

        float r = UnityEngine.Random.value;
        float fatigueK = WeatherManager.Instance != null ? WeatherManager.Instance.FatigueK : 3f;
        float severity = Mathf.Pow(r, fatigueK);
        
        // Use RoundToInt instead of CeilToInt. This allows low-severity rolls to round down to 0 days (no fatigue).
        int fatigueDays = Mathf.RoundToInt(duration * multiplier * severity);

        if (fatigueDays > 0)
        {
            w.isFatigued = true;
            w.returnDay = totalDays + fatigueDays;
            w.actionsParticipated++;
            
            fatiguedCount++;
            maxReturnDay = Mathf.Max(maxReturnDay, w.returnDay);
        }
    }

    if (fatiguedCount > 0)
    {
        OnPeopleFatigued?.Invoke(fatiguedCount, maxReturnDay);
        Debug.Log($"[Fatigue] {fatiguedCount} workers benched out of {workersToProcess}. (Exertion: {exertion:P0})");
    }
}

    private void CheckWorkerRecovery()
    {
        int recovered = 0;
        foreach (Worker w in allWorkers)
        {
            if (w.isFatigued && totalDays >= w.returnDay)
            {
                w.isFatigued = false;
                w.returnDay = 0;
                recovered++;
                OnWorkerRecovered?.Invoke(w);   // per-worker meaning-event (Law 2)
            }
        }
        if (recovered > 0)
        {
            OnPeopleRecovered?.Invoke(recovered);
            Debug.Log($"{recovered} worker(s) recovered. Available: {AvailablePeople}/{TotalPeople}");
        }
    }

    // ── Utility ───────────────────────────────────────────────────────────────
    public void IncreaseTotalPeople(int count)
    {
        if (count <= 0) return;
        var newWorkers = WorkerFactory.GenerateBatch(count);
        allWorkers.AddRange(newWorkers);
        Debug.Log($"ResourceManager: +{count} workers added. Total: {TotalPeople}");
    }

    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}