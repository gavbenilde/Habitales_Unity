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
    [SerializeField] private int daysPerYear = 365;
    [SerializeField] private int maxYears = 5;

    // Canonical run length — the single source of truth for when a run ends.
    // A future season-picker UI sets this one field (seasons × 182.5 days).
    [SerializeField] private int runLengthDays = 100;

    public int DaysPerYear => daysPerYear;
    public int CurrentYear => (totalDays / daysPerYear) + 1;

    // Run-length accessors (Law 1: getters, not setters).
    public int RunLengthDays => runLengthDays;
    public int DaysRemaining => Mathf.Max(0, runLengthDays - totalDays);

    // ── Workers ───────────────────────────────────────────────────────────────
    [Header("Workers")]
    [SerializeField] private int startingWorkerCount = 12;
    private List<Worker> allWorkers = new List<Worker>();

    // ── Research Points ───────────────────────────────────────────────────────
    [Header("Research Points")]
    private int researchPoints = 0;
    public int ResearchPoints => researchPoints;

    // ── Events ────────────────────────────────────────────────────────────────
    public event Action<int>      OnTimeAdvanced;
    public event Action<int, int> OnPeopleFatigued;  // (count, latestReturnDay)
    public event Action<int>      OnPeopleRecovered;
    public event Action           OnGameOver;
    public event Action<int>      OnRPChanged;

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
    public void AdvanceTime(int days)
    {
        totalDays += days;
        CheckWorkerRecovery();

        if (WeatherManager.Instance != null)
            WeatherManager.Instance.RollWeather(totalDays);

        if (totalDays >= runLengthDays)
        {
            OnGameOver?.Invoke();
            return;
        }

        OnTimeAdvanced?.Invoke(days);
        Debug.Log($"Day {totalDays} ({GetFullTimeDisplay()}) | Available: {AvailablePeople}/{TotalPeople}");
    }

    // Single-day primitive — advances simulation state by exactly one day.
    // Fires OnGameOver and OnTimeAdvanced(1) but does NOT wait for DayNightCycleHandler.
    // The heartbeat system (and AdvanceTimeStepped below) call this directly.
    public void AdvanceOneDay()
    {
        totalDays++;
        CheckWorkerRecovery();

        if (WeatherManager.Instance != null)
            WeatherManager.Instance.RollWeather(totalDays);

        if (totalDays >= runLengthDays)
        {
            OnGameOver?.Invoke();
            return;
        }

        OnTimeAdvanced?.Invoke(1); // triggers DayNightCycleHandler.StartCycle(1)
        Debug.Log($"Day {totalDays} ({GetFullTimeDisplay()}) | Available: {AvailablePeople}/{TotalPeople}");
    }

    // Stepped advance — used by ActionManager after real player actions.
    // Advances one day at a time and waits for DayNightCycleHandler to finish each cycle.
    public IEnumerator AdvanceTimeStepped(int days)
    {
        for (int i = 0; i < days; i++)
        {
            bool gameOver = totalDays + 1 >= runLengthDays;  // peek before advancing
            AdvanceOneDay();

            if (gameOver)
                yield break;

            yield return new WaitUntil(() => DayNightCycleHandler.IsIdle);
        }
    }

    public string GetFullTimeDisplay()
    {
        int year = totalDays / daysPerYear + 1;
        int day = totalDays % daysPerYear + 1;
        return $"Year {year}, Day {day}";
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

    // ── Research Points ───────────────────────────────────────────────────────
    public void EarnRP(int amount)
    {
        researchPoints += amount;
        OnRPChanged?.Invoke(researchPoints);
    }

    public bool SpendRP(int amount)
    {
        if (researchPoints < amount) return false;
        researchPoints -= amount;
        OnRPChanged?.Invoke(researchPoints);
        return true;
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