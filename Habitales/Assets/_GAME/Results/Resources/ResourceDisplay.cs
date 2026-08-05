using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Simple HUD display for Time and People resources. The people/workforce readout
/// renders as a fill bar (<see cref="workforceBar"/>) — available ÷ total people —
/// rather than text; the old <see cref="peopleText"/> field is now optional secondary
/// info (compact "available/total" plus a recovering count) and is skipped silently
/// if left unwired. Self-driven like the rest of this file: it subscribes directly to
/// ResourceManager events rather than being pushed by HudController, so
/// <see cref="UpdateDisplay"/>'s signature is unchanged.
///
/// WIRING (human): create a Slider under the ResourceDisplay panel in the editor
/// (non-interactable, min 0 max 1) and drag it into workforceBar. Optionally drag its
/// 'Fill' Image into workforceFill for color tinting, and optionally keep the old
/// peopleText TMP_Text wired for the secondary "available/total" string.
/// </summary>
public class ResourceDisplay : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI timeText;

    [Header("Workforce Bar")]
    [Tooltip("Primary display for people/workforce — fill = available ÷ total. Non-interactable, min 0 max 1.")]
    [SerializeField] private Slider workforceBar;
    [Tooltip("Optional — the bar's 'Fill' Image, tinted by availability so it matches the old text color coding.")]
    [SerializeField] private Image workforceFill;
    [Tooltip("Optional secondary info — compact \"available/total\" string. Skipped silently if left unwired.")]
    [SerializeField] private TextMeshProUGUI peopleText;

    private ResourceManager resourceManager;

    void Start()
    {
        ValidateRefs();
        if (!enabled) return;

        resourceManager = ResourceManager.Instance;

        if (resourceManager == null)
        {
            Debug.LogError("ResourceDisplay: ResourceManager not found!");
            enabled = false;
            return;
        }

        // Subscribe to updates (named handlers so OnDestroy can unsubscribe — no lambda leak).
        resourceManager.OnTimeAdvanced += UpdateDisplay;
        resourceManager.OnPeopleFatigued += HandlePeopleFatigued;
        resourceManager.OnPeopleRecovered += HandlePeopleRecovered;

        // Initial display
        UpdateDisplay(0);
    }

    void UpdateDisplay(int daysAdvanced)
    {
        if (resourceManager == null) return;

        // Time display
        if (timeText != null)
        {
            timeText.text = $"{resourceManager.GetFullTimeDisplay()}";
        }

        // Workforce bar — primary display. Fill = available ÷ total (guard divide-by-zero → 0).
        int available = resourceManager.AvailablePeople;
        int total = resourceManager.TotalPeople;
        int recovering = resourceManager.RecoveringPeopleCount;

        float fraction = total > 0 ? (float)available / total : 0f;
        
        // tween to new value
        LeanTween.cancel(workforceBar.gameObject);

        LeanTween.value(
                workforceBar.gameObject,
                workforceBar.value,
                fraction,
                0.3f
            )
            .setEaseOutQuad()
            .setOnUpdate((float value) =>
            {
                workforceBar.value = value;
            });

        if (workforceFill != null)
        {
            // string color = available > 15 ? "#61c415" : available > 5 ? "#e7b81d" : "#c41515";
            // ColorUtility.TryParseHtmlString(color, out Color tint);
            // workforceFill.color = tint;
        }

        // People text — optional secondary info, skipped silently if unwired.
        if (peopleText != null)
        {
            peopleText.text = $"{available}/{total}";

            if (recovering > 0)
            {
                peopleText.text += $" <color=orange>(−{recovering})</color>";
            }
        }
    }

    void HandlePeopleFatigued(int count, int returnDay) => UpdateDisplay(0);
    void HandlePeopleRecovered(int count) => UpdateDisplay(0);

    void OnDestroy()
    {
        if (resourceManager != null)
        {
            resourceManager.OnTimeAdvanced -= UpdateDisplay;
            resourceManager.OnPeopleFatigued -= HandlePeopleFatigued;
            resourceManager.OnPeopleRecovered -= HandlePeopleRecovered;
        }
    }

    // ─── Validation (Law 3 — loud-fail on unwired Inspector refs) ─────────────
    private void ValidateRefs()
    {
        if (workforceBar == null)
        {
            Debug.LogError($"{name}: workforceBar is not wired — drag a Slider (non-interactable, min 0 max 1) into the Inspector.", this);
            enabled = false;
        }
    }
}