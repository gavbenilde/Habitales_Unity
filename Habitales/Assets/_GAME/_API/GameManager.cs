using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central game loop coordinator.  
/// Handles: Action execution → Tile cascading → Zone health checks → Zone generation triggers
/// </summary>
public class GameManager : MonoBehaviour {
    [Header("System References")]
    [SerializeField] private TileManager tileManager;
    [SerializeField] private TileSelector tileSelector;
    [SerializeField] private ActionUI actionUI;
    [SerializeField] private ActionManager actionManager;
    [SerializeField] private ZoneManager zoneManager;
    [SerializeField] private ResourceManager resourceManager;
    [SerializeField] private RegionOutlineRenderer regionOutlineRenderer;

    
    [Header("Cascade Settings")]
    [SerializeField] [Range(0.05f, 0.5f)] private float diffusionRate = 0.15f;
    [SerializeField] [Tooltip("How many cascade iterations per action")]
    private int cascadeIterations = 3;
    
    [Header("Zone Progression")]
    [SerializeField] [Range(0f, 100f)] private float zoneUnlockThreshold = 80f;
    
    [Header("Victory/Loss Conditions")]
    [SerializeField] private float collapseThreshold = 90f; // Percentage of critical tiles
    [SerializeField] private float criticalHealthThreshold = 33f; // Critical state
    [SerializeField] private float thrivingHealthThreshold = 67f; // Thriving state
    private bool isGameOver = false;

    
    [Header("Initial Zone Setup")]
    [SerializeField] private bool spawnInitialZone = true;
    [SerializeField] private Vector2Int initialZoneOrigin = Vector2Int.zero;
    [SerializeField] private int initialZoneSize = 6; // 6x6 grid
    [SerializeField] private ZoneProfile zone1Profile;
    
    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;
    
    
    
    private HashSet<int> unlockedRegions = new HashSet<int>();
    
    
    public static GameManager Instance { get; private set; }
    
    void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    
    void Start() {
        InitializeSystems();
        
        if (spawnInitialZone && tileManager != null)
        {
            SpawnInitialZone();
        }
    }
    
