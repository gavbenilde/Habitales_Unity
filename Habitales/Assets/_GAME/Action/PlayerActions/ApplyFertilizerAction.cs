using System.Collections.Generic;
using UnityEngine;

public class ApplyFertilizerAction : PlayerAction
{
    public override ActionCategory Category => ActionCategory.Intervene;

    public override string ActionName => "Apply Fertilizer";
    public override string Description => "Improves soil quality by +20% per tile";
    public override SelectionMode selectionMode => SelectionMode.Adjacent;

    
    // Efficiency parameters
    public override int MinPeoplePerTile => 1;   // 1 person minimum (lighter work)
    public override int BaseDays => 3;            // 3 days at minimum crew
    public override int MinDays => 1;             // Can be done quickly with enough people
    
    // Lower fatigue - less intensive work
    public override float FatigueMultiplierPerTile => 1.5f; // +1.5% per tile (vs 2% default)
    
    private const float SOIL_BOOST = 20f;
    
    public override bool Execute(List<Tile> tiles, TileManager tileManager)
    {
        if (tiles == null || tiles.Count == 0)
        {
            Debug.LogError("ApplyFertilizerAction: No tiles provided!");
            return false;
        }
        
        foreach (Tile tile in tiles)
        {
            if (tile == null) continue;
            
            float oldSoil = tile.stats.soilQuality;
            tileManager.ModifyTileStats(tile, soilDelta: SOIL_BOOST);
            
            Debug.Log($"✓ Applied fertilizer to {tile.gridPosition} | Soil: {oldSoil:F1} → {tile.stats.soilQuality:F1}");
        }
        
        Debug.Log($"✓ Fertilizer applied to {tiles.Count} tiles | +{SOIL_BOOST} soil quality each");
        return true;
    }
    
    public override bool CanExecute(List<Tile> tiles)
    {
        // Optional: Only allow on tiles that actually need soil improvement
        foreach (Tile tile in tiles)
        {
            if (tile.stats.soilQuality >= 90f)
            {
                Debug.LogWarning($"Tile {tile.gridPosition} already has high soil quality ({tile.stats.soilQuality:F1})");
                // Still allow, but warn player they might be wasting resources
            }
        }
        return base.CanExecute(tiles);
    }
}
