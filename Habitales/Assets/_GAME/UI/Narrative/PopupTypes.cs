using System;
using System.Collections.Generic;
using UnityEngine;
using Habitales.Dialogue;

namespace Habitales.UI
{
    // ─────────────────────────────────────────────────────────────────────────
    // PopupTypes.cs — shared data types for the popup presentation layer (U2).
    //
    // Defines the public contract between callers (EventManager, UIManager,
    // OnboardingDirector) and PopupController. All other popup-related code
    // lives in PopupController (S2 — one concept, one place).
    //
    // Edited 2026-06-30 (WO-1): replaced the single-line content fields
    // (showPortrait/portrait/speakerName/body) with a thread
    // (IReadOnlyList<ResolvedLine> lines). PopupHandle and PopupIntrusiveness
    // are unchanged — UIManager's contract is unaffected.
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
        /// Anchors to a screen corner via <see cref="PopupRequest.anchor"/>.
        /// </summary>
        NonIntrusive,

        /// <summary>
        /// Non-modal like <see cref="NonIntrusive"/> (no dim, no input block, sim keeps
        /// running) — but placed at an explicit canvas position via
        /// <see cref="PopupRequest.position"/> instead of a corner anchor. Use when a
        /// popup must point at a specific spot on screen (a tile, a HUD widget).
        /// </summary>
        Positioned,
        
        Handbook
    }

    // ── Request value-type ────────────────────────────────────────────────────

    /// <summary>
    /// Everything a caller needs to describe a popup — passed by value so there
    /// is no shared mutable state between the caller and PopupController.
    /// Pass <c>in PopupRequest</c> to avoid copies on the hot path.
    ///
    /// <para>
    /// <b>Content:</b> the popup pages through <see cref="lines"/> one at a time.
    /// Each <see cref="ResolvedLine"/> carries its own <c>displayName</c>,
    /// <c>portrait</c>, and <c>body</c>, so every line in a thread can use a
    /// different speaker. The list must be non-null and non-empty;
    /// <c>PopupController.Show</c> loud-fails and returns
    /// <see cref="PopupHandle.None"/> otherwise.
    /// </para>
    ///
    /// <para>
    /// <b>Confirm/Next (Intrusive):</b> intermediate lines show a "Next" button;
    /// the final line shows <see cref="confirmLabel"/> (default "OK"). Pressing
    /// the button on the last line dismisses the popup and invokes
    /// <see cref="onConfirm"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Auto-advance (NonIntrusive):</b> if <see cref="autoDismissSeconds"/>
    /// is positive, each line auto-advances after that many seconds; the final
    /// advance dismisses the popup. A tap always advances/dismisses immediately.
    /// </para>
    /// </summary>
    [Serializable]
    public struct PopupRequest
    {
        // ── Classification ────────────────────────────────────────────────
        /// <summary>
        /// Controls presentation mode. Intrusive = modal dim + input block
        /// (UIManager pauses the sim). NonIntrusive = side bubble, game runs.
        /// </summary>
        public PopupIntrusiveness intrusiveness;

        // ── Content (thread) ─────────────────────────────────────────────
        /// <summary>
        /// Ordered sequence of lines to page through. Each line carries its own
        /// <c>displayName</c>, <c>portrait</c>, and <c>body</c>.
        /// Must be non-null and contain at least one element —
        /// <c>PopupController.Show</c> returns <see cref="PopupHandle.None"/>
        /// and logs an error otherwise.
        /// </summary>
        [NonSerialized] public IReadOnlyList<ResolvedLine> lines;

        // ── Confirm (Intrusive only) ──────────────────────────────────────
        /// <summary>
        /// Label on the final confirm/dismiss button. Defaults to "OK" when
        /// null or empty. Intermediate lines always show "Next".
        /// </summary>
        public string confirmLabel;

        /// <summary>
        /// Invoked when the player taps the confirm button on the last line
        /// (Intrusive) or when the popup auto-dismisses after all lines
        /// (NonIntrusive). Optional — null is safe.
        /// </summary>
        [NonSerialized] public Action onConfirm;

        // ── Auto-dismiss per line (NonIntrusive only) ─────────────────────
        /// <summary>
        /// Seconds to display each line before auto-advancing to the next.
        /// 0 = wait for a tap on every line.
        /// Ignored for Intrusive popups (they always wait for the button).
        /// </summary>
        public float autoDismissSeconds;

        // ── Screen placement (NonIntrusive only) ─────────────────────────
        /// <summary>
        /// Corner anchor for non-intrusive side bubbles.
        /// Ignored for Intrusive popups (they centre + dim) and for
        /// <see cref="PopupIntrusiveness.Positioned"/> popups (they use
        /// <see cref="position"/>).
        /// </summary>
        public ScreenAnchor anchor;

        // ── Explicit placement (Positioned only) ─────────────────────────
        /// <summary>
        /// Anchored canvas position for <see cref="PopupIntrusiveness.Positioned"/>
        /// popups, as an offset from the canvas centre (0,0 = centre). Fed straight
        /// into the bubble's <c>RectTransform.anchoredPosition</c>. Ignored for
        /// Intrusive and NonIntrusive popups.
        /// </summary>
        public Vector2 position;
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
