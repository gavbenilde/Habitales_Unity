using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Handles tile selection in single-select, Adjacent, NonAdjacent, and FloodFill modes.
/// FloodFill mode is driven entirely by SetFloodFillSize(); mouse clicks are suppressed.
/// </summary>
public class TileSelector : MonoBehaviour
{
    // ─── Inspector ───────────────────────────────────────────────────────────
    [Header("Selection Settings")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private LayerMask tileLayer;

    [Header("References")]
    [SerializeField] private TileManager tileManager;

    [Header("Input")]
    [SerializeField] private KeyCode deselectKey = KeyCode.Escape;

    // ─── Single-select state ─────────────────────────────────────────────────
    private Tile currentTile;

    // ─── Shared multi-select state ────────────────────────────────────────────
    private Tile originalTile = null;
    private bool multiSelectMode = false;
    private PlayerAction currentAction = null;
    private List<Tile> selectedTiles = new List<Tile>();
    public int MinSelectableTiles { get; private set; } = 1;
    private int maxSelectableTiles = 0;
    private Tile hoveredTile = null;
    private Tile lastHoveredTile = null;
    private HashSet<Tile> adjacentAvailableTiles = new HashSet<Tile>();

    // ─── FloodFill state (Phase 2) ────────────────────────────────────────────
    private bool floodFillMode = false;
    private Tile floodFillSeedTile = null;
    private List<Tile> _floodFillOrder = new List<Tile>();
    private int _currentFloodFillSize = 0;

    // ─── Events ───────────────────────────────────────────────────────────────
    public event Action<Tile, Vector3> OnTileSelected;
    public event Action OnTileDeselected;
    public event Action<List<Tile>> OnMultiSelectionConfirmed;
    public event Action OnMultiSelectExited;

    // ─── Lifecycle ────────────────────────────────────────────────────────────
    void Awake()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (tileManager == null)
        {
            tileManager = FindObjectOfType<TileManager>();
            if (tileManager == null)
                Debug.LogError("TileSelector requires TileManager in scene!");
        }
    }

    void Update()
    {
        UpdateHoverVisuals();
        HandleMouseInput();
        HandleDeselectInput();
    }

    // =========================================================================
    // FLOOD FILL MODE  (Phase 2)
    // =========================================================================

    /// <summary>
    /// Enters FloodFill mode. Precomputes the full BFS traversal order from the
    /// seed tile so that SetFloodFillSize() is O(n) thereafter.
    /// Called by ActionUI instead of EnterMultiSelectMode for FloodFill actions.
    /// </summary>
    public void EnterFloodFillMode(PlayerAction action, Tile seedTile)
    {
        multiSelectMode = true;
        floodFillMode = true;
        currentAction = action;
        floodFillSeedTile = seedTile;
        originalTile = seedTile;

        ResourceManager rm = ResourceManager.Instance;
        
        // Calculate max selectable (minimum people per tile)
        maxSelectableTiles = Mathf.Max(1, rm.AvailablePeople / action.MinPeoplePerTile);
        
        // Calculate min selectable (maximum people per tile = 2x min)
        int maxPeoplePerTile = action.MinPeoplePerTile * 2;
        MinSelectableTiles = Mathf.Max(1, rm.AvailablePeople / maxPeoplePerTile);

        ClearSelectionVisuals();
        selectedTiles.Clear();
        adjacentAvailableTiles.Clear();

        _floodFillOrder = ComputeFloodFillOrder(seedTile);
        _currentFloodFillSize = 0;

        // Initialize using the calculated minimum instead of 1
        SetFloodFillSize(MinSelectableTiles);

        Debug.Log($"FloodFill mode entered | Min: {MinSelectableTiles} | Max: {maxSelectableTiles}");
    }
    
