using System.Collections.Generic;
using UnityEngine;

public class FireSuppressionAction : PlayerAction
{
    public override ActionCategory Category => ActionCategory.Emergency;

    public override string ActionName => "Fire Suppression";
    public override string Description => "Deploy team to extinguish active fires";
    public override SelectionMode selectionMode => SelectionMode.Adjacent;

    
    public override int MinPeoplePerTile => 4;   // Requires 4 people minimum
    public override int BaseDays => 5;
    public override int MinDays => 3;            // Dangerous work = higher minimum
    
    // HIGHER fatigue for dangerous work
    public override float FatigueMultiplierPerTile => 3.0f; // +3% per tile instead of 2%
    
    public override bool Execute(List<Tile> tiles, TileManager tileManager)
    {
        int suppressedFires = 0;
        foreach (Tile tile in tiles)
        {
            if (tile.entity is FireEntity)
            {
                tileManager.RemoveEntity(tile);
                suppressedFires++;
                Debug.Log($"Fire suppressed at {tile.gridPosition}");
            }
            else if (tile.entity != null)
                Debug.LogWarning($"Tile {tile.gridPosition} has {tile.entity.entityType}, not Fire!");
            else
                Debug.LogWarning($"Tile {tile.gridPosition} has no entity — fire may have already burned out.");
        }

        Debug.Log($"Fire suppression complete: {suppressedFires}/{tiles.Count} fires extinguished");

        // Always return true — the team deployed regardless of what they found.
        // Spending resources on a burnt-out tile is intentional design pressure.
        return true;
    }


    
    public override bool CanExecute(List<Tile> tiles)
    {
        // Must have at least one fire
        foreach (Tile tile in tiles)
        {
            if (tile.entity != null && tile.entity.entityType == "Fire")
            {
                return true;
            }
        }
        return false;
    }
}