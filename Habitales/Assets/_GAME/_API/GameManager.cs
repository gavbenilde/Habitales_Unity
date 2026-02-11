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
    
    [Header("Cascade Settings")]
    [SerializeField] [Range(0.05f, 0.5f)] private float diffusionRate = 0.15f;
    [SerializeField] [Tooltip("How many cascade iterations per action")]
    private int cascadeIterations = 3;
    
    [Header("Zone Progression")]
    [SerializeField] [Range(0f, 100f)] private float zoneUnlockThreshold = 80f;
    
    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;
    
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
    void HandleActionCompleted(Tile targetTile) {
        if (targetTile == null) return;
    
        int regionID = targetTile.regionID;
    
        if (showDebugInfo) {
            Debug.Log($"─── Daily Update for Region {regionID} ───");
        }
    
        // Step 1: Cascade tile stats
        CascadeTileUpdates(regionID);
    
        // Step 2: Update all entities (NEW!) ⭐
        tileManager.UpdateEntitiesInRegion(regionID);
    
        // Step 3: Check zone health
        float regionHealth = zoneManager.GetRegionHealth(regionID);
    
        // Step 4: Check unlock
        if (regionHealth >= zoneUnlockThreshold) {
            Debug.Log($"<color=green>★ NEW ZONE UNLOCKED! ★</color>");
            zoneManager.GenerateNewZone(regionID);
        }
    
        if (showDebugInfo) {
            Debug.Log($"═══ UPDATE COMPLETE ═══\n");
        }
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
    
    void OnDestroy() {
        if (tileSelector != null) {
            tileSelector.OnTileSelected -= HandleTileSelected;
            tileSelector.OnTileDeselected -= HandleTileDeselected;
        }
        
        if (actionManager != null) {
            actionManager.OnActionCompleted -= HandleActionCompleted;
        }
    }
    
    // Public properties
    public TileManager TileManager => tileManager;
    public float DiffusionRate => diffusionRate;
    public int CascadeIterations => cascadeIterations;
}
