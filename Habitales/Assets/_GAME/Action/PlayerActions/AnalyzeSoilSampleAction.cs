using System.Collections.Generic;
using UnityEngine;

public class AnalyzeSoilSampleAction : PlayerAction
{
    public override string ActionName          => "Analyze Soil Sample";
    public override string Description        => "Assess the soil quality of a tile.";
    public override ActionCategory Category   => ActionCategory.Examine;
    public override SelectionMode selectionMode => SelectionMode.FloodFill;
    public override int MinPeoplePerTile       => 2;
    public override int BaseDays               => 1;
    public override int MinDays                => 1;
    public override float FatigueMultiplierPerTile => 1.0f;

    public override bool CanExecute(List<Tile> tiles)
    {
        return tiles.Exists(t => !t.isAnalyzed);
    }

    public override void ExecuteOnTile(Tile tile, TileManager tileManager)
    {
        tile.isAnalyzed = true;
        // OverflowTipSpawner.Instance.SpawnAtCursor(BuildReport(tile));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private string BuildReport(Tile tile)
    {
        return string.Format(
            "Soil Composite: {0:F0} | Nutrients {1}, Erosion {2}",
            tile.GetSoilComposite(),
            Label(tile.stats.nutrientBalance),
            Label(tile.stats.erosionResistance)
        );
    }

    private static string Label(float v)
    {
        if (v < 33f) return "low";
        if (v < 66f) return "moderate";
        return "good";
    }
}