using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ResourceManager : MonoBehaviour
{
    public static ResourceManager Instance { get; private set; }

    // ── Time ─────────────────────────────────────────────────────────────────
    [Header("Time")]
    [SerializeField] private int totalDays = 0;
    [SerializeField] private int daysPerYear = 365;
    [SerializeField] private int maxYears = 5;

    public int DaysPerYear => daysPerYear;
    public int CurrentYear => (totalDays / daysPerYear) + 1;

    // ── Workers ───────────────────────────────────────────────────────────────
    [Header("Workers")]
    [SerializeField] private int startingWorkerCount = 12;
    private List<Worker> allWorkers = new List<Worker>();

    // ── Research Points ───────────────────────────────────────────────────────
    [Header("Research Points")]
    private int researchPoints = 0;
    public int ResearchPoints => researchPoints;

    // ── Weather hook ──────────────────────────────────────────────────────────
    private float weatherK = 3f;

    // ── Events ────────────────────────────────────────────────────────────────
    public event Action<int>      OnTimeAdvanced;
    public event Action<int, int> OnPeopleFatigued;  // (count, latestReturnDay)
    public event Action<int>      OnPeopleRecovered;
    public event Action           OnGameOver;
    public event Action<int>      OnRPChanged;

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

        if (totalDays >= 100)
        {
            OnGameOver?.Invoke();
            return;
        }

        OnTimeAdvanced?.Invoke(days);
        Debug.Log($"Day {totalDays} ({GetFullTimeDisplay()}) | Available: {AvailablePeople}/{TotalPeople}");
    }

    // Stepped advance — used by ActionManager after real player actions.
    // Advances one day at a time, firing OnTimeAdvanced(1) each iteration,
    // then waits for DayNightCycleHandler to finish before moving to the next day.
    public IEnumerator AdvanceTimeStepped(int days)
    {
        for (int i = 0; i < days; i++)
        {
            totalDays++;
            CheckWorkerRecovery();

            if (WeatherManager.Instance != null)
                WeatherManager.Instance.RollWeather(totalDays);

            if (totalDays >= 100)
            {
                OnGameOver?.Invoke();
                yield break;
            }

            OnTimeAdvanced?.Invoke(1); // triggers DayNightCycleHandler.StartCycle(1)
            Debug.Log($"Day {totalDays} ({GetFullTimeDisplay()}) | Available: {AvailablePeople}/{TotalPeople}");

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
        // Ensure exertion is clamped between a tiny minimum and 1.0
        exertion = Mathf.Clamp(exertion, 0.05f, 1.0f);
    
        int fatiguedCount = 0;
        int maxReturnDay = 0;

        // Get all available workers to potentially fatigue them
        var candidates = allWorkers.Where(w => !w.isFatigued).ToList();
        Shuffle(candidates);

        // Only look at the number of people who actually went to work
        int workersToProcess = Mathf.Min(workerCount, candidates.Count);

        for (int i = 0; i < workersToProcess; i++)
        {
            Worker w = candidates[i];

            // NEW: Probability Gate based on exertion
            // If exertion is 0.1, there's only a 10% chance they get fatigued.
            if (UnityEngine.Random.value > exertion) 
                continue;

            float r = UnityEngine.Random.value;
            float severity = Mathf.Pow(r, weatherK);
        
            // Duration * Multiplier * Severity (the Random^3 curve)
            int fatigueDays = Mathf.CeilToInt(duration * multiplier * severity);

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
            Debug.Log($"[Fatigue] {fatiguedCount} workers fatigued from job (Exertion: {exertion:P0})");
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
            }
        }
        if (recovered > 0)
        {
            OnPeopleRecovered?.Invoke(recovered);
            Debug.Log($"{recovered} worker(s) recovered. Available: {AvailablePeople}/{TotalPeople}");
        }
    }

    // ── Weather hook ──────────────────────────────────────────────────────────
    public void SetWeatherFatigueK(float k) => weatherK = k;

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