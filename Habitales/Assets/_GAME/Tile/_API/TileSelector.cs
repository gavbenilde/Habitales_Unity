using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Handles tile selection in both single-select and multi-select modes.
/// Multi-select supports Adjacent and NonAdjacent modes.
/// </summary>
public class TileSelector : MonoBehaviour 
{
    [Header("Selection Settings")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private LayerMask tileLayer;
    
    [Header("References")]
    [SerializeField] private TileManager tileManager;
    
    [Header("Input")]
    [SerializeField] private KeyCode deselectKey = KeyCode.Escape;
    
    // Single-select mode (original behavior)
    private Tile currentTile;
    
    // Multi-select mode
    private Tile originalTile = null; 
    private bool multiSelectMode = false;
    private PlayerAction currentAction = null;
    private List<Tile> selectedTiles = new List<Tile>();
    private int maxSelectableTiles = 0;
    private Tile hoveredTile = null;
    private Tile lastHoveredTile = null;
    private HashSet<Tile> adjacentAvailableTiles = new HashSet<Tile>();
    
    // Events
    public event Action<Tile, Vector3> OnTileSelected;
    public event Action OnTileDeselected;
    public event Action<List<Tile>> OnMultiSelectionConfirmed;
    public event Action OnMultiSelectExited;
    
    
    void Awake() 
    {
        if (mainCamera == null) 
        {
            mainCamera = Camera.main;
        }
        
        if (tileManager == null)
        {
            tileManager = FindObjectOfType<TileManager>();
            if (tileManager == null)
            {
                Debug.LogError("TileSelector requires TileManager in scene!");
            }
        }
    }
    
    void Update() 
    {
        UpdateHoverVisuals();
        HandleMouseInput();
        HandleDeselectInput();
    }
    
    /// <summary>
    /// Updates which tile is currently being hovered over.
    /// </summary>
    void UpdateHoveredTile()
    {
        if (IsPointerOverUI())
        {
            hoveredTile = null;
            return;
        }
        
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        
        if (Physics.Raycast(ray, out hit, Mathf.Infinity, tileLayer))
        {
            TileVisualizer visualizer = hit.collider.GetComponent<TileVisualizer>();
            if (visualizer != null)
            {
                hoveredTile = visualizer.GetTileData();
                return;
            }
        }
        
        hoveredTile = null;
    }
    
    void HandleMouseInput() 
    {
        if (Input.GetMouseButtonDown(0)) 
        {
            if (IsPointerOverUI()) return;
            
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            
            if (Physics.Raycast(ray, out hit, Mathf.Infinity, tileLayer)) 
            {
                TileVisualizer visualizer = hit.collider.GetComponent<TileVisualizer>();
                if (visualizer != null) 
                {
                    Tile clickedTile = visualizer.GetTileData();
                    HandleTileClick(clickedTile, hit.point);
                }
            }
            else if (!multiSelectMode)
            {
                // Clicked empty space in single-select mode
                DeselectTile();
            }
        }
    }
    
    /// <summary>
    /// Handles tile click in both single-select and multi-select modes.
    /// </summary>
    void HandleTileClick(Tile tile, Vector3 worldPos)
    {
        if (!multiSelectMode)
        {
            // Single-select mode: select this tile and show action menu
            SelectSingleTile(tile, worldPos);
        }
        else
        {
            // Multi-select mode: add/remove from selection
            bool isShiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        
            if (selectedTiles.Contains(tile) && isShiftHeld)
            {
                if (tile == originalTile)
                {
                    Debug.Log($"Cannot deselect original tile {tile.gridPosition}");
                    return; // Block deselection
                }
            
                // Shift+Click on selected tile = deselect
                DeselectTile(tile);
            }
            else if (!selectedTiles.Contains(tile))
            {
                if (CanSelectTile(tile))
                {
                    SelectTile(tile);
                }
                else
                {
                    // Split reason: cap overflow vs. adjacency failure
                    if (selectedTiles.Count >= maxSelectableTiles)
                    {
                        // Spawn a new tip instance at cursor — each click gets its own
                        if (OverflowTipSpawner.Instance != null)
                            OverflowTipSpawner.Instance.SpawnAtCursor("Your team cannot handle this much work at once!");
                    }
                    // Adjacency failures are silent (tile just stays unhighlighted)
                    Debug.Log($"Cannot select tile at {tile.gridPosition}: invalid adjacency or max reached");
                }
            }
        }
    }

    
    /// <summary>
/// Updates hover visuals based on multi-select mode and tile validity.
/// </summary>
void UpdateHoverVisuals()
{
    UpdateHoveredTile(); // This already exists from previous implementation
    
    // Clear previous hover highlight (but keep Adjacent state)
    if (lastHoveredTile != null && lastHoveredTile != hoveredTile)
    {
        if (selectedTiles.Contains(lastHoveredTile))
        {
            // Keep as Selected
            UpdateTileVisual(lastHoveredTile, TileVisualState.Selected);
        }
        else if (adjacentAvailableTiles.Contains(lastHoveredTile))
        {
            // Revert to Adjacent (no hover)
            UpdateTileVisual(lastHoveredTile, TileVisualState.Adjacent);
        }
        else if (lastHoveredTile == currentTile && !multiSelectMode)
        {
            // NEW: Keep single-selected tile as Selected ⭐
            UpdateTileVisual(lastHoveredTile, TileVisualState.Selected);
        }
        else
        {
            // Revert to Default
            UpdateTileVisual(lastHoveredTile, TileVisualState.Default);
        }
    }
    
    // Apply new hover highlight
    if (hoveredTile != null)
    {
        // NEW: Don't apply hover to the currently selected tile in single-select mode ⭐
        if (hoveredTile == currentTile && !multiSelectMode)
        {
            // Keep it as Selected (cyan), don't override with hover
            lastHoveredTile = null;
            return;
        }
        
        // Don't highlight already-selected tiles in multi-select
        if (selectedTiles.Contains(hoveredTile))
        {
            lastHoveredTile = null;
            return;
        }
        
        if (multiSelectMode)
        {
            // Multi-select mode: show hover only on adjacent available tiles
            if (adjacentAvailableTiles.Contains(hoveredTile))
            {
                UpdateTileVisual(hoveredTile, TileVisualState.AdjacentHover);
            }
        }
        else
        {
            // Single-select mode: show regular hover (but not on selected tile - already handled above)
            UpdateTileVisual(hoveredTile, TileVisualState.Hover);
        }
    }
    
    lastHoveredTile = hoveredTile;
}

    
    /// <summary>
    /// Updates which tiles are visually marked as adjacent/available.
    /// Called after every selection/deselection.
    /// </summary>
    void UpdateAdjacentVisuals()
    {
        // Clear old adjacent visuals
        foreach (Tile tile in adjacentAvailableTiles)
        {
            if (!selectedTiles.Contains(tile))
            {
                UpdateTileVisual(tile, TileVisualState.Default);
            }
        }
    
        adjacentAvailableTiles.Clear();
    
        // Don't show adjacent tiles if not in multi-select or if mode is NonAdjacent
        if (!multiSelectMode || currentAction == null)
        {
            return;
        }
    
        if (currentAction.selectionMode != SelectionMode.Adjacent)
        {
            return;
        }
    
        // Don't show if max tiles reached
        if (selectedTiles.Count >= maxSelectableTiles)
        {
            return;
        }
    
        // Find all tiles adjacent to any selected tile
        HashSet<Tile> potentialTiles = new HashSet<Tile>();
    
        foreach (Tile selectedTile in selectedTiles)
        {
            List<Tile> neighbors = tileManager.GetAdjacentTiles(selectedTile);
            foreach (Tile neighbor in neighbors)
            {
                // Add if not already selected
                if (!selectedTiles.Contains(neighbor))
                {
                    potentialTiles.Add(neighbor);
                }
            }
        }
    
        // Apply Adjacent visual state to all potential tiles
        foreach (Tile tile in potentialTiles)
        {
            adjacentAvailableTiles.Add(tile);
            UpdateTileVisual(tile, TileVisualState.Adjacent);
        }
    }
    
    /// <summary>
    /// Updates a tile's visual state via TileManager.
    /// </summary>
    void UpdateTileVisual(Tile tile, TileVisualState state)
    {
        if (tile == null)
        {
            Debug.LogWarning("UpdateTileVisual: tile is null!"); // DEBUG
            return;
        }
    
        if (tileManager == null)
        {
            Debug.LogWarning("UpdateTileVisual: tileManager is null!"); // DEBUG
            return;
        }
    
        // Get the tile's GameObject from TileManager
        GameObject tileObj = tileManager.GetTileGameObject(tile);
    
        if (tileObj == null)
        {
            Debug.LogWarning($"UpdateTileVisual: Could not find GameObject for tile {tile.gridPosition}!"); // DEBUG
            return;
        }
    
        TileVisualizer visualizer = tileObj.GetComponent<TileVisualizer>();
    
        if (visualizer == null)
        {
            Debug.LogWarning($"UpdateTileVisual: No TileVisualizer on GameObject for tile {tile.gridPosition}!"); // DEBUG
            return;
        }
        visualizer.SetVisualState(state);
    }

    
    bool IsPointerOverUI() 
    {
        if (EventSystem.current == null) return false;
        return EventSystem.current.IsPointerOverGameObject();
    }
    
    void HandleDeselectInput() 
    {
        if (Input.GetKeyDown(deselectKey)) 
        {
            if (multiSelectMode)
            {
                CancelSelection();
            }
            else
            {
                DeselectTile();
            }
        }
    }
    
    // ═══════════════════════════════════════════════════════
    // SINGLE-SELECT MODE (Original behavior)
    // ═══════════════════════════════════════════════════════
    
    /// <summary>
    /// Selects a single tile and triggers the action menu.
    /// </summary>
    void SelectSingleTile(Tile tile, Vector3 worldPos)
    {
        Debug.Log($"SelectSingleTile called for tile at {tile.gridPosition}"); // DEBUG
    
        // Clear previous selection visual
        if (currentTile != null)
        {
            Debug.Log($"Clearing previous tile {currentTile.gridPosition}"); // DEBUG
            UpdateTileVisual(currentTile, TileVisualState.Default);
        }
    
        currentTile = tile;
    
        // Highlight the selected tile
        Debug.Log($"Setting tile {currentTile.gridPosition} to Selected (cyan)"); // DEBUG
        UpdateTileVisual(currentTile, TileVisualState.Selected);
    
        OnTileSelected?.Invoke(tile, worldPos);
    }
    
    void DeselectTile()
    {
        if (currentTile == null) return;
        
        UpdateTileVisual(currentTile, TileVisualState.Default);
        
        currentTile = null;
        OnTileDeselected?.Invoke();
    }
    
    // ═══════════════════════════════════════════════════════
    // MULTI-SELECT MODE
    // ═══════════════════════════════════════════════════════
    
    /// <summary>
    /// Enters multi-select mode after an action button is clicked.
    /// Called by ActionUI.
    /// </summary>
    public void EnterMultiSelectMode(PlayerAction action, Tile initialTile)
    {
        multiSelectMode = true;
        currentAction = action;
        selectedTiles.Clear();
        adjacentAvailableTiles.Clear();
    
        // Calculate max tiles based on available people
        ResourceManager rm = ResourceManager.Instance;
        maxSelectableTiles = rm.AvailablePeople / action.MinPeoplePerTile;
    
        // Store original tile (cannot be deselected)
        originalTile = initialTile;
    
        // Auto-select the initial tile (already cyan from SelectSingleTile)
        selectedTiles.Add(initialTile);
    
        // Update adjacent visuals
        UpdateAdjacentVisuals();
    
        Debug.Log($"Multi-select mode entered | Max tiles: {maxSelectableTiles} | Mode: {action.selectionMode}");
    }


    
    /// <summary>
    /// Exits multi-select mode and clears selection.
    /// </summary>
    public void ExitMultiSelectMode()
    {
        multiSelectMode = false;
        currentAction = null;
        originalTile = null;
    
        // Clear adjacent visuals first
        foreach (Tile tile in adjacentAvailableTiles)
        {
            UpdateTileVisual(tile, TileVisualState.Default);
        }
        adjacentAvailableTiles.Clear();
    
        ClearSelection();
        
        OnMultiSelectExited?.Invoke();
    }
    /// <summary>
    /// Checks if a tile can be selected based on adjacency rules and capacity.
    /// </summary>
    bool CanSelectTile(Tile tile)
    {
        // Check if at max capacity
        if (selectedTiles.Count >= maxSelectableTiles)
        {
            return false;
        }
        
        // First tile is always valid
        if (selectedTiles.Count == 0)
        {
            return true;
        }
        
        // Check selection mode
        if (currentAction.selectionMode == SelectionMode.Adjacent)
        {
            // Must be adjacent to at least one already-selected tile
            return IsAdjacentToAnySelected(tile);
        }
        else
        {
            // Non-adjacent mode: any tile is valid
            return true;
        }
    }
    
    /// <summary>
    /// Checks if tile is adjacent (4-directional) to any selected tile.
    /// </summary>
    bool IsAdjacentToAnySelected(Tile tile)
    {
        foreach (Tile selected in selectedTiles)
        {
            if (IsAdjacent(tile, selected))
            {
                return true;
            }
        }
        return false;
    }
    
    /// <summary>
    /// Checks if two tiles are adjacent (4-directional: N/S/E/W).
    /// </summary>
    bool IsAdjacent(Tile a, Tile b)
    {
        int dx = Mathf.Abs(a.gridPosition.x - b.gridPosition.x);
        int dy = Mathf.Abs(a.gridPosition.y - b.gridPosition.y);
        return (dx == 1 && dy == 0) || (dx == 0 && dy == 1);
    }
    
    /// <summary>
    /// Adds a tile to the multi-select list.
    /// </summary>
    void SelectTile(Tile tile)
    {
        selectedTiles.Add(tile);
        UpdateTileVisual(tile, TileVisualState.Selected);
        UpdateAdjacentVisuals(); // NEW! ⭐ - Recalculate after selection
        Debug.Log($"Selected tile {tile.gridPosition} | Total: {selectedTiles.Count}/{maxSelectableTiles}");
    }
    
    /// <summary>
    /// Removes a tile from the multi-select list (Shift+Click).
    /// </summary>
    void DeselectTile(Tile tile)
    {
        selectedTiles.Remove(tile);
        UpdateTileVisual(tile, TileVisualState.Default);
        ValidateConnectedCluster();
        UpdateAdjacentVisuals();
        Debug.Log($"Deselected tile {tile.gridPosition} | Total: {selectedTiles.Count}/{maxSelectableTiles}");
    }
    
    /// <summary>
    /// Clears all selected tiles.
    /// </summary>
    void ClearSelection()
    {
        foreach (Tile tile in selectedTiles)
        {
            UpdateTileVisual(tile, TileVisualState.Default);
        }
        selectedTiles.Clear();
    
        // Clear adjacent visuals
        foreach (Tile tile in adjacentAvailableTiles)
        {
            UpdateTileVisual(tile, TileVisualState.Default);
        }

        adjacentAvailableTiles.Clear();
    
        Debug.Log("Selection cleared");
    }
    
    /// <summary>
    /// Validates that all selected tiles form a connected cluster.
    /// Removes any orphaned tiles that are no longer adjacent to the group.
    /// </summary>
    void ValidateConnectedCluster()
    {
        if (selectedTiles.Count <= 1) return; // Single tile is always valid
        if (currentAction.selectionMode != SelectionMode.Adjacent) return; // Only for Adjacent mode
    
        // Use flood-fill to find which tiles are connected to the first tile
        HashSet<Tile> connectedTiles = new HashSet<Tile>();
        Queue<Tile> toCheck = new Queue<Tile>();
    
        // Start from first selected tile
        Tile startTile = selectedTiles[0];
        toCheck.Enqueue(startTile);
        connectedTiles.Add(startTile);
    
        // Flood-fill to find all connected tiles
        while (toCheck.Count > 0)
        {
            Tile current = toCheck.Dequeue();
        
            // Check all selected tiles to see if they're adjacent to current
            foreach (Tile tile in selectedTiles)
            {
                if (!connectedTiles.Contains(tile) && IsAdjacent(current, tile))
                {
                    connectedTiles.Add(tile);
                    toCheck.Enqueue(tile);
                }
            }
        }
    
        // Remove any tiles that aren't connected
        List<Tile> tilesToRemove = new List<Tile>();
        foreach (Tile tile in selectedTiles)
        {
            if (!connectedTiles.Contains(tile))
            {
                tilesToRemove.Add(tile);
            }
        }
    
        // Remove orphaned tiles
        foreach (Tile tile in tilesToRemove)
        {
            selectedTiles.Remove(tile);
            UpdateTileVisual(tile, TileVisualState.Default);
            Debug.LogWarning($"Removed orphaned tile {tile.gridPosition} - no longer connected to cluster");
        }
    }
    
    /// <summary>
    /// Confirms the multi-select and triggers action execution.
    /// Called by ActionUI's Confirm button.
    /// </summary>
    public void ConfirmSelection()
    {
        if (selectedTiles.Count == 0) return;
        
        OnMultiSelectionConfirmed?.Invoke(new List<Tile>(selectedTiles));
        ExitMultiSelectMode();
    }
    
    /// <summary>
    /// Cancels multi-select without executing.
    /// Called by ActionUI's Cancel button or ESC key.
    /// </summary>
    public void CancelSelection()
    {
        ExitMultiSelectMode();
    }
    
    // ═══════════════════════════════════════════════════════
    // PUBLIC PROPERTIES
    // ═══════════════════════════════════════════════════════
    
    public Tile GetSelectedTile() => currentTile;
    public bool HasSelection() => currentTile != null;
    public bool IsMultiSelectMode => multiSelectMode;
    public List<Tile> GetSelectedTiles() => new List<Tile>(selectedTiles);
    public int SelectedTileCount => selectedTiles.Count;
    public int MaxSelectableTiles => maxSelectableTiles;
    public Tile GetHoveredTile() => hoveredTile;
    public PlayerAction GetCurrentAction() => currentAction;
}
