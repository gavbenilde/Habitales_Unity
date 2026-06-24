using System.Collections.Generic;
using UnityEngine;

// Health tier for meaning-event crossings (arch §2.1b). The cutoffs live on RunManager's
// serialized thresholds (criticalHealthThreshold / thrivingHealthThreshold), NOT hardcoded
// here, so tier crossings stay consistent with the thriving-count / collapse logic.
public enum Tier { Critical, Degraded, Thriving }

public class Tile
{
    public Vector2Int gridPosition;
    public TileStats stats;
    public TileEntity entity;
    public List<TileIssue> issues = new List<TileIssue>();
    public List<TileOverlayType> tv = new List<TileOverlayType>();
    public int regionID;

    // Runtime tier-crossing tracking (arch §2.1b). RunManager.EvaluateThresholds seeds
    // lastTier on first evaluation (no event), then fires OnTileTierChanged on a change.
    // Per-instance runtime state — never persisted as definition data.
    public Tier lastTier;
    public bool tierSeeded = false;

    public float CalculateHealth() => stats.CalculateHealth();
    public float GetSoilComposite() => stats.soilComposite;
    public float GetVegetationCover() => stats.vegetationCover;
    public float GetContamination() => stats.contamination;
    
    // Prototype: substats and issues are revealed by default. The two actions
    // that flipped these (AnalyzeSoilSampleAction, EcologicalSurveyAction) are
    // dormant per Prototype/CLAUDE.md §7.
    public bool isAnalyzed     = true;
    public bool issuesRevealed = true;


}

[SerializeField]
public class TileStats
{
    public float nutrientBalance    = 50f;
    public float soilOrganicMatter  = 50f;
    public float soilStructure      = 50f;
    public float biologicalActivity = 50f;
    public float waterDynamics      = 50f;
    public float erosionResistance  = 50f;
    public float vegetationCover    = 50f;
    public float contamination      = 0f;

    public float soilComposite =>
        (nutrientBalance + soilOrganicMatter + soilStructure +
         biologicalActivity + waterDynamics + erosionResistance) / 6f;

    public float CalculateHealth()
    {
        float baseHealth = (soilComposite + vegetationCover + (100f - contamination)) / 3f;
        return contamination > 60f ? Mathf.Min(baseHealth, 33f) : baseHealth;
    }
}
