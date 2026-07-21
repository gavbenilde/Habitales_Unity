using UnityEngine;
using UnityEngine.UI;

namespace Habitales.Onboarding
{
    /// <summary>
    /// Bottom-right "hold Space to skip" control (ONBOARDING_HANDOFF §6-E). A grey fill bar over
    /// a black track grows while <see cref="skipKey"/> is held; releasing early eases it back to 0.
    /// Filling to 1 calls <see cref="OnboardingDirector.SkipOnboarding"/>, which dismisses the
    /// in-flight popup (releasing any sim pause) and then graduates — clearing every card lock and
    /// coach mark, so no scaffolding is left dimmed behind.
    ///
    /// Uses Time.unscaledDeltaTime throughout: intrusive Dialog phases pause the sim
    /// (Time.timeScale may be 0), and the skip control must keep working while paused — the
    /// director itself uses unscaledDeltaTime for the same reason.
    /// </summary>
    public class OnboardingSkipControl : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // Inspector
        // ─────────────────────────────────────────────────────────────────────

        [Header("Timing")]
        [Tooltip("Seconds of holding skipKey to trigger the skip.")]
        [SerializeField] private float holdDuration = 1.5f;

        [Tooltip("Key that must be held to fill the bar.")]
        [SerializeField] private KeyCode skipKey = KeyCode.Space;

        [Tooltip("How fast the fill eases back to 0 when the key is released (units of fillAmount/sec).")]
        [SerializeField] private float releaseLerpSpeed = 6f;

        [Header("UI References")]
        [Tooltip("Image set to Type = Filled (Horizontal). Its fillAmount is driven 0→1 as the grey " +
                 "fill grows over the black track.")]
        [SerializeField] private Image fillImage;

        [Tooltip("Optional. When assigned, its alpha/interactable/blocksRaycasts are driven so the " +
                 "control only shows while onboarding is active.")]
        [SerializeField] private CanvasGroup root;

        // ─────────────────────────────────────────────────────────────────────
        // Runtime state
        // ─────────────────────────────────────────────────────────────────────

        private float _progress;
        private OnboardingDirector _director;

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        void Start()
        {
            if (fillImage == null)
                Debug.LogError($"{name}: fillImage is not assigned — OnboardingSkipControl cannot " +
                               "render its fill bar. Assign a Filled (Horizontal) Image in the Inspector.", this);

            _director = OnboardingDirector.Instance;   // may still be null here; re-fetched lazily below
        }

        void Update()
        {
            if (_director == null)
                _director = OnboardingDirector.Instance;

            if (_director == null || !_director.IsActive)
            {
                // Nothing to skip — onboarding isn't running (or already graduated).
                if (root != null)
                {
                    root.alpha          = 0f;
                    root.interactable   = false;
                    root.blocksRaycasts = false;
                }
                _progress = 0f;
                if (fillImage != null) fillImage.fillAmount = 0f;
                return;
            }

            if (root != null)
            {
                root.alpha          = 1f;
                root.interactable   = true;
                root.blocksRaycasts = true;
            }

            if (Input.GetKey(skipKey))
                _progress += Time.unscaledDeltaTime / Mathf.Max(0.01f, holdDuration);
            else
                _progress = Mathf.MoveTowards(_progress, 0f, releaseLerpSpeed * Time.unscaledDeltaTime);

            _progress = Mathf.Clamp01(_progress);
            if (fillImage != null) fillImage.fillAmount = _progress;

            if (_progress >= 1f)
            {
                _director.SkipOnboarding();   // idempotent — guards on IsActive
                _progress = 0f;
            }
        }
    }
}

// =============================================================================
// INSPECTOR WIRING CHECKLIST
// =============================================================================
//
// Placement
//   • Put this component on a bottom-right HUD element (its own GameObject under the
//     onboarding/HUD canvas is fine).
//
// UI References
//   • fillImage — a UI Image set to Type = Filled, Fill Method = Horizontal, sitting on
//     top of a black "track" background Image of the same size. Colour it grey; the
//     component drives its fillAmount 0→1 as skipKey is held.
//   • root      — optional. Give the control's root GameObject a CanvasGroup and wire it
//     here so the bar only shows (alpha 1, interactable) while onboarding is IsActive,
//     and disappears once the run graduates or is skipped.
//
// Tuning
//   • holdDuration     — seconds of holding skipKey (default Space) before it triggers.
//   • releaseLerpSpeed — how snappy the "let go" ease-back feels; higher = snappier.
// =============================================================================
