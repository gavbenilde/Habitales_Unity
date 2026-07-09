using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;
using Habitales.Entities;
using Habitales.Core;

/// <summary>
/// Core manager for tile data and grid logic.
/// Handles tile storage, adjacency, entity spawning, and grid queries.
/// For Gavin's procedural shenanigans
/// </summary>
[DefaultExecutionOrder(-200)] // core service — initializes before consumers (arch §4 init order)
public class TileManager : MonoBehaviour
{
    [Header("Visualization (Optional)")] [SerializeField]
    private GameObject tilePrefab; // 3D tile prefab with TileVisualizer

    [SerializeField] private Transform tileParent; // Parent object for organization
    [SerializeField] private GameObject entityVisualizerPrefab;

    [Header("Entity System (arch §5.4)")]
    [Tooltip("The single entityId → TileEntitySO registry. Drives all data-driven spawning. " +
             "Assign the EntityRegistry asset in the Inspector.")]
    [SerializeField] private EntityRegistry entityRegistry;

    [Header("Entity Spawn Pop-In (cosmetic only — arch S3)")]
    [Tooltip("Duration of the scale-up pop when an entity visual first appears (fresh spawn) or " +
             "is promoted to its next stage (reads as new growth). Purely presentational.")]
    [SerializeField] private float entitySpawnPopDuration = 0.35f;
    [Tooltip("Ease-out-back overshoot amount — how far past the final scale it bounces before " +
             "settling. Keep low for a single clean overshoot-and-settle with no wobble " +
             "(unlike easeOutElastic, back-ease only overshoots once by construction).")]
    [SerializeField] private float entitySpawnPopOvershoot = 1f;

    [Header("Weather Streak Stress")]
    [Tooltip("Per resolved day of an active drought, how much WaterDynamics a fully-vulnerable " +
             "(0 VegetationCover) tile loses, before the severity ramp.")]
    [SerializeField] private float droughtWaterLoss = 1.5f;
    [Tooltip("Per resolved day of an active drought, how much VegetationCover a fully-vulnerable " +
             "tile loses, before the severity ramp.")]
    [SerializeField] private float droughtVegLoss = 0.75f;
    [Tooltip("Per resolved day of an active deluge, how much ErosionResistance a fully-vulnerable " +
             "tile loses, before the severity ramp.")]
    [SerializeField] private float delugeErosionLoss = 1.5f;
    [Tooltip("Per resolved day of an active deluge, how much NutrientBalance a fully-vulnerable " +
             "tile loses (nutrient leaching), before the severity ramp.")]
    [SerializeField] private float delugeLeachLoss = 0.75f;
    [Tooltip("Base per-day chance a plant withers/dies during an active drought, before the " +
             "vegetation-vulnerability skew, severity ramp, and species resistance are applied. " +
             "Lower than the rain chances by design — drought is survivable, rain destroys.")]
    [SerializeField] private float droughtKillChance = 0.05f;
    [Tooltip("Drought wither rolls only begin once the dry spell reaches this many days — short " +
             "droughts stall growth and drain stats; only EXTREME dry spells kill outright.")]
    [SerializeField] private int witherStartDays = 6;
    [Tooltip("Base per-day chance a plant is killed during an active deluge on a Rainy day, " +
             "before the vegetation-vulnerability skew, severity ramp, and species resistance.")]
    [SerializeField] private float rainKillChance = 0.15f;
    [Tooltip("Base per-day chance a plant is killed during an active deluge on a Stormy day, " +
             "before the vegetation-vulnerability skew, severity ramp, and species resistance.")]
    [SerializeField] private float stormKillChance = 0.30f;
    [Tooltip("How much a low VegetationCover skews a tile's vulnerability to weather stress. " +
             "1 = linear; >1 makes barren tiles disproportionately more vulnerable than lush ones.")]
    [SerializeField] private float vulnerabilityPower = 1.5f;
    [Tooltip("Severity growth per day once a streak passes WeatherManager's activation threshold " +
             "(ramp = 1 + rampPerDay * (streak - StreakThreshold)).")]
    [SerializeField] private float rampPerDay = 0.25f;
    [Tooltip("Hard cap on the severity ramp multiplier, so a very long streak doesn't spiral " +
             "into instant tile destruction.")]
    [SerializeField] private float rampCap = 2f;
    [Tooltip("Plant growth STALLS (no progress toward the next stage) once its weather stress — " +
             "spell days past threshold × (1 − VegCover/100) × (1 − species resistance) — reaches this.")]
    [SerializeField] private float growthStallPoint = 0.5f;
    [Tooltip("Plant growth REGRESSES (loses a day of progress per day) once its weather stress " +
             "reaches this. Keep well above the stall point so regression only hits deep spells.")]
    [SerializeField] private float growthRegressPoint = 2f;

