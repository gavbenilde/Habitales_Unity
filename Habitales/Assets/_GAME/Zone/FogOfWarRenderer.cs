using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// FogOfWarRenderer — the vision cutoff beyond the hinterland (ATMOSPHERE_BUILD_PLAN.md §2).
/// One world-anchored quad floating above the tiles, drawn with Graphics.DrawMesh, whose
/// opacity comes from a baked mask: 0 over the playable region, ramping across the hinterland
/// rings to 1 (fully solid) beyond them, then stays solid across a huge `skirt` apron — the
/// fog effectively runs to infinity, it never "cuts off". The hinterland tiles themselves stay
/// constant color (HinterlandRenderer) — THIS layer is the only thing that says "vision ends here".
///
/// The mask is a tiny R8 Texture2D, one texel per grid cell, baked from the same 4-neighbour
/// BFS ring distance HinterlandRenderer uses (0 at occupied cells). Bilinear filtering + the
/// shader's edge noise smooth the per-cell steps. Fully-fogged pixels render in the
/// `_HorizonColor` global (owned by AtmosphereDirector) — now a separate color from the camera
/// clear, so the edge fog and the backdrop sky can be tuned independently.
///
/// Rebuilds lazily, same pattern as HinterlandRenderer: RegionManager.OnRegionGenerated marks
/// it dirty; the next LateUpdate re-bakes mask + quad. No GameObjects, no colliders.
///
/// Wire-up (Law 3): one per scene (the Atmosphere object, next to HinterlandRenderer). Assign
/// the FogOfWar material. Keep fogEndRing &lt;= HinterlandRenderer.ringDepth so the fog closes
/// while there are still tiles under it — otherwise the chopped hinterland edge shows.
/// </summary>
public class FogOfWarRenderer : MonoBehaviour
{
    [Header("Rendering")]
    [Tooltip("FogOfWar.shader material. The mask texture is injected per-draw; leave its _MaskTex empty.")]
    [SerializeField] private Material material;
    [Tooltip("World Y of the fog quad — above the tile tops so the fog covers them, below nothing that matters.")]
    [SerializeField] private float fogHeight = 1.5f;

    [Header("Shape (rings of BFS distance from the region, like HinterlandRenderer)")]
    [Tooltip("Ring where the fog STARTS creeping in. Rings up to this are fully clear.")]
    [SerializeField, Min(0)] private int fogStartRing = 3;
    [Tooltip("Ring where the fog is fully solid. Keep <= HinterlandRenderer.ringDepth so vision " +
             "ends on tiles, not past their chopped edge.")]
    [SerializeField, Min(1)] private int fogEndRing = 7;
    [Tooltip("Apron of guaranteed-solid fog beyond the mask, in world units — the fog's " +
             "'to infinity'. The mask clamps to its solid border, so the whole skirt renders at " +
             "full opacity. Set larger than the camera could ever see past the play area; it also " +
             "must cover the CloudShadowOverlay quad's margin or drifting cloud blobs show on the " +
             "bare backdrop.")]
    [SerializeField, Min(0f)] private float skirt = 300f;

    // 1 texel of guaranteed-solid fog around the mask so bilinear clamp never leaks clear
    // values past the quad edge.
    private const int Pad = 1;

