using System.Collections.Generic;
using UnityEngine;

public class PhytoremedationPlantingAction : PlayerAction
{
    public override ActionCategory Category        => ActionCategory.Intervene;
    public override string ActionName              => "Phytoremediation Planting";
    public override string Description             => "Plant species that absorb and neutralize soil contaminants.";
    public override SelectionMode selectionMode    => SelectionMode.FloodFill;
    public override int MinPeoplePerTile           => 2;
    public override int BaseDays                   => 5;
    public override int MinDays                    => 2;
    // FatigueMultiplierPerTile = 2.0f matches base default — no override needed

    public override bool CanExecute(List<Tile> tiles)
    {
        foreach (Tile tile in tiles)
        {
            if (tile.entity is PhytoEntity)     return false;
            if (tile.entity != null &&
                (tile.entity.entityType == "Village" || tile.entity.entityType == "Factory"))
                return false;
        }
        return base.CanExecute(tiles);
    }

    public override void ExecuteOnTile(Tile tile, TileManager tileManager)
    {
        if (tile == null) return;
        tileManager.SpawnEntity<PhytoSeedlingEntity>(tile);
        Debug.Log($"Phytoremediation seedling planted at {tile.gridPosition}.");
    }
}