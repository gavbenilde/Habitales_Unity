using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Manages zone health, generation, and metadata.
/// Replaces HardCode zone spawning with a flexible, profile-driven system.
/// Subscribe to OnZoneGenerated to react to new zones (Azi callouts, camera pan, UI, etc.)
/// </summary>
public class ZoneManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TileManager tileManager;
    [SerializeField] private ResourceManager resourceManager;

    [Header("Zone Profiles")]
    [Tooltip("Profiles used in order as zones unlock. Index 0 = Zone 2, Index 1 = Zone 3, etc.")]
    [SerializeField] private List<ZoneProfile> defaultProfiles = new List<ZoneProfile>();
    [Tooltip("Used when defaultProfiles runs out. Should be a mid-to-late difficulty profile.")]
    [SerializeField] private ZoneProfile fallbackProfile;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;

    // Starts at 2 — Zone 1 is always spawned by GameManager.SpawnInitialZone()
    private int nextRegionID = 2;
    public int NextRegionID => nextRegionID;


    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
    };

    /// <summary>
    /// Fired after every successful zone generation.
    /// Subscribers: DialogueManager (Azi), camera controller, UI notifications.
    /// </summary>
    public event System.Action<ZoneGenerationResult> OnZoneGenerated;

    // =====================================================================
    // LIFECYCLE
    // =====================================================================

    private void Awake()
    {
        if (tileManager == null)
            tileManager = FindObjectOfType<TileManager>();
        if (tileManager == null)
            Debug.LogError("ZoneManager requires TileManager in scene!");

        if (resourceManager == null)
            resourceManager = FindObjectOfType<ResourceManager>();
    }

    // =====================================================================
    // PUBLIC API
    // =====================================================================

    /// <summary>
    /// Generates a new zone adjacent to the triggering region.
    /// Pass overrideProfile from an Event to force specific stats, entities, or themes.
    /// Returns a ZoneGenerationResult for subscribers to read from.
    /// </summary>
    public ZoneGenerationResult GenerateNewZone(int triggeringRegionID, ZoneProfile overrideProfile = null)
    {
        if (tileManager == null)
        {
            Debug.LogError("ZoneManager: TileManager missing!");
            return null;
        }

        ZoneProfile profile = overrideProfile ?? GetProfileForNextZone();
        if (profile == null)
        {
            Debug.LogError("ZoneManager: No ZoneProfile available! Assign defaultProfiles or a fallbackProfile.");
            return null;
        }

        int regionID = nextRegionID++;

        if (showDebugInfo)
            Debug.Log($"ZoneManager: Generating Zone {regionID} (triggered by Region {triggeringRegionID})...");

        // Step 1: Seed tile
        Vector2Int? seed = FindSeedTile(triggeringRegionID);
        if (seed == null)
        {
            Debug.LogError($"ZoneManager: No valid seed tile found adjacent to Region {triggeringRegionID}. Grid may be full.");
            nextRegionID--; // Roll back — generation failed
            return null;
        }

        // Step 2: Flood-fill shape
        int targetSize = Random.Range(profile.sizeRange.x, profile.sizeRange.y + 1);
        List<Vector2Int> positions = FloodFillShape(seed.Value, targetSize, profile.floodFillProbability);

        if (positions.Count == 0)
        {
            Debug.LogError("ZoneManager: Flood fill produced no positions!");
            nextRegionID--;
            return null;
        }

        // Step 3: Spawn tiles with profile-scaled stats
        List<Tile> zoneTiles = SpawnZoneTiles(positions, regionID, profile);

        // Step 4: Resolve theme
        ZoneTheme dominantTheme = profile.forceTheme ? profile.forcedTheme : RollTheme(profile);

        // Step 5: Issue assignment
        int issuesAssigned = AssignIssues(zoneTiles, dominantTheme, profile);

        // Step 6: Building placement
        int villagesPlaced = 0;
        int factoriesPlaced = 0;
        PlaceBuildings(zoneTiles, profile, ref villagesPlaced, ref factoriesPlaced);

        // Step 7: Forced entity overrides (event-driven zones)
        if (profile.forceSpecificEntities)
            ApplyForcedEntities(seed.Value, profile);

        // Step 8: Worker reward
        if (resourceManager != null && profile.workerReward > 0)
            resourceManager.IncreaseTotalPeople(profile.workerReward);

        // Build result
        float avgHealth = zoneTiles.Average(t => t.CalculateHealth());
        float contamCoverage = (float)zoneTiles.Count(t => t.stats.contamination > 60f) / zoneTiles.Count;
        Vector2Int center = CalculateCenter(positions);

        ZoneGenerationResult result = new ZoneGenerationResult
        {
            regionID              = regionID,
            tileCount             = zoneTiles.Count,
            dominantTheme         = dominantTheme,
            averageStartingHealth = avgHealth,
            villagesPlaced        = villagesPlaced,
            factoriesPlaced       = factoriesPlaced,
            issuesAssigned        = issuesAssigned,
            contaminationCoverage = contamCoverage,
            approximateCenter     = center,
            wasTriggeredByEvent   = overrideProfile != null,
            sourceEventID         = null
        };

        PopulateNotableFindings(result, dominantTheme);

        if (showDebugInfo)
            Debug.Log($"Zone {regionID} generated: {zoneTiles.Count} tiles | Theme: {dominantTheme} | Issues: {issuesAssigned} | Villages: {villagesPlaced} | Factories: {factoriesPlaced} | Avg Health: {avgHealth:F1}");

        OnZoneGenerated?.Invoke(result);
        return result;
    }

    /// <summary>
    /// Calculates average health of all tiles in a region. Returns 0–100.
    /// </summary>
    public float GetRegionHealth(int regionID)
    {
        List<Tile> tiles = tileManager.GetTilesInRegion(regionID);
        if (tiles == null || tiles.Count == 0)
        {
            if (showDebugInfo) Debug.LogWarning($"ZoneManager: No tiles found in region {regionID}!");
            return 0f;
        }

        float total = 0f;
        foreach (Tile tile in tiles)
            total += tile.CalculateHealth();

        float avg = total / tiles.Count;
        if (showDebugInfo) Debug.Log($"Region {regionID} Health: {avg:F1} ({tiles.Count} tiles)");
        return avg;
    }

    // =====================================================================
    // STEP 1 — SEED TILE
    // =====================================================================

    private Vector2Int? FindSeedTile(int adjacentToRegionID)
    {
        List<Tile> regionTiles = tileManager.GetTilesInRegion(adjacentToRegionID);
        if (regionTiles == null || regionTiles.Count == 0) return null;

        List<Vector2Int> candidates = new List<Vector2Int>();

        foreach (Tile tile in regionTiles)
        {
            foreach (Vector2Int dir in Directions)
            {
                Vector2Int neighbor = tile.gridPosition + dir;
                if (!IsInBounds(neighbor)) continue;
                if (tileManager.GetTile(neighbor.x, neighbor.y) != null) continue;
                candidates.Add(neighbor);
            }
        }

        if (candidates.Count == 0) return null;
        return candidates[Random.Range(0, candidates.Count)];
    }

    // =====================================================================
    // STEP 2 — FLOOD FILL
    // =====================================================================

    private List<Vector2Int> FloodFillShape(Vector2Int seed, int targetSize, float expandProbability)
    {
        HashSet<Vector2Int> chosen = new HashSet<Vector2Int>();
        List<Vector2Int> frontier = new List<Vector2Int>();

        chosen.Add(seed);
        frontier.Add(seed);

        while (chosen.Count < targetSize && frontier.Count > 0)
        {
            int idx = Random.Range(0, frontier.Count);
            Vector2Int current = frontier[idx];
            frontier.RemoveAt(idx);

            foreach (Vector2Int dir in Directions)
            {
                if (chosen.Count >= targetSize) break;

                Vector2Int neighbor = current + dir;
                if (chosen.Contains(neighbor)) continue;
                if (!IsInBounds(neighbor)) continue;
                if (tileManager.GetTile(neighbor.x, neighbor.y) != null) continue;

                if (Random.value <= expandProbability)
                {
                    chosen.Add(neighbor);
                    frontier.Add(neighbor);
                }
            }
        }

        return new List<Vector2Int>(chosen);
    }

    // =====================================================================
    // STEP 3 — SPAWN TILES WITH SCALED STATS
    // =====================================================================

    private List<Tile> SpawnZoneTiles(List<Vector2Int> positions, int regionID, ZoneProfile profile)
    {
        List<Tile> spawned = new List<Tile>();

        foreach (Vector2Int pos in positions)
        {
            TileStats stats = new TileStats
            {
                nutrientBalance    = Random.Range(profile.nutrientBalanceRange.x,    profile.nutrientBalanceRange.y),
                soilOrganicMatter  = Random.Range(profile.soilOrganicMatterRange.x,  profile.soilOrganicMatterRange.y),
                soilStructure      = Random.Range(profile.soilStructureRange.x,      profile.soilStructureRange.y),
                biologicalActivity = Random.Range(profile.biologicalActivityRange.x, profile.biologicalActivityRange.y),
                waterDynamics      = Random.Range(profile.waterDynamicsRange.x,      profile.waterDynamicsRange.y),
                erosionResistance  = Random.Range(profile.erosionResistanceRange.x,  profile.erosionResistanceRange.y),
                vegetationCover    = Random.Range(profile.vegetationCoverRange.x,    profile.vegetationCoverRange.y),
                contamination      = Random.Range(profile.contaminationRange.x,      profile.contaminationRange.y)
            };

            Tile tile = tileManager.SpawnTile(pos.x, pos.y, stats, regionID);
            if (tile != null)
            {
                tile.issues = new List<TileIssue>();
                tile.tv     = new List<TileOverlayType>();
                tileManager.UpdateTileVisual(tile);
                spawned.Add(tile);
            }
        }

        return spawned;
    }

    // =====================================================================
    // STEP 4 — ISSUE ASSIGNMENT
    // =====================================================================

    private int AssignIssues(List<Tile> tiles, ZoneTheme dominantTheme, ZoneProfile profile)
    {
        float density = Random.Range(profile.issueDensityRange.x, profile.issueDensityRange.y);
        int issueCount = Mathf.RoundToInt(tiles.Count * density);

        List<Tile> shuffled = new List<Tile>(tiles);
        Shuffle(shuffled);

        int assigned = 0;
        for (int i = 0; i < issueCount && i < shuffled.Count; i++)
        {
            Tile tile = shuffled[i];

            ZoneTheme themeToApply = (Random.value <= profile.offThemeIssueProbability)
                ? RollOffTheme(dominantTheme, profile)
                : dominantTheme;

            IssueType issueType = ThemeToIssueType(themeToApply);
            TileIssue issue = TileIssueLibrary.Get(issueType);

            if (issue != null)
            {
                tileManager.ApplyIssue(tile, issue);
                assigned++;
            }
        }

        return assigned;
    }

    // =====================================================================
    // STEP 5 — BUILDING PLACEMENT
    // =====================================================================

    private void PlaceBuildings(List<Tile> tiles, ZoneProfile profile, ref int villagesPlaced, ref int factoriesPlaced)
    {
        List<Tile> shuffled = new List<Tile>(tiles);
        Shuffle(shuffled);

        foreach (Tile tile in shuffled)
        {
            if (tile.entity != null) continue;

            bool villagesDone  = villagesPlaced  >= profile.maxVillages;
            bool factoriesDone = factoriesPlaced >= profile.maxFactories;
            if (villagesDone && factoriesDone) break;

            if (!villagesDone && Random.value <= profile.villageSpawnChance)
            {
                tileManager.SpawnEntity<VillageEntity>(tile);
                villagesPlaced++;
            }
            else if (!factoriesDone && Random.value <= profile.factorySpawnChance)
            {
                tileManager.SpawnEntity<FactoryEntity>(tile);
                factoriesPlaced++;
            }
        }
    }

    // =====================================================================
    // STEP 6 — FORCED ENTITIES (event-driven override)
    // =====================================================================

    private void ApplyForcedEntities(Vector2Int seedPosition, ZoneProfile profile)
    {
        foreach (ForcedEntityPlacement placement in profile.forcedEntities)
        {
            Vector2Int targetPos = seedPosition + placement.offset;
            Tile tile = tileManager.GetTile(targetPos.x, targetPos.y);

            if (tile == null)
            {
                Debug.LogWarning($"ZoneManager: ForcedEntityPlacement at {targetPos} has no tile — skipping.");
                continue;
            }

            switch (placement.entityType)
            {
                case "Village":  tileManager.SpawnEntity<VillageEntity>(tile);  break;
                case "Factory":  tileManager.SpawnEntity<FactoryEntity>(tile);  break;
                case "Fire":     tileManager.SpawnEntity<FireEntity>(tile);     break;
                default:
                    Debug.LogWarning($"ZoneManager: Unknown forced entity type '{placement.entityType}'.");
                    break;
            }
        }
    }

    // =====================================================================
    // THEME ROLLING
    // =====================================================================

    private ZoneTheme RollTheme(ZoneProfile profile)
    {
        int total = profile.loggedTreesWeight
                  + profile.nutrientDepletionWeight
                  + profile.heavyMetalContaminationWeight
                  + profile.activeErosionWeight
                  + profile.drainageCollapseWeight
                  + profile.soilCompactionWeight
                  + profile.chemicalBurnoutWeight;

        if (total <= 0) return ZoneTheme.LoggedTrees;

        int roll = Random.Range(0, total);
        int cumulative = 0;

        if ((cumulative += profile.loggedTreesWeight)             > roll) return ZoneTheme.LoggedTrees;
        if ((cumulative += profile.nutrientDepletionWeight)       > roll) return ZoneTheme.NutrientDepletion;
        if ((cumulative += profile.heavyMetalContaminationWeight) > roll) return ZoneTheme.HeavyMetalContamination;
        if ((cumulative += profile.activeErosionWeight)           > roll) return ZoneTheme.ActiveErosion;
        if ((cumulative += profile.drainageCollapseWeight)        > roll) return ZoneTheme.DrainageCollapse;
        if ((cumulative += profile.soilCompactionWeight)          > roll) return ZoneTheme.SoilCompaction;

        return ZoneTheme.ChemicalBurnout;
    }

    private ZoneTheme RollOffTheme(ZoneTheme exclude, ZoneProfile profile)
    {
        var options = new List<(ZoneTheme theme, int weight)>
        {
            (ZoneTheme.LoggedTrees,             profile.loggedTreesWeight),
            (ZoneTheme.NutrientDepletion,       profile.nutrientDepletionWeight),
            (ZoneTheme.HeavyMetalContamination, profile.heavyMetalContaminationWeight),
            (ZoneTheme.ActiveErosion,           profile.activeErosionWeight),
            (ZoneTheme.DrainageCollapse,        profile.drainageCollapseWeight),
            (ZoneTheme.SoilCompaction,          profile.soilCompactionWeight),
            (ZoneTheme.ChemicalBurnout,         profile.chemicalBurnoutWeight),
        };

        options.RemoveAll(o => o.theme == exclude || o.weight <= 0);
        if (options.Count == 0) return exclude;

        int total = options.Sum(o => o.weight);
        int roll = Random.Range(0, total);
        int cumulative = 0;

        foreach (var (theme, weight) in options)
        {
            cumulative += weight;
            if (cumulative > roll) return theme;
        }

        return options[options.Count - 1].theme;
    }

    private IssueType ThemeToIssueType(ZoneTheme theme)
    {
        switch (theme)
        {
            case ZoneTheme.LoggedTrees:             return IssueType.LoggedTrees;
            case ZoneTheme.NutrientDepletion:       return IssueType.NutrientDepletion;
            case ZoneTheme.HeavyMetalContamination: return IssueType.HeavyMetalContamination;
            case ZoneTheme.ActiveErosion:           return IssueType.ActiveErosion;
            case ZoneTheme.DrainageCollapse:        return IssueType.DrainageCollapse;
            case ZoneTheme.SoilCompaction:          return IssueType.SoilCompaction;
            case ZoneTheme.ChemicalBurnout:         return IssueType.ChemicalBurnout;
            default:                                return IssueType.LoggedTrees;
        }
    }

    // =====================================================================
    // HELPERS
    // =====================================================================

    /// <summary>
    /// Applies issue assignment and building placement to an already-spawned set of tiles.
    /// Used by GameManager.SpawnInitialZone() so Zone 1 goes through the same pipeline as all other zones.
    /// </summary>
    public void InitializeZone(List<Tile> tiles, ZoneProfile profile)
    {
        if (tiles == null || tiles.Count == 0 || profile == null) return;

        ZoneTheme theme = profile.forceTheme ? profile.forcedTheme : RollTheme(profile);
        AssignIssues(tiles, theme, profile);

        int v = 0, f = 0;
        PlaceBuildings(tiles, profile, ref v, ref f);

        if (showDebugInfo)
            Debug.Log($"ZoneManager.InitializeZone: theme={theme}, issues assigned, v={v}, f={f}");
    }
    
    private ZoneProfile GetProfileForNextZone()
    {
        int index = nextRegionID - 2; // Region 2 = index 0
        if (defaultProfiles != null && index < defaultProfiles.Count)
            return defaultProfiles[index];
        return fallbackProfile;
    }

    private bool IsInBounds(Vector2Int pos)
    {
        return pos.x >= 0 && pos.x < tileManager.GridWidth
            && pos.y >= 0 && pos.y < tileManager.GridHeight;
    }

    private Vector2Int CalculateCenter(List<Vector2Int> positions)
    {
        int sumX = 0, sumY = 0;
        foreach (var pos in positions) { sumX += pos.x; sumY += pos.y; }
        return new Vector2Int(sumX / positions.Count, sumY / positions.Count);
    }

    private void PopulateNotableFindings(ZoneGenerationResult result, ZoneTheme theme)
    {
        result.notableFindings.Add($"Dominant issue: {theme}.");

        if (result.villagesPlaced > 0)
            result.notableFindings.Add($"{result.villagesPlaced} village(s) detected — kaingin risk present.");

        if (result.factoriesPlaced > 0)
            result.notableFindings.Add($"{result.factoriesPlaced} factory(ies) detected — contamination spread risk.");

        if (result.contaminationCoverage > 0.3f)
            result.notableFindings.Add("High contamination coverage detected across zone.");
    }

    public float GetTotalAverageHealth()
    {
        List<Tile> allTiles = tileManager.GetAllTiles();
        if (allTiles == null || allTiles.Count == 0) return 0f;

        float total = 0f;
        foreach (Tile tile in allTiles)
            total += tile.CalculateHealth();

        return total / allTiles.Count;
    }
    
    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
