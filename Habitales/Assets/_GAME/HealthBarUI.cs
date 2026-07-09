using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Passive HUD view — renders zone-unlock progress as a gradient slider.
/// The bar is NORMALIZED, not raw health: 0 = the current progress floor
/// (world health right after the last zone spawned, ratcheted down to any
/// new low-point since), 1 = the region unlock threshold. HudController
/// owns that mapping and pushes the finished fraction via <see cref="Render"/>;
/// this class never reads RegionManager/RunManager (Law 1 / Law 2).
/// Update() polling has been removed: HudController pushes on meaning events
/// (day resolved, region generated, unlock ready/claimed) — the only times
/// the value can change.
/// </summary>
public class HealthBarUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Slider healthSlider; // Drag the Slider here
    [SerializeField] private Image fillImage;     // Drag the 'Fill' object here

    [Header("Zone Unlock Marker")]
    [Tooltip("Lock/unlock icon. The unlock threshold IS the end of the bar now, " +
             "so the marker sits at the far right and just swaps sprite when the " +
             "unlock is earned.")]
    [SerializeField] private RectTransform thresholdMarker;
    [SerializeField] private Image markerIcon;
    [SerializeField] private Sprite lockedSprite;
    [SerializeField] private Sprite unlockedSprite;

    [Header("Color Gradient Settings")]
    [SerializeField] private Color purple = new Color(0.6f, 0.2f, 0.8f);
    [SerializeField] private Color blue   = Color.blue;
    [SerializeField] private Color pink   = new Color(1f, 0.4f, 0.7f);
    [SerializeField] private Color green  = Color.green;

    private bool wasUnlocked = false;

    void Start()
    {
        // The bar renders a normalized 0-1 progress fraction, not 0-100 health.
        healthSlider.minValue = 0f;
        healthSlider.maxValue = 1f;

        PositionMarker();
        SetMarkerUnlocked(false);
    }

    /// <summary>
    /// Called by HudController with the normalized unlock progress.
    /// <paramref name="fraction"/> is 0–1 (floor → threshold);
    /// <paramref name="unlockReady"/> latches the bar full while an unlock
    /// is earned but unclaimed. Passive: no game-state reads or writes.
    /// </summary>
    public void Render(float fraction, bool unlockReady)
    {
        fraction = Mathf.Clamp01(fraction);

        if (healthSlider != null)
            healthSlider.value = fraction;

        if (fillImage != null)
            fillImage.color = GetStepGradient(fraction);

        if (unlockReady != wasUnlocked)
            SetMarkerUnlocked(unlockReady);
    }

    /// <summary>
    /// The threshold now maps to the bar's 100%, so the marker anchors at the far end.
    /// </summary>
    private void PositionMarker()
    {
        if (thresholdMarker == null) return;

        thresholdMarker.anchorMin = new Vector2(1f, thresholdMarker.anchorMin.y);
        thresholdMarker.anchorMax = new Vector2(1f, thresholdMarker.anchorMax.y);
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
