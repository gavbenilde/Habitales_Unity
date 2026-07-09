using System.Collections.Generic;
using UnityEngine;
using Habitales.Entities;

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
        return AnyTileHasEntityId(tiles, EntityIds.TrashBio)
               || AnyTileHasEntityId(tiles, EntityIds.TrashNonBio);
    }

    public override void ExecuteOnTile(Tile tile, TileManager tileManager)
    {
        if (tile.entity != null &&
            (tile.entity.entityId == EntityIds.TrashBio || tile.entity.entityId == EntityIds.TrashNonBio))
            tileManager.RemoveEntity(tile);
    }
}
