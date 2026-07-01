using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Habitales.UI;

/// <summary>
/// Displays the average health of the currently selected region as a 0–100 bar.
/// Attach to a Canvas panel GameObject. Call Show/Hide from RegionOutlineRenderer.
/// Text fields (regionLabel / healthText) are optional — disable their GameObjects
/// in the prefab for a minimal, numbers-and-bar-only readout.
/// </summary>
public class RegionHealthUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject panel;
    [SerializeField] private TextMeshProUGUI regionLabel;   // optional — e.g. "Zone 1"
    [SerializeField] private TextMeshProUGUI healthText;    // optional — e.g. "Avg. Health: 74.2% (Degraded)"

    [Header("Health Bar (0–100)")]
    [SerializeField] private Slider healthSlider;           // min 0 / max 100
    [SerializeField] private Image  fillImage;              // optional — tinted by state

    [Header("Trend")]
    [SerializeField] private TrendIndicatorUI trendIndicator; // optional — tiered up/down arrow

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
    }

    /// <summary>
    /// Back-compat overload — no trend data. Shows a flat (hidden) arrow.
    /// </summary>
    public void Show(int regionID, float avgHealth) => Show(regionID, avgHealth, 0f);

    /// <summary>
    /// Shows the panel and populates it with region data.
    /// avgHealth is 0–100; delta is the per-day change in health-points (drives the trend arrow).
    /// </summary>
    public void Show(int regionID, float avgHealth, float delta)
    {
        if (trendIndicator != null)
            trendIndicator.SetDelta(delta);

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

        if (fillImage != null)
            fillImage.color = color;

        if (healthText != null)
        {
            healthText.text  = $"Avg. Health: {avgHealth:F1}% ({state})";
            healthText.color = color;
        }

        if (panel != null) panel.SetActive(true);
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
    }
}
