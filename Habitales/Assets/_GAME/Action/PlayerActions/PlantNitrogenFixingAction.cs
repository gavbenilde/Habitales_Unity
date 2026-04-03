using System.Collections.Generic;
using UnityEngine;

public class PlantNitrogenFixingSpeciesAction : PlayerAction
{
    public override ActionCategory Category        => ActionCategory.Intervene;
    public override string ActionName              => "Plant Nitrogen-Fixing Species";
    public override string Description             => "Establish nitrogen-fixing plants that permanently enrich soil biology.";
    public override SelectionMode selectionMode    => SelectionMode.FloodFill;
    public override int MinPeoplePerTile           => 1;
    public override int BaseDays                   => 2;
    public override int MinDays                    => 1;
    public override float FatigueMultiplierPerTile => 1.5f;

    public override bool CanExecute(List<Tile> tiles)
    {
        // tile.entity == null catches all entities: trees, trash, buildings, other passives
        foreach (Tile tile in tiles)
            if (tile.entity != null)
                return false;
        return base.CanExecute(tiles);
    }

    public override void ExecuteOnTile(Tile tile, TileManager tileManager)
    {
        if (tile == null) return;
        tileManager.SpawnEntity<NitrogenFixerSeedlingEntity>(tile);
        Debug.Log($"Nitrogen fixer seedling planted at {tile.gridPosition}.");
    }
}