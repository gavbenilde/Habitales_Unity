using UnityEngine;

public class TileGenerationTest : MonoBehaviour {
    [SerializeField] private TileManager tileManager;
    [SerializeField] private bool generateOnStart = true;
    
    void Start() {
        if (generateOnStart) {
            GenerateGrid();
        }
    }
    
    [ContextMenu("Generate Grid")]
    void GenerateGrid() {
        if (tileManager == null) {
            Debug.LogError("TileManager reference is missing! Assign it in Inspector.");
            return;
        }
        
        Debug.Log($"Generating {tileManager.GridWidth}x{tileManager.GridHeight} grid...");
        
        int tilesSpawned = 0;
        
        for (int x = 0; x < tileManager.GridWidth; x++) {
            for (int y = 0; y < tileManager.GridHeight; y++) {
                Tile tile = tileManager.SpawnTile(x, y);
                
                if (tile != null) {
                    tilesSpawned++;
                }
            }
        }
        
        Debug.Log($"✓ Successfully spawned {tilesSpawned} tiles!");
    }
}