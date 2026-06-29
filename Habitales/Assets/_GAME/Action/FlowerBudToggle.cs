using System;
using UnityEngine;
using UnityEngine.UI;

namespace Habitales.UI.Actions
{
    /// <summary>
    /// Passive view for the two-state "flower" control that gates the action category bar.
    /// Owns the flower button (bud ↔ bloomed sprite) and the icon container that holds the
    /// category hexagons. It does NOT decide policy: on click it raises <see cref="OnBloomToggled"/>
    /// with the requested next state and waits for the controller (<see cref="ActionBarUI"/>) to
    /// call <see cref="SetBloomed"/> back down. No game-state writes (Law 1); meaning-event up,
    /// commands down (the same controller↔view contract as ActionCategoryBar / ActionStripView).
    ///
    /// <para><b>Two states:</b></para>
    /// <list type="number">
    ///   <item><b>Hidden</b>  — bud sprite; icon container hidden.</item>
    ///   <item><b>Shown</b>   — bloomed sprite; icon container revealed (tween from tiny → big).</item>
    /// </list>
    ///
    /// WIRING (human, Inspector):
    ///   1. Add this component to the "Flower Bud" GameObject.
    ///   2. flowerButton = its own Button; flowerImage = its own Image.
    ///   3. budSprite / bloomedSprite = the closed/open flower sprites.
    ///   4. iconContainer = the "CategoryTabs" RectTransform (the grid of category hexagons).
    ///   5. Clear the Flower Bud Button's Inspector OnClick (the old SetActive hack) — this
    ///      component wires its own click listener in OnEnable.
    /// </summary>
    [AddComponentMenu("Habitales/UI/Flower Bud Toggle")]
    public class FlowerBudToggle : MonoBehaviour
    {
        [Header("Flower button")]
        [SerializeField] private Button flowerButton;
        [SerializeField] private Image  flowerImage;
        [SerializeField] private Sprite budSprite;
        [SerializeField] private Sprite bloomedSprite;

        [Header("Category icons")]
        [Tooltip("The container (e.g. CategoryTabs grid) holding the 3 category hexagons.")]
        [SerializeField] private RectTransform iconContainer;

        [Header("Tween (stub)")]
        [Tooltip("Seconds for the reveal/collapse scale tween. Stub animation — tune later.")]
        [SerializeField] private float tweenSeconds = 0.18f;

        // The tiny scale the icons grow from / shrink back to (the 'bud' size).
        private const float CollapsedScale = 0.1f;

        /// <summary>
        /// Raised when the flower is clicked, carrying the <i>requested</i> next bloom state
        /// (Law 2: meaning = the click). The controller decides what to do and calls
        /// <see cref="SetBloomed"/> back down — this view does not self-mutate on click.
        /// </summary>
        public event Action<bool> OnBloomToggled;

        /// <summary>Whether the flower is currently bloomed (icons shown). Law-1 read-only getter.</summary>
        public bool IsBloomed { get; private set; }

        // ─── Lifecycle ────────────────────────────────────────────────────────

        void Awake()
        {
            // Law-3 loud-fail for every artist-touched ref.
            bool ok = true;
            if (flowerButton == null)
            {
                Debug.LogError($"{name}: flowerButton is not wired — assign the flower Button in the Inspector.", this);
                ok = false;
            }
            if (flowerImage == null)
            {
                Debug.LogError($"{name}: flowerImage is not wired — assign the flower Image in the Inspector.", this);
                ok = false;
            }
            if (budSprite == null)
                Debug.LogError($"{name}: budSprite is not wired — assign the closed-flower sprite in the Inspector.", this);
            if (bloomedSprite == null)
                Debug.LogError($"{name}: bloomedSprite is not wired — assign the open-flower sprite in the Inspector.", this);
            if (iconContainer == null)
            {
                Debug.LogError($"{name}: iconContainer is not wired — assign the CategoryTabs RectTransform in the Inspector.", this);
                ok = false;
            }

            if (!ok) { enabled = false; return; }
        }

        void OnEnable()
        {
            if (flowerButton != null)
                flowerButton.onClick.AddListener(HandleClicked);
        }

        void OnDisable()
        {
            if (flowerButton != null)
                flowerButton.onClick.RemoveListener(HandleClicked);
        }

        // ─── Click (upward channel) ───────────────────────────────────────────

        private void HandleClicked()
        {
            // Raise the requested state; the controller arbitrates and calls SetBloomed back.
            OnBloomToggled?.Invoke(!IsBloomed);
        }

        // ─── Commands (downward channel) ──────────────────────────────────────

        /// <summary>
        /// Applies the bloom state: swaps the flower sprite and reveals/collapses the icon
        /// container with a scale tween. Passive — no game-state writes, no cross-UI side-effects.
        /// </summary>
        public void SetBloomed(bool bloomed)
        {
            bool wasBloomed = IsBloomed;
            IsBloomed = bloomed;

            if (flowerImage != null)
                flowerImage.sprite = bloomed ? bloomedSprite : budSprite;

            if (iconContainer == null) return;

            // ── Stub tween seam ──────────────────────────────────────────────
            // Scale the icon container from the bud size out to full (and back) using LeanTween
            // (vendored at _UTILITIES/LeanTween). Replace with the real bloom animation later —
            // e.g. per-icon staggered pop, easing, fade. Keep the SetActive bookkeeping.
            LeanTween.cancel(iconContainer.gameObject);

            if (bloomed)
            {
                iconContainer.gameObject.SetActive(true);
                iconContainer.localScale = Vector3.one * CollapsedScale;
                LeanTween.scale(iconContainer, Vector3.one, tweenSeconds)
                         .setEase(LeanTweenType.easeOutBack);
            }
            else if (!wasBloomed)
            {
                // Initial / redundant collapse (e.g. the Awake Hidden init): snap instantly,
                // no tween, so the icons never flash open at startup.
                iconContainer.localScale = Vector3.one * CollapsedScale;
                iconContainer.gameObject.SetActive(false);
            }
            else
            {
                // User-driven collapse from an open state: animate closed, then hide.
                LeanTween.scale(iconContainer, Vector3.one * CollapsedScale, tweenSeconds)
                         .setEase(LeanTweenType.easeInBack)
                         .setOnComplete(() =>
                         {
                             if (iconContainer != null)
                                 iconContainer.gameObject.SetActive(false);
                         });
            }
        }

        // ─── Onboarding support ───────────────────────────────────────────────

        /// <summary>
        /// The flower button's RectTransform, for a coach-mark / FidgetArrow to point at
        /// ("tap here to see your actions"). Surfaced to onboarding via the ActionBarUI frozen surface.
        /// </summary>
        public RectTransform GetFlowerRect()
            => flowerButton != null ? flowerButton.transform as RectTransform : transform as RectTransform;
    }
}
