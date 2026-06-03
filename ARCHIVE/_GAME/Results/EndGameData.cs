using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Immutable data snapshot passed from GameManager to EndGameScreenUI at game-over.
/// GameManager populates everything. EndGameScreenUI only reads.
/// </summary>
public class EndGameData
{
    // Header
    public string    endReason;
    public string    aziSummaryLine;
    public int       currentYear;
    public int       totalDays;
    public float     worldHealth;

    // Health history — one entry per day elapsed (filled by GameManager per OnTimeAdvanced)
    public List<float> healthHistory = new();

    // Tile breakdown
    public int thrivingCount;
    public int degradedCount;
    public int criticalCount;

    // Peak-thriving snapshot — produced and captured by RunSnapshot, referenced here.
    public RunSnapshot snapshot;

    public int xpEarned;   // = snapshot.peakThrivingCount; kept separate for LevelUpScreenUI clarity
    public int xpBefore;   // totalXp BEFORE AddXp; drives bar fill start position

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
