using System.Collections.Generic;
using UnityEngine;

public class ClearTrashAction : PlayerAction
{
    public override ActionCategory Category        => ActionCategory.Cleanup;
    public override string ActionName              => "Clear Trash";
    public override string Description             => "Remove biological and non-biological waste from a tile.";
    public override SelectionMode selectionMode    => SelectionMode.NonAdjacent;
    public override int MinPeoplePerTile           => 1;
    public override int BaseDays                   => 1;
    public override int MinDays                    => 1;
    public override float FatigueMultiplierPerTile => 1.0f;

    public override bool CanExecute(List<Tile> tiles)
    {
        return AnyTileHasEntity<TrashBioEntity>(tiles)
               || AnyTileHasEntity<TrashNonBioEntity>(tiles);
    }

    public override void ExecuteOnTile(Tile tile, TileManager tileManager)
    {
        if (tile.entity is TrashBioEntity || tile.entity is TrashNonBioEntity)
            tileManager.RemoveEntity(tile);
    }
}
