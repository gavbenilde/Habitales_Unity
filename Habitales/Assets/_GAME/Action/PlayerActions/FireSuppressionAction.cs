using System.Collections.Generic;
using UnityEngine;

public class FireSuppressionAction : PlayerAction
{
    public override string ActionName => "Fire Suppression";
    public override string Description => "Deploy team to extinguish active fires";
    public override TileSelectionMethod SelectionMethod => TileSelectionMethod.AutoRegion;
    
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
            // FIXED: Check entity exists before accessing entityType
            if (tile.entity != null && tile.entity.entityType == "Fire")
            {
                tileManager.RemoveEntity(tile);
                suppressedFires++;
            }
        }
    
        Debug.Log($"✓ Fire suppression complete: {suppressedFires} fires extinguished");
        return suppressedFires > 0;
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