using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ZoneProfile", menuName = "Habitales/Zone Profile")]
public class ZoneProfile : ScriptableObject
{
    [Header("Shape")]
    [Tooltip("Min and max number of tiles to generate for this zone.")]
    public Vector2Int sizeRange = new Vector2Int(25, 40);

    [Tooltip("0.0–1.0. Higher = rounder blob. Lower = jagged, branching shape.")]
    [Range(0.4f, 0.9f)]
    public float floodFillProbability = 0.65f;

    [Header("Stat Ranges")]
    public Vector2 nutrientBalanceRange     = new Vector2(10f, 25f);
    public Vector2 soilOrganicMatterRange   = new Vector2(10f, 25f);
    public Vector2 soilStructureRange       = new Vector2(10f, 25f);
    public Vector2 biologicalActivityRange  = new Vector2(8f,  20f);
    public Vector2 waterDynamicsRange       = new Vector2(10f, 25f);
    public Vector2 erosionResistanceRange   = new Vector2(10f, 25f);
    public Vector2 vegetationCoverRange     = new Vector2(5f,  25f);
    public Vector2 contaminationRange       = new Vector2(5f,  20f);

    [Header("Theme Weights")]
    [Tooltip("Weight for LoggedTrees theme. Common — bare soil, zero vegetation.")]
    public int loggedTreesWeight = 3;
    [Tooltip("Weight for NutrientDepletion theme. Common — exhausted, overfarmed soil.")]
    public int nutrientDepletionWeight = 3;
    [Tooltip("Weight for HeavyMetalContamination theme. Keep low — high contamination impact.")]
    public int heavyMetalContaminationWeight = 1;
    [Tooltip("Weight for ActiveErosion theme. Moderate — targets Erosion Resistance primarily.")]
    public int activeErosionWeight = 2;
    [Tooltip("Weight for DrainageCollapse theme. Moderate — targets Water Dynamics primarily.")]
    public int drainageCollapseWeight = 2;
    [Tooltip("Weight for SoilCompaction theme. Moderate — targets Soil Structure primarily.")]
    public int soilCompactionWeight = 2;
    [Tooltip("Weight for ChemicalBurnout theme. Low — targets Biological Activity, similar severity to HMC.")]
    public int chemicalBurnoutWeight = 1;

    [Header("Issues")]
    [Tooltip("Override the theme roll and force a specific theme. Leave unset for weighted random.")]
    public bool forceTheme = false;
    public ZoneTheme forcedTheme = ZoneTheme.LoggedTrees;

    [Tooltip("Fraction of tiles that receive an issue tag. 0.3 = 30% of tiles.")]
    public Vector2 issueDensityRange = new Vector2(0.3f, 0.5f);

    [Tooltip("Chance each issued tile gets an off-theme issue instead of the dominant one.")]
    [Range(0f, 0.5f)]
    public float offThemeIssueProbability = 0.2f;

    [Header("Buildings")]
    public int maxVillages = 1;
    public int maxFactories = 1;
    [Range(0f, 1f)] public float villageSpawnChance = 0.5f;
    [Range(0f, 1f)] public float factorySpawnChance = 0.3f;

    [Header("Rewards")]
    [Tooltip("Workers added to the roster when this zone is unlocked.")]
    public int workerReward = 4;

    [Header("Forced Entity Overrides")]
    [Tooltip("Enable to place specific entities at specific offsets from the seed tile. For event-driven zones.")]
    public bool forceSpecificEntities = false;
    public List<ForcedEntityPlacement> forcedEntities = new List<ForcedEntityPlacement>();

    [Header("Azi Dialogue")]
    [Tooltip("Key passed to DialogueManager when this zone finishes generating. Maps to an Azi callout line.")]
    public string aziCalloutKey;
}
