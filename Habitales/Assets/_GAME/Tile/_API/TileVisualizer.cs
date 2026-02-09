using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TileVisualizer : MonoBehaviour {
    [SerializeField] private MeshRenderer meshRenderer;
    private Tile tile;
    private Material tileMaterial; // Instance material
    
    void Awake() {
        if (meshRenderer == null) {
            meshRenderer = GetComponent<MeshRenderer>();
        }
        
        // CRITICAL: Create instance material to avoid sharing
        if (meshRenderer != null) {
            tileMaterial = meshRenderer.material; // This creates a new instance
        }
    }
    
    public void Initialize(Tile tileData) {
        tile = tileData;
        UpdateVisuals();
    }
    
    public void UpdateVisuals() {
        if (tileMaterial == null) return;
        
        float health = tile.CalculateHealth();
        
        Color targetColor;
        if (health < 33) {
            targetColor = new Color(0.8f, 0.2f, 0.2f); // Red
        }
        else if (health < 66) {
            targetColor = new Color(0.9f, 0.8f, 0.3f); // Yellow
        }
        else {
            targetColor = new Color(0.3f, 0.8f, 0.3f); // Green
        }
        
        tileMaterial.color = targetColor;
    }
    
    // Getter for tile data
    public Tile GetTileData() => tile;
    
    // NEW: Get the base health color (before selection highlight)
    public Color GetBaseColor() {
        if (tileMaterial == null) return Color.white;
        
        float health = tile.CalculateHealth();
        
        if (health < 33) return new Color(0.8f, 0.2f, 0.2f);
        else if (health < 66) return new Color(0.9f, 0.8f, 0.3f);
        else return new Color(0.3f, 0.8f, 0.3f);
    }
    
    // NEW: Public method to set color (used by TileSelector)
    public void SetColor(Color color) {
        if (tileMaterial != null) {
            tileMaterial.color = color;
        }
    }
    
    void OnMouseEnter() {
        if (tile != null) {
            // UITooltip.Show(tile); // Uncomment when UITooltip exists
        }
    }
    
    void OnMouseExit() {
        // UITooltip.Hide(); // Uncomment when UITooltip exists
    }
    
    void OnDestroy() {
        // Clean up instance material to prevent memory leak
        if (tileMaterial != null) {
            Destroy(tileMaterial);
        }
    }
}
