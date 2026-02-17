using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Manages Time and People resources for the entire game.
/// Handles fatigue recovery and win condition checking.
/// </summary>
public class ResourceManager : MonoBehaviour
{
    public static ResourceManager Instance { get; private set; }

    [Header("People Settings")]
    [SerializeField] private int totalPeople = 25;
    
    [Header("Time Settings (Vertical Slice)")]
    private const int DAYS_PER_WEEK = 7;
    private const int WEEKS_PER_YEAR = 52;
    private const int DAYS_PER_YEAR = DAYS_PER_WEEK * WEEKS_PER_YEAR; // 364 days
    
    // Time tracking
    private int totalDays = 0;
    
    // Fatigue tracking
    private List<RecoveringPerson> recoveringPeople = new List<RecoveringPerson>();
    
    // Events
    public event Action<int> OnTimeAdvanced;
    public event Action<int, int> OnPeopleFatigued; // (count, returnDay)
    public event Action<int> OnPeopleRecovered; // (count)
    public event Action OnGameOver;

    // Public properties
    public int TotalPeople => totalPeople;
    public int AvailablePeople => totalPeople - recoveringPeople.Count;
    public int RecoveringPeopleCount => recoveringPeople.Count;
    public int TotalDays => totalDays;
    public bool IsGameOver => totalDays >= DAYS_PER_YEAR;
    
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Returns formatted time display: "Week X, Day Y"
    /// </summary>
    public string GetFullTimeDisplay()
    {
        int week = (totalDays / DAYS_PER_WEEK) + 1; // Week 1-52
        int day = (totalDays % DAYS_PER_WEEK) + 1;  // Day 1-7
        return $"Week {week}, Day {day}";
    }

    /// <summary>
    /// Returns simple week/day format: "XW, YD"
    /// </summary>
    public string GetCompactTimeDisplay()
    {
        int weeks = totalDays / DAYS_PER_WEEK;
        int days = totalDays % DAYS_PER_WEEK;
        return $"{weeks}W, {days}D";
    }

    /// <summary>
    /// Advances time and checks for people recovering from fatigue.
    /// </summary>
    public void AdvanceTime(int days)
    {
        totalDays += days;
        
        // Check if any people return from recovery
        int recovered = 0;
        for (int i = recoveringPeople.Count - 1; i >= 0; i--)
        {
            if (recoveringPeople[i].returnDay <= totalDays)
            {
                recoveringPeople.RemoveAt(i);
                recovered++;
            }
        }
        
        if (recovered > 0)
        {
            OnPeopleRecovered?.Invoke(recovered);
            Debug.Log($"✓ {recovered} people returned from recovery! Available: {AvailablePeople}/{TotalPeople}");
        }
        
        OnTimeAdvanced?.Invoke(days);
        
        if (IsGameOver)
        {
            Debug.Log($"★ GAME OVER: 1 year completed! Final time: {GetFullTimeDisplay()}");
            OnGameOver?.Invoke();
        }
    }

    /// <summary>
    /// Calculates and applies fatigue based on action parameters.
    /// Formula: baseFatigue (0-20%) + (tilesWorked × fatiguePerTile × 2%)
    /// Recovery: Half the days worked, minimum 1 day
    /// </summary>
    public void ApplyFatigue(int tilesWorked, int daysWorked, float fatigueMultiplierPerTile = 2.0f, float minBaseFatigue = 0f, float maxBaseFatigue = 20f)
    {
        if (AvailablePeople == 0) return;
        
        // Roll base fatigue (0-20%)
        float baseFatiguePercent = Random.Range(minBaseFatigue, maxBaseFatigue);
        
        // Add tile-based multiplier: tilesWorked × fatigueMultiplierPerTile × 2%
        float tileBonus = tilesWorked * fatigueMultiplierPerTile * 2f;
        
        // Total fatigue percentage
        float totalFatiguePercent = baseFatiguePercent + tileBonus;
        
        // Calculate number of people
        int peopleToFatigue = Mathf.CeilToInt(AvailablePeople * (totalFatiguePercent / 100f));
        peopleToFatigue = Mathf.Min(peopleToFatigue, AvailablePeople); // Cap at available
        
        if (peopleToFatigue == 0) return;
        
        // Calculate recovery time: half the days worked, minimum 1
        int recoveryDays = Mathf.Max(1, daysWorked / 2);
        int returnDay = totalDays + recoveryDays;
        
        // Add to recovery list
        for (int i = 0; i < peopleToFatigue; i++)
        {
            recoveringPeople.Add(new RecoveringPerson { returnDay = returnDay });
        }
        
        OnPeopleFatigued?.Invoke(peopleToFatigue, returnDay);
        Debug.Log($"⚠ Fatigue Applied: {peopleToFatigue} people ({totalFatiguePercent:F1}%) recovering for {recoveryDays} days | Available: {AvailablePeople}/{TotalPeople}");
    }

    /// <summary>
    /// Resets resources (for new game / testing).
    /// </summary>
    public void Reset()
    {
        totalDays = 0;
        recoveringPeople.Clear();
        Debug.Log($"ResourceManager reset | People: {AvailablePeople}/{TotalPeople}");
    }
}

[System.Serializable]
public class RecoveringPerson
{
    public int returnDay; // totalDays value when person returns
}
