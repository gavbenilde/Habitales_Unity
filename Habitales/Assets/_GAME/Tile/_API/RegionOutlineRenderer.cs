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
    [SerializeField] private ZoneManager zoneManager;
    [SerializeField] private RegionHealthUI regionHealthUI;
    
    [Header("Outline Settings")]
    [Tooltip("Thickness of the outline in world units. 0.08–0.15 works well for 1-unit tiles.")]
    [SerializeField] private float outlineWidth = 0.12f;

    [Tooltip("How far above tile surface to draw the mesh. Prevents Z-fighting.")]
    [SerializeField] private float yOffset = 0.02f;

    [Tooltip("Outline color. Black gives the classic Borderlands ink look.")]
    [SerializeField] private Color outlineColor = Color.black;

    // Runtime objects — created once and reused
    private GameObject outlineGO;
    private MeshFilter  meshFilter;
    private MeshRenderer meshRenderer;
    private Material outlineMaterial;

    private int currentRegionID = -1;
    private bool isActive = false;

    // ─────────────────────────────────────────────────────
    // LIFECYCLE
    // ─────────────────────────────────────────────────────

    void Awake()
    {
        if (tileManager == null)
            tileManager = FindObjectOfType<TileManager>();
        
        
        if (zoneManager == null) zoneManager = FindObjectOfType<ZoneManager>();

        BuildRenderObjects();
    }

    void BuildRenderObjects()
    {
        outlineGO = new GameObject("RegionOutlineMesh");
        outlineGO.transform.SetParent(transform);
        outlineGO.transform.localPosition = Vector3.zero;

        meshFilter   = outlineGO.AddComponent<MeshFilter>();
        meshRenderer = outlineGO.AddComponent<MeshRenderer>();

        // Unlit/Color works in both Built-in and URP without any custom shader
        outlineMaterial = new Material(Shader.Find("Unlit/Color"));
        outlineMaterial.color = outlineColor;
        meshRenderer.material = outlineMaterial;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows    = false;

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

        // Sync color in case it was changed in Inspector at runtime
        outlineMaterial.color = outlineColor;

        outlineGO.SetActive(true);
        
        if (regionHealthUI != null && zoneManager != null)
        {
            float avgHealth = zoneManager.GetRegionHealth(regionID);
            regionHealthUI.Show(regionID, avgHealth);
        }
    }

    /// <summary>
    /// Hides the outline. Does not destroy the mesh — fast to re-show.
    /// </summary>
    public void ClearRegion()
    {
        if (!isActive) return;

        outlineGO.SetActive(false);
        regionHealthUI?.Hide();
        currentRegionID = -1;
        isActive = false;
    }

    // ─────────────────────────────────────────────────────
    // EDITOR HELPERS
    // ─────────────────────────────────────────────────────

    void OnValidate()
    {
        // Live-update color while tweaking in Inspector during Play Mode
        if (outlineMaterial != null)
            outlineMaterial.color = outlineColor;
    }

    void OnDestroy()
    {
        if (outlineMaterial != null)
            Destroy(outlineMaterial);
    }
}
