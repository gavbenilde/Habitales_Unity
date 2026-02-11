using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages zones: health calculation, generation, and metadata.
/// </summary>
public class ZoneManager : MonoBehaviour {
    [Header("References")]
    [SerializeField] private TileManager tileManager;
    
    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;
    
    void Awake() {
        if (tileManager == null) {
            tileManager = FindObjectOfType<TileManager>();
            if (tileManager == null) {
                Debug.LogError("ZoneManager requires TileManager in scene!");
            }
        }
    }
    
    /// <summary>
    /// Calculates average health of all tiles in a region.
    /// Returns 0-100 percentage.
    /// </summary>
    public float GetRegionHealth(int regionID) {
        List<Tile> regionTiles = tileManager.GetTilesInRegion(regionID);
        
        if (regionTiles.Count == 0) {
            Debug.LogWarning($"No tiles found in region {regionID}!");
            return 0f;
        }
        
        float totalHealth = 0f;
        foreach (Tile tile in regionTiles) {
            totalHealth += tile.CalculateHealth();
        }
        
        float avgHealth = totalHealth / regionTiles.Count;
        
        if (showDebugInfo) {
            Debug.Log($"Region {regionID} Health: {avgHealth:F1}% ({regionTiles.Count} tiles)");
        }
        
        return avgHealth;
    }
    
    /// <summary>
    /// STUB: Generates a new zone adjacent to existing region.
    /// TODO: Implement full generation with flood-fill, issues, buildings, etc.
    /// </summary>
    public void GenerateNewZone(int adjacentToRegion) {
        Debug.Log($"<color=yellow>TODO: Generate new zone adjacent to region {adjacentToRegion}</color>");
        
        // Must implement:
        // 1. Flood-fill organic shape
        // 2. Apply weighted issues
        // 3. Place buildings (Villages/Factories)
        // 4. Set difficulty-based stats
    }
}

