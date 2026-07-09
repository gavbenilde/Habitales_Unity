using UnityEngine;

/// <summary>
/// CloudShadowProjector — keeps the cloud-shadow overlay quad covering the whole ground
/// (region + hinterland) as the world grows (ATMOSPHERE_BUILD_PLAN.md §4).
///
/// The LOOK lives in CloudShadowOverlay.shader (a multiplicative quad hovering just above the
/// tiles, sampling procedural noise in world XZ space — which is why the shadows conform to the
/// isometric ground for free). The MOTION lives in AtmosphereDirector's shader globals
/// (_CloudPhase/_CloudDir/_CloudScale/_CloudShadowStrength). This script only does geometry:
/// on every region spawn it re-fits the quad over TileManager's bounds plus a margin.
///
/// Wire-up (Law 3): a GameObject with a MeshFilter (Unity's built-in Quad), a MeshRenderer with
/// the CloudShadowOverlay material, and this script. Rotation/scale are overwritten at runtime.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class CloudShadowProjector : MonoBehaviour
{
    [Tooltip("Extra cells beyond the outermost occupied tile. Keep >= HinterlandRenderer's ringDepth " +
             "so the shadows roll in from the hinterland, not from a visible seam.")]
    [SerializeField, Min(0)] private int marginCells = 12;

    [Tooltip("Height above the tile tops. Must clear the tile mesh but stay under entities' feet.")]
    [SerializeField] private float yOffset = 0.05f;

    private bool _dirty = true;

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
        if (!_dirty) return;
        Fit();
    }

    private void Fit()
    {
        var tm = TileManager.Instance;
        if (tm == null) return; // stay dirty, retry

        var tiles = tm.GetAllTiles();
        if (tiles.Count == 0) return; // pre-generation — stay dirty until tiles exist
        _dirty = false;

        Vector2Int min = tiles[0].gridPosition, max = tiles[0].gridPosition;
        foreach (Tile t in tiles)
        {
            min = Vector2Int.Min(min, t.gridPosition);
            max = Vector2Int.Max(max, t.gridPosition);
        }
        min -= Vector2Int.one * marginCells;
        max += Vector2Int.one * marginCells;

        // Grid cell (x, y) occupies world [x, x+1] × [y, y+1] (GridToWorldPosition centers at +0.5).
        float sizeX = max.x - min.x + 1f;
        float sizeZ = max.y - min.y + 1f;
        transform.position = new Vector3(min.x + sizeX * 0.5f, yOffset, min.y + sizeZ * 0.5f);
        transform.rotation = Quaternion.Euler(90f, 0f, 0f); // built-in Quad faces -Z → lie flat, face up
        transform.localScale = new Vector3(sizeX, sizeZ, 1f);
    }
}
