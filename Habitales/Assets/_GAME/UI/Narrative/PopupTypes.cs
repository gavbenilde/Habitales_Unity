using System;
using UnityEngine;

namespace Habitales.UI
{
    // ─────────────────────────────────────────────────────────────────────────
    // PopupTypes.cs — shared data types for the popup presentation layer (U2).
    //
    // Defines the public contract between callers (EventManager, UIManager,
    // OnboardingDirector) and PopupController. All other popup-related code
    // lives in PopupController (S2 — one concept, one place).
    // ─────────────────────────────────────────────────────────────────────────

    // ── Intrusiveness axis ────────────────────────────────────────────────────

    /// <summary>
    /// Whether the popup demands full attention (dims + blocks input + pauses sim)
    /// or is a side-of-screen nudge that lets the player keep playing.
    /// </summary>
    public enum PopupIntrusiveness
    {
        /// <summary>
        /// Modal: full-screen dim, blocks all input, sim paused by UIManager.
        /// Requires an explicit Confirm/dismiss action to clear.
        /// </summary>
        Intrusive,

        /// <summary>
        /// Non-modal: side bubble, no dim, no input block, sim keeps running.
        /// Clears on tap or after <see cref="PopupRequest.autoDismissSeconds"/>.
        /// </summary>
        NonIntrusive
    }

    // ── Request value-type ────────────────────────────────────────────────────

    /// <summary>
    /// Everything a caller needs to describe a popup — passed by value so there
    /// is no shared mutable state between the caller and PopupController.
    /// Pass <c>in PopupRequest</c> to avoid copies on the hot path.
    /// </summary>
    [Serializable]
    public struct PopupRequest
    {
        // ── Classification ────────────────────────────────────────────────
        public PopupIntrusiveness intrusiveness;
        public bool               showPortrait;

        // ── Content ───────────────────────────────────────────────────────
        public Sprite portrait;
        public string speakerName;
        public string body;

        // ── Confirm (Intrusive only) ──────────────────────────────────────
        /// <summary>Label on the confirm button. Defaults to "OK" when null/empty.</summary>
        public string confirmLabel;

        /// <summary>
        /// Invoked when the player taps Confirm (Intrusive) or the popup auto-dismisses
        /// (NonIntrusive). Optional — null is safe.
        /// </summary>
        [NonSerialized] public Action onConfirm;

        // ── Auto-dismiss (NonIntrusive only) ─────────────────────────────
        /// <summary>
        /// Seconds before the bubble self-dismisses. 0 = wait for a tap.
        /// Ignored for Intrusive popups (they always wait for the button).
        /// </summary>
        public float autoDismissSeconds;

        // ── Screen placement (NonIntrusive only) ─────────────────────────
        /// <summary>
        /// Corner anchor for non-intrusive side bubbles.
        /// Ignored for Intrusive popups (they centre + dim).
        /// </summary>
        public ScreenAnchor anchor;
    }

    // ── Handle ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lightweight, opaque reference returned by <c>PopupController.Show</c>.
    /// Pass to <c>Dismiss</c> to close a specific popup from code.
    /// Carry the <see cref="intrusiveness"/> so the hub's dismissed handler can
    /// conditionally clear modal state without re-querying the controller.
    /// </summary>
    public readonly struct PopupHandle
    {
        public readonly int                id;
        public readonly PopupIntrusiveness intrusiveness;

        internal PopupHandle(int id, PopupIntrusiveness intrusiveness)
        {
            this.id            = id;
            this.intrusiveness = intrusiveness;
        }

        /// <summary>A sentinel "no popup" handle. id == -1.</summary>
        public static readonly PopupHandle None = new PopupHandle(-1, PopupIntrusiveness.NonIntrusive);

        public bool IsValid => id >= 0;

        public override string ToString() =>
            IsValid ? $"PopupHandle({id}, {intrusiveness})" : "PopupHandle.None";
    }
}
