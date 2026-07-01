namespace Habitales.UI
{
    /// <summary>
    /// Stable keys for <see cref="StatIconLibrary"/> lookups (icon + name + tooltip text).
    /// v1 covers the two world/region bars. Phase 2 (per-tile substats) will add the 8
    /// <c>Habitales.Core.TargetStat</c> values + <c>Hp</c> — append only, never reorder
    /// (the values are serialized as ints in the library asset).
    /// </summary>
    public enum IndicatorStatId
    {
        WorldHealth  = 0,
        RegionHealth = 1,
        // Phase 2: NutrientBalance, SoilOrganicMatter, SoilStructure, BiologicalActivity,
        //          WaterDynamics, ErosionResistance, VegetationCover, Contamination, Hp …
    }
}
