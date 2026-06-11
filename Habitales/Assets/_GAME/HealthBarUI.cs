using UnityEngine;
using UnityEngine.UI;

public class HealthBarUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Slider healthSlider; // Drag the Slider here
    [SerializeField] private Image fillImage;     // Drag the 'Fill' object here

    [Header("Zone Unlock Marker")]
    [Tooltip("Lock/unlock icon placed along the bar at the Zone Unlock Threshold. " +
             "Parent it to the bar's fill area so proportional anchoring lines up.")]
    [SerializeField] private RectTransform thresholdMarker;
    [SerializeField] private Image markerIcon;
    [SerializeField] private Sprite lockedSprite;
    [SerializeField] private Sprite unlockedSprite;

    [Header("Color Gradient Settings")]
    [SerializeField] private Color purple = new Color(0.6f, 0.2f, 0.8f);
    [SerializeField] private Color blue   = Color.blue;
    [SerializeField] private Color pink   = new Color(1f, 0.4f, 0.7f);
    [SerializeField] private Color green  = Color.green;

    private RegionManager regionManager;
    private float threshold = 80f;
    private bool wasUnlocked = false;

    void Start()
    {
        // Ensure the slider range matches 0-100 health
        healthSlider.minValue = 0;
        healthSlider.maxValue = 100;

        if (RunManager.Instance != null)
            threshold = RunManager.Instance.ZoneUnlockThreshold;

        PositionMarker();
        SetMarkerUnlocked(false);
    }

    void Update()
    {
        if (RunManager.Instance == null) return;

        // Cache the RegionManager singleton once.
        if (regionManager == null)
            regionManager = RegionManager.Instance;
        if (regionManager == null) return;

        float currentHealth = regionManager.GetTotalAverageHealth();

        // Update Slider
        healthSlider.value = currentHealth;

        // Update Color
        fillImage.color = GetStepGradient(currentHealth / 100f);

        // Flip the marker icon once health reaches the threshold
        bool unlocked = currentHealth >= threshold;
        if (unlocked != wasUnlocked)
            SetMarkerUnlocked(unlocked);
    }

    /// <summary>
    /// Anchors the marker proportionally along the bar at threshold/100.
    /// Assumes the marker's parent rect spans the same width as the 0-100 range.
    /// </summary>
    private void PositionMarker()
    {
        if (thresholdMarker == null) return;

        float frac = Mathf.Clamp01(threshold / 100f);
        thresholdMarker.anchorMin = new Vector2(frac, thresholdMarker.anchorMin.y);
        thresholdMarker.anchorMax = new Vector2(frac, thresholdMarker.anchorMax.y);
        thresholdMarker.anchoredPosition = new Vector2(0f, thresholdMarker.anchoredPosition.y);
    }

    private void SetMarkerUnlocked(bool unlocked)
    {
        wasUnlocked = unlocked;
        if (markerIcon == null) return;

        Sprite target = unlocked ? unlockedSprite : lockedSprite;
        if (target != null) markerIcon.sprite = target;
    }

    private Color GetStepGradient(float t)
    {
        // t is normalized 0 to 1
        if (t < 0.33f)
            return Color.Lerp(purple, blue, t / 0.33f);
        if (t < 0.66f)
            return Color.Lerp(blue, pink, (t - 0.33f) / 0.33f);

        return Color.Lerp(pink, green, (t - 0.66f) / 0.34f);
    }
}
