using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using Habitales.Triggers;

/// <summary>
/// Handles tile selection in single-select, Adjacent, NonAdjacent, and FloodFill modes.
/// FloodFill mode is a paint brush (2026-07-18 rework): the player sets a brush size via
/// slider / Ctrl+Scroll, a click stamps a blob of that size, and dragging stamps more seeds
/// along the stroke — each seed PERMANENTLY stores the brush size it was painted with, so one
/// stroke can vary in thickness. The selection is the union of all seed blobs, capped by the
/// people budget: when the union would exceed it, the OLDEST seeds (whole stamped blobs) are
/// retired. The stroke tolerates a one-tile gap (it can hop a single occupied tile); landing
/// farther away resets the flood at the cursor as if the player pressed LMB there. Seeds
/// stranded by retirement (no longer chain-connected to the newest seed by Chebyshev ≤ 2)
/// are destroyed as orphans on every rebuild.
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
    [Tooltip("Smallest allowed brush stamp, in tiles. The floor is deliberately NOT the " +
             "people-budget minimum (that made the smallest stamp huge, so the union cap " +
             "left room for only ~2-3 seeds and the stroke read as one circle) — a tight " +
             "brush paints many small seeds and reads as a real stroke.")]
    [SerializeField] private int minBrushSize = 3;

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

    // ─── FloodFill (brush) state ──────────────────────────────────────────────
    // The selection is the union of per-seed blobs. Each seed caches the blob it stamped
    // (BFS of `size` tiles from `tile`, occupied tiles as walls) at placement time, so
    // earlier stroke sections keep their shape and thickness while the player repaints
    // elsewhere with a different brush size.
    private struct BrushSeed
    {
        public Tile tile;
        public int size;
        public List<Tile> blob;
    }

    private bool floodFillMode = false;
    private readonly List<BrushSeed> _brushSeeds = new List<BrushSeed>();

    // The live brush size (tiles per stamp). A persistent tool setting: survives strokes,
    // re-arms, and mid-drag resets; clamped to [_brushMin, maxSelectableTiles] on each mode
    // entry. 0 = "never set" → defaults to the people-budget minimum on first entry.
    private int _brushSize = 0;

    // Brush-size floor for this mode entry: minBrushSize, but never above the budget max.
    private int _brushMin = 1;

    // ─── Singleton ────────────────────────────────────────────────────────────

    /// <summary>
    /// Law-1 read-only singleton. Set in Awake; Law-3 warning on double-instance.
    /// </summary>
    public static TileSelector Instance { get; private set; }

    // ─── Events ───────────────────────────────────────────────────────────────
    public event Action<Tile, Vector3> OnTileSelected;
    public event Action OnTileDeselected;
    public event Action<List<Tile>> OnMultiSelectionConfirmed;
    public event Action OnMultiSelectExited;

    /// <summary>
    /// Fires when the brush-size SETTING changes (mode entry, Ctrl+Scroll, slider write) —
    /// never from dragging, which stamps seeds but doesn't touch the setting. Args:
    /// (current, min, max) so a UI slider can bind its bounds directly.
    /// </summary>
    public event Action<int, int, int> OnBrushSizeChanged;

    // ─── Lifecycle ────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[TileSelector] Duplicate instance detected on '{name}' — destroying it. " +
                             "Only one TileSelector should exist per scene.", this);
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (mainCamera == null)
            mainCamera = Camera.main;

        if (tileManager == null)
        {
            tileManager = TileManager.Instance;
            if (tileManager == null)
                Debug.LogError("TileSelector requires TileManager in scene!");
        }

        if (actionManager == null)
            actionManager = ActionManager.Instance;
    }

    void Update()
    {
        UpdateHoverVisuals();

        // While days are passing (an action is mid-execution), block all selection inputs.
        if (actionManager != null && actionManager.IsActionRunning) return;

        HandleMouseInput();
        HandlePaintStroke();
        HandleBrushResizeInput();
        HandleDeselectInput();
    }

    // =========================================================================
    // FLOOD FILL MODE  (Phase 2)
    // =========================================================================

    /// <summary>
    /// Enters FloodFill (brush) mode and stamps the first seed at the current brush size.
    /// Dragging (HandlePaintStroke → TryAddPaintSeed) stamps more seeds along the stroke.
    /// Called by ActionBarUI instead of EnterMultiSelectMode for FloodFill actions, and by
    /// HandlePaintStroke to reset the stroke when the cursor jumps beyond the one-tile gap.
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

        ClearSelectionVisuals();
        selectedTiles.Clear();
        adjacentAvailableTiles.Clear();
        _brushSeeds.Clear();

        // The brush floor is the authored minBrushSize (a tight stamp), NOT the people-budget
        // minimum — a small brush just means more seeds per stroke, the budget still caps the
        // union in RebuildBrushSelection.
        _brushMin = Mathf.Clamp(minBrushSize, 1, maxSelectableTiles);

        // Brush size persists across strokes/resets as a tool setting; first-ever entry
        // defaults to the people-budget minimum (a sensible mid-size working stamp).
        _brushSize = _brushSize <= 0
            ? Mathf.Clamp(MinSelectableTiles, _brushMin, maxSelectableTiles)
            : Mathf.Clamp(_brushSize, _brushMin, maxSelectableTiles);

        AddBrushSeed(seedTile);
        OnBrushSizeChanged?.Invoke(_brushSize, _brushMin, maxSelectableTiles);

        Debug.Log($"FloodFill mode entered | Brush: {_brushSize} | BrushMin: {_brushMin} | Max: {maxSelectableTiles}");
    }

    // ─── Paint Stroke (LMB held, flood-fill mode) ─────────────────────────────

    /// <summary>
    /// Drag-paint: stamp <paramref name="tile"/> as a new brush seed at the CURRENT brush size
    /// (the size is stored on the seed — earlier stroke sections keep theirs). Seeds must be
    /// within a ONE-TILE GAP of the current selection (Chebyshev ≤ 2 — see
    /// IsWithinOneTileGapOfSelection), so the stroke can hop over a single occupied tile
    /// instead of stalling on it. Beyond that gap the caller (HandlePaintStroke) resets the
    /// flood at the cursor instead. Returns true if the seed was stamped.
    /// </summary>
    public bool TryAddPaintSeed(Tile tile)
    {
        if (!floodFillMode || tile == null) return false;
        if (tile.entity != null) return false;
        if (ContainsSeed(tile)) return false;
        if (!IsWithinOneTileGapOfSelection(tile)) return false;

        AddBrushSeed(tile);
        return true;
    }

    bool ContainsSeed(Tile tile)
    {
        foreach (BrushSeed s in _brushSeeds)
            if (s.tile == tile) return true;
        return false;
    }

    /// <summary>Stamps a seed at the current brush size (blob cached now) and rebuilds the union.</summary>
    void AddBrushSeed(Tile tile)
    {
        _brushSeeds.Add(new BrushSeed
        {
            tile = tile,
            size = _brushSize,
            blob = FloodFrom(tile, _brushSize)
        });
        RebuildBrushSelection();
    }

    /// <summary>
    /// True when <paramref name="tile"/> is at most one tile away from the selected blob
    /// (Chebyshev distance ≤ 2 to any selected tile — seeds are always part of the selection).
    /// The one-tile tolerance lets the stroke bridge a single occupied tile (or one skipped
    /// cell on a fast drag) without the cursor getting "stuck" behind an entity.
    /// </summary>
    bool IsWithinOneTileGapOfSelection(Tile tile)
    {
        foreach (Tile sel in selectedTiles)
        {
            int dx = Mathf.Abs(tile.gridPosition.x - sel.gridPosition.x);
            int dy = Mathf.Abs(tile.gridPosition.y - sel.gridPosition.y);
            if (dx <= 2 && dy <= 2 && (dx + dy) > 0) return true;
        }
        return false;
    }

    /// <summary>
    /// Rebuilds the selection as the union of every seed's cached blob (insertion order, so
    /// visuals are stable). Budget cap: while the union exceeds maxSelectableTiles, the OLDEST
    /// seed — its whole stamped blob — is retired. This is how a long thin stroke gets eaten
    /// from the tail when the player switches to a big brush mid-stroke and keeps painting.
    /// Terminates because a single seed's blob is at most _brushSize ≤ maxSelectableTiles tiles.
    /// After retirement, orphan seeds (no longer chain-connected to the newest seed) are
    /// destroyed via <see cref="PruneOrphanSeeds"/>.
    /// </summary>
    void RebuildBrushSelection()
    {
        foreach (Tile tile in selectedTiles)
            UpdateTileVisual(tile, TileVisualState.Default);
        selectedTiles.Clear();

        var union = new List<Tile>();
        var seen  = new HashSet<Tile>();

        int start = 0;
        while (true)
        {
            union.Clear();
            seen.Clear();
            for (int i = start; i < _brushSeeds.Count; i++)
                foreach (Tile tile in _brushSeeds[i].blob)
                    if (seen.Add(tile)) union.Add(tile);

            if (union.Count <= maxSelectableTiles || start >= _brushSeeds.Count - 1) break;
            start++; // over budget → retire the oldest seed and try again
        }
        if (start > 0) _brushSeeds.RemoveRange(0, start);

        // Retirement can strand islands (a stroke that doubled back loses the section that
        // connected it). Destroy them, then recompute the union from the survivors.
        if (PruneOrphanSeeds())
        {
            union.Clear();
            seen.Clear();
            foreach (BrushSeed s in _brushSeeds)
                foreach (Tile tile in s.blob)
                    if (seen.Add(tile)) union.Add(tile);
        }

        foreach (Tile tile in union)
        {
            selectedTiles.Add(tile);
            UpdateTileVisual(tile, TileVisualState.Selected);
        }
    }

    /// <summary>
    /// Destroys orphan seeds (user spec 2026-07-20): flood across the seed TILES from the
    /// NEWEST seed (the live brush under the cursor), where two seeds are linked when their
    /// tiles are within Chebyshev distance 2 — the same one-tile-gap tolerance the stroke
    /// uses — and remove every seed the flood can't reach. Insertion order of survivors is
    /// preserved so budget retirement still eats the oldest end first. Returns true if any
    /// seed was destroyed.
    /// </summary>
    bool PruneOrphanSeeds()
    {
        if (_brushSeeds.Count <= 1) return false;

        var keep  = new bool[_brushSeeds.Count];
        var stack = new Stack<int>();
        keep[_brushSeeds.Count - 1] = true;
        stack.Push(_brushSeeds.Count - 1);

        while (stack.Count > 0)
        {
            int i = stack.Pop();
            for (int j = 0; j < _brushSeeds.Count; j++)
            {
                if (keep[j]) continue;
                int dx = Mathf.Abs(_brushSeeds[i].tile.gridPosition.x - _brushSeeds[j].tile.gridPosition.x);
                int dy = Mathf.Abs(_brushSeeds[i].tile.gridPosition.y - _brushSeeds[j].tile.gridPosition.y);
                if (dx <= 2 && dy <= 2)
                {
                    keep[j] = true;
                    stack.Push(j);
                }
            }
        }

        bool removedAny = false;
        for (int i = _brushSeeds.Count - 1; i >= 0; i--)
        {
            if (!keep[i])
            {
                _brushSeeds.RemoveAt(i);
                removedAny = true;
            }
        }
        return removedAny;
    }

    /// <summary>
    /// BFS from <paramref name="seed"/>, occupied tiles as walls (an enclosed empty pocket
    /// stays bounded, so the blob may come back smaller than <paramref name="count"/>).
    /// Neighbours are shuffled to keep the frontier organic. Computed ONCE per seed at stamp
    /// time and cached on the seed, so already-painted stroke sections never reshuffle.
    /// </summary>
    private List<Tile> FloodFrom(Tile seed, int count)
    {
        var order   = new List<Tile>(count);
        var visited = new HashSet<Tile>();
        var queue   = new Queue<Tile>();

        if (seed == null) return order;
        visited.Add(seed);
        queue.Enqueue(seed);

        while (queue.Count > 0 && order.Count < count)
        {
            Tile current = queue.Dequeue();
            order.Add(current);

            List<Tile> neighbors = tileManager.GetAdjacentTiles(current);
            ShuffleTiles(neighbors);

            foreach (Tile neighbor in neighbors)
            {
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

        // Single mode reuses multi-select but is hard-capped to one tile.
        if (action.selectionMode == SelectionMode.Single)
            maxSelectableTiles = 1;

        originalTile = initialTile;
        selectedTiles.Add(initialTile);

        UpdateAdjacentVisuals();

        Debug.Log($"Multi-select mode entered | Max tiles: {maxSelectableTiles} | Mode: {action.selectionMode}");
    }

    /// <summary>Exits multi-select (and FloodFill) mode and clears all selection state.</summary>
    /// <param name="restoreSeed">
    /// When true (cancel/ESC), the seed tile keeps its single-select highlight so the player
    /// resumes where they were. When false (confirm), everything is deselected — no tile is
    /// left highlighted once the action is committed.
    /// </param>
    public void ExitMultiSelectMode(bool restoreSeed = true)
    {
        // Capture seed before wipe so its single-select highlight can be restored.
        Tile seedToRestore = originalTile;

        // ── FloodFill (brush) resets ──────────────────────────────────────────
        // Note: _brushSize is deliberately NOT reset — it's a persistent tool setting.
        floodFillMode = false;
        _brushSeeds.Clear();

        // ── Shared resets ─────────────────────────────────────────────────────
        multiSelectMode = false;
        currentAction = null;
        originalTile = null;

        foreach (Tile tile in adjacentAvailableTiles)
            UpdateTileVisual(tile, TileVisualState.Default);
        adjacentAvailableTiles.Clear();

        ClearSelection();

        if (restoreSeed && seedToRestore != null)
        {
            currentTile = seedToRestore;
            UpdateTileVisual(currentTile, TileVisualState.Selected);
        }
        else
        {
            // Confirm path: clear the single-select highlight too, so no tile lingers.
            if (currentTile != null)
                UpdateTileVisual(currentTile, TileVisualState.Default);
            currentTile = null;
        }

        OnMultiSelectExited?.Invoke();
    }

    /// <summary>Confirms the current selection and fires OnMultiSelectionConfirmed.</summary>
    public void ConfirmSelection()
    {
        if (selectedTiles.Count == 0) return;
        OnMultiSelectionConfirmed?.Invoke(new List<Tile>(selectedTiles));
        ExitMultiSelectMode(restoreSeed: false);   // confirm = full deselect, no lingering tile
    }

    /// <summary>Cancels without executing. Called by ActionUI's Cancel button or ESC.</summary>
    public void CancelSelection() => ExitMultiSelectMode();

    // ─── Paint Stroke (LMB held, flood-fill mode) ─────────────────────────────

    void HandlePaintStroke()
    {
        if (!floodFillMode) return;
        if (Input.GetMouseButtonDown(0)) return; // down-frame is owned by HandleMouseInput (re-seed)
        if (!Input.GetMouseButton(0)) return;
        if (TriggerManager.Instance != null && TriggerManager.Instance.IsBusy) return;
        if (IsPointerOverUI()) return;
        if (hoveredTile == null) return;        // off-grid → pause, keep selection intact
        if (hoveredTile.entity != null) return; // occupied → stroke passes over it, never seeds it

        if (TryAddPaintSeed(hoveredTile)) return;

        // Not addable: either the cursor is inside the blob / on an existing seed (fine — keep
        // painting), or it's beyond the one-tile-gap bridge. In the latter case the stroke has
        // left the cluster, so reset the flood at the cursor — same as a fresh LMB press there.
        if (!selectedTiles.Contains(hoveredTile) && !IsWithinOneTileGapOfSelection(hoveredTile))
            EnterFloodFillMode(currentAction, hoveredTile);
    }

    // ─── Brush resize (Ctrl+Scroll / slider, flood-fill mode) ─────────────────

    /// <summary>
    /// Ctrl+Scroll grows/shrinks the brush-size setting one tile per notch.
    /// CameraZoomOrtho ignores scroll while Ctrl is held, so the two never fight.
    /// </summary>
    void HandleBrushResizeInput()
    {
        if (!floodFillMode) return;
        if (TriggerManager.Instance != null && TriggerManager.Instance.IsBusy) return;
        if (!(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) return;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll == 0f) return;

        SetBrushSize(_brushSize + (scroll > 0f ? 1 : -1));
    }

    /// <summary>
    /// Sets the brush-size setting (clamped to [_brushMin, maxSelectableTiles]).
    /// Called by ActionBarUI when the slider moves, and by Ctrl+Scroll. The LATEST seed is
    /// the live brush under the cursor, so it re-stamps at the new size for immediate
    /// feedback; every earlier seed keeps the size it was painted with (user spec 2026-07-18).
    /// No-op outside flood-fill mode.
    /// </summary>
    public void SetBrushSize(int tiles)
    {
        if (!floodFillMode) return;

        int clamped = Mathf.Clamp(tiles, _brushMin, maxSelectableTiles);
        if (clamped == _brushSize) return;
        _brushSize = clamped;

        if (_brushSeeds.Count > 0)
        {
            int last = _brushSeeds.Count - 1;
            BrushSeed live = _brushSeeds[last];
            live.size = _brushSize;
            live.blob = FloodFrom(live.tile, _brushSize);
            _brushSeeds[last] = live;
            RebuildBrushSelection();
        }

        OnBrushSizeChanged?.Invoke(_brushSize, _brushMin, maxSelectableTiles);
    }

    // ─── Click Handling ───────────────────────────────────────────────────────

    void HandleMouseInput()
    {
        if (TriggerManager.Instance != null && TriggerManager.Instance.IsBusy) return;

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
        
        Debug.Log(Input.GetKey(KeyCode.LeftShift));
        Debug.Log(Input.GetKey(KeyCode.RightShift));

        if (selectedTiles.Contains(tile) && isShiftHeld)
        {
            if (tile == originalTile)
            {
                Debug.Log($"Cannot deselect original tile {tile.gridPosition}");
                return;
            }
            Debug.Log("Tile Deslected");
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
        if (TriggerManager.Instance != null && TriggerManager.Instance.IsBusy) return;

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

    /// <summary>The live brush-size setting (tiles per stamp). Law-1 read-only; write via SetBrushSize.</summary>
    public int BrushSize => _brushSize;
}