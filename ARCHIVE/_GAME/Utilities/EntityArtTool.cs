using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Artist-friendly tool for manually placing entity sprites on tiles.
/// Use the + button to add entries, then click "Place All Entities" button.
/// </summary>
public class EntityArtTool : MonoBehaviour {
    
    [System.Serializable]
    public class EntityPlacement {
        [Tooltip("Grid X position (must be within grid bounds)")]
        public int x;
    
        [Tooltip("Grid Y position (must be within grid bounds)")]
        public int y;
    
        [Tooltip("2D sprite to display on this tile")]
        public Sprite sprite;
    
        [Tooltip("Custom scale for sprite (1, 1 = original size)")]
        public Vector2 customSize = Vector2.one;
    
        [Tooltip("Optional: Override sprite height offset")]
        public float customHeightOffset = 0f;
    }

    
    [Header("Setup")]
    [SerializeField] private TileManager tileManager;
    [SerializeField] private bool generateGridOnStart = true;
    
    [Header("Entity Placements")]
    [Tooltip("Click the + button to add entity placements")]
    [SerializeField] private List<EntityPlacement> entityPlacements = new List<EntityPlacement>();
    
    [Header("Visual Settings")]
    [SerializeField] private bool billboardToCamera = true;
    [SerializeField] private float defaultSpriteHeight = 0.5f;
    
    private List<GameObject> spawnedEntityObjects = new List<GameObject>();
    private Camera mainCamera;

    void Start() {
        mainCamera = Camera.main;
        
        if (generateGridOnStart) {
            // GenerateGridAndEntities();
        }
    }

    // [ContextMenu("Generate Grid + Place Entities")]
    // public void GenerateGridAndEntities() {
    //     GenerateGrid();
    //     PlaceAllEntities();
    // }
    //
    // [ContextMenu("Generate Grid Only")]
    // void GenerateGrid() {
    //     if (tileManager == null) {
    //         Debug.LogError("TileManager reference missing! Assign it in Inspector.");
    //         return;
    //     }
    //     
    //     Debug.Log($"Generating {tileManager.GridWidth}x{tileManager.GridHeight} grid...");
    //     int tilesSpawned = 0;
    //     
    //     for (int x = 0; x < tileManager.GridWidth; x++) {
    //         for (int y = 0; y < tileManager.GridHeight; y++) {
    //             Tile tile = tileManager.SpawnTile(x, y);
    //             if (tile != null) tilesSpawned++;
    //         }
    //     }
    //     
    //     Debug.Log($"✓ Successfully spawned {tilesSpawned} tiles!");
    // }
    
    [ContextMenu("Place All Entities")]
    public void PlaceAllEntities() {
        if (tileManager == null) {
            Debug.LogError("TileManager reference missing!");
            return;
        }
        
        ClearAllEntities();
        
        int successCount = 0;
        int failCount = 0;
        
        foreach (EntityPlacement placement in entityPlacements) {
            if (placement.sprite == null) {
                Debug.LogWarning($"Skipping placement at ({placement.x}, {placement.y}) - no sprite assigned!");
                failCount++;
                continue;
            }
            
            Tile tile = tileManager.GetTile(placement.x, placement.y);
            if (tile == null) {
                Debug.LogWarning($"Tile at ({placement.x}, {placement.y}) doesn't exist! Check grid bounds.");
                failCount++;
                continue;
            }
            
            GameObject entityObj = CreateEntitySprite(tile, placement);
            if (entityObj != null) {
                spawnedEntityObjects.Add(entityObj);
                successCount++;
            }
        }
        
        Debug.Log($"✓ Placed {successCount} entities ({failCount} failed)");
    }
    
    [ContextMenu("Clear All Entities")]
    public void ClearAllEntities() {
        foreach (GameObject obj in spawnedEntityObjects) {
            if (obj != null) Destroy(obj);
        }
        spawnedEntityObjects.Clear();
        Debug.Log("Cleared all entity sprites");
    }
    
    private GameObject CreateEntitySprite(Tile tile, EntityPlacement placement) {
        // Get tile center position (already at 0.5 increments)
        Vector3 worldPos = tileManager.GridToWorldPosition(tile.gridPosition);
    
        GameObject entityObj = new GameObject($"ArtEntity_{tile.gridPosition.x}_{tile.gridPosition.y}");
        entityObj.transform.position = worldPos;
    
        SpriteRenderer sr = entityObj.AddComponent<SpriteRenderer>();
        sr.sprite = placement.sprite;
        sr.sortingOrder = 10;
    
        // Apply custom size
        entityObj.transform.localScale = new Vector3(placement.customSize.x, placement.customSize.y, 1f);
    
        // Position sprite so bottom sits on tile (only adjust Y, keep X/Z centered)
        float spriteHeight = placement.sprite.bounds.size.y * placement.customSize.y;
        float heightOffset = placement.customHeightOffset != 0f 
            ? placement.customHeightOffset 
            : defaultSpriteHeight;
        
        Vector3 pos = entityObj.transform.position;
        pos.y += spriteHeight * 0.5f + heightOffset;
        entityObj.transform.position = pos;
    
        if (billboardToCamera && mainCamera != null) {
            Billboard billboard = entityObj.AddComponent<Billboard>();
            billboard.targetCamera = mainCamera;
        }
    
        return entityObj;
    }


}

/// <summary>
/// Simple billboard component that makes sprite face camera
/// </summary>
public class Billboard : MonoBehaviour {
    public Camera targetCamera;
    
    void LateUpdate() {
        if (targetCamera != null) {
            transform.rotation = targetCamera.transform.rotation;
        }
    }
}
