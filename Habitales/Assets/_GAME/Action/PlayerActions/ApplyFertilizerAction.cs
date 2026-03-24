using System.Collections.Generic;
using UnityEngine;

public class ApplyFertilizerAction : PlayerAction
{
    public override ActionCategory Category => ActionCategory.Intervene;
    public override string ActionName => "Apply Fertilizer";
    public override string Description => "Improves soil quality by 20 per tile.";
    public override SelectionMode selectionMode => SelectionMode.FloodFill;
    public override int MinPeoplePerTile => 1;
    public override int BaseDays => 3;
    public override int MinDays => 1;
    public override float FatigueMultiplierPerTile => 1.5f;

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
            float oldComposite = tile.stats.soilComposite;
            tileManager.ModifyTileStats(tile, soilDelta: SOIL_BOOST);
            Debug.Log($"Applied fertilizer to {tile.gridPosition}. Soil {oldComposite:F1} → {tile.stats.soilComposite:F1}");
        }

        Debug.Log($"Fertilizer applied to {tiles.Count} tiles, +{SOIL_BOOST} soil each.");
        return true;
    }

    public override bool CanExecute(List<Tile> tiles)
    {
        foreach (Tile tile in tiles)
            if (tile.stats.soilComposite >= 90f)
            {
                Debug.LogWarning($"Tile {tile.gridPosition} already has high soil quality ({tile.stats.soilComposite:F1}).");
                return base.CanExecute(tiles);
            }
        return base.CanExecute(tiles);
    }
}