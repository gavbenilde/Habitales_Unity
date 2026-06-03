using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Handles tile selection in single-select, Adjacent, NonAdjacent, and FloodFill modes.
/// FloodFill mode is drag-driven: a click seeds a minimum blob (multi-source BFS), then
/// holding LMB and dragging adds seeds that steer the blob and grow it toward the cap.
/// At the cap, each new seed pops the oldest so the fixed-size blob migrates with the cursor.
/// </summary>
public class TileSelector : MonoBehaviour
{
    // ─── Inspector ───────────────────────────────────────────────────────────
    [Header("Selection Settings")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private LayerMask tileLayer;

    [Header("References")]
    [SerializeField] private TileManager tileManager;
    [SerializeField] private ActionManager actionManager;

    [Header("Input")]
    [SerializeField] private KeyCode deselectKey = KeyCode.Escape;

    [Header("Flood Fill Brush")]
    [Tooltip("Seed divisor 'C': the blob steers from at most (maxSelectableTiles / C) paint seeds. " +
             "Higher C = fewer seeds = rounder, more compact blob; lower C = more seeds = the blob " +
             "strings out along the drag path. Bounds the seed count regardless of how far you drag.")]
    [SerializeField] private int seedDivisor = 4;

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

    // ─── FloodFill state ──────────────────────────────────────────────────────
    // The selection is the first N tiles of a multi-source BFS across _floodFillSeeds,
    // so it stays a connected blob. Dragging adds seeds (steering the blob toward the
    // cursor) and grows N toward the cap; at the cap, each new seed pops the oldest so
    // the fixed-size blob migrates instead of stretching thin.
    private bool floodFillMode = false;
    private readonly Queue<Tile> _floodFillSeeds = new Queue<Tile>();
    private int _seedCap = 1; // = ceil(maxSelectableTiles / seedDivisor), computed on enter
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

        if (actionManager == null)
            actionManager = FindObjectOfType<ActionManager>();
    }

    void Update()
    {
        UpdateHoverVisuals();

        // While days are passing (an action is mid-execution), block all selection inputs.
        if (actionManager != null && actionManager.IsActionRunning) return;

        HandleMouseInput();
        HandlePaintStroke();
        HandleDeselectInput();
    }

    // =========================================================================
    // FLOOD FILL MODE  (Phase 2)
    // =========================================================================

    /// <summary>
    /// Enters FloodFill mode. Precomputes the BFS traversal order from the seed and shows
    /// an initial blob of the minimum tile count. Dragging (HandlePaintStroke → TryAddPaintSeed)
    /// adds seeds and grows the blob toward the cap.
    /// Called by ActionUI/ActionBarUI instead of EnterMultiSelectMode for FloodFill actions.
    /// </summary>
    public void EnterFloodFillMode(PlayerAction action, Tile seedTile)
    {
        multiSelectMode = true;
        floodFillMode = true;
        currentAction = action;
        originalTile = seedTile;
        currentTile = seedTile;

        ResourceManager rm = ResourceManager.Instance;

        // Calculate max selectable (minimum people per tile)
        maxSelectableTiles = Mathf.Max(1, rm.AvailablePeople / action.MinPeoplePerTile);

        // Calculate min selectable (maximum people per tile = 2x min)
        int maxPeoplePerTile = action.MinPeoplePerTile * 2;
        MinSelectableTiles = Mathf.Max(1, rm.AvailablePeople / maxPeoplePerTile);

        // Seed cap = maxSelectableTiles / seedDivisor (higher divisor → fewer seeds → rounder blob).
        _seedCap = Mathf.Max(1, Mathf.CeilToInt(maxSelectableTiles / (float)Mathf.Max(1, seedDivisor)));

        ClearSelectionVisuals();
        selectedTiles.Clear();
        adjacentAvailableTiles.Clear();

        _floodFillSeeds.Clear();
        _floodFillSeeds.Enqueue(seedTile);

        _floodFillOrder = ComputeFloodFillOrder(_floodFillSeeds);
        _currentFloodFillSize = 0;

        // Initial blob = the minimum tile count (not the max — dragging grows it from here).
        RebuildFloodFillSelection(EffectiveMinTiles);

        Debug.Log($"FloodFill mode entered | Min: {MinSelectableTiles} | Max: {maxSelectableTiles}");
    }

    // ─── Paint Stroke (LMB held, flood-fill mode) ─────────────────────────────

    /// <summary>
    /// Drag-paint: add <paramref name="tile"/> as an additional flood-fill seed so the blob
    /// steers toward the cursor. Seeds must be 8-adjacent to the existing cluster (this keeps
    /// the "clay" connected and makes the stroke pause over occupied tiles, off-grid, or fast
    /// drags that outrun adjacency — the selection is never cleared).
    ///
    /// Each added seed grows the blob by one tile until it reaches maxSelectableTiles, then the
    /// count holds steady. Independently, the seed set is capped at _seedCap (= maxSelectableTiles
    /// / seedDivisor): past that, adding a seed retires the oldest so the small cluster slides
    /// along the cursor and the blob stays round instead of stretching thin.
    /// Returns true if the seed was added.
    /// </summary>
    public bool TryAddPaintSeed(Tile tile)
    {
        if (!floodFillMode || tile == null) return false;
        if (tile.entity != null) return false;
        if (_floodFillSeeds.Contains(tile)) return false;
        if (!IsEightAdjacentToAnySeed(tile)) return false;

        bool atMax = _currentFloodFillSize >= maxSelectableTiles;

        // Cap the seed set at _seedCap so the blob stays round; retiring the oldest seed lets
        // the small cluster slide along the cursor (1-in / 1-out) instead of stringing out.
        if (_floodFillSeeds.Count >= _seedCap)
            _floodFillSeeds.Dequeue();

        _floodFillSeeds.Enqueue(tile);
        _floodFillOrder = ComputeFloodFillOrder(_floodFillSeeds);

        // Below max: grow by one tile per dragged seed. At max: hold the count steady.
        int target = atMax ? maxSelectableTiles : _currentFloodFillSize + 1;
        RebuildFloodFillSelection(target);
        return true;
    }

