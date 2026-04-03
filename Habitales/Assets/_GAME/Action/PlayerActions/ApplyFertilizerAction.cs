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

    public override bool CanExecute(List<Tile> tiles)
    {
        foreach (Tile tile in tiles)
            if (tile.stats.soilComposite >= 90f)
                Debug.LogWarning($"Tile {tile.gridPosition} already has high soil ({tile.stats.soilComposite:F1}) — risk of imbalance.");
        return base.CanExecute(tiles);
    }

    public override void ExecuteOnTile(Tile tile, TileManager tileManager)
    {
        tile.stats.nutrientBalance = Mathf.Clamp(tile.stats.nutrientBalance + SOIL_BOOST, 0f, 100f);
        if (tile.stats.nutrientBalance > 100f)
            tile.stats.nutrientBalance = Mathf.Clamp(tile.stats.nutrientBalance - (2 * (tile.stats.nutrientBalance - 100f)), 0f, 100f);

        tile.stats.soilOrganicMatter  = Mathf.Clamp(tile.stats.soilOrganicMatter  + 3.0f, 0f, 100f);
        tile.stats.soilStructure      = Mathf.Clamp(tile.stats.soilStructure      - 3.0f, 0f, 100f);
        tile.stats.biologicalActivity = Mathf.Clamp(tile.stats.biologicalActivity - 3.0f, 0f, 100f);
        tile.stats.waterDynamics      = Mathf.Clamp(tile.stats.waterDynamics      - 3.0f, 0f, 100f);
    }
}