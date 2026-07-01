using UnityEngine;
using UnityEngine.EventSystems;

namespace Habitales.UI
{
    /// <summary>
    /// U1 — Hover trigger that calls the UIManager tooltip API.
    /// Drop this component on any Button or Image that should show a tooltip on hover.
    /// Author fills in Tooltip Text (and optionally Anchor) in the Inspector.
    ///
    /// WIRING (human steps):
    ///   1. Select the Button or Image GameObject in the scene.
    ///   2. Add Component → Habitales.UI → HoverTooltipTrigger.
    ///   3. Set "Tooltip Text" to the label you want shown.
    ///   4. (Optional) Change "Anchor" — default is Top (tooltip above the element).
    ///   5. Ensure the GameObject has a Graphic component (Image/Text) so the EventSystem
    ///      can detect pointer events; Buttons have this automatically.
    ///   6. No further wiring needed — the trigger reaches UIManager via UIManager.Instance.
    ///
    /// NOTE: requires UIManager to be present in the scene with a TooltipController wired.
    /// </summary>
    [AddComponentMenu("Habitales/UI/Hover Tooltip Trigger")]
    public class HoverTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("Tooltip Content")]
        [TextArea]
        [SerializeField] private string _tooltipText;

        [SerializeField] private TooltipAnchor _anchor = TooltipAnchor.Top;

        /// <summary>
        /// Sets the tooltip text at runtime — e.g. a <see cref="TrendIndicatorUI"/> pushing its
        /// current delta, or a <see cref="TooltipBadge"/> pulling from a <see cref="StatIconLibrary"/>.
        /// Positioning still lives only in TooltipController (S2); this just swaps the content.
        /// </summary>
        public void SetText(string text) => _tooltipText = text;

        // ── Serialized-ref validation (Law 3) ────────────────────────────────
        // No [SerializeField] ref to UIManager — it is accessed via .Instance (S4: managers
        // are read via .Instance; subsystem views use [SerializeField]).  No loud-fail needed
        // for the Instance path, but we do warn loudly at hover time if it is absent.

        // ── IPointerEnterHandler ──────────────────────────────────────────────
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (string.IsNullOrWhiteSpace(_tooltipText))
                return; // nothing to show — skip silently

            if (UIManager.Instance == null)
            {
                Debug.LogError($"{name}: HoverTooltipTrigger — UIManager.Instance is null. "
                             + "Ensure UIManager exists in the scene.", this);
                return;
            }

            UIManager.Instance.ShowTooltip((RectTransform)transform, _tooltipText, _anchor);
        }

        // ── IPointerExitHandler ───────────────────────────────────────────────
        public void OnPointerExit(PointerEventData eventData)
        {
            if (UIManager.Instance == null) return;
            UIManager.Instance.HideTooltip();
        }

        // ── Safety: hide if disabled mid-hover ───────────────────────────────
        private void OnDisable()
        {
            if (UIManager.Instance != null)
                UIManager.Instance.HideTooltip();
        }
    }
}