    bool IsEightAdjacentToAnySeed(Tile tile)
    {
        foreach (Tile seed in _floodFillSeeds)
        {
            int dx = Mathf.Abs(tile.gridPosition.x - seed.gridPosition.x);
            int dy = Mathf.Abs(tile.gridPosition.y - seed.gridPosition.y);
            if (dx <= 1 && dy <= 1 && (dx + dy) > 0) return true;
        }
        return false;
    }

    /// <summary>
    /// Full visual rebuild of the blob to the first <paramref name="count"/> entries of
    /// <see cref="_floodFillOrder"/>. Used by the initial flood and by paint-seed additions
    /// (since adding a seed reshuffles the order list).
    /// </summary>
    void RebuildFloodFillSelection(int count)
    {
        // A pocket enclosed by occupied tiles may hold fewer tiles than the
        // people-budget minimum — clamp both bounds to whatever's reachable.
        int reachable = _floodFillOrder.Count;
        int min = Mathf.Min(EffectiveMinTiles, reachable);
        int max = Mathf.Min(maxSelectableTiles, reachable);
        count = Mathf.Clamp(count, min, Mathf.Max(min, max));

        foreach (Tile tile in selectedTiles)
            UpdateTileVisual(tile, TileVisualState.Default);
        selectedTiles.Clear();

        for (int i = 0; i < count && i < _floodFillOrder.Count; i++)
        {
            Tile tile = _floodFillOrder[i];
            selectedTiles.Add(tile);
            UpdateTileVisual(tile, TileVisualState.Selected);
        }

        _currentFloodFillSize = selectedTiles.Count;
    }

    /// <summary>
    /// Multi-source BFS across the union of <paramref name="seeds"/>.
    /// All seeds are enqueued first, so they're guaranteed to appear at the
    /// front of the returned order (every seed is always part of the selection).
    /// Neighbours are shuffled to keep the frontier organic.
    /// </summary>
    private List<Tile> ComputeFloodFillOrder(IEnumerable<Tile> seeds)
    {
        var order   = new List<Tile>();
        var visited = new HashSet<Tile>();
        var queue   = new Queue<Tile>();

        foreach (Tile seed in seeds)
        {
            if (seed != null && visited.Add(seed))
                queue.Enqueue(seed);
        }

        while (queue.Count > 0)
        {
            Tile current = queue.Dequeue();
            order.Add(current);

            List<Tile> neighbors = tileManager.GetAdjacentTiles(current);
            ShuffleTiles(neighbors);

            foreach (Tile neighbor in neighbors)
            {
                // Occupied tiles act as walls — the flood cannot spread through them,
                // so an enclosed empty pocket stays bounded.
                if (neighbor.entity != null) continue;
                if (visited.Add(neighbor))
                    queue.Enqueue(neighbor);
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
        // Capture seed before wipe so its single-select highlight can be restored.
        Tile seedToRestore = originalTile;

        // ── FloodFill resets ──────────────────────────────────────────────────
        floodFillMode = false;
        _floodFillSeeds.Clear();
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

        if (seedToRestore != null)
        {
            currentTile = seedToRestore;
            UpdateTileVisual(currentTile, TileVisualState.Selected);
        }

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

    // ─── Paint Stroke (LMB held, flood-fill mode) ─────────────────────────────

    void HandlePaintStroke()
    {
        if (!floodFillMode) return;
        if (Input.GetMouseButtonDown(0)) return; // down-frame is owned by HandleMouseInput (re-seed)
        if (!Input.GetMouseButton(0)) return;
        if (EventManager.Instance != null && EventManager.Instance.IsShowingEvent) return;
        if (IsPointerOverUI()) return;
        if (hoveredTile == null) return; // off-grid → pause, keep selection intact

        TryAddPaintSeed(hoveredTile);
    }

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

        // FloodFill mode: every LMB-down re-seeds the flood (resets to a fresh min blob).
        // SelectSingleTile fires OnTileSelected → ActionBarUI.HandleTileClicked → EnterFloodFillMode(new seed).
        // The subsequent drag (with LMB held) grows/steers the blob via HandlePaintStroke → TryAddPaintSeed.
        if (floodFillMode)
        {
            // Can't re-seed the flood onto an occupied tile.
            if (tile.entity != null) return;
            SelectSingleTile(tile, worldPos);
            return;
        }

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
            // Multi-select ESC is owned by ActionBarUI.Update → Disarm → CancelSelection.
            // Handling it here too would double-fire ExitMultiSelectMode.
            if (!multiSelectMode)
                DeselectTile();
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
    /// Lower bound for the blob: the larger of MinSelectableTiles and the current seed
    /// count (every seed is always part of the selection). Internal — drives clamping.
    /// </summary>
    private int EffectiveMinTiles => Mathf.Max(MinSelectableTiles, _floodFillSeeds.Count);
}