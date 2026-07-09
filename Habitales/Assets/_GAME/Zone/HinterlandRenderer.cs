using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// HinterlandRenderer — the "world beyond the region" (ATMOSPHERE_BUILD_PLAN.md §2).
/// Rings of monochrome ghost tiles grown outward from every occupied cell. Constant color at
/// any distance — cutting off vision is FogOfWarRenderer's job (a fog quad drawn on top),
/// NOT a per-ring fade here.
///
/// These are NOT Tiles: no TileManager entry, no TileVisualizer, no collider (TileSelector can
/// never hit them), no GameObjects at all — the whole hinterland is drawn with
/// Graphics.DrawMeshInstanced in batches of 1023.
///
/// Rebuilds lazily: RegionManager.OnRegionGenerated marks it dirty; the next LateUpdate BFS-grows
/// the rings again (a few hundred cells — brute-force rebuild beats incremental bookkeeping).
///
/// Wire-up (Law 3): one per scene. Assign the tile MESH (the same mesh the tile prefab renders)
/// and the HinterlandTile MATERIAL — the material must have "Enable GPU Instancing" ticked,
/// which is validated here with a loud warning.
/// </summary>
public class HinterlandRenderer : MonoBehaviour
{
    [Header("Rendering")]
    [Tooltip("The tile mesh to ghost outward — use the same mesh as the gameplay tile prefab.")]
    [SerializeField] private Mesh tileMesh;
    [Tooltip("HinterlandTile.shader material. MUST have 'Enable GPU Instancing' ticked.")]
    [SerializeField] private Material material;
    [Tooltip("Vertical offset of hinterland tiles relative to gameplay tiles (slight sink reads well).")]
    [SerializeField] private float yOffset = -0.02f;

    [Header("Shape")]
    [Tooltip("How many rings of hinterland to grow outward from occupied cells (4-neighbour BFS). " +
             "Keep >= FogOfWarRenderer's fogEndRing so vision ends on tiles, not past them.")]
    [SerializeField, Min(1)] private int ringDepth = 8;

    private const int BatchSize = 1023; // Graphics.DrawMeshInstanced hard limit

    private static readonly Vector2Int[] Dirs =
        { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

    private readonly List<Matrix4x4[]> _matrixBatches = new List<Matrix4x4[]>();
    private readonly List<int> _batchCounts = new List<int>();
    private bool _dirty = true;
    private bool _warnedRefs;

    void OnEnable()
    {
        if (RegionManager.Instance != null)
            RegionManager.Instance.OnRegionGenerated += HandleRegionGenerated;
        _dirty = true;
    }

    void OnDisable()
    {
        if (RegionManager.Instance != null)
            RegionManager.Instance.OnRegionGenerated -= HandleRegionGenerated;
    }

    private void HandleRegionGenerated(RegionGenerationResult _) => _dirty = true;

    void LateUpdate()
    {
        if (tileMesh == null || material == null)
        {
            if (!_warnedRefs)
            {
                _warnedRefs = true;
                Debug.LogWarning("HinterlandRenderer: tileMesh and/or material not assigned — " +
                                 "the hinterland will not render.", this);
            }
            return;
        }

        if (_dirty) Rebuild();

        for (int i = 0; i < _matrixBatches.Count; i++)
            Graphics.DrawMeshInstanced(
                tileMesh, 0, material, _matrixBatches[i], _batchCounts[i], null,
                ShadowCastingMode.Off, receiveShadows: false, gameObject.layer);
    }

    private void Rebuild()
    {
        var tm = TileManager.Instance;
        if (tm == null) return; // stay dirty, retry next frame

        var occupied = new HashSet<Vector2Int>();
        foreach (Tile t in tm.GetAllTiles())
            occupied.Add(t.gridPosition);
        if (occupied.Count == 0) return; // pre-generation — stay dirty until tiles exist

        _dirty = false;

        if (!material.enableInstancing)
        {
            // Law 3: fix it loudly at runtime rather than silently drawing nothing.
            material.enableInstancing = true;
            Debug.LogWarning("HinterlandRenderer: material had 'Enable GPU Instancing' off — " +
                             "enabled at runtime. Tick it on the material asset to silence this.", this);
        }

        // BFS outward from every occupied cell: ring 1 touches the region, ring N = ringDepth.
        var dist = new Dictionary<Vector2Int, int>();
        var queue = new Queue<Vector2Int>();
        foreach (var c in occupied) { dist[c] = 0; queue.Enqueue(c); }

        var cells = new List<Vector2Int>();
        while (queue.Count > 0)
        {
            Vector2Int cur = queue.Dequeue();
            int d = dist[cur];
            if (d >= ringDepth) continue;

            foreach (var dir in Dirs)
            {
                Vector2Int n = cur + dir;
                if (dist.ContainsKey(n)) continue;
                dist[n] = d + 1;
                queue.Enqueue(n);
                cells.Add(n);
            }
        }

        // Pack into instancing batches (matrices only — no per-instance state anymore).
        _matrixBatches.Clear();
        _batchCounts.Clear();

        for (int start = 0; start < cells.Count; start += BatchSize)
        {
            int count = Mathf.Min(BatchSize, cells.Count - start);
            var matrices = new Matrix4x4[count];

            for (int i = 0; i < count; i++)
            {
                Vector3 world = tm.GridToWorldPosition(cells[start + i]);
                world.y += yOffset;
                matrices[i] = Matrix4x4.TRS(world, Quaternion.identity, Vector3.one);
            }

            _matrixBatches.Add(matrices);
            _batchCounts.Add(count);
        }
    }
}
