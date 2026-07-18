using System.Collections.Generic;
using UnityEngine;

public class RegionGenerationResult
{
    public int regionID;
    public int tileCount;
    public RegionTheme dominantTheme;
    public float averageStartingHealth;

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
