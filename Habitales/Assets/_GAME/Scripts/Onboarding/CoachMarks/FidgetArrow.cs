using UnityEngine;
using UnityEngine.UI;

namespace Habitales.Onboarding
{
    // =========================================================================
    //  FidgetArrow — CoachMarkKind.FidgetArrow
    //
    //  An arrow sprite that sits near the world-space target and bobs ("fidgets")
    //  toward it — a small repeating nudge that draws the eye without screaming.
    //
    //  Layout: arrow is positioned just offset from the target and bobs along the
    //  nudge direction (default: downward, toward the tile). Rotation is set once
    //  on Show() so the sprite arrowhead faces the target.
    //
    //  Canvas: Screen Space – Overlay (same as the layer canvas).
    //
    //  Inspector wiring:
    //    arrowRect      — RectTransform of the arrow Image.
    //    arrowImage     — Image showing the arrow sprite.
    //    canvasRect     — RectTransform of the overlay Canvas root.
    //    offsetPx       — pixel offset from target center (default: 0, -70).
    //    bobDistance    — how far the arrow bobs each cycle in pixels (default 12).
    //    bobPeriod      — full bob cycle in seconds (default 0.55).
    // =========================================================================

    /// <summary>
    /// Fidget-nudge arrow that points at a world-space target and bobs toward it.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class FidgetArrow : CoachMarkWidget
    {
        [Header("Refs")]
        [SerializeField] private RectTransform arrowRect;
        [SerializeField] private Image         arrowImage;
        [SerializeField] private RectTransform canvasRect;

        [Header("Layout")]
        [Tooltip("Pixel offset from the target center in canvas space. Negative Y places the arrow above the tile.")]
        [SerializeField] private Vector2 offsetPx   = new Vector2(0f, -70f);

        [Header("Bob tuning")]
        [Tooltip("How far the arrow bobs per half-cycle (pixels).")]
        [SerializeField] private float bobDistance  = 12f;
        [Tooltip("Full bob period in seconds.")]
        [SerializeField] private float bobPeriod    = 0.55f;

        private bool   _refsOk;
        private float  _bobTime;
        private Canvas _canvas;

        // ── Lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
            _refsOk = arrowRect != null && arrowImage != null && canvasRect != null;
            if (!_refsOk)
            {
                Debug.LogError($"{name}: FidgetArrow is missing serialized refs — " +
                               "wire arrowRect / arrowImage / canvasRect in the Inspector.", this);
                enabled = false;
                return;
            }

            _canvas = arrowRect.GetComponentInParent<Canvas>();
        }

        private void LateUpdate()
        {
            if (!_refsOk || !gameObject.activeSelf) return;

            // Target in screen pixels (CurrentScreenPos handles world- vs UI-space targets),
            // recomputed every frame so the arrow tracks a moving or late-appearing target
            // (e.g. the Plant Trees card once its strip opens).
            Vector2 screen = CurrentScreenPos();

            // Manual sine bob along the offset direction (0 → bobDistance → 0).
            _bobTime += Time.unscaledDeltaTime;
            float phase = Mathf.Sin(_bobTime / Mathf.Max(0.0001f, bobPeriod) * Mathf.PI * 2f) * 0.5f + 0.5f;
            Vector2 bobDir = offsetPx.sqrMagnitude > 0.0001f ? offsetPx.normalized : Vector2.up;

            // offsetPx / bobDistance are authored in canvas-reference units; scale to screen
            // pixels so a CanvasScaler doesn't shift the arrow off the target.
            float sf = _canvas != null ? _canvas.scaleFactor : 1f;
            Vector2 screenPos = screen + (offsetPx + bobDir * (bobDistance * phase)) * sf;

            // Set screen-space position directly — anchor/parent independent for overlay.
            Vector3 p = arrowRect.position;
            arrowRect.position = new Vector3(screenPos.x, screenPos.y, p.z);
        }

        // ── CoachMarkWidget ────────────────────────────────────────────────

        protected override void DoShow()
        {
            if (!_refsOk) return;

            _bobTime = 0f;

            // Point arrowhead toward target: offset direction is (targetPos - arrowPos),
            // which is -offsetPx normalized. Use atan2 to rotate the sprite.
            float angle = Mathf.Atan2(-offsetPx.y, -offsetPx.x) * Mathf.Rad2Deg;
            arrowRect.localRotation = Quaternion.Euler(0f, 0f, angle - 90f); // -90 because default Unity arrow points up.

            // Position is driven every frame by LateUpdate (tracks + bobs).
        }

        protected override void DoHide()
        {
            // LeanTween.cancel called by base.
        }
    }
}
