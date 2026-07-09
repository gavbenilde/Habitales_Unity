// EntityIds — the single code-side home for entity id string literals (design decision
// 2026-07-07). Profiles/designers never author raw id strings; every id used by code
// (RegionManager, PlayerActions, behaviour hooks, UI, etc.) resolves through these
// constants instead of a hand-typed literal.
//
// IMPORTANT (revised 2026-07-07): entity ids are no longer a serialized field on TileEntitySO.
// Each id is DERIVED from the asset's displayName via TileEntitySO.GenerateId (trim, lowercase,
// collapse non-alphanumeric runs to '_', trim leading/trailing '_'). The values below are
// ground-truthed by running that slug rule against the `displayName:` field of every asset
// under Habitales/Assets/_SO/Entities/ — NOT by reading a stored id. Renaming a displayName
// changes its derived id; there is no persistent save data keyed by entity ids in the
// prototype, so keep this file in sync whenever a display name authors here changes.
namespace Habitales.Entities
{
    public static class EntityIds
    {
        // --- Buildings ---
        public const string Village = "village";               // displayName "Village"
        public const string Factory = "factory";                // displayName "Factory"

        // --- Hazards ---
        public const string Fire = "fire";                      // displayName "Fire"

        // --- Narra tree lifecycle ---
        public const string TreeSeedling = "narra_tree_seedling"; // displayName "Narra Tree Seedling"
        public const string TreeSapling  = "narra_tree_sapling";  // displayName "Narra Tree Sapling"
        public const string TreeMature   = "narra_tree_mature";   // displayName "Narra Tree Mature"

        // --- Shared debris (reused across every species, arch §5.1) ---
        public const string DeadTree = "dead_tree";             // displayName "Dead Tree"
        public const string Stump    = "stump";                 // displayName "Stump"

        // --- Trash / debris ---
        public const string TrashBio    = "biodegradeable_trash";     // displayName "Biodegradeable Trash"
        public const string TrashNonBio = "non_biodegradeable_trash"; // displayName "Non-Biodegradeable Trash"

        // --- Grass ---
        public const string GrassSeedling = "grass_seedling";   // displayName "Grass Seedling"
        public const string GrassMature   = "mature_grass";     // displayName "Mature Grass"

        // --- Buckwheat ---
        public const string BuckwheatSeedling = "buckwheat_seedling"; // displayName "Buckwheat Seedling"
        public const string BuckwheatSapling  = "buckwheat_sapling";  // displayName "Buckwheat Sapling"
        public const string BuckwheatMature   = "buckwheat_mature";   // displayName "Buckwheat Mature"

        // --- Legume ---
        public const string LegumeSeedling  = "legume_seedling";  // displayName "Legume Seedling"
        public const string LegumeSapling   = "legume_sapling";   // displayName "Legume Sapling"
        public const string LegumeMature    = "mature_legumes";   // displayName "Mature Legumes"
        public const string LegumeFlowering = "legume_flowering"; // displayName "Legume Flowering"

        // --- Nitrogen-fixer ---
        public const string NitrogenSeedling  = "nitrogen_seedling";  // displayName "Nitrogen Seedling"
        public const string NitrogenSapling   = "nitrogen_sapling";   // displayName "Nitrogen Sapling"
        public const string NitrogenMature    = "nitrogen_mature";    // displayName "Nitrogen Mature"
        public const string NitrogenFlowering = "nitrogen_flowering"; // displayName "Nitrogen Flowering"

        // --- Phytoremediator (Rinorea) ---
        public const string PhytoSeedling  = "rinorea_seedling"; // displayName "Rinorea Seedling"
        public const string PhytoSapling   = "rinorea_sapling";  // displayName "Rinorea Sapling"
        public const string PhytoMature    = "rinorea_mature";   // displayName "Rinorea Mature"
        public const string PhytoFlowering = "rinorea_shrub";    // displayName "Rinorea Shrub"

        // --- Sunflower ---
        public const string SunflowerSeedling  = "sunflower_seedling";  // displayName "Sunflower Seedling"
        public const string SunflowerSapling   = "sunflower_stem";      // displayName "Sunflower Stem"
        public const string SunflowerBud       = "sunflower_bud";       // displayName "Sunflower Bud"
        public const string SunflowerMature    = "sunflower_mature";    // displayName "Sunflower Mature"
        public const string SunflowerFlowering = "sunflower_flowering"; // displayName "Sunflower Flowering"
    }
}
