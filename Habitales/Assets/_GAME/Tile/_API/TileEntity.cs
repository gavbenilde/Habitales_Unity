using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class TileEntity
{
    public string entityType;
    public float health = 100f;

    public abstract void OnDailyUpdate(Tile tile, TileManager manager);
}

public class FireEntity : TileEntity
{
    // Internal duration counter — never shown to player
    private int fireDuration = 0;

    // Vegetation captured at ignition — used to normalize the spread ramp
    private float startingVegetation = -1f;

    // Fire cannot spread in its first N days (too fresh)
    private const int MIN_SPREAD_DAYS = 3;

    // Damage per day — kept at original value
    private const float BASE_DAMAGE = 8f;

    // Maximum spread probability reached at peak burn (mid-to-late life)
    private const float PEAK_SPREAD_CHANCE = 0.2f;

    // Spread is checked every day (daysSinceSpreading removed — duration handles pacing)
    public FireEntity() { entityType = "Fire"; health = 100f; }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        // Capture starting vegetation on the first tick
        if (startingVegetation < 0f)
            startingVegetation = Mathf.Max(1f, tile.stats.vegetationCover);

        float bonusDamage = WeatherManager.Instance != null
            ? WeatherManager.Instance.GetFireBonusDamage() : 0f;
        float totalDamage = BASE_DAMAGE + bonusDamage;

        tile.stats.vegetationCover    = Mathf.Clamp(tile.stats.vegetationCover    - totalDamage, 0f, 100f);
        tile.stats.nutrientBalance    = Mathf.Clamp(tile.stats.nutrientBalance    - totalDamage, 0f, 100f);
        tile.stats.biologicalActivity = Mathf.Clamp(tile.stats.biologicalActivity - totalDamage, 0f, 100f);

        fireDuration++;

        TrySpread(tile, manager);

        if (tile.stats.vegetationCover <= 0f)
            manager.RemoveEntity(tile);
    }

    private void TrySpread(Tile tile, TileManager manager)
    {
        // Hard block: fire is too fresh to throw embers
        if (fireDuration < MIN_SPREAD_DAYS) return;

        // maxDuration = how many days this tile was always going to burn
        // Ramp is 0 at MIN_SPREAD_DAYS, reaching 1.0 at maxDuration
        float maxDuration = startingVegetation / BASE_DAMAGE;
        float spreadProgress = Mathf.Clamp01(
            (fireDuration - MIN_SPREAD_DAYS) / Mathf.Max(1f, maxDuration - MIN_SPREAD_DAYS)
        );

        float spreadMult = WeatherManager.Instance != null
            ? WeatherManager.Instance.GetFireSpreadMultiplier() : 1f;
        float spreadChance = PEAK_SPREAD_CHANCE * spreadProgress * spreadMult;

        foreach (Tile neighbor in manager.GetAdjacentTiles(tile))
        {
            if (neighbor.tv.Contains(TileOverlayType.Firebreak)) continue;
            if (neighbor.entity != null) continue;

            if (Random.value < spreadChance)
                manager.SpawnEntity<FireEntity>(neighbor);
        }
    }
}


public class VillageEntity : TileEntity
{
    private int daysPassed = 0;
    private bool hasFireOccurred = false;

    public VillageEntity()
    {
        entityType = "Village";
        health = 100f;
    }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        if (Random.value < 0.015f)
        {
            EventManager.Instance?.FireEventByID("first_kaingin");
            SpawnKainginFire(tile, manager);
        }
        daysPassed++;
    }

    private void SpawnKainginFire(Tile tile, TileManager manager)
    {
        int fireCount = Random.Range(4, 8);
        var potentialTargets = GetTilesInRadius(tile, manager, 4);
        for (int i = 0; i < fireCount && potentialTargets.Count > 0; i++)
        {
            int idx = Random.Range(0, potentialTargets.Count);
            Tile target = potentialTargets[idx];
            bool isSafe = target.entity == null ||
                          (!(target.entity is VillageEntity) && !(target.entity is FactoryEntity));
            if (isSafe && !target.tv.Contains(TileOverlayType.Firebreak))
                manager.SpawnEntity<FireEntity>(target);
            potentialTargets.RemoveAt(idx);
        }
    }

    private System.Collections.Generic.List<Tile> GetTilesInRadius(Tile center, TileManager manager, int radius)
    {
        var result = new System.Collections.Generic.List<Tile>();
        for (int x = -radius; x <= radius; x++)
        for (int y = -radius; y <= radius; y++)
        {
            if (x == 0 && y == 0) continue;
            Tile t = manager.GetTile(center.gridPosition.x + x, center.gridPosition.y + y);
            if (t != null) result.Add(t);
        }

        return result;
    }
}

