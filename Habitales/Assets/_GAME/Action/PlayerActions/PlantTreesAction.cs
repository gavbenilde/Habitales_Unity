using System.Collections.Generic;
using UnityEngine;

public class PlantTreesAction : PlayerAction
{
    public override ActionCategory Category        => ActionCategory.Intervene;
    public override string ActionName              => "Plant Trees";
    public override string Description             => "Restores vegetation on degraded land. Seedlings grow gradually over time.";
    public override SelectionMode selectionMode    => SelectionMode.FloodFill;
    public override int MinPeoplePerTile           => 2;
    public override int BaseDays                   => 2;
    public override int MinDays                    => 1;


    public override bool CanExecute(List<Tile> tiles)
    {
        foreach (Tile tile in tiles)
            if (tile.entity != null && tile.entity.entityId == "factory")
                return false;
        return base.CanExecute(tiles);
    }

    public override void ExecuteOnTile(Tile tile, TileManager tileManager)
    {
        if (tile == null) return;
        if (tile.entity == null)
            tileManager.SpawnById(tile, "tree_seedling");
        Debug.Log($"Planted at {tile.gridPosition}.");
    }
}