using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TileVisualizer : MonoBehaviour {
    [SerializeField] private MeshRenderer meshRenderer;
    private Tile tile;
    private Material tileMaterial;
    
    void Awake() {
        // Get or create material instance to avoid affecting all tiles
        if (meshRenderer != null) {
            tileMaterial = meshRenderer.material;
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
            targetColor = new Color(0.8f, 0.2f, 0.2f); // Red - Critical
        }
        else if (health < 66) {
            targetColor = new Color(0.9f, 0.8f, 0.3f); // Yellow - Stable
        }
        else {
            targetColor = new Color(0.3f, 0.8f, 0.3f); // Green - Thriving
        }
        
        tileMaterial.color = targetColor;
    }
    
    void OnMouseEnter() {
        if (tile != null) {
            // UITooltip.Show(tile);
        }
    }
    
    void OnMouseExit() {
        // UITooltip.Hide();
    }
    
    // Optional: Visual feedback on click
    void OnMouseDown() {
        if (tile != null) {
            Debug.Log($"Clicked Tile at {tile.gridPosition} | Health: {tile.CalculateHealth():F1}");
            
            // Test: Modify soil quality to see visual update
            tile.stats.soilQuality += 10f;
            tile.stats.soilQuality = Mathf.Clamp(tile.stats.soilQuality, 0f, 100f);
            UpdateVisuals();
        }
    }
}