    private static readonly int MaskTexId = Shader.PropertyToID("_MaskTex");
    private static readonly Vector2Int[] Dirs =
        { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

    private Mesh _quad;
    private Texture2D _mask;
    private MaterialPropertyBlock _props;
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

    void OnDestroy()
    {
        if (_quad != null) Destroy(_quad);
        if (_mask != null) Destroy(_mask);
    }

    private void HandleRegionGenerated(RegionGenerationResult _) => _dirty = true;

    void LateUpdate()
    {
        if (material == null)
        {
            if (!_warnedRefs)
            {
                _warnedRefs = true;
                Debug.LogWarning("FogOfWarRenderer: material not assigned — " +
                                 "nothing will block vision beyond the hinterland.", this);
            }
            return;
        }

        if (_dirty) Rebuild();
        if (_quad == null) return;

        Graphics.DrawMesh(_quad, Matrix4x4.identity, material, gameObject.layer, null, 0,
                          _props, ShadowCastingMode.Off, receiveShadows: false);
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

        int endRing = Mathf.Max(fogEndRing, fogStartRing + 1);

        // Same BFS as HinterlandRenderer: ring distance from the nearest occupied cell.
        var dist = new Dictionary<Vector2Int, int>();
        var queue = new Queue<Vector2Int>();
        foreach (var c in occupied) { dist[c] = 0; queue.Enqueue(c); }

        Vector2Int min = new Vector2Int(int.MaxValue, int.MaxValue);
        Vector2Int max = new Vector2Int(int.MinValue, int.MinValue);
        foreach (var c in occupied) { min = Vector2Int.Min(min, c); max = Vector2Int.Max(max, c); }

        while (queue.Count > 0)
        {
            Vector2Int cur = queue.Dequeue();
            int d = dist[cur];
            if (d >= endRing) continue;

            foreach (var dir in Dirs)
            {
                Vector2Int n = cur + dir;
                if (dist.ContainsKey(n)) continue;
                dist[n] = d + 1;
                queue.Enqueue(n);
                min = Vector2Int.Min(min, n);
                max = Vector2Int.Max(max, n);
            }
        }

        // ── Bake the mask: one texel per cell + a solid-fog border (Pad). ──
        int w = (max.x - min.x + 1) + Pad * 2;
        int h = (max.y - min.y + 1) + Pad * 2;

        if (_mask == null || _mask.width != w || _mask.height != h)
        {
            if (_mask != null) Destroy(_mask);
            _mask = new Texture2D(w, h, TextureFormat.R8, mipChain: false, linear: true)
            {
                name = "FogOfWarMask",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
        }

        var pixels = new byte[w * h];
        float span = endRing - fogStartRing;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var cell = new Vector2Int(min.x + x - Pad, min.y + y - Pad);
                float fog = 1f; // anything the BFS never reached is beyond the hinterland
                if (dist.TryGetValue(cell, out int d))
                {
                    float t = Mathf.Clamp01((d - fogStartRing) / span);
                    fog = t * t * (3f - 2f * t); // smoothstep — softer ramp than linear
                }
                pixels[y * w + x] = (byte)Mathf.RoundToInt(fog * 255f);
            }
        }
        _mask.SetPixelData(pixels, 0);
        _mask.Apply(updateMipmaps: false, makeNoLongerReadable: false);

        // ── Quad: the mask's world footprint plus a huge solid skirt on every side. UVs are a
        // linear world→mask mapping, so cell centers hit texel centers over the footprint
        // (cell (gx,gy) spans world [gx, gx+1] — see TileManager.GridToWorldPosition) and the
        // skirt's out-of-range UVs clamp onto the mask's solid-fog border ⇒ full opacity all
        // the way out. "Infinity" without an infinite mesh. ──
        float x0 = min.x - Pad, z0 = min.y - Pad;
        float x1 = max.x + 1 + Pad, z1 = max.y + 1 + Pad;
        float invW = 1f / (x1 - x0), invH = 1f / (z1 - z0);

        float qx0 = x0 - skirt, qz0 = z0 - skirt;
        float qx1 = x1 + skirt, qz1 = z1 + skirt;

        if (_quad == null)
        {
            _quad = new Mesh { name = "FogOfWarQuad" };
        }
        _quad.Clear();
        _quad.vertices = new[]
        {
            new Vector3(qx0, fogHeight, qz0),
            new Vector3(qx0, fogHeight, qz1),
            new Vector3(qx1, fogHeight, qz1),
            new Vector3(qx1, fogHeight, qz0),
        };
        _quad.uv = new[]
        {
            new Vector2((qx0 - x0) * invW, (qz0 - z0) * invH),
            new Vector2((qx0 - x0) * invW, (qz1 - z0) * invH),
            new Vector2((qx1 - x0) * invW, (qz1 - z0) * invH),
            new Vector2((qx1 - x0) * invW, (qz0 - z0) * invH),
        };
        _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        _quad.RecalculateBounds();

        if (_props == null) _props = new MaterialPropertyBlock();
        _props.SetTexture(MaskTexId, _mask);
    }
}
