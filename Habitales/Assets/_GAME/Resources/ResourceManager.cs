using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ResourceManager : MonoBehaviour
{
    public static ResourceManager Instance { get; private set; }

    // ── Time ─────────────────────────────────────────────────────────────────
    [Header("Time")]
    [SerializeField] private int totalDays   = 0;
    [SerializeField] private int daysPerYear = 365;
    [SerializeField] private int maxYears    = 5;
    
    public int DaysPerYear => daysPerYear;

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
    public int TotalDays       => totalDays;                                  // FIX 1
    public int TotalPeople     => allWorkers.Count;
    public int AvailablePeople => allWorkers.Count(w => !w.isFatigued);
    public int RecoveringPeopleCount => allWorkers.Count(w => w.isFatigued);

    public List<Worker> AllWorkers       => allWorkers;
    public List<Worker> AvailableWorkers => allWorkers.Where(w => !w.isFatigued).ToList();
    public List<Worker> FatiguedWorkers  => allWorkers.Where(w =>  w.isFatigued).ToList();
    

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
    public void AdvanceTime(int days)
    {
        totalDays += days;
        CheckWorkerRecovery();
        
        if (WeatherManager.Instance != null)
            WeatherManager.Instance.RollWeather(totalDays); 

        if (totalDays >= daysPerYear * maxYears)
        {
            OnGameOver?.Invoke();
            return;
        }
        OnTimeAdvanced?.Invoke(days);
        Debug.Log($"Day {totalDays} ({GetFullTimeDisplay()}) | Available: {AvailablePeople}/{TotalPeople}");
    }

    public string GetFullTimeDisplay()
    {
        int year = totalDays / daysPerYear + 1;
        int day  = totalDays % daysPerYear + 1;
        return $"Year {year}, Day {day}";
    }

    // ── Fatigue ───────────────────────────────────────────────────────────────
    public void ApplyFatigue(int workerCount, int daysWorked, float multiplier)
    {
        List<Worker> available = AvailableWorkers;
        if (available.Count == 0) return;

        int toFatigue = Mathf.Min(workerCount, available.Count);
        Shuffle(available);

        // FIX 2: increment FIRST — all available workers participated in the action
        foreach (Worker w in available)
            w.actionsParticipated++;

        // Then mark the subset as fatigued
        int fatiguedCount   = 0;
        int latestReturnDay = totalDays;

        for (int i = 0; i < toFatigue; i++)
        {
            Worker w = available[i];

            float r            = UnityEngine.Random.value;
            float x            = Mathf.Pow(r, weatherK);
            int   recoveryDays = Mathf.Max(1, Mathf.CeilToInt(x * daysWorked * 0.5f * multiplier));

            w.isFatigued    = true;
            w.returnDay     = totalDays + recoveryDays;
            latestReturnDay = Mathf.Max(latestReturnDay, w.returnDay);
            fatiguedCount++;
        }

        if (fatiguedCount > 0)
        {
            OnPeopleFatigued?.Invoke(fatiguedCount, latestReturnDay);
            Debug.Log($"{fatiguedCount} worker(s) fatigued. Latest return: day {latestReturnDay}.");
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
                w.returnDay  = 0;
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