    /// <summary>
    /// Rebuilds the selected tile set to the first <paramref name="count"/> tiles
    /// in the precomputed BFS order. Called every time the slider value changes.
    /// </summary>
    public void SetFloodFillSize(int count)
    {
        if (!floodFillMode) return;

        // Clamp using the new minimum boundary
        count = Mathf.Clamp(count, MinSelectableTiles, Mathf.Min(maxSelectableTiles, _floodFillOrder.Count));
        if (count == _currentFloodFillSize) return;

        if (count < _currentFloodFillSize)
        {
            for (int i = count; i < _currentFloodFillSize; i++)
                UpdateTileVisual(_floodFillOrder[i], TileVisualState.Default);
        }
        else
        {
            for (int i = _currentFloodFillSize; i < count; i++)
                UpdateTileVisual(_floodFillOrder[i], TileVisualState.Selected);
        }

        selectedTiles.Clear();
        for (int i = 0; i < count; i++)
            selectedTiles.Add(_floodFillOrder[i]);

        _currentFloodFillSize = count;
    }

    /// <summary>
    /// BFS from <paramref name="seed"/> across all reachable tiles.
    /// Neighbours are shuffled before each enqueue to produce an organic blob.
    /// </summary>
    private List<Tile> ComputeFloodFillOrder(Tile seed)
    {
        var order   = new List<Tile>();
        var visited = new HashSet<Tile>();
        var queue   = new Queue<Tile>();

        queue.Enqueue(seed);
        visited.Add(seed);

        while (queue.Count > 0)
        {
            Tile current = queue.Dequeue();
            order.Add(current);

            List<Tile> neighbors = tileManager.GetAdjacentTiles(current);
            ShuffleTiles(neighbors);

            foreach (Tile neighbor in neighbors)
            {
                if (!visited.Contains(neighbor))
                {
                    visited.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
            }
        }

        return order;
    }

    /// <summary>Fisher-Yates in-place shuffle for List&lt;Tile&gt;.</summary>
    private void ShuffleTiles(List<Tile> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // =========================================================================
    // MULTI-SELECT MODE  (original — Adjacent / NonAdjacent, unchanged)
    // =========================================================================

    /// <summary>
    /// Enters click-based multi-select mode.
    /// Only called for Adjacent and NonAdjacent actions; FloodFill uses EnterFloodFillMode.
    /// </summary>
    public void EnterMultiSelectMode(PlayerAction action, Tile initialTile)
    {
        multiSelectMode = true;
        currentAction = action;
        selectedTiles.Clear();
        adjacentAvailableTiles.Clear();

        ResourceManager rm = ResourceManager.Instance;
        maxSelectableTiles = rm.AvailablePeople / action.MinPeoplePerTile;

        originalTile = initialTile;
        selectedTiles.Add(initialTile);

        UpdateAdjacentVisuals();

        Debug.Log($"Multi-select mode entered | Max tiles: {maxSelectableTiles} | Mode: {action.selectionMode}");
    }

    /// <summary>Exits multi-select (and FloodFill) mode and clears all selection state.</summary>
    public void ExitMultiSelectMode()
    {
        // ── FloodFill resets ──────────────────────────────────────────────────
        floodFillMode = false;
        floodFillSeedTile = null;
        _floodFillOrder.Clear();
        _currentFloodFillSize = 0;

        // ── Shared resets ─────────────────────────────────────────────────────
        multiSelectMode = false;
        currentAction = null;
        originalTile = null;

        foreach (Tile tile in adjacentAvailableTiles)
            UpdateTileVisual(tile, TileVisualState.Default);
        adjacentAvailableTiles.Clear();

        ClearSelection();
        OnMultiSelectExited?.Invoke();
    }

    /// <summary>Confirms the current selection and fires OnMultiSelectionConfirmed.</summary>
    public void ConfirmSelection()
    {
        if (selectedTiles.Count == 0) return;
        OnMultiSelectionConfirmed?.Invoke(new List<Tile>(selectedTiles));
        ExitMultiSelectMode();
    }

    /// <summary>Cancels without executing. Called by ActionUI's Cancel button or ESC.</summary>
    public void CancelSelection() => ExitMultiSelectMode();

    // ─── Click Handling ───────────────────────────────────────────────────────

    void HandleMouseInput()
    {
        if (EventManager.Instance != null && EventManager.Instance.IsShowingEvent) return;
        
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

                    float currentHealth = clickedTile.stats.CalculateHealth();
                    
                    if (currentHealth >= 66f)
                    {
                        AudioManager.instance.PlayOneShot(FMODEvents.instance.tileSelectedHealthy, mainCamera.transform.position);
                    }
                    else if (currentHealth >= 0f)
                    {
                        AudioManager.instance.PlayOneShot(FMODEvents.instance.tileSelectedCritical, mainCamera.transform.position);
                    }
                    else
                    {
                        AudioManager.instance.PlayOneShot(FMODEvents.instance.tileSelectedHealthy, mainCamera.transform.position);
                    }
                }
            }
            else if (!multiSelectMode)
            {
                DeselectTile();
            }
        }
    }

    void HandleTileClick(Tile tile, Vector3 worldPos)
    {
        if (!multiSelectMode)
        {
            SelectSingleTile(tile, worldPos);
            return;
        }

        // FloodFill mode: clicks are suppressed; slider controls everything.
        if (floodFillMode) return;

        // Adjacent / NonAdjacent click logic (original, unchanged).
        bool isShiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (selectedTiles.Contains(tile) && isShiftHeld)
        {
            if (tile == originalTile)
            {
                Debug.Log($"Cannot deselect original tile {tile.gridPosition}");
                return;
            }
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
                if (selectedTiles.Count >= maxSelectableTiles)
                {
                    if (OverflowTipSpawner.Instance != null)
                        OverflowTipSpawner.Instance.SpawnAtCursor("Your team cannot handle this much work at once!");
                }
                Debug.Log($"Cannot select tile at {tile.gridPosition}: invalid adjacency or max reached");
            }
        }
    }

    // ─── Adjacent visual update ───────────────────────────────────────────────

    void UpdateAdjacentVisuals()
    {
        foreach (Tile tile in adjacentAvailableTiles)
            if (!selectedTiles.Contains(tile))
                UpdateTileVisual(tile, TileVisualState.Default);
        adjacentAvailableTiles.Clear();

        // FloodFill mode has no adjacent tile highlights — the blob IS the selection.
        if (floodFillMode) return;

        if (!multiSelectMode || currentAction == null) return;
        if (currentAction.selectionMode != SelectionMode.Adjacent) return;
        if (selectedTiles.Count >= maxSelectableTiles) return;

        HashSet<Tile> potentialTiles = new HashSet<Tile>();
        foreach (Tile selectedTile in selectedTiles)
        {
            List<Tile> neighbors = tileManager.GetAdjacentTiles(selectedTile);
            foreach (Tile neighbor in neighbors)
                if (!selectedTiles.Contains(neighbor))
                    potentialTiles.Add(neighbor);
        }

        foreach (Tile tile in potentialTiles)
        {
            adjacentAvailableTiles.Add(tile);
            UpdateTileVisual(tile, TileVisualState.Adjacent);
        }
    }

    // ─── Hover visuals ────────────────────────────────────────────────────────

    /// <summary>
    /// Updates hover visuals based on multi-select mode and tile validity.
    /// </summary>
    void UpdateHoverVisuals()
    {
        UpdateHoveredTile();

        // Restore previous hovered tile's correct visual state.
        if (lastHoveredTile != null && lastHoveredTile != hoveredTile)
        {
            if (selectedTiles.Contains(lastHoveredTile))
                UpdateTileVisual(lastHoveredTile, TileVisualState.Selected);
            else if (!floodFillMode && adjacentAvailableTiles.Contains(lastHoveredTile))
                UpdateTileVisual(lastHoveredTile, TileVisualState.Adjacent);
            else if (lastHoveredTile == currentTile && !multiSelectMode)
                UpdateTileVisual(lastHoveredTile, TileVisualState.Selected);
            else
                UpdateTileVisual(lastHoveredTile, TileVisualState.Default);
        }

        // Apply new hover highlight.
        if (hoveredTile != null)
        {
            // Don't apply hover to the currently selected tile in single-select mode.
            if (hoveredTile == currentTile && !multiSelectMode)
            {
                lastHoveredTile = null;
                return;
            }

            // Don't highlight already-selected tiles in multi-select.
            if (selectedTiles.Contains(hoveredTile))
            {
                lastHoveredTile = null;
                return;
            }

            if (multiSelectMode)
            {
                if (floodFillMode)
                {
                    // FloodFill: plain hover on non-selected tiles only.
                    UpdateTileVisual(hoveredTile, TileVisualState.Hover);
                }
                else if (adjacentAvailableTiles.Contains(hoveredTile))
                {
                    // Adjacent mode: AdjacentHover only on eligible tiles.
                    UpdateTileVisual(hoveredTile, TileVisualState.AdjacentHover);
                }
            }
            else
            {
                UpdateTileVisual(hoveredTile, TileVisualState.Hover);
            }
        }

        lastHoveredTile = hoveredTile;
    }

    void UpdateHoveredTile()
    {
        if (IsPointerOverUI()) { hoveredTile = null; return; }

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, Mathf.Infinity, tileLayer))
        {
            TileVisualizer visualizer = hit.collider.GetComponent<TileVisualizer>();
            if (visualizer != null) { hoveredTile = visualizer.GetTileData(); return; }
        }
        hoveredTile = null;
    }

    // ─── Single-select helpers ────────────────────────────────────────────────

    void SelectSingleTile(Tile tile, Vector3 worldPos)
    {
        Debug.Log($"SelectSingleTile called for tile at {tile.gridPosition}");

        if (currentTile != null)
        {
            Debug.Log($"Clearing previous tile {currentTile.gridPosition}");
            UpdateTileVisual(currentTile, TileVisualState.Default);
        }

        currentTile = tile;
        Debug.Log($"Setting tile {currentTile.gridPosition} to Selected (cyan)");
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

    // ─── Multi-select helpers ─────────────────────────────────────────────────

    void SelectTile(Tile tile)
    {
        selectedTiles.Add(tile);
        UpdateTileVisual(tile, TileVisualState.Selected);
        UpdateAdjacentVisuals(); // Recalculate after selection (original behaviour preserved)
        Debug.Log($"Selected tile {tile.gridPosition} | Total: {selectedTiles.Count}/{maxSelectableTiles}");
    }

    void DeselectTile(Tile tile)
    {
        selectedTiles.Remove(tile);
        UpdateTileVisual(tile, TileVisualState.Default);
        ValidateConnectedCluster();
        UpdateAdjacentVisuals();
        Debug.Log($"Deselected tile {tile.gridPosition} | Total: {selectedTiles.Count}/{maxSelectableTiles}");
    }

    public void ClearSelection()
    {
        foreach (Tile tile in selectedTiles)
            UpdateTileVisual(tile, TileVisualState.Default);
        selectedTiles.Clear();

        foreach (Tile tile in adjacentAvailableTiles)
            UpdateTileVisual(tile, TileVisualState.Default);
        adjacentAvailableTiles.Clear();

        Debug.Log("Selection cleared");
    }

    /// <summary>Clears selection visuals without modifying the list — used before a FloodFill rebuild.</summary>
    void ClearSelectionVisuals()
    {
        foreach (Tile tile in selectedTiles)
            UpdateTileVisual(tile, TileVisualState.Default);
    }

    void ValidateConnectedCluster()
    {
        if (selectedTiles.Count <= 1) return;
        if (currentAction == null || currentAction.selectionMode != SelectionMode.Adjacent) return;

        HashSet<Tile> connectedTiles = new HashSet<Tile>();
        Queue<Tile> toCheck = new Queue<Tile>();
        Tile startTile = selectedTiles[0];
        toCheck.Enqueue(startTile);
        connectedTiles.Add(startTile);

        while (toCheck.Count > 0)
        {
            Tile current = toCheck.Dequeue();
            foreach (Tile tile in selectedTiles)
            {
                if (!connectedTiles.Contains(tile) && IsAdjacent(current, tile))
                {
                    connectedTiles.Add(tile);
                    toCheck.Enqueue(tile);
                }
            }
        }

        List<Tile> tilesToRemove = new List<Tile>();
        foreach (Tile tile in selectedTiles)
            if (!connectedTiles.Contains(tile))
                tilesToRemove.Add(tile);

        foreach (Tile tile in tilesToRemove)
        {
            selectedTiles.Remove(tile);
            UpdateTileVisual(tile, TileVisualState.Default);
            Debug.LogWarning($"Removed orphaned tile {tile.gridPosition} - no longer connected to cluster");
        }
    }

    bool CanSelectTile(Tile tile)
    {
        if (selectedTiles.Count >= maxSelectableTiles) return false;
        if (selectedTiles.Count == 0) return true;
        if (currentAction.selectionMode == SelectionMode.Adjacent)
            return IsAdjacentToAnySelected(tile);
        return true;
    }

    bool IsAdjacentToAnySelected(Tile tile)
    {
        foreach (Tile selected in selectedTiles)
            if (IsAdjacent(tile, selected)) return true;
        return false;
    }

    bool IsAdjacent(Tile a, Tile b)
    {
        int dx = Mathf.Abs(a.gridPosition.x - b.gridPosition.x);
        int dy = Mathf.Abs(a.gridPosition.y - b.gridPosition.y);
        return (dx == 1 && dy == 0) || (dx == 0 && dy == 1);
    }

    // ─── Visual dispatch ──────────────────────────────────────────────────────

    void UpdateTileVisual(Tile tile, TileVisualState state)
    {
        if (tile == null) { Debug.LogWarning("UpdateTileVisual: tile is null!"); return; }
        if (tileManager == null) { Debug.LogWarning("UpdateTileVisual: tileManager is null!"); return; }

        GameObject tileObj = tileManager.GetTileGameObject(tile);
        if (tileObj == null) { Debug.LogWarning($"UpdateTileVisual: Could not find GameObject for tile {tile.gridPosition}!"); return; }

        TileVisualizer visualizer = tileObj.GetComponent<TileVisualizer>();
        if (visualizer == null) { Debug.LogWarning($"UpdateTileVisual: No TileVisualizer on GameObject for tile {tile.gridPosition}!"); return; }

        visualizer.SetVisualState(state);
    }

    // ─── Utility ─────────────────────────────────────────────────────────────

    bool IsPointerOverUI()
    {
        if (EventSystem.current == null) return false;
        return EventSystem.current.IsPointerOverGameObject();
    }

    void HandleDeselectInput()
    {
        if (EventManager.Instance != null && EventManager.Instance.IsShowingEvent) return;

        if (Input.GetKeyDown(deselectKey))
        {
            if (multiSelectMode) CancelSelection();
            else DeselectTile();
        }
    }

    // ─── Public Properties ────────────────────────────────────────────────────

    public Tile GetSelectedTile() => currentTile;
    public bool HasSelection() => currentTile != null;
    public bool IsMultiSelectMode => multiSelectMode;
    public bool IsFloodFillMode => floodFillMode;
    public List<Tile> GetSelectedTiles() => new List<Tile>(selectedTiles);
    public int SelectedTileCount => selectedTiles.Count;
    public int MaxSelectableTiles => maxSelectableTiles;
    public Tile GetHoveredTile() => hoveredTile;
    public PlayerAction GetCurrentAction() => currentAction;

    /// <summary>
    /// Upper bound for the slider: the smaller of maxSelectableTiles and how
    /// many tiles the BFS could actually reach from the seed.
    /// Only meaningful in FloodFill mode.
    /// </summary>
    public int FloodFillReachableCount => _floodFillOrder.Count;
}