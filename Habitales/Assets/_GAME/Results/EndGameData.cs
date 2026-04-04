using System.Collections.Generic;

/// <summary>
/// Immutable data snapshot passed from GameManager to EndGameScreenUI at game-over.
/// GameManager populates everything. EndGameScreenUI only reads.
/// </summary>
public class EndGameData
{
    // Header
    public string endReason;
    public int    currentYear;
    public int    totalDays;
    public float  worldHealth;

    // Health history — one entry per day elapsed (filled by GameManager per OnTimeAdvanced)
    public List<float> healthHistory = new();

    // Tile breakdown
    public int thrivingCount;
    public int degradedCount;
    public int criticalCount;

    // Per-unlocked-zone health snapshot  (regionID → avg health)
    public Dictionary<int, float> zoneHealths = new();

    // Employee of the Year
    public Worker topWorker;

    // Silly stats — pre-derived strings, UI never touches raw dicts
    public string favouriteAction;
    public string mostAvoidedAction;
    public string mostChattedWorker;

    // Footer
    public int researchPoints;
}