    void InitializeSystems() {
        // Auto-find systems if not assigned
        if (tileManager == null) {
            tileManager = FindObjectOfType<TileManager>();
            if (tileManager == null) Debug.LogError("TileManager not found!");
        }
        
        if (tileSelector == null) {
            tileSelector = GetComponent<TileSelector>();
            if (tileSelector == null) Debug.LogError("TileSelector missing!");
        }
        
        if (actionUI == null) {
            actionUI = FindObjectOfType<ActionUI>();
            if (actionUI == null) Debug.LogError("ActionUI not found!");
        }
        
        if (actionManager == null) {
            actionManager = FindObjectOfType<ActionManager>();
            if (actionManager == null) Debug.LogError("ActionManager not found!");
        }
        
        if (zoneManager == null) {
            zoneManager = FindObjectOfType<ZoneManager>();
            if (zoneManager == null) Debug.LogError("ZoneManager not found!");
        }
        
        if (resourceManager == null)
        {
            resourceManager = FindObjectOfType<ResourceManager>();
            if (resourceManager == null) Debug.LogError("ResourceManager not found!");
        }
    
        // Subscribe to resource events (optional but useful)
        if (resourceManager != null)
        {
            resourceManager.OnTimeAdvanced += HandleTimeAdvanced;
            resourceManager.OnPeopleFatigued += HandlePeopleFatigued;
            resourceManager.OnPeopleRecovered += HandlePeopleRecovered;
            resourceManager.OnGameOver += HandleGameOver;
        }
        
        // Connect events
        if (tileSelector != null) {
            tileSelector.OnTileSelected += HandleTileSelected;
            tileSelector.OnTileDeselected += HandleTileDeselected;
        }
        
        if (actionManager != null) {
            actionManager.OnActionCompleted += HandleActionCompleted;
        }
        
        if (regionOutlineRenderer == null)
            regionOutlineRenderer = FindObjectOfType<RegionOutlineRenderer>();
        if (regionOutlineRenderer == null)
            Debug.LogError("RegionOutlineRenderer not found!");

        
        if (showDebugInfo) {
            Debug.Log($"GameManager initialized | Systems: TileManager={tileManager != null}, TileSelector={tileSelector != null}, ActionUI={actionUI != null}, ActionManager={actionManager != null}, ZoneManager={zoneManager != null}");
        }
    }
    
    
    /// <summary>
    /// Spawns the starting 6x6 zone (Zone 1) with tutorial-friendly stats.
    /// </summary>
    void SpawnInitialZone()
{
    Debug.Log("Generating initial Zone 1...");

    List<Tile> zoneTiles = tileManager.SpawnTileArea(
        initialZoneOrigin.x, initialZoneOrigin.y,
        initialZoneSize, initialZoneSize,
        regionID: 1
    );

    if (zoneTiles.Count == 0)
    {
        Debug.LogError("Failed to spawn initial zone!");
        return;
    }

    // Apply Zone 1 profile stats if assigned, otherwise fall back to hardcoded defaults
    foreach (Tile tile in zoneTiles)
    {
        if (zone1Profile != null)
        {
            tile.stats.nutrientBalance    = Random.Range(zone1Profile.nutrientBalanceRange.x,    zone1Profile.nutrientBalanceRange.y);
            tile.stats.soilOrganicMatter  = Random.Range(zone1Profile.soilOrganicMatterRange.x,  zone1Profile.soilOrganicMatterRange.y);
            tile.stats.soilStructure      = Random.Range(zone1Profile.soilStructureRange.x,      zone1Profile.soilStructureRange.y);
            tile.stats.biologicalActivity = Random.Range(zone1Profile.biologicalActivityRange.x, zone1Profile.biologicalActivityRange.y);
            tile.stats.waterDynamics      = Random.Range(zone1Profile.waterDynamicsRange.x,      zone1Profile.waterDynamicsRange.y);
            tile.stats.erosionResistance  = Random.Range(zone1Profile.erosionResistanceRange.x,  zone1Profile.erosionResistanceRange.y);
            tile.stats.vegetationCover    = Random.Range(zone1Profile.vegetationCoverRange.x,    zone1Profile.vegetationCoverRange.y);
            tile.stats.contamination      = Random.Range(zone1Profile.contaminationRange.x,      zone1Profile.contaminationRange.y);
        }
        else
        {
            // Fallback defaults — friendlier than later zones
            tile.stats.nutrientBalance    = Random.Range(20f, 35f);
            tile.stats.soilOrganicMatter  = Random.Range(15f, 30f);
            tile.stats.soilStructure      = Random.Range(20f, 35f);
            tile.stats.biologicalActivity = Random.Range(10f, 25f);
            tile.stats.waterDynamics      = Random.Range(20f, 35f);
            tile.stats.erosionResistance  = Random.Range(15f, 30f);
            tile.stats.vegetationCover    = Random.Range(10f, 30f);
            tile.stats.contamination      = Random.Range(0f, 10f);
        }

        tile.issues = new List<TileIssue>();
        tile.tv     = new List<TileOverlayType>();
        tileManager.UpdateTileVisual(tile);
    }

    // Building + issue placement via profile (same pipeline as all other zones)
    if (zone1Profile != null)
        zoneManager.InitializeZone(zoneTiles, zone1Profile);


    Debug.Log($"Zone 1 spawned — {zoneTiles.Count} tiles at {initialZoneOrigin}");
}

    
    void HandleTileSelected(Tile tile, Vector3 worldPosition) {
        if (showDebugInfo) {
            Debug.Log($"Tile selected: {tile.gridPosition} | Health: {tile.CalculateHealth():F1}%");
        }
        
        regionOutlineRenderer?.ActivateRegion(tile.regionID); 
        
        if (actionUI != null) {
            actionUI.ShowActionsForTile(tile, worldPosition);
        }
    }
    
