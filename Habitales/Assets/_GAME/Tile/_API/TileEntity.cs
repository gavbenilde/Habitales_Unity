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
    private const float VEG_BOOST_PER_DAY = 2f;
    private const float ORG_BOOST_PER_DAY = 1f;
    private const float BIO_BOOST_PER_DAY = 1f;
    private const float SOIL_THRESHOLD_DIE = 10f;

    public TreeEntity()
    {
        entityType = "Mature Tree";
        health = 100f;
    }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        tile.stats.vegetationCover = Mathf.Clamp(tile.stats.vegetationCover + VEG_BOOST_PER_DAY, 0f, 100f);
        tile.stats.soilOrganicMatter = Mathf.Clamp(tile.stats.soilOrganicMatter + ORG_BOOST_PER_DAY, 0f, 100f);
        tile.stats.biologicalActivity = Mathf.Clamp(tile.stats.biologicalActivity + BIO_BOOST_PER_DAY, 0f, 100f);
        if (tile.stats.soilComposite < SOIL_THRESHOLD_DIE)
        {
            manager.TransformEntity<DeadTreeEntity>(tile);
            Debug.Log($"Tree died at {tile.gridPosition} due to poor soil.");
        }

        manager.UpdateTileVisual(tile);
    }
}

public class SaplingEntity : TileEntity
{
    private const float NUTRIENT_CONSUME_PER_DAY = 1f;
    private const float SOIL_THRESHOLD_DIE = 15f;
    private const float ORGANIC_BOOST_ON_DEATH = 10f;
    private const int DAYS_UNTIL_GROWTH = 10;
    private int daysExisting = 0;

    public SaplingEntity()
    {
        entityType = "Sapling";
        health = 100f;
    }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        daysExisting++;
        tile.stats.nutrientBalance = Mathf.Clamp(tile.stats.nutrientBalance - NUTRIENT_CONSUME_PER_DAY, 0f, 100f);
        if (tile.stats.soilComposite < SOIL_THRESHOLD_DIE)
        {
            tile.stats.soilOrganicMatter =
                Mathf.Clamp(tile.stats.soilOrganicMatter + ORGANIC_BOOST_ON_DEATH, 0f, 100f);
            manager.RemoveEntity(tile);
            Debug.Log($"Sapling died at {tile.gridPosition} — nutrients returned to soil.");
            return;
        }

        if (daysExisting >= DAYS_UNTIL_GROWTH)
        {
            manager.TransformEntity<TreeEntity>(tile);
            Debug.Log($"Sapling grew into a Tree at {tile.gridPosition}.");
        }

        manager.UpdateTileVisual(tile);
    }
}

public class SeedlingEntity : TileEntity
{
    private const float SOIL_THRESHOLD_DIE = 20f;
    private const int DAYS_UNTIL_GROWTH = 5;
    private int daysExisting = 0;

    public SeedlingEntity()
    {
        entityType = "Seedling";
        health = 100f;
    }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        daysExisting++;
        tile.stats.nutrientBalance = Mathf.Clamp(tile.stats.nutrientBalance, 0f, 100f);
        if (tile.stats.soilComposite < SOIL_THRESHOLD_DIE)
        {
            manager.RemoveEntity(tile);
            Debug.Log($"Seedling died at {tile.gridPosition} due to poor soil.");
            return;
        }

        if (daysExisting >= DAYS_UNTIL_GROWTH)
        {
            manager.TransformEntity<SaplingEntity>(tile);
            Debug.Log($"Seedling grew into a Sapling at {tile.gridPosition}.");
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

public class CoverCropEntity : TileEntity
{
    public enum CoverCropVariant { Legume, DeepRoot, General }

    public CoverCropVariant variant;
    private int daysRemaining = 30;

    public CoverCropEntity()
    {
        entityType = "CoverCrop";
    }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        switch (variant)
        {
            case CoverCropVariant.Legume:
                // Targeted boost — nutrient + organic only
                tile.stats.nutrientBalance   += 2f;
                tile.stats.soilOrganicMatter += 1f;
                break;

            case CoverCropVariant.DeepRoot:
                // Targeted boost — erosion + structure + water only
                tile.stats.erosionResistance += 2f;
                tile.stats.soilStructure     += 1f;
                tile.stats.waterDynamics     += 1f;
                break;

            case CoverCropVariant.General:
                // soilDelta 3f ÷ 6 stats = +0.5 to all soil stats
                manager.ModifyTileStats(tile, soilDelta: 3f, vegDelta: 0f, contamDelta: 0f);
                break;
        }

        daysRemaining--;
        if (daysRemaining <= 0)
            manager.RemoveEntity(tile);
    }
}

public class PhytoEntity : TileEntity
{
    public PhytoEntity()
    {
        entityType = "Phyto";
    }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        manager.ModifyTileStats(tile, soilDelta: 0f, vegDelta: 0f, contamDelta: -1.5f);

        if (tile.stats.contamination <= 0f)
            manager.RemoveEntity(tile);
    }
}

