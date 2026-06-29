using TMPro;
using UnityEngine;

namespace Habitales.UI
{
    /// <summary>
    /// Anchor side for tooltip placement relative to a target RectTransform.
    /// Position math lives ONLY here (S2 — one concept, one place).
    /// </summary>
    public enum TooltipAnchor { Top, Bottom, Left, Right }

    /// <summary>
    /// U1 — Tooltip subsystem.
    /// One shared tooltip instance, repositioned per-request.
    /// Positioning math is centralised here and nowhere else (S2).
    /// Implements IUISubsystem so the hub can drive master-visibility (§3).
    ///
    /// WIRING (human steps):
    ///   1. Build prefab: root RectTransform (e.g. "Tooltip") with Image/Panel + child TMP_Text label.
    ///      Add a CanvasGroup to the root (SetVisible toggles alpha + blocksRaycasts).
    ///   2. Place the prefab as a child of the UIManager canvas (above other panels in hierarchy).
    ///   3. On this MonoBehaviour in the Inspector set:
    ///        • Tooltip Root  → root RectTransform of the prefab
    ///        • Label         → the TMP_Text child
    ///        • Canvas        → the root Canvas the whole UI lives on (for clamping)
    ///        • Gap           → pixel gap between tooltip bottom edge and target top edge (default 8)
    ///   4. Add TooltipController to UIManager's serialized subsystems list AND to the typed
    ///      "tooltip" slot on UIManager so hub routing works.
    ///   5. Start with the tooltip root deactivated (or alpha 0) — Hide() is called in Awake.
    /// </summary>
    [DefaultExecutionOrder(-490)]
    public class TooltipController : MonoBehaviour, IUISubsystem
    {
        // ── IUISubsystem ─────────────────────────────────────────────────────
        public string SubsystemId => "tooltip";
        public bool   IsVisible   => _tooltipRoot != null && _tooltipRoot.gameObject.activeSelf;

        public void SetVisible(bool visible)
        {
            if (_tooltipRoot == null) return;
            _tooltipRoot.gameObject.SetActive(visible);
        }

        // ── Serialized refs (Law 3 — loud-fail) ──────────────────────────────
        [Header("References")]
        [SerializeField] private RectTransform _tooltipRoot;
        [SerializeField] private TMP_Text      _label;
        [SerializeField] private Canvas        _canvas;

        [Header("Layout")]
        [SerializeField] private float _gap = 8f;

        // ── Lifecycle ─────────────────────────────────────────────────────────
        private void Awake()
        {
            ValidateRefs();
            Hide();
        }

        private void ValidateRefs()
        {
            if (_tooltipRoot == null)
            {
                Debug.LogError($"{name}: _tooltipRoot missing — wire it in the Inspector.", this);
                enabled = false;
            }
            if (_label == null)
            {
                Debug.LogError($"{name}: _label (TMP_Text) missing — wire it in the Inspector.", this);
                enabled = false;
            }
            if (_canvas == null)
            {
                Debug.LogError($"{name}: _canvas missing — wire it in the Inspector.", this);
                enabled = false;
            }
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Show the shared tooltip anchored relative to <paramref name="target"/>.
        /// Default anchor is Top (tooltip appears above the target, centred).
        /// </summary>
        public void Show(RectTransform target, string text, TooltipAnchor anchor = TooltipAnchor.Top)
        {
            if (!enabled) return;
            if (target == null)
            {
                Debug.LogWarning($"{name}: Show() called with null target — ignoring.", this);
                return;
            }

            _label.text = text;
            _tooltipRoot.gameObject.SetActive(true);

            // Force an immediate layout rebuild so size is current before we place it.
            Canvas.ForceUpdateCanvases();

            PositionTooltip(target, anchor);
        }

        /// <summary>Hide the shared tooltip.</summary>
        public void Hide()
        {
            if (_tooltipRoot != null)
                _tooltipRoot.gameObject.SetActive(false);
        }

        // ── Position math (S2 — lives ONLY here) ─────────────────────────────

        private void PositionTooltip(RectTransform target, TooltipAnchor anchor)
        {
            // Gather target world corners: [0]=BL [1]=TL [2]=TR [3]=BR
            Vector3[] targetCorners = new Vector3[4];
            target.GetWorldCorners(targetCorners);

            // Gather canvas world corners for clamping: [0]=BL [1]=TL [2]=TR [3]=BR
            RectTransform canvasRect = _canvas.GetComponent<RectTransform>();
            Vector3[] canvasCorners  = new Vector3[4];
            canvasRect.GetWorldCorners(canvasCorners);

            float canvasLeft   = canvasCorners[0].x;
            float canvasBottom = canvasCorners[0].y;
            float canvasRight  = canvasCorners[2].x;
            float canvasTop    = canvasCorners[2].y;

            float tipWidth  = _tooltipRoot.rect.width  * _tooltipRoot.lossyScale.x;
            float tipHeight = _tooltipRoot.rect.height * _tooltipRoot.lossyScale.y;

            // Anchor-point on the target (world space), then offset outward by gap + half tip
            float worldX, worldY;

            switch (anchor)
            {
                case TooltipAnchor.Bottom:
                    // Centre-bottom of target; tooltip sits below
                    worldX = (targetCorners[0].x + targetCorners[3].x) * 0.5f;
                    worldY = targetCorners[0].y - _gap - tipHeight * 0.5f;
                    break;

                case TooltipAnchor.Left:
                    // Centre-left of target; tooltip sits to the left
                    worldX = targetCorners[0].x - _gap - tipWidth * 0.5f;
                    worldY = (targetCorners[0].y + targetCorners[1].y) * 0.5f;
                    break;

                case TooltipAnchor.Right:
                    // Centre-right of target; tooltip sits to the right
                    worldX = targetCorners[3].x + _gap + tipWidth * 0.5f;
                    worldY = (targetCorners[0].y + targetCorners[1].y) * 0.5f;
                    break;

                default: // TooltipAnchor.Top
                    // Centre-top of target; tooltip sits above
                    worldX = (targetCorners[1].x + targetCorners[2].x) * 0.5f;
                    worldY = targetCorners[1].y + _gap + tipHeight * 0.5f;
                    break;
            }

            // Clamp so tooltip stays inside the canvas bounds
            worldX = Mathf.Clamp(worldX, canvasLeft   + tipWidth  * 0.5f,
                                          canvasRight  - tipWidth  * 0.5f);
            worldY = Mathf.Clamp(worldY, canvasBottom + tipHeight * 0.5f,
                                          canvasTop   - tipHeight * 0.5f);

            _tooltipRoot.position = new Vector3(worldX, worldY, _tooltipRoot.position.z);
        }
    }
}
