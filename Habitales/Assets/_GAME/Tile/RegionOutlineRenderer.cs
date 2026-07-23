using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the region boundary outline. Call ActivateRegion(id) when a tile is
/// clicked and ClearRegion() when the player deselects. All visual tuning
/// (thickness, color, lift) is exposed in the Inspector.
/// </summary>
public class RegionOutlineRenderer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TileManager tileManager;
    [UnityEngine.Serialization.FormerlySerializedAs("zoneManager")]
    [SerializeField] private RegionManager regionManager;
    // NOTE (2026-07-23): the RegionHealthUI header ref was REMOVED. The Selected Info Panel's
    // header is owned exclusively by SelectedInfoPanelController now; a daily push from here
    // would clobber the controller's Tile-mode content. This renderer only draws the boundary
    // mesh and tints it by region health — do NOT re-add a RegionHealthUI reference here.

    [Header("Outline Settings")]
    [Tooltip("Assign a Material asset here that uses the 'Unlit/Color' or equivalent URP unlit shader.")]
    [SerializeField] private Material outlineMaterialTemplate;

    [Tooltip("Thickness of the outline in world units. 0.08–0.15 works well for 1-unit tiles.")]
    [SerializeField] private float outlineWidth = 0.12f;

    [Tooltip("How far above tile surface to draw the mesh. Prevents Z-fighting.")]
    [SerializeField] private float yOffset = 0.02f;

    // Runtime objects — created once and reused
    private GameObject outlineGO;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Material outlineMaterial;

    private int currentRegionID = -1;
    private bool isActive = false;

    // ─────────────────────────────────────────────────────
    // LIFECYCLE
    // ─────────────────────────────────────────────────────

    void Awake()
    {
        if (tileManager == null) tileManager = TileManager.Instance;
        if (regionManager == null) regionManager = RegionManager.Instance;

        BuildRenderObjects();
    }

    void OnEnable()
    {
        // RegionManager recomputes each region's health on OnDayResolved. Subscribe so the
        // *currently-selected* region's OUTLINE COLOUR tracks live health every resolved day
        // instead of freezing at its select-time value (Law 2 — push on meaning). The header
        // panel's own daily refresh is owned by SelectedInfoPanelController, not here.
        if (RunManager.Instance != null)
            RunManager.Instance.OnDayResolved += HandleDayResolved;
    }

    void OnDisable()
    {
        if (RunManager.Instance != null)
            RunManager.Instance.OnDayResolved -= HandleDayResolved;
    }

    private void HandleDayResolved(int day)
    {
        // Only the selected region's outline is on-screen to refresh.
        if (isActive && currentRegionID >= 0)
            RefreshRegionOutline(currentRegionID);
    }

    void BuildRenderObjects()
    {
        outlineGO = new GameObject("RegionOutlineMesh");
        outlineGO.transform.SetParent(transform);
        outlineGO.transform.localPosition = Vector3.zero;

        meshFilter = outlineGO.AddComponent<MeshFilter>();
        meshRenderer = outlineGO.AddComponent<MeshRenderer>();

        // Create an instance of the assigned material template so we don't modify the project asset
        if (outlineMaterialTemplate != null)
        {
            outlineMaterial = new Material(outlineMaterialTemplate);
            meshRenderer.material = outlineMaterial;
        }
        else
        {
            Debug.LogError("RegionOutlineRenderer: Please assign an Outline Material Template in the Inspector!");
        }
        
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        outlineGO.SetActive(false);
    }

    // ─────────────────────────────────────────────────────
    // PUBLIC API
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// Draws the outline around every tile that shares regionID.
    /// Safe to call repeatedly — rebuilds the mesh each time.
    /// </summary>
    public void ActivateRegion(int regionID)
    {
        if (tileManager == null) return;

        currentRegionID = regionID;
        isActive = true;

        List<Tile> regionTiles = tileManager.GetTilesInRegion(regionID);
        if (regionTiles == null || regionTiles.Count == 0)
        {
            Debug.LogWarning($"RegionOutlineRenderer: No tiles found for region {regionID}");
            return;
        }

        Mesh mesh = RegionBoundaryMeshBuilder.Build(regionTiles, tileManager, outlineWidth, yOffset);
        meshFilter.mesh = mesh;

        outlineGO.SetActive(true);

        RefreshRegionOutline(regionID);
    }

    /// <summary>
    /// Recomputes the region's current health and tints the outline mesh by it (red 0 → green 100).
    /// Called on selection and on every resolved day while the region stays selected, so the outline
    /// tracks live health instead of freezing at its select-time snapshot. The RegionHealthUI header
    /// panel is NOT driven from here — SelectedInfoPanelController owns it (2026-07-23).
    /// </summary>
    private void RefreshRegionOutline(int regionID)
    {
        if (regionManager == null) return;

        float avgHealth = regionManager.GetRegionHealth(regionID);

        // Dynamically shift the outline color from red (0) to green (100).
        if (outlineMaterial != null)
        {
            // Health scale in Habitales is 0 to 100, so we divide by 100f for Lerp
            float t = Mathf.Pow(avgHealth / 100f, 2f); // try 2, 2.5, or 3
            outlineMaterial.color = Color.Lerp(Color.red, Color.green, t);
        }
    }

    /// <summary>
    /// Hides the outline. Does not destroy the mesh — fast to re-show.
    /// </summary>
    public void ClearRegion()
    {
        if (!isActive) return;

        outlineGO.SetActive(false);
        currentRegionID = -1;
        isActive = false;
    }

    // ─────────────────────────────────────────────────────
    // EDITOR HELPERS
    // ─────────────────────────────────────────────────────

    void OnDestroy()
    {
        if (outlineMaterial != null)
            Destroy(outlineMaterial);
    }
}
