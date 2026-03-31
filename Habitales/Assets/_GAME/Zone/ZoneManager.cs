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
    public static ZoneManager Instance { get; private set; }
    
    [Header("References")]
    [SerializeField] private TileManager tileManager;
    [SerializeField] private ResourceManager resourceManager;

    [Header("Zone 1 (Initial Zone)")]
    [Tooltip("Profile used exclusively for the first zone. If null, falls back to the random pool.")]
    [SerializeField] private ZoneProfile zone1Profile;
    
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
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        
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
        Vector2Int? seed = FindSeedTile();
        if (seed == null)
        {
            Debug.LogError($"ZoneManager: No valid seed tile found adjacent to Region {triggeringRegionID}.");
            nextRegionID--; // Roll back — generation failed
            return null;
        }

        // Step 2: Flood-fill shape
        int targetSize = Random.Range(profile.sizeRange.x, profile.sizeRange.y + 1);
        List<Vector2Int> positions = FloodFillShape(seed.Value, targetSize, profile.flowFalloff, profile.enclosureBonus);
        
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
        
        PlaceOrganicEntities(zoneTiles, profile);

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

    private Vector2Int? FindSeedTile()
    {
        List<Tile> allTiles = tileManager.GetAllTiles();
        if (allTiles == null || allTiles.Count == 0) return null;

        List<Vector2Int> candidates = new List<Vector2Int>();

        foreach (Tile tile in allTiles)
        {
            foreach (Vector2Int dir in Directions)
            {
                Vector2Int neighbor = tile.gridPosition + dir;
                if (tileManager.GetTile(neighbor.x, neighbor.y) == null)
                    candidates.Add(neighbor);
            }
        }

        if (candidates.Count == 0) return null;
        return candidates[Random.Range(0, candidates.Count)];
    }

    // =====================================================================
    // STEP 2 — FLOOD FILL
    // =====================================================================

    private List<Vector2Int> FloodFillShape(Vector2Int seed, int targetSize, float flowFalloff, float enclosureBonus)
    {
        
        
        var chosen = new HashSet<Vector2Int>();
        // Priority: higher score = picked first. Score = enclosedNeighbors * 10 - dist * flowFalloff
        var candidates = new SortedDictionary<float, List<Vector2Int>>(Comparer<float>.Create((a, b) => b.CompareTo(a)));
        chosen.Add(seed);
        AddCandidates(seed, chosen, candidates, seed, flowFalloff);

        while (chosen.Count < targetSize && candidates.Count > 0)
        {
            // Pull highest-priority candidate
            var topKey = candidates.Keys.First();
            var topList = candidates[topKey];
            int pickIdx = Random.Range(0, topList.Count); // small jag: random among same-score
            Vector2Int selected = topList[pickIdx];
            topList.RemoveAt(pickIdx);
            if (topList.Count == 0) candidates.Remove(topKey);

            chosen.Add(selected);
            AddCandidates(selected, chosen, candidates, seed, flowFalloff);
        }

        // Phase 2 — Jag pass: ~15% chance to extend 1 tile into open frontier for organic edges
        if (chosen.Count >= targetSize)
        {
            var border = chosen
                .SelectMany(p => Directions.Select(d => p + d))
                .Where(p => !chosen.Contains(p) && tileManager.GetTile(p.x, p.y) == null)
                .Distinct().ToList();
            int jagCount = Mathf.RoundToInt(targetSize * 0.15f);
            Shuffle(border);
            for (int i = 0; i < jagCount && i < border.Count; i++)
                chosen.Add(border[i]);
        }
        
        FillEnclosedHoles(chosen, targetSize);

        return new List<Vector2Int>(chosen);
    }

    private void AddCandidates(Vector2Int pos, HashSet<Vector2Int> chosen,
        SortedDictionary<float, List<Vector2Int>> candidates, Vector2Int seed, float flowFalloff)
    {
        foreach (var dir in Directions)
        {
            Vector2Int nb = pos + dir;
            if (chosen.Contains(nb)) continue;
            if (tileManager.GetTile(nb.x, nb.y) != null) continue; // occupied by another zone

            int enclosed = CountChosenNeighbors(nb, chosen);
            float dist = Vector2Int.Distance(nb, seed);
            float score = enclosed * 10f - dist * flowFalloff;

            // Avoid duplicate insertion
            bool alreadyQueued = candidates.Values.Any(list => list.Contains(nb));
            if (alreadyQueued) continue;

            if (!candidates.ContainsKey(score)) candidates[score] = new List<Vector2Int>();
            candidates[score].Add(nb);
        }
    }

    private void AddNeighborsToCandidates(Vector2Int pos,
        HashSet<Vector2Int> chosen, Dictionary<Vector2Int, int> candidates)
    {
        foreach (Vector2Int dir in Directions)
        {
            Vector2Int neighbor = pos + dir;
            if (chosen.Contains(neighbor)) continue;
            if (candidates.ContainsKey(neighbor)) continue;
            if (tileManager.GetTile(neighbor.x, neighbor.y) != null) continue;
            candidates[neighbor] = 1;
        }
    }

    private int CountChosenNeighbors(Vector2Int pos, HashSet<Vector2Int> chosen)
    {
        int count = 0;
        foreach (Vector2Int dir in Directions)
            if (chosen.Contains(pos + dir)) count++;
        return count;
    }

    private Vector2Int WeightedPick(Dictionary<Vector2Int, int> candidates, Vector2Int seed, float flowFalloff, float enclosureBonus)
    {
        float totalWeight = 0f;
        foreach (var kvp in candidates)
        {
            float dist = Vector2Int.Distance(kvp.Key, seed);
            float distWeight = 1f / Mathf.Pow(dist + 1f, flowFalloff);
            float encWeight = enclosureBonus * Mathf.Max(1, kvp.Value);
            totalWeight += distWeight + encWeight;
        }

        float roll = Random.Range(0f, totalWeight);
        float cumulative = 0f;

        foreach (var kvp in candidates)
        {
            float dist = Vector2Int.Distance(kvp.Key, seed);
            float distWeight = 1f / Mathf.Pow(dist + 1f, flowFalloff);
            float encWeight = enclosureBonus * Mathf.Max(1, kvp.Value);
            cumulative += distWeight + encWeight;
            if (cumulative >= roll) return kvp.Key;
        }

        foreach (var kvp in candidates) return kvp.Key;
        return Vector2Int.zero;
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
                case "Seedling":     tileManager.SpawnEntity<SeedlingEntity>(tile);  break;
                case "Sapling":      tileManager.SpawnEntity<SaplingEntity>(tile);   break;
                case "Mature Tree":  tileManager.SpawnEntity<TreeEntity>(tile);      break;
                case "DeadTree":     tileManager.SpawnEntity<DeadTreeEntity>(tile);  break;
                case "Stump":        tileManager.SpawnEntity<StumpEntity>(tile);     break;
                case "Fire":         tileManager.SpawnEntity<FireEntity>(tile);      break;
                case "Village":      tileManager.SpawnEntity<VillageEntity>(tile);   break;
                case "Factory":      tileManager.SpawnEntity<FactoryEntity>(tile);   break;
                case "TrashBio":     tileManager.SpawnEntity<TrashBioEntity>(tile);  break;
                default:
                    Debug.LogWarning($"ZoneManager: Unknown forced entity type '{placement.entityType}'. " +
                                     $"Valid types: Seedling, Sapling, Mature Tree, DeadTree, Stump, Fire, Village, Factory, TrashBio");
                    break;
            }
        }
    }
    
    private void PlaceOrganicEntities(List<Tile> tiles, ZoneProfile profile)
    {
        // Skip entirely if no organic chances are configured — avoid a pointless shuffle
        if (profile.matureTreeSpawnChance == 0f && profile.saplingSpawnChance == 0f &&
            profile.seedlingSpawnChance == 0f && profile.deadTreeSpawnChance == 0f &&
            profile.stumpSpawnChance == 0f && profile.bioTrashSpawnChance == 0f)
            return;

        List<Tile> shuffled = new List<Tile>(tiles);
        Shuffle(shuffled);

        int placed = 0;
        foreach (Tile tile in shuffled)
        {
            if (tile.entity != null) continue; // already has a Village, Factory, etc.

            if (profile.matureTreeSpawnChance > 0f && Random.value < profile.matureTreeSpawnChance)
            { tileManager.SpawnEntity<TreeEntity>(tile); placed++; }
            else if (profile.saplingSpawnChance > 0f && Random.value < profile.saplingSpawnChance)
            { tileManager.SpawnEntity<SaplingEntity>(tile); placed++; }
            else if (profile.seedlingSpawnChance > 0f && Random.value < profile.seedlingSpawnChance)
            { tileManager.SpawnEntity<SeedlingEntity>(tile); placed++; }
            else if (profile.deadTreeSpawnChance > 0f && Random.value < profile.deadTreeSpawnChance)
            { tileManager.SpawnEntity<DeadTreeEntity>(tile); placed++; }
            else if (profile.stumpSpawnChance > 0f && Random.value < profile.stumpSpawnChance)
            { tileManager.SpawnEntity<StumpEntity>(tile); placed++; }
            else if (profile.bioTrashSpawnChance > 0f && Random.value < profile.bioTrashSpawnChance)
            { tileManager.SpawnEntity<TrashBioEntity>(tile); placed++; }
        }

        if (showDebugInfo)
            Debug.Log($"ZoneManager: PlaceOrganicEntities placed {placed} entities across {tiles.Count} tiles.");
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
        PlaceOrganicEntities(tiles, profile);
        if (showDebugInfo)
            Debug.Log($"ZoneManager.InitializeZone: theme={theme}, issues assigned, v={v}, f={f}");
    }
    
    /// <summary>
    /// After flood-fill, finds empty-cell pockets that are fully enclosed by the
    /// new zone (chosen) + any already-existing tiles. Adds enclosed cells to
    /// chosen so the spawned zone has no interior voids.
    /// 
    /// "Enclosed" = the pocket's BFS cannot reach a cell farther than
    /// (targetSize + 10) from the zone centroid without passing through a tile.
    /// 4-directional only.
    /// </summary>
    private void FillEnclosedHoles(HashSet<Vector2Int> chosen, int targetSize)
    {
        // --- Phase A: Collect frontier ---
        // All empty cells directly adjacent to any chosen tile.
        // These are the only possible entry-points for enclosed pockets.
        var frontier = new HashSet<Vector2Int>();
        foreach (Vector2Int pos in chosen)
        {
            foreach (Vector2Int dir in Directions)
            {
                Vector2Int nb = pos + dir;
                if (!chosen.Contains(nb) && tileManager.GetTile(nb.x, nb.y) == null)
                    frontier.Add(nb);
            }
        }

        if (frontier.Count == 0) return;

        // Compute zone centroid for the exterior threshold check.
        float cx = 0f, cy = 0f;
        foreach (Vector2Int pos in chosen) { cx += pos.x; cy += pos.y; }
        cx /= chosen.Count;
        cy /= chosen.Count;

        // Any empty cell farther than this from the centroid is definitionally
        // outside the zone's influence — reaching it means the component is open.
        float exteriorThreshold = targetSize + 10f;

        // --- Phase B: Connected-component BFS on empty space ---
        var visited = new HashSet<Vector2Int>();

        foreach (Vector2Int startCell in frontier)
        {
            if (visited.Contains(startCell)) continue;

            var component = new List<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            bool isExterior = false;

            visited.Add(startCell);
            queue.Enqueue(startCell);

            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();

                // Exterior check: is this cell far enough to be "open world"?
                float dx = cell.x - cx;
                float dy = cell.y - cy;
                if (dx * dx + dy * dy > exteriorThreshold * exteriorThreshold)
                {
                    isExterior = true;
                    // Early exit — no need to map the full exterior component.
                    // Remaining queued cells are already in `visited` so they
                    // won't seed duplicate components.
                    break;
                }

                component.Add(cell);

                foreach (Vector2Int dir in Directions)
                {
                    Vector2Int nb = cell + dir;
                    if (visited.Contains(nb)) continue;          // already seen
                    if (chosen.Contains(nb)) continue;           // wall: new zone tile
                    if (tileManager.GetTile(nb.x, nb.y) != null) continue; // wall: existing zone tile
                    visited.Add(nb);
                    queue.Enqueue(nb);
                }
            }

            // --- Phase C: Fill holes ---
            // Exterior components are open space — leave them alone.
            // Enclosed components are holes — absorb into this zone.
            if (!isExterior)
            {
                foreach (Vector2Int cell in component)
                    chosen.Add(cell);
            }
        }
    }
    
    private ZoneProfile lastUsedProfile = null;

    private ZoneProfile GetProfileForNextZone()
    {
        // Build the candidate pool — fallback is always included as a safety net
        List<ZoneProfile> pool = new List<ZoneProfile>();

        if (defaultProfiles != null)
            foreach (ZoneProfile p in defaultProfiles)
                if (p != null) pool.Add(p);

        if (fallbackProfile != null && !pool.Contains(fallbackProfile))
            pool.Add(fallbackProfile);

        if (pool.Count == 0)
        {
            Debug.LogError("ZoneManager: No profiles available! Assign defaultProfiles or fallbackProfile.");
            return null;
        }

        // With more than one option, remove the last used to prevent back-to-back repeats
        if (pool.Count > 1 && lastUsedProfile != null)
            pool.Remove(lastUsedProfile);

        ZoneProfile selected = pool[Random.Range(0, pool.Count)];
        lastUsedProfile = selected;

        if (showDebugInfo)
            Debug.Log($"ZoneManager: Selected profile '{selected.name}' for Zone {nextRegionID}.");

        return selected;
    }
    
    /// <summary>
    /// Generates Zone 1 using the same flood-fill + full pipeline as GenerateNewZone,
    /// but accepts an explicit seed position instead of searching for one (no tiles
    /// exist yet). Called exclusively by GameManager.SpawnInitialZone.
    /// </summary>
    public ZoneGenerationResult GenerateInitialZone(Vector2Int seed, ZoneProfile overrideProfile = null)
    {
        if (tileManager == null) { Debug.LogError("ZoneManager: TileManager missing!"); return null; }

        ZoneProfile profile = overrideProfile ?? GetProfileForNextZone();
        if (profile == null) { Debug.LogError("ZoneManager: No Zone 1 profile assigned!"); return null; }

        const int regionID = 1;

        // Step 2 — Organic flood-fill shape (identical to GenerateNewZone)
        int targetSize = Random.Range(profile.sizeRange.x, profile.sizeRange.y + 1);
        List<Vector2Int> positions = FloodFillShape(seed, targetSize, profile.flowFalloff, profile.enclosureBonus);
        if (positions.Count == 0)
        {
            Debug.LogError("ZoneManager: GenerateInitialZone flood fill produced no positions!");
            return null;
        }

        // Steps 3–8 are identical to GenerateNewZone
        List<Tile> zoneTiles = SpawnZoneTiles(positions, regionID, profile);

        ZoneTheme dominantTheme = profile.forceTheme ? profile.forcedTheme : RollTheme(profile);
        int issuesAssigned = AssignIssues(zoneTiles, dominantTheme, profile);

        int villagesPlaced = 0, factoriesPlaced = 0;
        PlaceBuildings(zoneTiles, profile, ref villagesPlaced, ref factoriesPlaced);
        PlaceOrganicEntities(zoneTiles, profile);

        if (profile.forceSpecificEntities)
            ApplyForcedEntities(seed, profile);

        if (resourceManager != null && profile.workerReward > 0)
            resourceManager.IncreaseTotalPeople(profile.workerReward);

        float avgHealth = zoneTiles.Average(t => t.CalculateHealth());
        float contamCoverage = (float)zoneTiles.Count(t => t.stats.contamination >= 60f) / zoneTiles.Count;

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
            approximateCenter     = CalculateCenter(positions),
            wasTriggeredByEvent   = false,
            sourceEventID         = null
        };
        PopulateNotableFindings(result, dominantTheme);

        if (showDebugInfo)
            Debug.Log($"Zone 1 (initial) generated — {zoneTiles.Count} tiles | Theme: {dominantTheme} | Issues: {issuesAssigned} | Avg Health: {avgHealth:F1}");

        OnZoneGenerated?.Invoke(result);
        return result;
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
