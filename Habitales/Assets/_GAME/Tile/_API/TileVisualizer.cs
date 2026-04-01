using System.Collections.Generic;
using UnityEngine;

public class TileVisualizer : MonoBehaviour
{
    // ── Refs ──────────────────────────────────────────────────────────────────
    private MeshRenderer meshRenderer;
    private Material     materialInstance;
    private Tile         tile;
    private TileVisualState currentState = TileVisualState.Default;

    // ── Firebreak ─────────────────────────────────────────────────────────────
    [Header("Overlays")]
    [SerializeField] private GameObject firebreakPrefab;
    private GameObject firebreakInstance;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer != null)
            materialInstance = meshRenderer.material;
    }

    public void Initialize(Tile tileData)
    {
        tile = tileData;
        SetVisualState(TileVisualState.Default);
        UpdateVisuals();
    }

    // ── Called by TileManager.UpdateTileVisual ────────────────────────────────
    public void UpdateVisuals()
    {
        if (tile == null || materialInstance == null) return;
        if (currentState == TileVisualState.Default      ||
            currentState == TileVisualState.RegionHighlight ||
            currentState == TileVisualState.RegionDimmed)
            UpdateMaterial();
    }

    // ── Visual state ──────────────────────────────────────────────────────────
    public void SetVisualState(TileVisualState state)
    {
        currentState = state;
        UpdateMaterial();
    }

    void UpdateMaterial()
    {
        if (meshRenderer == null || materialInstance == null || tile == null) return;
        meshRenderer = GetTileRenderer();
        
        // Base health colour
        // Color baseColor = GetHealthColor(tile.CalculateHealth());
        Color baseColor = meshRenderer.sharedMaterial.color;

        // Contamination tint — blends toward sickly purple above 60
        if (tile.stats.contamination > 60f)
        {
            float t = Mathf.Clamp01((tile.stats.contamination - 60f) / 40f);
            Color contaminationTint = new Color(0.45f, 0.18f, 0.50f);
            baseColor = Color.Lerp(baseColor, contaminationTint, t * 0.65f);
        }

        Color finalColor;
        switch (currentState)
        {
            case TileVisualState.Hover:
                finalColor = Color.Lerp(baseColor, Color.yellow, 0.8f);              break;
            case TileVisualState.Selected:
                finalColor = Color.Lerp(baseColor, new Color(0f, 0.8f, 0.8f, 1f), 0.7f); break;
            case TileVisualState.Adjacent:
                finalColor = Color.Lerp(baseColor, new Color(1f, 1f, 1f, 0.5f), 0.5f);  break;
            case TileVisualState.AdjacentHover:
                finalColor = Color.Lerp(baseColor, new Color(1f, 1f, 1f, 0.7f), 0.7f);  break;
            case TileVisualState.RegionHighlight:
                finalColor = Color.Lerp(baseColor, Color.white, 0.35f);             break;
            case TileVisualState.RegionDimmed:
                finalColor = Color.Lerp(baseColor, Color.black, 0.6f);              break;
            default: // Default
                finalColor = baseColor;                                              break;
        }
        materialInstance.color = finalColor;
    }

    // ── Overlay list reader ───────────────────────────────────────────────────
    /// Called by TileManager.UpdateTileVisual after every stat change.
    /// Reads tile.tv and syncs the firebreak model.
    /// Contamination tint is driven by tile.stats.contamination in UpdateMaterial
    /// so TileOverlayType.Contaminated acts as a marker only (e.g. for Examine).
    public void UpdateOverlays(List<TileOverlayType> tv)
    {
        bool hasFirebreak = tv != null && tv.Contains(TileOverlayType.Firebreak);

        if (hasFirebreak && firebreakInstance == null)
        {
            if (firebreakPrefab != null)
            {
                firebreakInstance = Instantiate(firebreakPrefab, transform);
                firebreakInstance.transform.localPosition = new Vector3(0f, 0.05f, 0f);
                firebreakInstance.transform.localRotation = Quaternion.identity;
                firebreakInstance.name = "Firebreak";
            }
            else
                Debug.LogWarning($"TileVisualizer: firebreakPrefab not assigned on {gameObject.name}!");
        }
        else if (!hasFirebreak && firebreakInstance != null)
        {
            Destroy(firebreakInstance);
            firebreakInstance = null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    Color GetHealthColor(float health)
    {
        // if (health < 33f) return new Color(0.8f, 0.2f, 0.2f);  // Red   — Critical
        // if (health < 67f) return new Color(0.9f, 0.8f, 0.3f);  // Yellow — Degraded
        // return             new Color(0.3f, 0.8f, 0.3f);         // Green  — Thriving

        return new Color(1.0f, 1.0f, 1.0f);
    }

    MeshRenderer GetTileRenderer()
    {
        meshRenderer.material.SetFloat("_Soil_Composite", (tile.GetSoilComposite() / 100f));
        meshRenderer.material.SetFloat("_Vegetation_Cover", (tile.GetVegetationCover() / 100f));
        
        return meshRenderer;
    }

    public Color GetBaseColor() =>
        tile != null ? GetHealthColor(tile.CalculateHealth()) : Color.white;

    public Tile           GetTileData()    => tile;
    public TileVisualState GetCurrentState() => currentState;

    void OnDestroy()
    {
        if (materialInstance != null) Destroy(materialInstance);
        if (firebreakInstance != null) Destroy(firebreakInstance);
    }
}
