using System.Collections.Generic;
using UnityEngine;

public class ZoneGenerationResult
{
    public int regionID;
    public int tileCount;
    public ZoneTheme dominantTheme;
    public float averageStartingHealth;

    // Building counts — for Azi callouts and UI
    public int villagesPlaced;
    public int factoriesPlaced;

    // Issue summary — for Azi callouts
    public int issuesAssigned;
    public float contaminationCoverage; // 0–1, proportion of tiles with HMC

    // Notable findings — plain strings Azi can read directly
    public List<string> notableFindings = new List<string>();

    // For camera pan and map UI
    public Vector2Int approximateCenter;

    // Event tracking — for future event system hookup
    public bool wasTriggeredByEvent;
    public string sourceEventID;
}