    void HandleTileDeselected() {
        if (showDebugInfo) {
            Debug.Log("Tile deselected");
        }
        
        regionOutlineRenderer?.ClearRegion();
        
        if (actionUI != null) {
            actionUI.HideActions();
        }
    }
    
    /// <summary>
    /// CORE GAME LOOP: Called after any action completes.
    /// 1. Cascade tile updates in the region
    /// 2. Check zone health
    /// 3. Trigger zone generation if threshold met
    /// </summary>
    void HandleActionCompleted(Tile targetTile, int daysElapsed)
    {
        if (targetTile == null || isGameOver) return;

        if (showDebugInfo)
            Debug.Log($"─── Action Complete — Updating World ───");

        // Step 1: Cascade ALL tiles across ALL regions
        CascadeTileUpdates();

        // Step 3: Update all entities for every day elapsed
        for (int day = 0; day < daysElapsed; day++)
            tileManager.UpdateAllEntities();

        // Step 4: Refresh visuals for ALL tiles
        foreach (Tile tile in tileManager.GetAllTiles())
            tileManager.UpdateTileVisual(tile);

        // Step 5: Check collapse condition
        CheckCollapseCondition();

        // Step 6: Check zone unlock against TOTAL average health across all zones
        float totalAverageHealth = zoneManager.GetTotalAverageHealth();

        if (showDebugInfo)
            Debug.Log($"Total World Health: {totalAverageHealth:F1} / Unlock Threshold: {zoneUnlockThreshold}");

        if (totalAverageHealth >= zoneUnlockThreshold && !unlockedRegions.Contains(zoneManager.NextRegionID - 1))
        {
            int newRegionFrom = zoneManager.NextRegionID - 1;
            unlockedRegions.Add(newRegionFrom);
            Debug.Log($"NEW ZONE UNLOCKED! World avg health {totalAverageHealth:F1} passed threshold.");
            zoneManager.GenerateNewZone(newRegionFrom);
        }

        if (showDebugInfo)
            Debug.Log($"═══ UPDATE COMPLETE ═══\n");
    }


    
    /// <summary>
    /// Checks if ecosystem collapse has occurred (≥90% tiles critical).
    /// Called after each action completes.
    /// </summary>
    void CheckCollapseCondition()
    {
        if (isGameOver) return;
    
        List<Tile> allTiles = tileManager.GetAllTiles();
        if (allTiles.Count == 0) return;
    
        // Count tiles with health ≤33%
        int criticalCount = 0;
        foreach (Tile tile in allTiles)
        {
            if (tile.CalculateHealth() <= criticalHealthThreshold)
            {
                criticalCount++;
            }
        }
    
        // Calculate percentage
        float criticalPercent = ((float)criticalCount / allTiles.Count) * 100f;
    
        if (showDebugInfo)
        {
            Debug.Log($"Critical tiles: {criticalCount}/{allTiles.Count} ({criticalPercent:F1}%)");
        }
    
        // Trigger collapse if threshold exceeded
        if (criticalPercent >= collapseThreshold)
        {
            int thrivingTiles = GetThrivingTileCount();
            TriggerGameOver($"Ecosystem Collapse", thrivingTiles);
        }
    }

    /// <summary>
    /// Counts tiles with health ≥67% (Thriving state).
    /// Used for final score calculation.
    /// </summary>
    int GetThrivingTileCount()
    {
        List<Tile> allTiles = tileManager.GetAllTiles();
        int thrivingCount = 0;
    
        foreach (Tile tile in allTiles)
        {
            if (tile.CalculateHealth() >= thrivingHealthThreshold)
            {
                thrivingCount++;
            }
        }
        return thrivingCount;
    }




    /// <summary>
    /// Triggers game over with reason and final score.
    /// </summary>
    void TriggerGameOver(string reason, int thrivingTiles)
    {
        if (isGameOver) return; // Prevent double-trigger
    
        isGameOver = true;
        Debug.Log($"🏁 GAME OVER: {reason} | Thriving Tiles: {thrivingTiles}");
    
        // TODO: Show end screen UI with score
    }

    
    
