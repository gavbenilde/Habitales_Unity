using System.Collections.Generic;
using UnityEngine;

public enum TileSelectionMethod
{
    Single,          // Single tile only
    Floodfill,       // Floodfill from selected tile
    CustomMultiple,  // Player manually selects each tile
    AutoRegion       // Automatically select all matching tiles in region
}

public abstract class PlayerAction
{
    public abstract string ActionName { get; }
    public abstract string Description { get; }
    
    // Selection method for this action
    public abstract TileSelectionMethod SelectionMethod { get; }
    
    // Efficiency parameters
    public abstract int MinPeoplePerTile { get; }  // Minimum crew per tile
    public abstract int BaseDays { get; }          // Base time at minimum crew
    public abstract int MinDays { get; }           // Absolute minimum duration
    
    // Fatigue parameters
    public virtual float FatigueMultiplierPerTile => 2.0f; // Default: +2% per tile
    
    /// <summary>
    /// Calculates maximum tiles affordable with current people.
    /// </summary>
    public int GetMaxTiles(int availablePeople)
    {
        return Mathf.Max(1, availablePeople / MinPeoplePerTile);
    }
    
    /// <summary>
    /// Calculates days required using diminishing returns formula.
    /// Formula: days = baseDays / sqrt(efficiency)
    /// Where efficiency = (peoplePerTile / minPeoplePerTile)
    /// </summary>
    public int CalculateDays(int availablePeople, int targetTiles)
    {
        if (targetTiles == 0) return MinDays;
        
        float peoplePerTile = (float)availablePeople / targetTiles;
        float efficiency = peoplePerTile / MinPeoplePerTile;
        
        // Diminishing returns via square root
        float efficiencyFactor = Mathf.Sqrt(efficiency);
        
        int calculatedDays = Mathf.CeilToInt(BaseDays / efficiencyFactor);
        return Mathf.Max(MinDays, calculatedDays);
    }
    
    /// <summary>
    /// Executes the action on target tiles.
    /// Returns true if successful.
    /// </summary>
    public abstract bool Execute(List<Tile> tiles, TileManager tileManager);
    
    /// <summary>
    /// Optional: Validate if action can be executed on these tiles.
    /// Override for custom logic (e.g., "can only plant on logged tiles").
    /// </summary>
    public virtual bool CanExecute(List<Tile> tiles)
    {
        return tiles != null && tiles.Count > 0;
    }
}
