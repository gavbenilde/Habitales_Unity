using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Habitales.UI;

/// <summary>
/// Mode-aware health header for the Selected Info Panel. Renders EITHER a Region
/// (label "Zone {id}", region average health + region trend) OR a single Tile
/// (entity/tile name, tile health + tile trend), selected by the caller — the shared
/// <c>SelectedInfoPanelController</c> owns which mode is showing. Kept a SEPARATE script
/// from the body (<c>InspectPanelUI</c>); both are parented under one shared panel root.
///
/// Text fields (regionLabel / healthText) are optional — disable their GameObjects
/// in the prefab for a minimal, numbers-and-bar-only readout.
///
/// Legacy note: the two-arg / three-arg <c>Show</c> overloads are preserved for
/// RegionOutlineRenderer, which still pushes the selected region's live health daily.
/// </summary>
public class RegionHealthUI : MonoBehaviour
{
    [Header("Systems")]
    [SerializeField] private TileSelector tileSelector;
    
    [Header("References")]
    [SerializeField] private GameObject panel;
    [SerializeField] private TextMeshProUGUI regionLabel;   // optional — e.g. "Zone 1" or an entity name
    [SerializeField] private TextMeshProUGUI healthText;    // optional — e.g. "Avg. Health: 74.2% (Degraded)"

    [Header("Header Background (tinted per mode by the controller)")]
    [SerializeField] private Image headerBackground;        // optional — SetHeaderColor tints it

    [Header("Health Bar (0–100)")]
    [SerializeField] private Slider healthSlider;           // min 0 / max 100
    [SerializeField] private Image  fillImage;              // optional — tinted by state

    [Header("Trend")]
    [SerializeField] private TrendIndicatorUI trendIndicator;          // optional — tiered up/down arrow
    [SerializeField] private TrendDualIndicatorUI trendDualIndicator;  // optional — decaying/thriving pair flanking the bar

    [Header("Health Colors")]
    [SerializeField] private Color thrivingColor  = new Color(0.3f, 0.9f, 0.3f);
    [SerializeField] private Color degradedColor  = new Color(0.9f, 0.8f, 0.3f);
    [SerializeField] private Color criticalColor  = new Color(0.9f, 0.3f, 0.3f);

    void Awake()
    {
        if (healthSlider != null)
        {
            healthSlider.minValue = 0;
            healthSlider.maxValue = 100;
        }

        Hide();
        //
        // if (tileSelector != null)
        // {
        //     tileSelector.OnTileSelected   += HandleTileSelected;
        //     tileSelector.OnTileDeselected += HandleTileDeselected;
        // }
    }
    //
    // void OnDestroy()
    // {
    //     if (tileSelector != null)
    //     {
    //         tileSelector.OnTileSelected   -= HandleTileSelected;
    //         tileSelector.OnTileDeselected -= HandleTileDeselected;
    //     }
    // }
    //
    // private void HandleTileSelected(Tile tile, Vector3 _) => ;
    // private void HandleTileDeselected() => Hide();

    // ── Region mode ──────────────────────────────────────────────────────────

    /// <summary>Back-compat overload — no trend data. Shows a flat (hidden) arrow.</summary>
    public void Show(int regionID, float avgHealth) => Show(regionID, avgHealth, 0f);

    /// <summary>
    /// Region path. avgHealth is 0–100; trend is the per-day change in health-points.
    /// Preserved signature for RegionOutlineRenderer; delegates to <see cref="ShowRegion"/>.
    /// </summary>
    // public void Show(int regionID, float avgHealth, float trend) => ShowRegion(regionID, avgHealth, trend);
    //
    // /// <summary>Populates the header for a REGION: label "Zone {id}", region avg health + trend.</summary>
    // public void ShowRegion(int regionID, float avgHealth, float trend)
    //     => Populate($"Zone {regionID}", "Avg. Health: ", avgHealth, trend);
    //
    // // ── Tile mode ────────────────────────────────────────────────────────────
    //
    // /// <summary>Populates the header for a single TILE: the entity/tile name, tile health + trend.</summary>
    // public void ShowTile(string tileName, float tileHealth, float trend)
    //     => Populate(tileName, "", tileHealth, trend);
    //
    // // ── Shared populate ──────────────────────────────────────────────────────
    //
    // private void Populate(string label, string healthPrefix, float health, float trend)
    
    public void Show(int regionID, float avgHealth, float trend)
    {
        if (trendIndicator != null)
            trendIndicator.SetTrend(trend);

        if (regionLabel != null)
            regionLabel.text = $"Zone {regionID}";

        // Resolve state + color once, apply to both bar and (optional) text.
        string state;
        Color  color;

        if (avgHealth < 33f)      { state = "Critical"; color = criticalColor; }
        else if (avgHealth < 67f) { state = "Degraded"; color = degradedColor; }
        else                      { state = "Thriving"; color = thrivingColor; }

        if (healthSlider != null)
            healthSlider.value = avgHealth;

        // BUG FIX (2026-07-22): the fillImage tint had been commented out, which left this
        // `if (fillImage != null)` dangling onto the healthText block below — so health text
        // only updated when fillImage happened to be non-null. Restore correct control flow:
        // tint fillImage when present; ALWAYS update healthText.
        if (fillImage != null) ;
            // fillImage.color = color;

        if (healthText != null)
        {
            healthText.text  = $"Avg. Health: {avgHealth:F1}% ({state})";
            healthText.color = color;
        }

        if (panel != null) panel.SetActive(true);
    }

    /// <summary>Tints the header background for the active mode (called by the controller).</summary>
    public void SetHeaderColor(Color color)
    {
        if (headerBackground != null)
            headerBackground.color = color;
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
    }
}
