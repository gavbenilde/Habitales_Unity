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

    // Exponentially escalating neglect decay (per-tile runtime state). Each resolved day the
    // tile loses decayK from every soil substat + vegetation cover, then decayK grows by
    // DecayGrowth — so an untended tile degrades faster the longer it's ignored. Working the
    // tile (a player action) resets it to DecayStart, and a Plant-category occupant pins it
    // there every day (a living plant tends its own tile). Applied by TileManager.ApplyDailyDecay.
    public const float DecayStart  = 0.1f;   // starting / reset decay rate
    public const float DecayGrowth = 1.016f; // per-day multiplier (k *= 1.016)
    public float decayK = DecayStart;

    public void ResetDecay() => decayK = DecayStart;

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
