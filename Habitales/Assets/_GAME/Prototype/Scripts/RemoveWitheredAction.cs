using System.Collections.Generic;
using UnityEngine;

public class RemoveWitheredAction : PlayerAction
{
    public override ActionCategory Category     => ActionCategory.Cleanup;
    public override string ActionName           => "Remove Withered";
    public override string Description          => "Remove dead plants that have withered.";
    public override SelectionMode selectionMode => SelectionMode.FloodFill;
    public override int MinPeoplePerTile        => 1;
    public override int BaseDays               => 1;
    public override int MinDays                => 1;

    public override bool CanExecute(List<Tile> tiles)
    {
        if (tiles == null || tiles.Count == 0) return false;
        foreach (Tile t in tiles)
            if (t.entity is PlantedCubeEntity { isWithered: true }) return true;
        return false;
    }

    public override void ExecuteOnTile(Tile tile, TileManager tileManager)
    {
        if (tile.entity is PlantedCubeEntity { isWithered: true })
            tileManager.RemoveEntity(tile);
    }
}
