using System.Collections.Generic;
using UnityEngine;

public class CreateFirebreakAction : PlayerAction
{
    public override ActionCategory Category => ActionCategory.Emergency;
    
    public override string ActionName => "Create Firebreak";
    
    public override string Description => "Clear vegetation to stop fire spread. Firebreak vanishes when vegetation exceeds 66%.";
    
    public override SelectionMode selectionMode => SelectionMode.NonAdjacent;
    
    // Efficiency parameters
    public override int MinPeoplePerTile => 2;
    public override int BaseDays => 1;
    public override int MinDays => 1;
    
    // Standard fatigue multiplier (elevated fatigue handled in ActionManager)
    public override float FatigueMultiplierPerTile => 2.0f;
    
    private const float VEGETATION_REDUCTION = 0.5f; // Halve vegetation

    public override bool Execute(List<Tile> tiles, TileManager tileManager)
    {
        if (tiles == null || tiles.Count == 0)
        {
            Debug.LogError("CreateFirebreakAction: No tiles provided!");
            return false;
        }

        foreach (Tile tile in tiles)
        {
            if (tile == null) continue;

            // Set firebreak flag
            tile.stats.hasFirebreak = true;

            // Halve vegetation cover
            float oldVeg = tile.stats.vegetationCover;
            tile.stats.vegetationCover *= VEGETATION_REDUCTION;
            tile.stats.vegetationCover = Mathf.Max(0f, tile.stats.vegetationCover);

            // Keep entity as-is (user requirement)
            // No entity removal

            // Update visual
            tileManager.UpdateTileVisual(tile);

            Debug.Log($"Firebreak created at {tile.gridPosition}. Veg: {oldVeg:F1} → {tile.stats.vegetationCover:F1}");
        }

        Debug.Log($"Firebreak created on {tiles.Count} tiles. Vegetation halved.");
        return true;
    }

    public override bool CanExecute(List<Tile> tiles)
    {
        if (tiles == null || tiles.Count == 0)
            return false;

        // Block tiles with active fires
        foreach (Tile tile in tiles)
        {
            if (tile.entity is FireEntity)
            {
                Debug.LogWarning($"Cannot create firebreak on burning tile at {tile.gridPosition}! Extinguish fire first.");
                return false;
            }
        }

        return true;
    }
}
