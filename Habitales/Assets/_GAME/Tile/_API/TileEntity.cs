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
    private int daysSinceSpreading = 0;

    public FireEntity()
    {
        entityType = "Fire";
        health = 100f;
    }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        
        float bonusDamage = WeatherManager.Instance != null
            ? WeatherManager.Instance.GetFireBonusDamage() : 0f;
        const float BASE_DAMAGE = 8f;
        float totalDamage = BASE_DAMAGE + bonusDamage;

        tile.stats.vegetationCover    = Mathf.Clamp(tile.stats.vegetationCover    - totalDamage, 0f, 100f);
        tile.stats.nutrientBalance    = Mathf.Clamp(tile.stats.nutrientBalance    - totalDamage, 0f, 100f);
        tile.stats.biologicalActivity = Mathf.Clamp(tile.stats.biologicalActivity - totalDamage, 0f, 100f);

        daysSinceSpreading++;
        if (daysSinceSpreading >= 2)
        {
            if (tile.stats.vegetationCover > 5f)
                TrySpread(tile, manager);
            daysSinceSpreading = 0;
        }
        if (tile.stats.vegetationCover <= 0f)
            manager.RemoveEntity(tile);
    }


    private void TrySpread(Tile tile, TileManager manager)
    {
        float spreadMult = WeatherManager.Instance != null
            ? WeatherManager.Instance.GetFireSpreadMultiplier() : 1f;

        foreach (Tile neighbor in manager.GetAdjacentTiles(tile))
        {
            if (neighbor.tv.Contains(TileOverlayType.Firebreak)) continue;
            float spreadChance = 0.2f * Mathf.Clamp01(tile.stats.vegetationCover / 100f) * spreadMult;
            if (UnityEngine.Random.value < spreadChance && neighbor.entity == null)
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
        if (Random.value < 0.018f)
            SpawnKainginFire(tile, manager);
        daysPassed++;
        if (daysPassed >= 5)
        {
            SpawnKainginFire(tile, manager);
            daysPassed = 0;
        }
    }

    private void SpawnKainginFire(Tile tile, TileManager manager)
    {
        int fireCount = Random.Range(2, 4);
        var potentialTargets = GetTilesInRadius(tile, manager, 2);
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
    private const float SOIL_THRESHOLD_DIE = 20f;

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
    private const float SOIL_THRESHOLD_DIE = 25f;
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
    private const float NUTRIENT_CONSUME_PER_DAY = 2f;
    private const float SOIL_THRESHOLD_DIE = 30f;
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
        tile.stats.nutrientBalance = Mathf.Clamp(tile.stats.nutrientBalance - NUTRIENT_CONSUME_PER_DAY, 0f, 100f);
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