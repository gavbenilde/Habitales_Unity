using System.Collections.Generic;
using UnityEngine;

public class CoverCroppingAction : PlayerAction
{
    public enum Variant { Legume, Grass, Phyto }
    public Variant cropVariant;

    // Data structure to hold variant-specific requirements
    private struct VariantSettings
    {
        public int minPeople;
        public int baseDays;
        public string description;
    }

    private static readonly Dictionary<Variant, VariantSettings> Settings = new Dictionary<Variant, VariantSettings>
    {
        { Variant.Legume, new VariantSettings { 
            minPeople = 1, baseDays = 3, 
            description = "Plant nitrogen-fixing species to enrich soil biology." 
        }},
        { Variant.Grass, new VariantSettings { 
            minPeople = 1, baseDays = 3, 
            description = "Plant all-rounder grasses to improve all soil composite stats." 
        }},
        { Variant.Phyto, new VariantSettings { 
            minPeople = 2, baseDays = 5, 
            description = "Plant species that remove soil contamination over time." 
        }}
    };

    public override ActionCategory Category        => ActionCategory.Intervene;
    public override string ActionName              => $"Plant {cropVariant}";
    public override string Description             => Settings[cropVariant].description;
    public override SelectionMode selectionMode    => SelectionMode.FloodFill;
    public override string VariantGroupName        => "Cover Cropping";

    // Flexible properties pulling from the Settings dictionary
    public override int MinPeoplePerTile           => Settings[cropVariant].minPeople;
    public override int BaseDays                   => Settings[cropVariant].baseDays;
    public override int MinDays                    => 1;
    public override float FatigueMultiplierPerTile => 1.5f;

    public override bool CanExecute(List<Tile> tiles)
    {
        foreach (Tile tile in tiles)
        {
            if (tile.entity != null) return false;
        }
        return base.CanExecute(tiles);
    }

    public override void ExecuteOnTile(Tile tile, TileManager tileManager)
    {
        if (tile == null) return;
        
        tileManager.SpawnEntity<CoverCropSeedlingEntity>(tile);
        if (tile.entity is CoverCropSeedlingEntity seedling)
        {
            seedling.variant = cropVariant;
        }
    }
}