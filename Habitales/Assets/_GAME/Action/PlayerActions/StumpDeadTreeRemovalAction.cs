using System.Collections.Generic;
using Habitales.Entities;

public class StumpDeadTreeRemovalAction : PlayerAction
{
    public override ActionCategory Category        => ActionCategory.Cleanup;
    public override string ActionName              => "Remove Stumps & Dead Trees";
    public override string Description             => "Clear stumps and dead trees to make way for new growth.";
    public override SelectionMode selectionMode    => SelectionMode.NonAdjacent;
    public override int MinPeoplePerTile           => 1;
    public override int BaseDays                   => 2;
    public override int MinDays                    => 1;
    public override float FatigueMultiplierPerTile => 1.5f;

    public override bool CanExecute(List<Tile> tiles)
    {
        return AnyTileHasEntityId(tiles, EntityIds.Stump)
               || AnyTileHasEntityId(tiles, EntityIds.DeadTree);
    }

    public override void ExecuteOnTile(Tile tile, TileManager tileManager)
    {
        // Both stumps and dead trees are cleared the same way.
        // Stump passive (+0.05 organicMatter/day) simply stops ticking once the entity is removed.
        if (tile.entity != null &&
            (tile.entity.entityId == EntityIds.Stump || tile.entity.entityId == EntityIds.DeadTree))
            tileManager.RemoveEntity(tile);
    }
}
