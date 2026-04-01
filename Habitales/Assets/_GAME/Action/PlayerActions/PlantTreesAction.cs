using System.Collections.Generic;
using UnityEngine;

public class PlantTreesAction : PlayerAction
{
    public override ActionCategory Category        => ActionCategory.Intervene;
    public override string ActionName              => "Plant Trees";
    public override string Description             => "Restores vegetation and soil quality on degraded land.";
    public override SelectionMode selectionMode    => SelectionMode.FloodFill;
    public override int MinPeoplePerTile           => 2;
    public override int BaseDays                   => 2;
    public override int MinDays                    => 1;

    private const float VEGETATION_BOOST = 30f;
    private const float SOIL_BOOST       = 10f;

    public override bool CanExecute(List<Tile> tiles)
    {
        foreach (Tile tile in tiles)
            if (tile.entity != null && tile.entity.entityType == "Factory")
                return false;
        return base.CanExecute(tiles);
    }

    public override void ExecuteOnTile(Tile tile, TileManager tileManager)
    {
        if (tile == null) return;
        tileManager.ModifyTileStats(tile, soilDelta: SOIL_BOOST, vegDelta: VEGETATION_BOOST);
        if (tile.entity == null)
            tileManager.SpawnEntity<SeedlingEntity>(tile);
        Debug.Log($"Planted at {tile.gridPosition}. +{VEGETATION_BOOST} veg, +{SOIL_BOOST} soil.");
    }
}