using System.Collections.Generic;
using UnityEngine;
using Habitales.Core;

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

    // The fixed trade-offs the fertilizer always applies alongside the nutrient boost: it feeds the
    // soil but acidifies/compacts it slightly. Routed through TileManager.ApplyStatChanges (Law 1).
    private static readonly StatChange[] FertilizerTradeoffs =
    {
        new StatChange { stat = TargetStat.SoilOrganicMatter,  delta =  3.0f },
        new StatChange { stat = TargetStat.SoilStructure,      delta = -3.0f },
        new StatChange { stat = TargetStat.BiologicalActivity, delta = -3.0f },
        new StatChange { stat = TargetStat.WaterDynamics,      delta = -3.0f },
    };

    public override bool CanExecute(List<Tile> tiles)
    {
        foreach (Tile tile in tiles)
            if (tile.stats.soilComposite >= 90f)
                Debug.LogWarning($"Tile {tile.gridPosition} already has high soil ({tile.stats.soilComposite:F1}) — risk of imbalance.");
        return base.CanExecute(tiles);
    }

    public override void ExecuteOnTile(Tile tile, TileManager tileManager)
    {
        // Over-fertilization penalty: the +20 nutrient boost is applied UNCLAMPED first, and any
        // amount past the 100 ceiling back-fires — every point over is subtracted twice
        // (raw → raw - 2·(raw-100) == 200 - raw). So dumping fertilizer on already-rich soil
        // (nutrients > 80) wastes the boost and can actively degrade the tile. Reading the current
        // value here is a Law-1 read; the write still goes through TileManager.
        float raw            = tile.stats.nutrientBalance + SOIL_BOOST;
        float penalized      = raw > 100f ? 200f - raw : raw;
        float nutrientDelta  = penalized - tile.stats.nutrientBalance;

        tileManager.ApplyStatChange(tile, new StatChange { stat = TargetStat.NutrientBalance, delta = nutrientDelta });
        tileManager.ApplyStatChanges(tile, FertilizerTradeoffs);
    }
}