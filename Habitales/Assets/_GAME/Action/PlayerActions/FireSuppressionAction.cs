using System.Collections.Generic;
using UnityEngine;

public class FireSuppressionAction : PlayerAction
{
    public override ActionCategory Category        => ActionCategory.Emergency;
    public override string ActionName              => "Fire Suppression";
    public override string Description             => "Deploy team to extinguish active fires.";
    public override SelectionMode selectionMode    => SelectionMode.NonAdjacent;
    public override int MinPeoplePerTile           => 4;
    public override int BaseDays                   => 5;
    public override int MinDays                    => 3;
    public override float FatigueMultiplierPerTile => 3.0f;

    // Always return true — the team deploys regardless of what they find.
    // Spending time on a burned-out tile is intentional design pressure.
    public override bool Execute(List<Tile> tiles, TileManager tileManager) => true;

    public override bool CanExecute(List<Tile> tiles) => AnyTileHasEntityId(tiles, "fire");

    public override void ExecuteOnTile(Tile tile, TileManager tileManager)
    {
        if (tile == null) return;
        if (tile.entity != null && tile.entity.entityId == "fire")
        {
            tileManager.RemoveEntity(tile);
            Debug.Log($"Fire suppressed at {tile.gridPosition}.");
        }
        else if (tile.entity != null)
            Debug.LogWarning($"Tile {tile.gridPosition} has {tile.entity.entityId}, not Fire.");
        else
            Debug.LogWarning($"Tile {tile.gridPosition} has no entity — fire may have already burned out.");
    }
}