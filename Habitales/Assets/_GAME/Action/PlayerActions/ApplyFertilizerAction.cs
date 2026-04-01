using System.Collections.Generic;
using UnityEngine;

public class ApplyFertilizerAction : PlayerAction
{
    public override ActionCategory Category        => ActionCategory.Intervene;
    public override string ActionName              => "Apply Fertilizer";
    public override string Description             => "Improves soil quality by 20 per tile.";
    public override SelectionMode selectionMode    => SelectionMode.FloodFill;
    public override int MinPeoplePerTile           => 1;
    public override int BaseDays                   => 3;
    public override int MinDays                    => 1;
    public override float FatigueMultiplierPerTile => 1.5f;

    private const float SOIL_BOOST = 20f;

    // Warn if soil is already high, but never block — player's call.
    public override bool CanExecute(List<Tile> tiles)
    {
        foreach (Tile tile in tiles)
            if (tile.stats.soilComposite >= 90f)
                Debug.LogWarning($"Tile {tile.gridPosition} already has high soil ({tile.stats.soilComposite:F1}) — risk of imbalance.");
        return base.CanExecute(tiles);
    }

    public override void ExecuteOnTile(Tile tile, TileManager tileManager)
    {
        if (tile == null) return;
        float oldComposite = tile.stats.soilComposite;
        tileManager.ModifyTileStats(tile, soilDelta: SOIL_BOOST);
        Debug.Log($"Fertilized {tile.gridPosition}. Soil {oldComposite:F1} → {tile.stats.soilComposite:F1}");
    }
}