public class TreeEntity : TileEntity
{
    private const float VC_BOOST_PER_DAY   = 2.0f;
    private const float BA_BOOST_PER_DAY   = 1.2f;
    private const float ER_BOOST_PER_DAY   = 1.2f;
    private const float SOM_BOOST_PER_DAY  = 0.8f;
    private const float SS_BOOST_PER_DAY   = 0.6f;
    private const float WD_BOOST_PER_DAY   = 0.6f;
    private const float NB_BOOST_PER_DAY   = 0.3f;
    private const float SOIL_THRESHOLD_DIE = 15f;

    public TreeEntity()
    {
        entityType = "Mature Tree";
        health = 100f;
    }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        tile.stats.vegetationCover    = Mathf.Clamp(tile.stats.vegetationCover    + VC_BOOST_PER_DAY,  0f, 100f);
        tile.stats.biologicalActivity = Mathf.Clamp(tile.stats.biologicalActivity + BA_BOOST_PER_DAY,  0f, 100f);
        tile.stats.erosionResistance  = Mathf.Clamp(tile.stats.erosionResistance  + ER_BOOST_PER_DAY,  0f, 100f);
        tile.stats.soilOrganicMatter  = Mathf.Clamp(tile.stats.soilOrganicMatter  + SOM_BOOST_PER_DAY, 0f, 100f);
        tile.stats.soilStructure      = Mathf.Clamp(tile.stats.soilStructure      + SS_BOOST_PER_DAY,  0f, 100f);
        tile.stats.waterDynamics      = Mathf.Clamp(tile.stats.waterDynamics      + WD_BOOST_PER_DAY,  0f, 100f);
        tile.stats.nutrientBalance    = Mathf.Clamp(tile.stats.nutrientBalance    + NB_BOOST_PER_DAY,  0f, 100f);

        if (tile.stats.soilComposite < SOIL_THRESHOLD_DIE)
        {
            manager.TransformEntity<DeadTreeEntity>(tile);
            Debug.Log($"Tree died at {tile.gridPosition} due to poor soil.");
            return;
        }

        manager.UpdateTileVisual(tile);
    }
}

public class SaplingEntity : TileEntity
{
    private const float NB_CONSUME_PER_DAY  = 1.0f;
    private const float VC_BOOST_PER_DAY    = 1.0f;
    private const float BA_BOOST_PER_DAY    = 0.6f;
    private const float ER_BOOST_PER_DAY    = 0.6f;
    private const float SOM_BOOST_PER_DAY   = 0.4f;
    private const float SS_BOOST_PER_DAY    = 0.3f;
    private const float WD_BOOST_PER_DAY    = 0.3f;
    private const float SOIL_THRESHOLD_DIE  = 15f;
    private const float ORGANIC_BOOST_ON_DEATH = 10f;
    private const int   DAYS_UNTIL_GROWTH   = 10;
    private int daysExisting = 0;

    public SaplingEntity()
    {
        entityType = "Sapling";
        health = 100f;
    }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        daysExisting++;

        // Nutrient cost of growing
        tile.stats.nutrientBalance = Mathf.Clamp(
            tile.stats.nutrientBalance - NB_CONSUME_PER_DAY, 0f, 100f);

        // Early ecosystem contributions
        tile.stats.vegetationCover   = Mathf.Clamp(tile.stats.vegetationCover   + VC_BOOST_PER_DAY,  0f, 100f);
        tile.stats.biologicalActivity = Mathf.Clamp(tile.stats.biologicalActivity + BA_BOOST_PER_DAY, 0f, 100f);
        tile.stats.erosionResistance  = Mathf.Clamp(tile.stats.erosionResistance  + ER_BOOST_PER_DAY, 0f, 100f);
        tile.stats.soilOrganicMatter  = Mathf.Clamp(tile.stats.soilOrganicMatter  + SOM_BOOST_PER_DAY, 0f, 100f);
        tile.stats.soilStructure      = Mathf.Clamp(tile.stats.soilStructure      + SS_BOOST_PER_DAY, 0f, 100f);
        tile.stats.waterDynamics      = Mathf.Clamp(tile.stats.waterDynamics      + WD_BOOST_PER_DAY, 0f, 100f);

        if (tile.stats.soilComposite < SOIL_THRESHOLD_DIE)
        {
            // Dying sapling returns what it took to grow
            tile.stats.soilOrganicMatter = Mathf.Clamp(
                tile.stats.soilOrganicMatter + ORGANIC_BOOST_ON_DEATH, 0f, 100f);
            manager.RemoveEntity(tile);
            Debug.Log($"Sapling died at {tile.gridPosition} — nutrients returned to soil.");
            return;
        }

