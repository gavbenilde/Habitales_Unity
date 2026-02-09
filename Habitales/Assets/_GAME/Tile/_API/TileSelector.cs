using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine;

public class TileSelector : MonoBehaviour {
    [Header("Selection Settings")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private LayerMask tileLayer;
    [SerializeField] private Color selectionHighlightColor = new Color(0.3f, 0.3f, 0.3f, 1f); // Additive brightness
    
    [Header("Input")]
    [SerializeField] private KeyCode deselectKey = KeyCode.Escape;
    
    private Tile selectedTile;
    private TileVisualizer selectedVisualizer; // Store visualizer reference
    private Color originalColor;
    
    public event Action<Tile, Vector3> OnTileSelected;
    public event Action OnTileDeselected;
    
    void Awake() {
        if (mainCamera == null) {
            mainCamera = Camera.main;
        }
    }
    
    void Update() {
        HandleMouseInput();
        HandleDeselectInput();
    }
    
    void HandleMouseInput() {
        if (Input.GetMouseButtonDown(0)) {
            
            if (IsPointerOverUI()) {
                return;
            }
            
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            
            if (Physics.Raycast(ray, out hit, Mathf.Infinity, tileLayer)) {
                GameObject hitObject = hit.collider.gameObject;
                TileVisualizer visualizer = hitObject.GetComponent<TileVisualizer>();
                
                if (visualizer != null) {
                    SelectTile(visualizer, hit.point);
                }
            }
            else {
                DeselectTile();
            }
        }
    }
    
    bool IsPointerOverUI() {
        // Check if EventSystem exists and pointer is over UI
        if (EventSystem.current == null) return false;
        return EventSystem.current.IsPointerOverGameObject();
    }
    
    void HandleDeselectInput() {
        if (Input.GetKeyDown(deselectKey)) {
            DeselectTile();
        }
    }
    
    void SelectTile(TileVisualizer visualizer, Vector3 worldPos) {
        // Deselect previous tile first
        if (selectedTile != null) {
            DeselectTile();
        }
        
        // Store references
        selectedVisualizer = visualizer;
        selectedTile = visualizer.GetTileData();
        
        // Get the current base color (before highlight)
        originalColor = visualizer.GetBaseColor();
        
        // Apply highlight by adding brightness
        Color highlightedColor = originalColor + selectionHighlightColor;
        highlightedColor.a = 1f; // Keep alpha at 1
        visualizer.SetColor(highlightedColor);
        
        Debug.Log($"Selected tile at {selectedTile.gridPosition} | Base: {originalColor} | Highlighted: {highlightedColor}");
        
        // Notify listeners
        OnTileSelected?.Invoke(selectedTile, worldPos);
    }
    
    void DeselectTile() {
        if (selectedTile == null || selectedVisualizer == null) return;
        
        // Restore original color using the visualizer
        selectedVisualizer.SetColor(originalColor);
        
        Debug.Log($"Deselected tile - restored to {originalColor}");
        
        // Clear references
        selectedTile = null;
        selectedVisualizer = null;
        
        // Notify listeners
        OnTileDeselected?.Invoke();
    }
    
    public Tile GetSelectedTile() => selectedTile;
    public bool HasSelection() => selectedTile != null;
}