    /// <summary>Weather-stress level at which plant growth stalls — RunManager threads this into TickContext (Law 1).</summary>
    public float GrowthStallPoint => growthStallPoint;
    /// <summary>Weather-stress level at which plant growth regresses — RunManager threads this into TickContext (Law 1).</summary>
    public float GrowthRegressPoint => growthRegressPoint;

    private Dictionary<Vector2Int, Tile> tileCache;
    private Dictionary<Tile, GameObject> tileGameObjects; // Links data to GameObjects
    private Dictionary<Tile, VisualEffect> activeVFX = new Dictionary<Tile, VisualEffect>();

    // ── Entity meaning-event seams (arch §6.1 HOOK) ─────────────────────────────
    // Law 2: a NEW entity appearing or an entity DYING is meaning, not mutation. In-place
    // promotion/transform (ReplaceWithSO) is NOT a spawn/death — it does not fire these.
    // No real sink is wired yet (entities ride NullEntityEventSink); these are the
    // ready-to-connect hooks for the story-weaver / chat layer.
    /// <summary>Fires when a fresh entity is placed on a tile. args: tile, entityId.</summary>
    public event System.Action<Tile, string> OnEntitySpawned;
    /// <summary>Fires when an entity is promoted to its next stage via ReplaceWithSO (seedling→sapling→mature).
    /// Distinct from OnEntitySpawned — this is an in-place transform, not a fresh placement. args: tile, newEntityId.</summary>
    public event System.Action<Tile, string> OnEntityEvolved;
    /// <summary>Fires when an entity is removed from a tile (death/cleanup, not replacement). args: tile, entityId, cause.</summary>
    public event System.Action<Tile, string, string> OnEntityDied;

    #region Initialization

    /// <summary>
    /// Singleton — exactly one TileManager per scene. Read anywhere via TileManager.Instance
    /// (Law 1 / S4); mutate tiles only through this manager's own methods.
    /// </summary>
    public static TileManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        InitializeGrid();