        if (daysExisting >= DAYS_UNTIL_GROWTH)
        {
            manager.TransformEntity<TreeEntity>(tile);
            // VFXManager.Instance?.SpawnVFX();
            Debug.Log($"Sapling grew into a Tree at {tile.gridPosition}.");
            return;
        }

        manager.UpdateTileVisual(tile);
    }
}

public class SeedlingEntity : TileEntity
{
    private const float NB_CONSUME_PER_DAY  = 2.0f;
    private const float SOIL_THRESHOLD_DIE  = 20f;
    private const float VC_BUMP_ON_PROMOTE  = 10f;
    private const int   DAYS_UNTIL_GROWTH   = 5;
    private int daysExisting = 0;

    public SeedlingEntity()
    {
        entityType = "Seedling";
        health = 100f;
    }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        daysExisting++;
        tile.stats.nutrientBalance = Mathf.Clamp(
            tile.stats.nutrientBalance - NB_CONSUME_PER_DAY, 0f, 100f);

        if (tile.stats.soilComposite < SOIL_THRESHOLD_DIE)
        {
            manager.RemoveEntity(tile);
            Debug.Log($"Seedling died at {tile.gridPosition} due to poor soil.");
            return;
        }

        if (daysExisting >= DAYS_UNTIL_GROWTH)
        {
            // Visual bump — the "something appeared" moment for the player
            tile.stats.vegetationCover = Mathf.Clamp(
                tile.stats.vegetationCover + VC_BUMP_ON_PROMOTE, 0f, 100f);

            manager.TransformEntity<SaplingEntity>(tile);
            Debug.Log($"Seedling grew into a Sapling at {tile.gridPosition}.");
            return;
        }

        manager.UpdateTileVisual(tile);
    }
}

public class StumpEntity : TileEntity
{
    private const float ORGANIC_BOOST_PER_DAY = 0.05f;

    public StumpEntity()
    {
        entityType = "Stump";
        health = 0f;
    }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        tile.stats.soilOrganicMatter = Mathf.Clamp(tile.stats.soilOrganicMatter + ORGANIC_BOOST_PER_DAY, 0f, 100f);
        manager.UpdateTileVisual(tile);
    }
}

public class DeadTreeEntity : TileEntity
{
    private const float ORGANIC_BOOST_PER_DAY = 0.3f;
    private const int DECOMPOSITION_DAYS = 30;
    private int daysExisting = 0;

    public DeadTreeEntity()
    {
        entityType = "DeadTree";
        health = 0f;
    }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        daysExisting++;
        tile.stats.soilOrganicMatter = Mathf.Clamp(tile.stats.soilOrganicMatter + ORGANIC_BOOST_PER_DAY, 0f, 100f);
        if (daysExisting >= DECOMPOSITION_DAYS)
        {
            manager.RemoveEntity(tile);
            Debug.Log($"Dead tree fully decomposed at {tile.gridPosition}.");
        }

        manager.UpdateTileVisual(tile);
    }
}

public class TrashBioEntity : TileEntity
{
    private int daysExisting = 0;
    public bool isInspected = false;

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        daysExisting++;
        if (daysExisting >= 7)
        {
            tile.stats.biologicalActivity = Mathf.Clamp(tile.stats.biologicalActivity - 10f, 0f, 100f);
            manager.RemoveEntity(tile);
        }
    }
}

public class TrashNonBioEntity : TileEntity
{
    public bool isInspected = false;
    
    // Non-biodegradable trash does not self-degrade.
    // It persists indefinitely until removed via ClearTrashAction.
    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        // No passive effect for now. Extend here when pollution stats are added.
    }
}

public class FactoryEntity : TileEntity
{
    public FactoryEntity()
    {
        entityType = "Factory";
    }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
    }
}

public class CoverCropSeedlingEntity : TileEntity
{
    public CoverCroppingAction.Variant variant;
    private const int DAYS_UNTIL_GROWTH = 5;
    private int daysExisting = 0;

    public CoverCropSeedlingEntity() { entityType = "CoverCropSeedling"; }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        daysExisting++;

        if (daysExisting >= DAYS_UNTIL_GROWTH)
        {
            if (variant == CoverCroppingAction.Variant.Phyto)
            {
                // Phyto Path: Seedling -> Sapling
                manager.TransformEntity<CoverCropSaplingEntity>(tile);
                if (tile.entity is CoverCropSaplingEntity sapling) sapling.variant = variant;
            }
            else
            {
                // Legume/Grass Path: Seedling -> Mature
                manager.TransformEntity<CoverCropMatureEntity>(tile);
                if (tile.entity is CoverCropMatureEntity mature) mature.variant = variant;
            }
        }
        manager.UpdateTileVisual(tile);
    }
}

