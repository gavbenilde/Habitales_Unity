using UnityEngine;
using TMPro;

/// <summary>
/// Displays the average health of the currently selected region.
/// Attach to a Canvas panel GameObject. Call Show/Hide from RegionOutlineRenderer.
/// </summary>
public class RegionHealthUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject panel;
    [SerializeField] private TextMeshProUGUI regionLabel;   // e.g. "Zone 1"
    [SerializeField] private TextMeshProUGUI healthText;    // e.g. "Avg. Health: 74.2% (Degraded)"

    [Header("Health Colors")]
    [SerializeField] private Color thrivingColor  = new Color(0.3f, 0.9f, 0.3f);
    [SerializeField] private Color degradedColor  = new Color(0.9f, 0.8f, 0.3f);
    [SerializeField] private Color criticalColor  = new Color(0.9f, 0.3f, 0.3f);

    void Awake()
    {
        Hide();
    }

    /// <summary>
    /// Shows the panel and populates it with region data.
    /// avgHealth is 0–100.
    /// </summary>
    public void Show(int regionID, float avgHealth)
    {
        if (regionLabel != null)
            regionLabel.text = $"Zone {regionID}";

        if (healthText != null)
        {
            string state;
            Color  color;

            if (avgHealth < 33f)
            {
                state = "Critical";
                color = criticalColor;
            }
            else if (avgHealth < 67f)
            {
                state = "Degraded";
                color = degradedColor;
            }
            else
            {
                state = "Thriving";
                color = thrivingColor;
            }

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