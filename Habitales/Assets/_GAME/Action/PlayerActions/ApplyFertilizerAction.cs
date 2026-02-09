using UnityEngine;

public class ApplyFertilizerAction : PlayerAction {
    public override string ActionName => "Apply Fertilizer";
    public override string Description => "Improves soil quality by +20%";
    
    private const float SOIL_BOOST = 20f;
    
    public override bool Execute(Tile targetTile, TileManager tileManager) {
        if (targetTile == null) {
            Debug.LogError("Cannot apply fertilizer to null tile!");
            return false;
        }
        
        // Apply direct soil boost
        float oldSoil = targetTile.stats.soilQuality;
        tileManager.ModifyTileStats(targetTile, soilDelta: SOIL_BOOST);
        
        Debug.Log($"✓ Applied fertilizer to {targetTile.gridPosition} | Soil: {oldSoil:F1} → {targetTile.stats.soilQuality:F1}");
        
        return true;
    }
}