public class NitrogenFixerEntity : TileEntity
{
    public NitrogenFixerEntity()
    {
        entityType = "NitrogenFixer";
    }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        tile.stats.nutrientBalance    += 2f;
        tile.stats.biologicalActivity += 1f;
        // Intentionally no removal condition — cleared manually via ClearOrganicsAction
    }
}

public class CoverCropSeedlingEntity : TileEntity
{
    public CoverCropEntity.CoverCropVariant variant;

    private const int DAYS_UNTIL_GROWTH = 5;
    private int daysExisting = 0;

    public CoverCropSeedlingEntity()
    {
        entityType = "CoverCropSeedling";
    }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        daysExisting++;

        if (daysExisting >= DAYS_UNTIL_GROWTH)
        {
            manager.TransformEntity<CoverCropSaplingEntity>(tile);
            if (tile.entity is CoverCropSaplingEntity sapling)
                sapling.variant = variant;
            Debug.Log($"Cover crop ({variant}) seedling → sapling at {tile.gridPosition}.");
        }

        manager.UpdateTileVisual(tile);
    }
}

public class CoverCropSaplingEntity : TileEntity
{
    public CoverCropEntity.CoverCropVariant variant;

    private const int DAYS_UNTIL_GROWTH = 10;
    private int daysExisting = 0;

    public CoverCropSaplingEntity()
    {
        entityType = "CoverCropSapling";
    }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        daysExisting++;

        // Half of mature effects
        switch (variant)
        {
            case CoverCropEntity.CoverCropVariant.Legume:
                tile.stats.nutrientBalance   += 1f;
                tile.stats.soilOrganicMatter += 0.5f;
                break;

            case CoverCropEntity.CoverCropVariant.DeepRoot:
                tile.stats.erosionResistance += 1f;
                tile.stats.soilStructure     += 0.5f;
                tile.stats.waterDynamics     += 0.5f;
                break;

            case CoverCropEntity.CoverCropVariant.General:
                manager.ModifyTileStats(tile, soilDelta: 1.5f, vegDelta: 0f, contamDelta: 0f);
                break;
        }

        if (daysExisting >= DAYS_UNTIL_GROWTH)
        {
            manager.TransformEntity<CoverCropEntity>(tile);
            if (tile.entity is CoverCropEntity mature)
                mature.variant = variant;
            Debug.Log($"Cover crop ({variant}) sapling → mature at {tile.gridPosition}.");
        }

        manager.UpdateTileVisual(tile);
    }
}

public class PhytoSeedlingEntity : TileEntity
{
    private const int DAYS_UNTIL_GROWTH = 5;
    private int daysExisting = 0;

    public PhytoSeedlingEntity()
    {
        entityType = "PhytoSeedling";
    }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        daysExisting++;

        if (daysExisting >= DAYS_UNTIL_GROWTH)
        {
            manager.TransformEntity<PhytoSaplingEntity>(tile);
            Debug.Log($"Phyto seedling → sapling at {tile.gridPosition}.");
        }

        manager.UpdateTileVisual(tile);
    }
}

public class PhytoSaplingEntity : TileEntity
{
    private const int DAYS_UNTIL_GROWTH = 10;
    private int daysExisting = 0;

    public PhytoSaplingEntity()
    {
        entityType = "PhytoSapling";
    }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        daysExisting++;

        // Half of mature contamination removal
        manager.ModifyTileStats(tile, soilDelta: 0f, vegDelta: 0f, contamDelta: -0.75f);

        if (tile.stats.contamination <= 0f)
        {
            manager.RemoveEntity(tile);
            return; // tile is clean — no further work
        }

        if (daysExisting >= DAYS_UNTIL_GROWTH)
        {
            manager.TransformEntity<PhytoEntity>(tile);
            Debug.Log($"Phyto sapling → mature at {tile.gridPosition}.");
        }

        manager.UpdateTileVisual(tile);
    }
}

public class NitrogenFixerSeedlingEntity : TileEntity
{
    private const int DAYS_UNTIL_GROWTH = 5;
    private int daysExisting = 0;

    public NitrogenFixerSeedlingEntity()
    {
        entityType = "NitrogenFixerSeedling";
    }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        daysExisting++;

        if (daysExisting >= DAYS_UNTIL_GROWTH)
        {
            manager.TransformEntity<NitrogenFixerSaplingEntity>(tile);
            Debug.Log($"Nitrogen fixer seedling → sapling at {tile.gridPosition}.");
        }

        manager.UpdateTileVisual(tile);
    }
}

public class NitrogenFixerSaplingEntity : TileEntity
{
    private const int DAYS_UNTIL_GROWTH = 10;
    private int daysExisting = 0;

    public NitrogenFixerSaplingEntity()
    {
        entityType = "NitrogenFixerSapling";
    }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        daysExisting++;

        // Half of mature effects
        tile.stats.nutrientBalance    += 1f;
        tile.stats.biologicalActivity += 0.5f;

        if (daysExisting >= DAYS_UNTIL_GROWTH)
        {
            manager.TransformEntity<NitrogenFixerEntity>(tile);
            Debug.Log($"Nitrogen fixer sapling → mature at {tile.gridPosition}.");
        }

        manager.UpdateTileVisual(tile);
    }
}
