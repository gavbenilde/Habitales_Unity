using System.Collections.Generic;
using UnityEngine;

public class CreateFirebreakAction : PlayerAction
{
    public override ActionCategory Category        => ActionCategory.Emergency;
    public override string ActionName              => "Create Firebreak";
    public override string Description             => "Clear vegetation to stop fire spread. Firebreak vanishes when vegetation exceeds 66.";
    public override SelectionMode selectionMode    => SelectionMode.NonAdjacent;
    public override int MinPeoplePerTile           => 1;
    public override int BaseDays                   => 1;
    public override int MinDays                    => 1;
    public override float FatigueMultiplierPerTile => 4.0f;

    private const float VEGETATION_REDUCTION = 0.5f;

    public override bool CanExecute(List<Tile> tiles)
    {
        if (tiles == null || tiles.Count == 0) return false;
        foreach (Tile tile in tiles)
        {
            if (tile.entity != null && tile.entity.entityId == "fire")
            {
                Debug.LogWarning($"Cannot create firebreak on burning tile at {tile.gridPosition}. Extinguish fire first.");
                return false;
            }
        }
        return true;
    }

    public override void ExecuteOnTile(Tile tile, TileManager tileManager)
    {
        if (tile == null) return;
        if (!tile.tv.Contains(TileOverlayType.Firebreak))
            tile.tv.Add(TileOverlayType.Firebreak);
        float oldVeg = tile.stats.vegetationCover;
        tile.stats.vegetationCover = Mathf.Max(0f, tile.stats.vegetationCover * VEGETATION_REDUCTION);
        Debug.Log($"Firebreak at {tile.gridPosition}. Veg {oldVeg:F1} → {tile.stats.vegetationCover:F1}");
    }
}