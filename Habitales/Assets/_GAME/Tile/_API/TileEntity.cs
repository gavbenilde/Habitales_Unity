using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public abstract class TileEntity {
    public string entityType;
    public float health = 100f;
    
    public abstract void OnDailyUpdate(Tile tile, TileManager manager);
}

public class FireEntity : TileEntity {
    private int daysSinceSpreading = 0;
    
    public override void OnDailyUpdate(Tile tile, TileManager manager) {
    // Daily damage
    float damage = TileStats.VEGETATION_COVER_MAX * 0.08f; // 8% of VegCover
    tile.stats.vegetationCover -= damage; 
    tile.stats.soilQuality -= damage;
    
    daysSinceSpreading++;
    if (daysSinceSpreading >= 4) {
        TrySpread(tile, manager);
        daysSinceSpreading = 0;
    }
        
    if (tile.stats.vegetationCover <= 0) {
        manager.RemoveEntity(tile);
    }
}
    
private void TrySpread(Tile tile, TileManager manager) {
    foreach (Tile neighbor in manager.GetAdjacentTiles(tile)) {
        if (!neighbor.stats.hasFirebreak && Random.value < 0.2f) {
            manager.SpawnEntity<FireEntity>(neighbor);
        }
    }
}
}

public class VillageEntity : TileEntity {
    public override void OnDailyUpdate(Tile tile, TileManager manager) {
        // 12% weekly chance = ~1.8% daily
        if (Random.value < 0.018f) {
            SpawnKainginFire(tile, manager);
        }
    }
    
    private void SpawnKainginFire(Tile tile, TileManager manager) {
        // Get tiles within radius 3-5 of village
        int fireCount = Random.Range(2, 4); // 2-3 fires per event
        List<Tile> potentialTargets = GetTilesInRadius(tile, manager, 5);
        
        for (int i = 0; i < fireCount && potentialTargets.Count > 0; i++) {
            // Pick random tile from potential targets
            int randomIndex = Random.Range(0, potentialTargets.Count);
            Tile target = potentialTargets[randomIndex];
            
            // Only spawn fire if tile doesn't have a building
            if (target.entity == null || 
                !(target.entity is VillageEntity) && 
                !(target.entity is FactoryEntity)) {
                manager.SpawnEntity<FireEntity>(target);
            }
            
            potentialTargets.RemoveAt(randomIndex);
        }
    }
    
    // Helper method to get tiles in radius
    private List<Tile> GetTilesInRadius(Tile center, TileManager manager, int radius) {
        List<Tile> tilesInRadius = new List<Tile>();
        
        for (int x = -radius; x <= radius; x++) {
            for (int y = -radius; y <= radius; y++) {
                if (x == 0 && y == 0) continue; // Skip center tile
                
                Tile target = manager.GetTile(
                    center.gridPosition.x + x,
                    center.gridPosition.y + y
                );
                
                if (target != null) {
                    tilesInRadius.Add(target);
                }
            }
        }
        
        return tilesInRadius;
    }
}

public class FactoryEntity : TileEntity {
    public override void OnDailyUpdate(Tile tile, TileManager manager) {
        // TODO: Implement factory trash dumping (33% weekly chance)
        // For now, does nothing
    }
}

public class TrashBioEntity : TileEntity {
    private int daysExisting = 0;
    
    public override void OnDailyUpdate(Tile tile, TileManager manager) {
        daysExisting++;
        if (daysExisting >= 7) {
            tile.stats.soilQuality += 10f;
            manager.RemoveEntity(tile);
        }
    }
}

public class DeadTreeEntity : TileEntity {
    private const float SOIL_BOOST_PER_DAY = 0.3f;
    private const int DECOMPOSITION_DAYS = 30;
    
    private int daysExisting = 0;
    
    public DeadTreeEntity() {
        entityType = "DeadTree";
        health = 0f;
    }
    
    public override void OnDailyUpdate(Tile tile, TileManager manager) {
        daysExisting++;
        
        // Decompose → boost soil quality
        tile.stats.soilQuality += SOIL_BOOST_PER_DAY;
        tile.stats.soilQuality = Mathf.Clamp(tile.stats.soilQuality, 0f, 100f);
        
        // Fully decomposed
        if (daysExisting >= DECOMPOSITION_DAYS) {
            manager.RemoveEntity(tile);
            Debug.Log($"Dead tree fully decomposed at {tile.gridPosition}");
        }
        
        manager.UpdateTileVisual(tile);
    }
}

public class TreeEntity : TileEntity {
    private const float VEGETATION_BOOST_PER_DAY = 0.5f;
    private const float SOIL_THRESHOLD_TO_DIE = 20f;
    
    public TreeEntity() {
        entityType = "Tree";
        health = 100f;
    }
    
    public override void OnDailyUpdate(Tile tile, TileManager manager) {
        // Boost vegetation
        tile.stats.vegetationCover += VEGETATION_BOOST_PER_DAY;
        tile.stats.vegetationCover = Mathf.Clamp(tile.stats.vegetationCover, 0f, 100f);
        
        // Check if soil is too degraded
        if (tile.stats.soilQuality < SOIL_THRESHOLD_TO_DIE) {
            manager.TransformEntity<DeadTreeEntity>(tile);
            Debug.Log($"Tree died at {tile.gridPosition} due to poor soil quality");
        }
        
        manager.UpdateTileVisual(tile);
    }
}
