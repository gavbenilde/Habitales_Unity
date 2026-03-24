using System;
using UnityEngine;

public enum WeatherState { Sunny, Cloudy, Rainy, Stormy }

public class WeatherManager : MonoBehaviour
{
    public static WeatherManager Instance { get; private set; }

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;

    public WeatherState CurrentWeather { get; private set; } = WeatherState.Cloudy;

    public event Action<WeatherState> OnWeatherChanged;

    // Dry season: days 1–182   |   Wet season: days 183–365
    // Weights order: Sunny, Cloudy, Rainy, Stormy
    private static readonly float[] DryWeights = { 40f, 35f, 20f,  5f };
    private static readonly float[] WetWeights = { 15f, 30f, 40f, 15f };

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── Called by ResourceManager.AdvanceTime once per action ─────────────────
    public void RollWeather(int currentTotalDay)
    {
        int dayOfYear = currentTotalDay % 365;
        bool isDry    = dayOfYear < 182;
        float[] weights = isDry ? DryWeights : WetWeights;

        WeatherState next = WeightedRoll(weights);
        if (next == CurrentWeather) return;

        CurrentWeather = next;
        ApplySideEffects();
        OnWeatherChanged?.Invoke(CurrentWeather);

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

    private void ApplySideEffects()
    {
        if (ResourceManager.Instance == null) return;

        switch (CurrentWeather)
        {
            case WeatherState.Sunny:
            case WeatherState.Stormy:
                ResourceManager.Instance.SetWeatherFatigueK(1.5f); break;
            case WeatherState.Cloudy:
            case WeatherState.Rainy:
                ResourceManager.Instance.SetWeatherFatigueK(3.0f); break;
        }
    }

    // ── Multipliers queried by other systems ──────────────────────────────────

    /// Work speed multiplier applied to action days.
    public float GetWorkSpeedMultiplier() => CurrentWeather switch
    {
        WeatherState.Rainy  => 0.85f,
        WeatherState.Stormy => 0.50f,
        _                   => 1.0f
    };

    /// Fire spread chance multiplier queried by FireEntity.
    public float GetFireSpreadMultiplier() => CurrentWeather switch
    {
        WeatherState.Sunny  => 1.5f,
        WeatherState.Rainy  => 0.5f,
        WeatherState.Stormy => 0.5f,
        _                   => 1.0f
    };

    /// Extra vegetation damage per day on sunny tiles with fire.
    public float GetFireBonusDamage() =>
        CurrentWeather == WeatherState.Sunny ? 4f : 0f;
}
