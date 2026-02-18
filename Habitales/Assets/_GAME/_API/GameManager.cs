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
    
    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;

    [SerializeField] private HardCode hardCode;
    
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
        
        if (showDebugInfo) {
            Debug.Log($"GameManager initialized | Systems: TileManager={tileManager != null}, TileSelector={tileSelector != null}, ActionUI={actionUI != null}, ActionManager={actionManager != null}, ZoneManager={zoneManager != null}");
        }
    }
    
    
    /// <summary>
    /// Spawns the starting 6x6 zone (Zone 1) with tutorial-friendly stats.
    /// </summary>
    void SpawnInitialZone()
    {
        Debug.Log("🌱 Generating initial Zone 1...");
    
        // Spawn 6x6 grid at origin (36 tiles)
        List<Tile> zoneTiles = tileManager.SpawnTileArea(
            initialZoneOrigin.x, 
            initialZoneOrigin.y, 
            initialZoneSize, 
            initialZoneSize, 
            regionID: 1
        );
    
        if (zoneTiles.Count == 0)
        {
            Debug.LogError("Failed to spawn initial zone!");
            return;
        }
    
        // Set tutorial-friendly stats (74% tiles should have no issues)
        foreach (Tile tile in zoneTiles)
        {
            tile.stats.soilQuality = Random.Range(20f, 35f);
            tile.stats.vegetationCover = Random.Range(10f, 30f);
            tile.stats.contamination = Random.Range(0f, 10f);
            tile.stats.waterPurity = 100f;
            tile.stats.hasFirebreak = false;
        
            // Initialize issues list
            tile.issues = new List<IssueType>();
        
            tileManager.UpdateTileVisual(tile);
        }
        
        hardCode.SpawnRandomEntityInFirstZone();
    
        Debug.Log($"✓ Zone 1 spawned: {zoneTiles.Count} tiles at {initialZoneOrigin}");
    }
    
    void HandleTileSelected(Tile tile, Vector3 worldPosition) {
        if (showDebugInfo) {
            Debug.Log($"Tile selected: {tile.gridPosition} | Health: {tile.CalculateHealth():F1}%");
        }
        
        if (actionUI != null) {
            actionUI.ShowActionsForTile(tile, worldPosition);
        }
    }
    
    void HandleTileDeselected() {
        if (showDebugInfo) {
            Debug.Log("Tile deselected");
        }
        
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
        int regionID = targetTile.regionID;
    
        if (showDebugInfo) 
        {
            Debug.Log($"─── Daily Update for Region {regionID} ───");
        }
    
        // Step 1: Cascade tile stats
        CascadeTileUpdates(regionID);
        
        CheckFirebreakStatus(regionID);
    
        // Step 2: Update all entities MULTIPLE TIMES based on how many days elapsed
        for (int day = 0; day < daysElapsed; day++)
        {
            tileManager.UpdateAllEntities();
        }
    
        // Step 3: Check collapse condition
        CheckCollapseCondition();
    
        // Step 4: Check zone health
        float regionHealth = zoneManager.GetRegionHealth(regionID);
    
        // Step 5: Check unlock
        if (regionHealth >= zoneUnlockThreshold) 
        {
            Debug.Log($"★ NEW ZONE UNLOCKED! ★");
            // zoneManager.GenerateNewZone(regionID);
            
            if (regionID == 1)
                hardCode.GenerateSecondZone();
            else if (regionID == 2)
                hardCode.GenerateThirdZone();
        }
    
        if (showDebugInfo) 
        {
            Debug.Log($"═══ UPDATE COMPLETE ═══\n");
        }
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
    /// Disables firebreaks on tiles where vegetation exceeds 66%.
    /// Called after cascade to check all tiles in the region.
    /// </summary>
    void CheckFirebreakStatus(int regionID)
    {
        List<Tile> regionTiles = tileManager.GetTilesInRegion(regionID);
    
        int disabledCount = 0;

        foreach (Tile tile in regionTiles)
        {
            if (tile.stats.hasFirebreak && tile.stats.vegetationCover > 66f)
            {
                tile.stats.hasFirebreak = false;
                tileManager.UpdateTileVisual(tile);
                disabledCount++;

                if (showDebugInfo)
                    Debug.Log($"Firebreak at {tile.gridPosition} overgrown by vegetation ({tile.stats.vegetationCover:F1}%)");
            }
        }

        if (disabledCount > 0 && showDebugInfo)
            Debug.Log($"Disabled {disabledCount} firebreaks due to vegetation regrowth (>66%)");
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
        {
            Debug.Log($"😴 {count} people fatigued | Return: Day {returnDay}");
        }
    }

    void HandlePeopleRecovered(int count)
    {
        if (showDebugInfo)
        {
            Debug.Log($"✨ {count} people recovered!");
        }
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
    void CascadeTileUpdates(int regionID) {
        List<Tile> regionTiles = tileManager.GetTilesInRegion(regionID);
        
        if (regionTiles.Count == 0) {
            Debug.LogWarning($"No tiles in region {regionID} to cascade!");
            return;
        }
        
        // Run multiple iterations for visible propagation
        for (int iteration = 0; iteration < cascadeIterations; iteration++) {
            // Store new stats separately to avoid order-dependent results
            Dictionary<Tile, TileStats> newStats = new Dictionary<Tile, TileStats>();
            
            foreach (Tile tile in regionTiles) {
                TileStats targetStats = CalculateTargetStats(tile);
                TileStats lerpedStats = LerpStats(tile.stats, targetStats, diffusionRate);
                newStats[tile] = lerpedStats;
            }
            
            // Apply all changes simultaneously
            foreach (var kvp in newStats) {
                Tile tile = kvp.Key;
                TileStats newStat = kvp.Value;
                
                tile.stats.soilQuality = newStat.soilQuality;
                tile.stats.vegetationCover = newStat.vegetationCover;
                tile.stats.contamination = newStat.contamination;
                tile.stats.waterPurity = newStat.waterPurity;
                
                // Update visuals
                tileManager.UpdateTileVisual(tile);
            }
        }
        
        if (showDebugInfo) {
            Debug.Log($"✓ Cascade complete ({cascadeIterations} iterations, {regionTiles.Count} tiles)");
        }
    }
    
    /// <summary>
    /// Calculates target stats by averaging 4-directional neighbors.
    /// </summary>
    TileStats CalculateTargetStats(Tile tile) {
        List<Tile> neighbors = tileManager.GetAdjacentTiles(tile);
        
        if (neighbors.Count == 0) {
            return CloneStats(tile.stats);
        }
        
        float avgSoil = 0f;
        float avgVeg = 0f;
        float avgContam = 0f;
        float avgWater = 0f;
        
        foreach (Tile neighbor in neighbors) {
            avgSoil += neighbor.stats.soilQuality;
            avgVeg += neighbor.stats.vegetationCover;
            avgContam += neighbor.stats.contamination;
            avgWater += neighbor.stats.waterPurity;
        }
        
        int count = neighbors.Count;
        return new TileStats {
            soilQuality = avgSoil / count,
            vegetationCover = avgVeg / count,
            contamination = avgContam / count,
            waterPurity = avgWater / count,
            hasFirebreak = tile.stats.hasFirebreak
        };
    }
    
    /// <summary>
    /// Lerps from current stats toward target stats by diffusionRate.
    /// </summary>
    TileStats LerpStats(TileStats current, TileStats target, float rate) {
        return new TileStats {
            soilQuality = Mathf.Lerp(current.soilQuality, target.soilQuality, rate),
            vegetationCover = Mathf.Lerp(current.vegetationCover, target.vegetationCover, rate),
            contamination = Mathf.Lerp(current.contamination, target.contamination, rate),
            waterPurity = Mathf.Lerp(current.waterPurity, target.waterPurity, rate),
            hasFirebreak = current.hasFirebreak
        };
    }
    
    /// <summary>
    /// Helper to clone TileStats.
    /// </summary>
    TileStats CloneStats(TileStats original) {
        return new TileStats {
            soilQuality = original.soilQuality,
            vegetationCover = original.vegetationCover,
            contamination = original.contamination,
            waterPurity = original.waterPurity,
            hasFirebreak = original.hasFirebreak
        };
    }
    
    [ContextMenu("Test Collapse")]
    void TestCollapse()
    {
        List<Tile> allTiles = tileManager.GetAllTiles();
        int targetCount = Mathf.CeilToInt(allTiles.Count * 0.91f); // 91%
    
        for (int i = 0; i < targetCount; i++)
        {
            // Force tiles to critical health
            allTiles[i].stats.soilQuality = 10f;
            allTiles[i].stats.vegetationCover = 10f;
            allTiles[i].stats.contamination = 90f;
            tileManager.UpdateTileVisual(allTiles[i]);
        }
    
        Debug.Log($"Set {targetCount}/{allTiles.Count} tiles to critical");
        CheckCollapseCondition(); // Manually trigger check
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