        // Loud-fail registry validation at boot (Law 3) — surfaces blank/duplicate entityIds
        // before any spawn tries to look them up.
        if (entityRegistry != null) entityRegistry.ValidateAll();
        else Debug.LogError("TileManager: entityRegistry is not assigned — data-driven entity spawning will fail. Wire the EntityRegistry asset.", this);
    }

    /// <summary>
    /// Creates empty grid structure. Call this before spawning tiles.
    /// </summary>
    public void InitializeGrid()
    {
        tileCache = new Dictionary<Vector2Int, Tile>();
        tileGameObjects = new Dictionary<Tile, GameObject>();
    }

    #endregion
    
    #region Tile Spawning & Creation

    /// <summary>
    /// Spawns a tile at grid position with custom stats.
    /// Returns the created Tile data object.
    /// </summary>
    /// <param name="x">Grid X coordinate</param>
    /// <param name="y">Grid Y coordinate</param>
    /// <param name="stats">Custom TileStats (optional, defaults to random)</param>
    /// <param name="regionID">Zone/region identifier</param>
    public Tile SpawnTile(int x, int y, TileStats stats = null, int regionID = 0)
    {
        Vector2Int pos = new Vector2Int(x, y);

        if (tileCache.ContainsKey(pos))
        {
            Debug.LogWarning($"Tile already exists at ({x}, {y}) — skipping.");
            return tileCache[pos];
        }

        Tile tile = new Tile
        {
            gridPosition = pos,
            stats = stats ?? GenerateDefaultStats(),
            regionID = regionID
        };

        tileCache[tile.gridPosition] = tile;

        if (tilePrefab != null)
            InstantiateTileVisual(tile);

        return tile;
    }

    /// <summary>
    /// Spawns multiple tiles in a rectangular area.
    /// Useful for zone generation.
    /// </summary>
    public List<Tile> SpawnTileArea(int startX, int startY, int width, int height, int regionID = 0)
    {
        List<Tile> spawnedTiles = new List<Tile>();

        for (int x = startX; x < startX + width; x++)
        {
            for (int y = startY; y < startY + height; y++)
            {
                Tile tile = SpawnTile(x, y, null, regionID);
                if (tile != null)
                {
                    spawnedTiles.Add(tile);
                }
            }
        }

        return spawnedTiles;
    }

    /// <summary>
    /// Spawns tiles from a list of grid positions.
    /// Perfect for irregular/organic zone shapes.
    /// </summary>
    public List<Tile> SpawnTilesFromPositions(List<Vector2Int> positions, int regionID = 0)
    {
        List<Tile> spawnedTiles = new List<Tile>();

        foreach (Vector2Int pos in positions)
        {
            Tile tile = SpawnTile(pos.x, pos.y, null, regionID);
            if (tile != null)
            {
                spawnedTiles.Add(tile);
            }
        }

        return spawnedTiles;
    }

    /// <summary>
    /// Generates default random stats for a tile.
    /// Override this or pass custom stats to SpawnTile for procedural variety.
    /// </summary>
    private TileStats GenerateDefaultStats()
    {
        return new TileStats
        {
            nutrientBalance    = Random.Range(10f, 20f),
            soilOrganicMatter  = Random.Range(10f, 20f),
            soilStructure      = Random.Range(10f, 20f),
            biologicalActivity = Random.Range(5f,  15f),
            waterDynamics      = Random.Range(10f, 20f),
            erosionResistance  = Random.Range(5f,  15f),
            vegetationCover    = Random.Range(60f, 70f),
            contamination      = Random.Range(0f,  20f)
        };
    }


    #endregion

    #region Tile Queries & Access

    /// <summary>
    /// Gets tile at grid coordinates. Returns null if out of bounds.
    /// </summary>
    public Tile GetTile(int x, int y)
    {
        tileCache.TryGetValue(new Vector2Int(x, y), out Tile tile);
        return tile; // returns null naturally if not found
    }

    public Tile GetTile(Vector2Int position)
    {
        tileCache.TryGetValue(position, out Tile tile);
        return tile;
    }

    /// <summary>
    /// Gets all tiles in a specific region/zone.
    /// </summary>
    public List<Tile> GetTilesInRegion(int regionID)
    {
        List<Tile> regionTiles = new List<Tile>();

        foreach (Tile tile in tileCache.Values)
        {
            if (tile.regionID == regionID)
            {
                regionTiles.Add(tile);
            }
        }

        return regionTiles;
    }

    /// <summary>
    /// Gets all tiles currently in the grid.
    /// </summary>
    public List<Tile> GetAllTiles()
    {
        return new List<Tile>(tileCache.Values);
    }

    /// <summary>
    /// Gets tiles within a radius of a center position.
    /// </summary>
    public List<Tile> GetTilesInRadius(Vector2Int center, int radius)
    {
        List<Tile> tilesInRadius = new List<Tile>();

        for (int x = -radius; x <= radius; x++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                Tile tile = GetTile(center.x + x, center.y + y);
                if (tile != null)
                {
                    tilesInRadius.Add(tile);
                }
            }
        }

        return tilesInRadius;
    }

    /// <summary>
    /// Gets 4-directional neighbors (N/S/E/W).
    /// Critical for fire spread and adjacency checks.
    /// </summary>
    public List<Tile> GetAdjacentTiles(Tile tile)
    {
        var neighbors = new List<Tile>();
        Vector2Int[] directions =
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        foreach (var dir in directions)
        {
            var neighbor = GetTile(
                tile.gridPosition.x + dir.x,
                tile.gridPosition.y + dir.y
            );
            if (neighbor != null)
            {
                neighbors.Add(neighbor);
            }
        }

        return neighbors;
    }

    /// <summary>
    /// Gets 8-directional neighbors (includes diagonals).
    /// </summary>
    public List<Tile> GetAdjacentTiles8Dir(Tile tile)
    {
        var neighbors = new List<Tile>();

        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0) continue; // Skip center

                Tile neighbor = GetTile(
                    tile.gridPosition.x + x,
                    tile.gridPosition.y + y
                );
                if (neighbor != null)
                {
                    neighbors.Add(neighbor);
                }
            }
        }

        return neighbors;
    }

    /// <summary>
    /// Checks if grid position is valid (within bounds).
    /// </summary>
    public bool HasTileAt(int x, int y)
    {
        return tileCache.ContainsKey(new Vector2Int(x, y));
    }
    
    /// <summary>
    /// Gets the GameObject associated with a tile (for visual updates).
    /// </summary>
    public GameObject GetTileGameObject(Tile tile)
    {
        if (tile == null || !tileGameObjects.ContainsKey(tile))
        {
            return null;
        }
        return tileGameObjects[tile];
    }

    #endregion

    #region Tile Modification

    /// <summary>
    /// Applies an IssueConfig to a tile (for zone generation).
    /// </summary>
    public void ApplyIssue(Tile tile, TileIssue issue)
    {
        if (tile == null || issue == null) return;
        tile.stats.nutrientBalance    *= issue.nutrientMult;
        tile.stats.soilOrganicMatter  *= issue.organicMult;
        tile.stats.soilStructure      *= issue.structureMult;
        tile.stats.biologicalActivity *= issue.biologicalMult;
        tile.stats.waterDynamics      *= issue.waterDynMult;
        tile.stats.erosionResistance  *= issue.erosionMult;
        tile.stats.vegetationCover    *= issue.vegetationMult;
        tile.stats.contamination       = Mathf.Min(100f, tile.stats.contamination + issue.contaminationAdd);
        tile.issues.Add(issue);
        if (tile.stats.contamination > 60f && !tile.tv.Contains(TileOverlayType.Contaminated))
            tile.tv.Add(TileOverlayType.Contaminated);
        UpdateTileVisual(tile);
    }


    /// <summary>
    /// Modifies tile stats directly and updates visual.
    /// </summary>
    public void ModifyTileStats(Tile tile, float soilDelta = 0, float vegDelta = 0, float contamDelta = 0)
    {
        if (tile == null) return;
        if (soilDelta != 0)
        {
            float perStat = soilDelta / 6f;
            tile.stats.nutrientBalance    = Mathf.Clamp(tile.stats.nutrientBalance    + perStat, 0f, 100f);
            tile.stats.soilOrganicMatter  = Mathf.Clamp(tile.stats.soilOrganicMatter  + perStat, 0f, 100f);
            tile.stats.soilStructure      = Mathf.Clamp(tile.stats.soilStructure      + perStat, 0f, 100f);
            tile.stats.biologicalActivity = Mathf.Clamp(tile.stats.biologicalActivity + perStat, 0f, 100f);
            tile.stats.waterDynamics      = Mathf.Clamp(tile.stats.waterDynamics      + perStat, 0f, 100f);
            tile.stats.erosionResistance  = Mathf.Clamp(tile.stats.erosionResistance  + perStat, 0f, 100f);
        }
        tile.stats.vegetationCover = Mathf.Clamp(tile.stats.vegetationCover + vegDelta,   0f, 100f);
        tile.stats.contamination   = Mathf.Clamp(tile.stats.contamination   + contamDelta, 0f, 100f);
        UpdateTileVisual(tile);
    }

    /// <summary>
    /// Exponentially escalating neglect decay — one step per resolved day (called by the heartbeat).
    /// Every tile loses its current <c>decayK</c> from each of the 6 soil substats and vegetation
    /// cover (clamped ≥0), then its <c>decayK</c> grows by <see cref="Tile.DecayGrowth"/> so an
    /// untended tile degrades faster the longer it's ignored. Contamination and the derived
    /// SoilComposite are untouched. Working a tile resets its decay via <see cref="ResetTileDecay"/>.
    /// </summary>
    public void ApplyDailyDecay()
    {
        foreach (Tile tile in tileCache.Values)
        {
            TileStats s = tile.stats;
            float k = tile.decayK;
            s.nutrientBalance    = Mathf.Max(0f, s.nutrientBalance    - k);
            s.soilOrganicMatter  = Mathf.Max(0f, s.soilOrganicMatter  - k);
            s.soilStructure      = Mathf.Max(0f, s.soilStructure      - k);
            s.biologicalActivity = Mathf.Max(0f, s.biologicalActivity - k);
            s.waterDynamics      = Mathf.Max(0f, s.waterDynamics      - k);
            s.erosionResistance  = Mathf.Max(0f, s.erosionResistance  - k);
            s.vegetationCover    = Mathf.Max(0f, s.vegetationCover    - k);

            tile.decayK = k * Tile.DecayGrowth;
        }
    }

    /// <summary>
    /// Weather-streak stress — one step per resolved day (called by the heartbeat, after
    /// ApplyDailyDecay). Once a dry or wet streak reaches its 3rd day (arch: WeatherManager's
    /// IsDroughtActive / IsDelugeActive), the active weather starts actively hurting tiles instead
    /// of just failing to help: DROUGHT drains WaterDynamics + VegetationCover, DELUGE drains
    /// ErosionResistance + NutrientBalance (leaching), both clamped ≥0. Severity ramps the longer
    /// the streak runs (capped). Every tile's vulnerability is skewed by its own VegetationCover —
    /// a fully vegetated tile (100) is immune, a barren tile takes the full hit — so lush tiles
    /// shrug off a dry spell that devastates a bare one. Plants on vulnerable tiles additionally
    /// roll a chance to wither/die outright, removed via the existing RemoveEntity path so the
    /// meaning-event (OnEntityDied) still fires (Law 2). Read-only against WeatherManager (Law 1) —
    /// this method never writes weather state, only reacts to it.
    /// </summary>
    public void ApplyWeatherStress()
    {
        if (WeatherManager.Instance == null) return; // weather optional in test scenes (Law 3 — silent by design here)

        bool drought = WeatherManager.Instance.IsDroughtActive;
        bool deluge  = WeatherManager.Instance.IsDelugeActive;
        if (!drought && !deluge) return;

        int streak = drought ? WeatherManager.Instance.DrySpellDays : WeatherManager.Instance.WetSpellDays;
        float ramp = Mathf.Min(1f + rampPerDay * (streak - WeatherManager.Instance.StreakThreshold), rampCap);

        // Drought kills are gated behind an EXTREME spell (witherStartDays) — short droughts only
        // stall growth and drain stats. Rain has no such grace period: an active deluge kills at once.
        float baseKillChance;
        if (drought)
            baseKillChance = streak >= witherStartDays ? droughtKillChance : 0f;
        else
            baseKillChance = WeatherManager.Instance.CurrentWeather == WeatherState.Stormy ? stormKillChance : rainKillChance;

        int tilesStressed = 0;
        int plantsWithered = 0;

        foreach (Tile tile in tileCache.Values)
        {
            TileStats s = tile.stats;
            float vulnerability = Mathf.Pow(1f - s.vegetationCover / 100f, vulnerabilityPower);

            if (drought)
            {
                s.waterDynamics   = Mathf.Max(0f, s.waterDynamics   - droughtWaterLoss * vulnerability * ramp);
                s.vegetationCover = Mathf.Max(0f, s.vegetationCover - droughtVegLoss   * vulnerability * ramp);
            }
            else
            {
                s.erosionResistance = Mathf.Max(0f, s.erosionResistance - delugeErosionLoss * vulnerability * ramp);
                s.nutrientBalance   = Mathf.Max(0f, s.nutrientBalance   - delugeLeachLoss    * vulnerability * ramp);
            }
            if (vulnerability > 0.001f) tilesStressed++;

            // Plant wither/death roll — the designer's "random number skewed by low Vegetation
            // Coverage that just causes plants to die," further scaled by the species' own
            // resistance (trees shrug off what kills a crop). Only plants roll; other categories
            // (Building, Hazard, Debris) are untouched by this check.
            if (baseKillChance > 0f &&
                tile.entity is Habitales.Entities.GenericTileEntity g && g.def != null &&
                g.def.category == Habitales.Entities.EntityCategory.Plant)
            {
                float resistance = drought ? g.def.droughtResistance : g.def.floodResistance;
                if (UnityEngine.Random.value < baseKillChance * vulnerability * ramp * (1f - resistance))
                {
                    RemoveEntity(tile); // fires OnEntityDied — a plant dying is meaning (Law 2)
                    plantsWithered++;
                }
            }
        }

        if (tilesStressed > 0)
        {
            string label = drought ? "Drought" : "Deluge";
            Debug.Log($"[WeatherStress] {label} day {streak}: {tilesStressed} tiles stressed, {plantsWithered} plants withered.");
        }
    }

    /// <summary>
    /// Resets a tile's neglect decay back to <see cref="Tile.DecayStart"/> — call whenever the
    /// player interacts with (works) the tile, so tending it slows its degradation again (Law 1
    /// write path; callers don't poke <c>tile.decayK</c> directly).
    /// </summary>
    public void ResetTileDecay(Tile tile)
    {
        if (tile == null) return;
        tile.ResetDecay();
    }

    /// <summary>
    /// Owner-side write path for §3.5 StatChange-shaped effects: applies a delta DIRECTLY to one
    /// named stat (clamped 0–100), then refreshes the visual. Actions and other systems mutate tile
    /// stats through here instead of writing <c>tile.stats</c> themselves (Law 1). Distinct from
    /// <see cref="ModifyTileStats"/>, whose <c>soilDelta</c> spreads ÷6 across all soil substats.
    /// <c>SoilComposite</c> is derived/read-only and is rejected loudly (Law 3).
    /// </summary>
    public void ApplyStatChange(Tile tile, StatChange change)
    {
        if (tile == null) return;
        ApplyStatChangeInternal(tile, change);
        UpdateTileVisual(tile);
    }

    /// <summary>Batch overload — applies several StatChanges with a single visual refresh.</summary>
    public void ApplyStatChanges(Tile tile, IReadOnlyList<StatChange> changes)
    {
        if (tile == null || changes == null) return;
        for (int i = 0; i < changes.Count; i++) ApplyStatChangeInternal(tile, changes[i]);
        UpdateTileVisual(tile);
    }

    /// <summary>
    /// Owner-side write path for the examine reveal flags (Law 1): actions reveal a tile's
    /// substat analysis through here instead of writing <c>tile.isAnalyzed</c> themselves.
    /// </summary>
    public void MarkTileAnalyzed(Tile tile)
    {
        if (tile == null) return;
        tile.isAnalyzed = true;
        UpdateTileVisual(tile);
    }

    /// <summary>
    /// Owner-side write path for the examine reveal flags (Law 1): actions reveal a tile's
    /// issue list through here instead of writing <c>tile.issuesRevealed</c> themselves.
    /// </summary>
    public void RevealTileIssues(Tile tile)
    {
        if (tile == null) return;
        tile.issuesRevealed = true;
        UpdateTileVisual(tile);
    }

    private static void ApplyStatChangeInternal(Tile tile, StatChange c)
    {
        var s = tile.stats;
        switch (c.stat)
        {
            case TargetStat.NutrientBalance:    s.nutrientBalance    = Mathf.Clamp(s.nutrientBalance    + c.delta, 0f, 100f); break;
            case TargetStat.SoilOrganicMatter:  s.soilOrganicMatter  = Mathf.Clamp(s.soilOrganicMatter  + c.delta, 0f, 100f); break;
            case TargetStat.SoilStructure:      s.soilStructure      = Mathf.Clamp(s.soilStructure      + c.delta, 0f, 100f); break;
            case TargetStat.BiologicalActivity: s.biologicalActivity = Mathf.Clamp(s.biologicalActivity + c.delta, 0f, 100f); break;
            case TargetStat.WaterDynamics:      s.waterDynamics      = Mathf.Clamp(s.waterDynamics      + c.delta, 0f, 100f); break;
            case TargetStat.ErosionResistance:  s.erosionResistance  = Mathf.Clamp(s.erosionResistance  + c.delta, 0f, 100f); break;
            case TargetStat.VegetationCover:    s.vegetationCover    = Mathf.Clamp(s.vegetationCover    + c.delta, 0f, 100f); break;
            case TargetStat.Contamination:      s.contamination      = Mathf.Clamp(s.contamination      + c.delta, 0f, 100f); break;
            case TargetStat.SoilComposite:
                Debug.LogError("TileManager.ApplyStatChange: SoilComposite is derived/read-only — change ignored.");
                break;
        }
    }


    #endregion

    #region Entity Management

    /// <summary>
    /// Data-driven spawn (new entity system): resolves an entityId through the registry and
    /// spawns a GenericTileEntity from its TileEntitySO. The canonical spawn path post-Phase-4 —
    /// callers pass an EntityIds constant (ids are derived from the SO's displayName, 2026-07-07)
    /// instead of a C# type; the registry also accepts a raw display name (it slugs its input).
    /// </summary>
    public void SpawnById(Tile tile, string entityId)
    {
        if (tile == null) return;
        if (entityRegistry == null)
        {
            Debug.LogError($"TileManager.SpawnById('{entityId}'): entityRegistry not assigned.", this);
            return;
        }
        TileEntitySO def = entityRegistry.Get(entityId); // Get() loud-fails on a miss (Law 3)
        if (def != null) SpawnFromDef(tile, def);
    }

    /// <summary>
    /// Spawns a GenericTileEntity from a TileEntitySO definition directly. Mirrors SpawnEntity&lt;T&gt;'s
    /// fresh-spawn visual/VFX setup (distinct from ReplaceWithSO, which refreshes an existing visual
    /// for in-place promotion/transform).
    /// </summary>
    public void SpawnFromDef(Tile tile, TileEntitySO def)
    {
        if (tile == null || def == null) return;

        // Pre-clear is a replacement, not a death — suppress the death meaning-event (Law 2).
        if (tile.entity != null) RemoveEntity(tile, raiseDeathEvent: false);

        tile.entity = new GenericTileEntity(def);

        if (tileGameObjects.ContainsKey(tile))
        {
            GameObject tileObj = tileGameObjects[tile];
            InstantiateEntityVisual(tile, tileObj);
            InitializeEntityVFX(tile, tileObj);
        }

        OnEntitySpawned?.Invoke(tile, tile.entity.entityId);
    }

    /// <summary>
    /// Data-driven transform/spawn: replaces a tile's entity with a GenericTileEntity built
    /// from a TileEntitySO (new entity system — used for promotion and death-transform).
    /// Mirrors TransformEntity&lt;T&gt;'s visual refresh.
    /// </summary>
    public void ReplaceWithSO(Tile tile, Habitales.Entities.TileEntitySO def)
    {
        if (tile == null || def == null) return;

        tile.entity = new Habitales.Entities.GenericTileEntity(def);

        if (tileGameObjects.ContainsKey(tile))
        {
            GameObject tileObj = tileGameObjects[tile];
            EntityVisualizer visualizer = tileObj.GetComponentInChildren<EntityVisualizer>();
            if (visualizer != null)
            {
                visualizer.SetEntity(tile.entity);

                // Promotion pop (cosmetic only, arch S3) — an in-place stage transform reads as
                // new growth, so it gets the same pop-in as a fresh spawn. Final scale respects
                // the NEW stage's billboardScale (species/stage can resize on promote).
                float billboardScale = def.billboardScale;
                Vector3 finalScale = Vector3.one * billboardScale;
                EntitySpawnTween.PopIn(visualizer.gameObject, finalScale, entitySpawnPopDuration, entitySpawnPopOvershoot);
            }
            // Refresh VFX regardless of visualizer presence — InitializeEntityVFX tears down the
            // outgoing entity's VFX before spawning the new one, so the missing-visualizer path
            // no longer leaks the old effect.
            InitializeEntityVFX(tile, tileObj);
        }

        OnEntityEvolved?.Invoke(tile, tile.entity.entityId);
    }

    /// <summary>
    /// Removes entity from a tile and destroys its visual. Fires OnEntityDied (Law 2 meaning-event)
    /// unless raiseDeathEvent is false — internal replacement paths suppress it. cause is a free-form
    /// tag for subscribers (e.g. "removed", "suppressed", "burned_out").
    /// </summary>
    public void RemoveEntity(Tile tile, bool raiseDeathEvent = true, string cause = "removed")
    {
        if (tile == null || tile.entity == null) return;

        string diedEntityId = tile.entity.entityId;

        // Destroy visual
        if (tileGameObjects.ContainsKey(tile))
        {
            GameObject tileObj = tileGameObjects[tile];
            EntityVisualizer[] visualizers = tileObj.GetComponentsInChildren<EntityVisualizer>();
            foreach (EntityVisualizer v in visualizers)
            {
                if (v != null)
                    Destroy(v.gameObject);
            }
        }

        if (activeVFX.ContainsKey(tile))
        {
            if (activeVFX[tile] != null)
                VFXManager.Instance.DestroyVFX(activeVFX[tile]);

            activeVFX.Remove(tile);
        }

        tile.entity = null;

        if (raiseDeathEvent) OnEntityDied?.Invoke(tile, diedEntityId, cause);
    }

    /// <summary>
    /// Creates the visual GameObject for an entity.
    /// </summary>
    private void InstantiateEntityVisual(Tile tile, GameObject parentTileObj) {
        if (tile.entity == null) return;

        // Instantiate from prefab instead of creating new GameObject
        GameObject entityObj = Instantiate(entityVisualizerPrefab, parentTileObj.transform);
        entityObj.name = $"Entity_{tile.entity.entityId}";
        entityObj.transform.localPosition = Vector3.zero;

        // Final scale respects the SO's billboardScale (arch §5.1 — data-driven per-entity size).
        float billboardScale = tile.entity.def != null ? tile.entity.def.billboardScale : 1f;
        Vector3 finalScale = Vector3.one * billboardScale;
        entityObj.transform.localScale = finalScale;

        // Get existing visualizer component
        EntityVisualizer visualizer = entityObj.GetComponent<EntityVisualizer>();
        visualizer.Initialize(tile.entity, tile);

        // Fresh-spawn pop-in (cosmetic only, arch S3) — genuine new growth appearing on the tile.
        // Never fires from RefreshAllVisuals: that path only touches TileVisualizer, it never
        // reaches EntityVisualizer or this GameObject's scale (see UpdateTileVisual).
        EntitySpawnTween.PopIn(entityObj, finalScale, entitySpawnPopDuration, entitySpawnPopOvershoot);
    }

    private void InitializeEntityVFX(Tile tile, GameObject parent)
    {
        if (tile.entity == null) return;

        // Remove existing VFX if any (important for transform cases)
        if (activeVFX.ContainsKey(tile))
        {
            if (activeVFX[tile] != null)
                VFXManager.Instance.DestroyVFX(activeVFX[tile]);

            activeVFX.Remove(tile);
        }

        // VFX key comes from the SO's explicit vfxKey (arch §5.1). Empty key = no VFX for this entity.
        string key = tile.entity.def != null ? tile.entity.def.vfxKey : null;
        if (string.IsNullOrEmpty(key)) return;

        // Spawn new VFX
        VisualEffect vfx = VFXManager.Instance.SpawnVFX(
            key,
            parent.transform.position
        );

        if (vfx != null)
        {
            activeVFX[tile] = vfx;
        }
    }
    
    public EntityVisualizer GetEntityVisualizer(Tile tile)
    {
        if (!tileGameObjects.ContainsKey(tile)) return null;
        return tileGameObjects[tile].GetComponentInChildren<EntityVisualizer>();
    }

    /// <summary>
    /// Calls OnDailyUpdate for ALL entities in the entire game world.
    /// Use this for global daily updates (fires, villages, factories, etc.)
    /// RunManager assembles the per-day TickContext once and threads it here (arch §5.2).
    /// </summary>
    public void UpdateAllEntities(in Habitales.Entities.TickContext ctx)
    {
        List<Tile> allTiles = GetAllTiles();
        foreach (Tile tile in allTiles)
        {
            if (tile.entity != null)
            {
                tile.entity.OnDailyUpdate(tile, in ctx);
            }
        }
    }

    #endregion

    #region Visualization

    /// <summary>
    /// Creates the 3D GameObject for a tile.
    /// Called automatically by SpawnTile if tilePrefab is set.
    /// </summary>
    private void InstantiateTileVisual(Tile tile)
    {
        Vector3 worldPos = GridToWorldPosition(tile.gridPosition);

        GameObject tileObj = Instantiate(tilePrefab, worldPos, Quaternion.identity, tileParent);
        tileObj.name = $"Tile_{tile.gridPosition.x}_{tile.gridPosition.y}";

        // Initialize visualizer
        TileVisualizer visualizer = tileObj.GetComponent<TileVisualizer>();
        if (visualizer != null)
        {
            visualizer.Initialize(tile);
        }

        tileGameObjects[tile] = tileObj;
    }

    /// <summary>
    /// Updates tile visual after stat changes.
    /// </summary>
    public void UpdateTileVisual(Tile tile)
    {
        if (tile == null || !tileGameObjects.ContainsKey(tile)) return;
        GameObject tileObj = tileGameObjects[tile];
        TileVisualizer visualizer = tileObj.GetComponent<TileVisualizer>();
        if (visualizer == null) return;
        visualizer.UpdateVisuals();
        visualizer.UpdateOverlays(tile.tv);
    }

    /// <summary>
    /// Bulk visual sync — refreshes every tile's visuals + overlays in one pass.
    /// Called once per day by the heartbeat (arch §2.1 step 6) AFTER all data has settled
    /// (entities ticked, cascade applied). Cheaper and more correct than per-mutation
    /// UpdateTileVisual during cascade.
    /// </summary>
    public void RefreshAllVisuals()
    {
        foreach (Tile tile in tileCache.Values)
            UpdateTileVisual(tile);
    }

    /// <summary>
    /// Converts grid coordinates to world position.
    /// Adjust this based on your 3D tilemap setup.
    /// </summary>
    public Vector3 GridToWorldPosition(Vector2Int gridPos)
    {
        // Based on your description: {0.5, 0.5} offset from origin
        // Adjust Y position as needed (0 for flat ground, or custom height)
        return new Vector3(
            gridPos.x + 0.5f,
            0f, // Ground level - adjust if tiles have height variation
            gridPos.y + 0.5f
        );
    }

    #endregion

    #region Properties
    
    public Vector2Int WorldMin
    {
        get
        {
            int minX = int.MaxValue, minY = int.MaxValue;
            foreach (var pos in tileCache.Keys)
            {
                if (pos.x < minX) minX = pos.x;
                if (pos.y < minY) minY = pos.y;
            }
            return tileCache.Count == 0 ? Vector2Int.zero : new Vector2Int(minX, minY);
        }
    }

    public Vector2Int WorldMax
    {
        get
        {
            int maxX = int.MinValue, maxY = int.MinValue;
            foreach (var pos in tileCache.Keys)
            {
                if (pos.x > maxX) maxX = pos.x;
                if (pos.y > maxY) maxY = pos.y;
            }
            return tileCache.Count == 0 ? Vector2Int.zero : new Vector2Int(maxX, maxY);
        }
    }


    #endregion
}