    void HandleTimeAdvanced(int days)
    {
        if (showDebugInfo)
        {
            Debug.Log($"⏰ Time advanced by {days} days | Now: {resourceManager.GetFullTimeDisplay()}");
        }
    }

    void HandlePeopleFatigued(int count, int returnDay)
    {
        if (showDebugInfo)
            Debug.Log($"{count} worker(s) fatigued. Return by day {returnDay}.");
    }

    void HandlePeopleRecovered(int count)
    {
        if (showDebugInfo)
            Debug.Log($"{count} worker(s) recovered! Available: {resourceManager.AvailablePeople}/{resourceManager.TotalPeople}");
    }

    void HandleGameOver()
    {
        if (isGameOver) return; // Already handled by collapse
    
        isGameOver = true;
        int thrivingTiles = GetThrivingTileCount();
    
        Debug.Log($"🏁 GAME OVER: Year Complete! | Final time: {resourceManager.GetFullTimeDisplay()} | Thriving tiles: {thrivingTiles}");
    
        // TODO: Show end screen UI with score
    }


    
    
    
    /// <summary>
    /// Cascades tile stat changes across a region using diffusion.
    /// Each tile lerps toward the average of its 4 neighbors.
    /// </summary>
    void CascadeTileUpdates()
    {
        List<Tile> allTiles = tileManager.GetAllTiles();
        if (allTiles.Count == 0) return;

        for (int iteration = 0; iteration < cascadeIterations; iteration++)
        {
            Dictionary<Tile, TileStats> newStats = new Dictionary<Tile, TileStats>();

            foreach (Tile tile in allTiles)
            {
                TileStats targetStats = CalculateTargetStats(tile);
                newStats[tile] = LerpStats(tile.stats, targetStats, diffusionRate);
            }

            foreach (var kvp in newStats)
            {
                Tile tile = kvp.Key;
                TileStats ns = kvp.Value;
                tile.stats.nutrientBalance    = ns.nutrientBalance;
                tile.stats.soilOrganicMatter  = ns.soilOrganicMatter;
                tile.stats.soilStructure      = ns.soilStructure;
                tile.stats.biologicalActivity = ns.biologicalActivity;
                tile.stats.waterDynamics      = ns.waterDynamics;
                tile.stats.erosionResistance  = ns.erosionResistance;
                tile.stats.vegetationCover    = ns.vegetationCover;
                tile.stats.contamination      = ns.contamination;

                // Sync contamination overlay
                if (tile.stats.contamination > 60f && !tile.tv.Contains(TileOverlayType.Contaminated))
                    tile.tv.Add(TileOverlayType.Contaminated);
                else if (tile.stats.contamination <= 60f && tile.tv.Contains(TileOverlayType.Contaminated))
                    tile.tv.Remove(TileOverlayType.Contaminated);
            }
        }

        if (showDebugInfo)
            Debug.Log($"Cascade complete — {cascadeIterations} iterations, {allTiles.Count} tiles.");
    }

    
    /// <summary>
    /// Calculates target stats by averaging 4-directional neighbors.
    /// </summary>
    TileStats CalculateTargetStats(Tile tile)
    {
        List<Tile> neighbors = tileManager.GetAdjacentTiles(tile);
        if (neighbors.Count == 0) return CloneStats(tile.stats);

        float n = 0, o = 0, s = 0, b = 0, w = 0, e = 0, v = 0, c = 0;
        foreach (Tile nb in neighbors)
        {
            n += nb.stats.nutrientBalance;    o += nb.stats.soilOrganicMatter;
            s += nb.stats.soilStructure;      b += nb.stats.biologicalActivity;
            w += nb.stats.waterDynamics;      e += nb.stats.erosionResistance;
            v += nb.stats.vegetationCover;    c += nb.stats.contamination;
        }
        int count = neighbors.Count;
        return new TileStats {
            nutrientBalance    = n / count, soilOrganicMatter  = o / count,
            soilStructure      = s / count, biologicalActivity = b / count,
            waterDynamics      = w / count, erosionResistance  = e / count,
            vegetationCover    = v / count, contamination      = c / count
        };
    }

    
    /// <summary>
    /// Lerps from current stats toward target stats by diffusionRate.
    /// </summary>
    TileStats LerpStats(TileStats cur, TileStats tgt, float rate) => new TileStats
    {
        nutrientBalance    = Mathf.Lerp(cur.nutrientBalance,    tgt.nutrientBalance,    rate),
        soilOrganicMatter  = Mathf.Lerp(cur.soilOrganicMatter,  tgt.soilOrganicMatter,  rate),
        soilStructure      = Mathf.Lerp(cur.soilStructure,      tgt.soilStructure,      rate),
        biologicalActivity = Mathf.Lerp(cur.biologicalActivity, tgt.biologicalActivity, rate),
        waterDynamics      = Mathf.Lerp(cur.waterDynamics,      tgt.waterDynamics,      rate),
        erosionResistance  = Mathf.Lerp(cur.erosionResistance,  tgt.erosionResistance,  rate),
        vegetationCover    = Mathf.Lerp(cur.vegetationCover,    tgt.vegetationCover,    rate),
        contamination      = Mathf.Lerp(cur.contamination,      tgt.contamination,      rate)
    };

    
    /// <summary>
    /// Helper to clone TileStats.
    /// </summary>
    TileStats CloneStats(TileStats o) => new TileStats
    {
        nutrientBalance    = o.nutrientBalance,   soilOrganicMatter  = o.soilOrganicMatter,
        soilStructure      = o.soilStructure,     biologicalActivity = o.biologicalActivity,
        waterDynamics      = o.waterDynamics,     erosionResistance  = o.erosionResistance,
        vegetationCover    = o.vegetationCover,   contamination      = o.contamination
    };

    
    [ContextMenu("Test Collapse")]
    void TestCollapse()
    {
        List<Tile> allTiles = tileManager.GetAllTiles();
        int targetCount = Mathf.CeilToInt(allTiles.Count * 0.91f);
        for (int i = 0; i < targetCount; i++)
        {
            allTiles[i].stats.nutrientBalance    = 10f; allTiles[i].stats.soilOrganicMatter  = 10f;
            allTiles[i].stats.soilStructure      = 10f; allTiles[i].stats.biologicalActivity = 10f;
            allTiles[i].stats.waterDynamics      = 10f; allTiles[i].stats.erosionResistance  = 10f;
            allTiles[i].stats.vegetationCover    = 10f; allTiles[i].stats.contamination      = 90f;
            tileManager.UpdateTileVisual(allTiles[i]);
        }
        Debug.Log($"Set {targetCount}/{allTiles.Count} tiles to critical");
        CheckCollapseCondition();
    }
    
    [ContextMenu("Test Year Complete")]
    void TestYearComplete()
    {
        // Fast-forward to day 364
        int daysToAdvance = 364 - resourceManager.TotalDays;
        if (daysToAdvance > 0)
        {
            resourceManager.AdvanceTime(daysToAdvance);
        }
    }
    
    void OnDestroy() {
        if (tileSelector != null) {
            tileSelector.OnTileSelected -= HandleTileSelected;
            tileSelector.OnTileDeselected -= HandleTileDeselected;
        }
        
        if (actionManager != null) {
            actionManager.OnActionCompleted -= HandleActionCompleted;
        }
        
        if (resourceManager != null)
        {
            resourceManager.OnTimeAdvanced -= HandleTimeAdvanced;
            resourceManager.OnPeopleFatigued -= HandlePeopleFatigued;
            resourceManager.OnPeopleRecovered -= HandlePeopleRecovered;
            resourceManager.OnGameOver -= HandleGameOver;
        }
    }
    
    
    // Public properties
    public TileManager TileManager => tileManager;
    public float DiffusionRate => diffusionRate;
    public int CascadeIterations => cascadeIterations;
}
