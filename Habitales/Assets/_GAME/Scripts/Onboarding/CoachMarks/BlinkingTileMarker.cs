using UnityEngine;
using UnityEngine.UI;

namespace Habitales.Onboarding
{
    // =========================================================================
    //  BlinkingTileMarker — CoachMarkKind.BlinkingTileMarker
    //
    //  A pulsing highlight ring/sprite that snaps to a world-space tile and
    //  LeanTween-loops its scale and alpha so it gently draws the eye.
    //
    //  Canvas: Screen Space – Overlay (set on the CoachMarkLayer canvas).
    //  Position: WorldToScreenPoint each frame so it tracks moving targets.
    //
    //  Inspector wiring:
    //    markerRect   — RectTransform of the highlight Image child.
    //    markerImage  — Image (or RawImage) displaying the ring sprite.
    //    canvasRect   — RectTransform of the overlay Canvas root (drag the Canvas).
    //    pulseScale   — peak scale multiplier (default 1.25).
    //    pulsePeriod  — full pulse cycle in seconds (default 0.7).
    // =========================================================================

    /// <summary>
    /// Pulsing highlight placed at a world-space tile position.
    /// Loops a LeanTween scale + alpha ping-pong until hidden.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class BlinkingTileMarker : CoachMarkWidget
    {
        [Header("Refs")]
        [SerializeField] private RectTransform markerRect;
        [SerializeField] private Image         markerImage;
        [SerializeField] private RectTransform canvasRect;

        [Header("Pulse tuning")]
        [SerializeField] private float pulseScale  = 1.25f;
        [SerializeField] private float pulsePeriod = 0.7f;

        private bool _refsOk;

        // ── Lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
            _refsOk = markerRect != null && markerImage != null && canvasRect != null;
            if (!_refsOk)
            {
                Debug.LogError($"{name}: BlinkingTileMarker is missing serialized refs — " +
                               "wire markerRect / markerImage / canvasRect in the Inspector.", this);
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (!_refsOk || !gameObject.activeSelf) return;
            SnapToTarget();
        }

        // ── CoachMarkWidget ────────────────────────────────────────────────

        protected override void DoShow()
        {
            if (!_refsOk) return;

            SnapToTarget();
            markerRect.localScale = Vector3.one;
            SetAlpha(1f);

            // Scale pulse: 1 → pulseScale → 1, looping.
            LeanTween.scale(markerRect, Vector3.one * pulseScale, pulsePeriod * 0.5f)
                .setEaseInOutSine()
                .setLoopPingPong()
                .setIgnoreTimeScale(true);

            // Alpha pulse: 1 → 0.3 → 1, looping, offset slightly.
            LeanTween.value(gameObject, SetAlpha, 1f, 0.3f, pulsePeriod * 0.5f)
                .setEaseInOutSine()
                .setLoopPingPong()
                .setIgnoreTimeScale(true);
        }

        protected override void DoHide()
        {
            if (!_refsOk) return;
            // LeanTween.cancel called by base after DoHide; reset visuals here.
            markerRect.localScale = Vector3.one;
            SetAlpha(1f);
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private void SnapToTarget()
        {
            if (ScreenCamera == null) return;
            Vector2 screenPos = CurrentScreenPos();
            markerRect.anchoredPosition = ScreenToCanvasPos(screenPos, canvasRect);
        }

        private void SetAlpha(float a)
        {
            if (markerImage != null)
            {
                Color c = markerImage.color;
                c.a = a;
                markerImage.color = c;
            }
        }
    }
}
