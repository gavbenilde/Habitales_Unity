using System.Collections.Generic;
using UnityEngine;

public class CoverCroppingAction : PlayerAction
{
    public CoverCropEntity.CoverCropVariant cropVariant;

    public override ActionCategory Category        => ActionCategory.Intervene;
    public override string ActionName              => $"Cover Cropping ({cropVariant})";
    public override string Description             => "Plant cover crops to gradually improve soil health.";
    public override SelectionMode selectionMode    => SelectionMode.FloodFill;
    public override int MinPeoplePerTile           => 1;
    public override int BaseDays                   => 3;
    public override int MinDays                    => 1;
    public override float FatigueMultiplierPerTile => 1.5f;
    public override string VariantGroupName        => "Cover Cropping";

    public override bool CanExecute(List<Tile> tiles)
    {
        foreach (Tile tile in tiles)
        {
            if (tile.entity is CoverCropEntity)  return false;
            if (tile.entity is FireEntity)        return false;
            if (tile.entity != null &&
                (tile.entity.entityType == "Village" || tile.entity.entityType == "Factory"))
                return false;
        }
        return base.CanExecute(tiles);
    }

    public override void ExecuteOnTile(Tile tile, TileManager tileManager)
    {
        if (tile == null) return;
        tileManager.SpawnEntity<CoverCropSeedlingEntity>(tile);
        if (tile.entity is CoverCropSeedlingEntity seedling)
            seedling.variant = cropVariant;
        Debug.Log($"Cover Cropping ({cropVariant}) seedling planted at {tile.gridPosition}.");
    }
}