public class CoverCropSaplingEntity : TileEntity
{
    public CoverCroppingAction.Variant variant;
    private const int DAYS_UNTIL_GROWTH = 30; // 30-day stage for Phyto
    private const float PHYTO_DRAIN_RATE     = 0.5f; 
    private int daysExisting = 0;

    public CoverCropSaplingEntity() { entityType = "CoverCropSapling " + variant; }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        daysExisting++;

        if (variant == CoverCroppingAction.Variant.Phyto)
        {
            tile.stats.contamination = Mathf.Max(0f, tile.stats.contamination - PHYTO_DRAIN_RATE);
        }

        if (daysExisting >= DAYS_UNTIL_GROWTH)
        {
            manager.TransformEntity<CoverCropMatureEntity>(tile);
            if (tile.entity is CoverCropMatureEntity mature) mature.variant = variant;
        }
        manager.UpdateTileVisual(tile);
    }
}

public class CoverCropMatureEntity : TileEntity
{
    public CoverCroppingAction.Variant variant;

    public CoverCropMatureEntity() { entityType = "CoverCropMature " + variant; }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        switch (variant)
        {
            case CoverCroppingAction.Variant.Legume:
                // Nitrogen-fixing cover crop — NB and BA primary, broad secondary support
                tile.stats.nutrientBalance    = Mathf.Clamp(tile.stats.nutrientBalance    + 1.2f, 0f, 100f);
                tile.stats.biologicalActivity = Mathf.Clamp(tile.stats.biologicalActivity + 1.0f, 0f, 100f);
                tile.stats.soilOrganicMatter  = Mathf.Clamp(tile.stats.soilOrganicMatter  + 0.6f, 0f, 100f);
                tile.stats.erosionResistance  = Mathf.Clamp(tile.stats.erosionResistance  + 0.5f, 0f, 100f);
                tile.stats.soilStructure      = Mathf.Clamp(tile.stats.soilStructure      + 0.4f, 0f, 100f);
                tile.stats.waterDynamics      = Mathf.Clamp(tile.stats.waterDynamics      + 0.3f, 0f, 100f);
                tile.stats.vegetationCover    = Mathf.Clamp(tile.stats.vegetationCover    + 0.2f, 0f, 100f);
                break;

            case CoverCroppingAction.Variant.Grass:
                // Comprehensive all-rounder — SOM and ER primary
                tile.stats.soilOrganicMatter  = Mathf.Clamp(tile.stats.soilOrganicMatter  + 1.0f, 0f, 100f);
                tile.stats.erosionResistance  = Mathf.Clamp(tile.stats.erosionResistance  + 0.8f, 0f, 100f);
                tile.stats.nutrientBalance    = Mathf.Clamp(tile.stats.nutrientBalance    + 0.5f, 0f, 100f);
                tile.stats.soilStructure      = Mathf.Clamp(tile.stats.soilStructure      + 0.4f, 0f, 100f);
                tile.stats.biologicalActivity = Mathf.Clamp(tile.stats.biologicalActivity + 0.4f, 0f, 100f);
                tile.stats.waterDynamics      = Mathf.Clamp(tile.stats.waterDynamics      + 0.3f, 0f, 100f);
                tile.stats.vegetationCover    = Mathf.Clamp(tile.stats.vegetationCover    + 0.2f, 0f, 100f);
                break;

            case CoverCroppingAction.Variant.Phyto:
                // Hyperaccumulator — contam primary, slight soil secondary, NB cost
                tile.stats.contamination      = Mathf.Max(0f, tile.stats.contamination    - 1.0f);
                tile.stats.soilOrganicMatter  = Mathf.Clamp(tile.stats.soilOrganicMatter  + 0.3f, 0f, 100f);
                tile.stats.biologicalActivity = Mathf.Clamp(tile.stats.biologicalActivity + 0.2f, 0f, 100f);
                tile.stats.soilStructure      = Mathf.Clamp(tile.stats.soilStructure      + 0.2f, 0f, 100f);
                tile.stats.erosionResistance  = Mathf.Clamp(tile.stats.erosionResistance  + 0.2f, 0f, 100f);
                tile.stats.vegetationCover    = Mathf.Clamp(tile.stats.vegetationCover    + 0.1f, 0f, 100f);
                tile.stats.nutrientBalance    = Mathf.Clamp(tile.stats.nutrientBalance    - 0.2f, 0f, 100f);
                break;
        }

        manager.UpdateTileVisual(tile);
    }
}