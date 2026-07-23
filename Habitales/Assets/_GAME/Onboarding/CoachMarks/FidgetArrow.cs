using UnityEngine;
using UnityEngine.UI;

namespace Habitales.Onboarding
{
    // =========================================================================
    //  FidgetArrow — CoachMarkKind.FidgetArrow
    //
    //  An arrow sprite that ORBITS a world-space / UI target and bobs ("fidgets")
    //  toward it — a small repeating nudge that draws the eye without screaming.
    //
    //  Layout model (the "orbit"):
    //    • pivot  = target + pivotOffsetPx   — the point the arrow circles + aims at.
    //    • the arrow sits on a circle of radius orbitRadius around that pivot, at
    //      orbitAngleDeg (0 = right, 90 = up, 180 = left, 270 = below).
    //    • the arrowhead always points inward at the pivot; rotation follows the angle.
    //    • arrowPosOffsetPx is a final fine-tune of the sprite position (for art whose
    //      tip isn't centred on its pivot).
    //    • the arrow bobs a few pixels inward toward the pivot each cycle.
    //    • orbitSpeedDegPerSec > 0 makes the arrow actually travel around the pivot
    //      (a true orbit); 0 holds it static at orbitAngleDeg.
    //
    //  Per-phase override: the OnboardingDirector reuses one shared FidgetArrow for
    //  several phases, each aimed at a different target. It stamps orbit values onto
    //  CoachMarkRequest (applyOrbit + angle/radius/pivot/arrow offsets), which win over
    //  these serialized defaults for that Show. See OnboardingDirector.FidgetArrowTuning.
    //
    //  Canvas: Screen Space – Overlay (same as the layer canvas).
    //
    //  Inspector wiring:
    //    arrowRect        — RectTransform of the arrow Image.
    //    arrowImage       — Image showing the arrow sprite.
    //    canvasRect       — RectTransform of the overlay Canvas root.
    //    orbitAngleDeg    — 0-360 seat around the target (default 270 = below).
    //    orbitRadius      — pixel distance from pivot to arrow (default 70).
    //    orbitSpeedDegPerSec — 0 = static; >0 travels around the pivot.
    //    pivotOffsetPx    — x/y nudge of the pivot off the target.
    //    arrowPosOffsetPx — x/y fine-tune of the arrow sprite after orbit.
    //    bobDistance      — how far the arrow nudges inward each cycle (default 12).
    //    bobPeriod        — full bob cycle in seconds (default 0.55).
    // =========================================================================

    /// <summary>
    /// Fidget-nudge arrow that orbits a target and points inward at it.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class FidgetArrow : CoachMarkWidget
    {
        [Header("Refs")]
        [SerializeField] private RectTransform arrowRect;
        [SerializeField] private Image         arrowImage;
        [SerializeField] private RectTransform canvasRect;

        [Header("Orbit")]
        [Tooltip("Where the arrow sits around the target, in degrees. 0 = right, 90 = up, " +
                 "180 = left, 270 = below. The arrowhead always points inward at the target.")]
        [Range(0f, 360f)]
        [SerializeField] private float orbitAngleDeg = 270f;

        [Tooltip("Distance from the pivot to the arrow, in canvas-reference pixels.")]
        [SerializeField] private float orbitRadius = 70f;

        [Tooltip("Degrees per second the arrow travels around the pivot. 0 = hold at " +
                 "orbitAngleDeg (static). Positive = counter-clockwise.")]
        [SerializeField] private float orbitSpeedDegPerSec = 0f;

        [Tooltip("Nudge the pivot (the point the arrow orbits and points at) off the tracked " +
                 "target, in canvas pixels. Use when the arrow should circle a spot next to the target.")]
        [SerializeField] private Vector2 pivotOffsetPx = Vector2.zero;

        [Tooltip("Final fine-tune of the arrow sprite's position after orbit, in canvas pixels. " +
                 "Use when the arrowhead art isn't centred on its pivot.")]
        [SerializeField] private Vector2 arrowPosOffsetPx = Vector2.zero;

        [Header("Bob tuning")]
        [Tooltip("How far the arrow nudges inward toward the pivot per half-cycle (pixels).")]
        [SerializeField] private float bobDistance  = 12f;
        [Tooltip("Full bob period in seconds.")]
        [SerializeField] private float bobPeriod    = 0.55f;

        // Effective (post-override) orbit params, resolved on Show().
        private float   _angleDeg;
        private float   _radius;
        private float   _speed;
        private Vector2 _pivotOffset;
        private Vector2 _arrowPosOffset;
        
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

            // Manual sine bob (0 → 1 → 0) — the arrow nudges inward toward the pivot.
            _bobTime += Time.unscaledDeltaTime;
            float bob = Mathf.Sin(_bobTime / Mathf.Max(0.0001f, bobPeriod) * Mathf.PI * 2f) * 0.5f + 0.5f;

            // Continuous orbit: advance the angle (and re-aim the head) when a speed is set.
            if (!Mathf.Approximately(_speed, 0f))
            {
                _angleDeg = Mathf.Repeat(_angleDeg + _speed * Time.unscaledDeltaTime, 360f);
                ApplyRotation();
            }

            float rad = _angleDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)); // pivot → arrow

            // offsets / radius are authored in canvas-reference units; scale to screen pixels so a
            // CanvasScaler doesn't shift the arrow off the target.
            float sf = _canvas != null ? _canvas.scaleFactor : 1f;
            Vector2 pivot = screen + _pivotOffset * sf;
            float   r     = _radius - bobDistance * bob;                 // nudge inward
            Vector2 pos   = pivot + dir * (r * sf) + _arrowPosOffset * sf;

            // Set screen-space position directly — anchor/parent independent for overlay.
            Vector3 p = arrowRect.position;
            arrowRect.position = new Vector3(pos.x, pos.y, p.z);
        }

        // ── CoachMarkWidget ────────────────────────────────────────────────

        protected override void DoShow()
        {
            if (!_refsOk) return;

            _bobTime = 0f;

            // Resolve effective orbit params: the director's per-phase override (if any) wins over
            // the serialized defaults. orbitSpeed stays widget-level (not part of the request).
            CoachMarkRequest r = Request;
            bool o = r.applyOrbit;
            _angleDeg       = o ? r.orbitAngleDeg : orbitAngleDeg;
            _radius         = (o && r.orbitRadius > 0f) ? r.orbitRadius : orbitRadius;
            _pivotOffset    = o ? r.pivotOffsetPx     : pivotOffsetPx;
            _arrowPosOffset = o ? r.arrowPosOffsetPx  : arrowPosOffsetPx;
            _speed          = orbitSpeedDegPerSec;

            ApplyRotation();

            // Position is driven every frame by LateUpdate (orbits + bobs + tracks).
        }

        protected override void DoHide()
        {
            // LeanTween.cancel called by base.
        }

        // ── Helpers ────────────────────────────────────────────────────────

        // Aim the arrowhead inward at the pivot for the current angle. The arrow sits at
        // orbitAngleDeg around the pivot, so the head must point back the other way; the sprite's
        // default art points up, hence the +90 (down-seat 270 → z 0 → sprite points up at target).
        private void ApplyRotation()
        {
            arrowRect.localRotation = Quaternion.Euler(0f, 0f, _angleDeg + 90f);
        }
    }
}
