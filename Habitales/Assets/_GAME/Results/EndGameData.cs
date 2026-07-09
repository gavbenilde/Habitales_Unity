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

    // Peak-thriving snapshot — tracked by RunManager, captured UI-free by ScreenshotService.
    // The texture is owned by RunManager (destroyed on scene teardown); UI only reads it.
    public int       peakThrivingCount;
    public Texture2D peakScreenshot;

    // Day index (into healthHistory) at which the peak-thriving high-water mark was set.
    // -1 when no peak has been recorded (e.g. run ended before EvaluateThrivingPeak ever fired).
    public int peakAtDay = -1;

    public int xpEarned;   // = peakThrivingCount; kept separate for LevelUpScreenUI clarity
    public int xpBefore;   // totalXp BEFORE AddXp; drives bar fill start position

    // HQ's verdict on the season — forced to Collapse when endReason is an Ecosystem Collapse.
    public Habitales.Meta.SeasonGrade seasonGrade;

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
