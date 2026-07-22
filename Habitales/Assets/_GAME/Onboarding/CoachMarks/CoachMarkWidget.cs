using UnityEngine;

namespace Habitales.Onboarding
{
    // =========================================================================
    //  CoachMarkWidget — abstract base for all coach-mark visuals (B2).
    //
    //  Convention:
    //    • Show(request) — activate and animate this widget.
    //    • Hide()        — stop and deactivate.
    //    • Subclasses implement DoShow / DoHide.
    //    • If trackTarget != null, LateUpdate follows it every frame.
    //    • World→screen conversion uses ScreenCamera (set by CoachMarkLayer).
    // =========================================================================

    /// <summary>
    /// Abstract base for all dumb coach-mark visuals. No game logic — the
    /// <see cref="CoachMarkLayer"/> routes director requests to the matching
    /// subclass, which owns only its own animation lifetime.
    /// </summary>
    public abstract class CoachMarkWidget : MonoBehaviour
    {
        // Injected by CoachMarkLayer on Start.
        protected Camera ScreenCamera { get; private set; }

        // Populated by Show(request).
        protected Vector3     WorldTarget   { get; private set; }
        protected Transform   TrackTarget   { get; private set; }
        protected bool        ScreenSpaceTarget { get; private set; }
        protected string      LabelText     { get; private set; }

        /// <summary>
        /// The full request that drove the current <see cref="Show"/>. Subclasses read this for
        /// kind-specific fields (e.g. FidgetArrow's orbit override) that the base doesn't surface.
        /// </summary>
        protected CoachMarkRequest Request { get; private set; }

        private bool _refsOk;

        // ── Injection (called by CoachMarkLayer) ──────────────────────────

        /// <summary>Called once by <see cref="CoachMarkLayer"/> after it resolves the camera.</summary>
        public void InjectCamera(Camera cam)
        {
            ScreenCamera = cam;
        }

        // ── Public surface ────────────────────────────────────────────────

        /// <summary>
        /// Show this widget. Subclasses override <see cref="DoShow"/> for their
        /// LeanTween setup; this base stores the request fields first.
        /// </summary>
        public void Show(CoachMarkRequest request)
        {
            Request           = request;
            WorldTarget       = request.worldTarget;
            TrackTarget       = request.trackTarget;
            ScreenSpaceTarget = request.screenSpaceTarget;
            LabelText         = request.labelText;

            gameObject.SetActive(true);
            DoShow();
        }

        /// <summary>Stop and hide this widget.</summary>
        public void Hide()
        {
            DoHide();
            LeanTween.cancel(gameObject);
            gameObject.SetActive(false);
            TrackTarget = null;
        }

        // ── Overridable ───────────────────────────────────────────────────

        protected abstract void DoShow();
        protected virtual  void DoHide() { }

        // ── Helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Returns the current screen position for this widget's target.
        /// Uses <see cref="TrackTarget"/> if set (dynamic); else <see cref="WorldTarget"/> (static).
        /// Requires <see cref="ScreenCamera"/> to be non-null.
        /// </summary>
        protected Vector2 CurrentScreenPos()
        {
            // UI target: a Screen-Space-Overlay RectTransform's position is already in
            // screen pixels — return it directly, no camera projection.
            if (ScreenSpaceTarget && TrackTarget != null)
                return TrackTarget.position;

            if (ScreenCamera == null) return Vector2.zero;

            Vector3 world = TrackTarget != null ? TrackTarget.position : WorldTarget;
            return ScreenCamera.WorldToScreenPoint(world);
        }

        /// <summary>
        /// Returns the current screen position as a RectTransform anchoredPosition
        /// for an overlay Canvas (Screen Space – Overlay). Pass the parent RectTransform
        /// of your widget so the conversion uses its canvas scale factor.
        /// </summary>
        protected Vector2 ScreenToCanvasPos(Vector2 screenPos, RectTransform canvasRect)
        {
            // In Screen Space – Overlay the canvas pixel scale is screen-size / canvas-size.
            // RectTransformUtility handles this correctly.
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, screenPos, null, out Vector2 local);
            return local;
        }

        /// <summary>
        /// Places a RectTransform at a screen-pixel position. For a Screen-Space-Overlay canvas a
        /// child's world <c>position</c> is already in screen pixels, so this is anchor- and
        /// parent-independent (unlike anchoredPosition). Keeps the existing z.
        /// </summary>
        protected static void SetScreenPos(RectTransform rect, Vector2 screen)
        {
            Vector3 p = rect.position;
            rect.position = new Vector3(screen.x, screen.y, p.z);
        }

        /// <summary>
        /// Tweens a RectTransform's world position to a screen-pixel position (overlay canvas).
        /// Returns the LTDescr so callers can chain ease / onComplete.
        /// </summary>
        protected static LTDescr MoveToScreen(RectTransform rect, Vector2 screen, float time)
        {
            float z = rect.position.z;
            return LeanTween.move(rect.gameObject, new Vector3(screen.x, screen.y, z), time);
        }
    }
}
