using System;
using UnityEngine;

namespace Habitales.Onboarding
{
    // ─────────────────────────────────────────────────────────────────────────
    // THE CONTRACT — enums and data types that B2 / B3 / B4 build against.
    // Do NOT rename these values without updating sibling agents.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every beat in the Alpha onboarding sequence. Values are stable integers — safe to
    /// store in save data. Beat numbering mirrors the design doc (0, 1_1, 1_2, …).
    /// </summary>
    public enum OnboardingBeatId
    {
        None            = -1,

        Beat_0_PriorityZero     = 0,    // Captain premise — full-screen GameEventSO headline

        // ── Click-teaching cycle (repeats 3×) ──────────────────────────────────
        Beat_Click_Arm          = 11,   // Arm the Plant Trees action from the bar
        Beat_Click_Place        = 12,   // Click a tile to select one seed
        Beat_Click_Confirm      = 13,   // Arrow → Confirm; press it to commit
        Beat_1_4_PassDay        = 14,   // "Pass a day" nudge — fires after first action confirms

        // ── Drag-teaching cycle (repeats 3×) ───────────────────────────────────
        Beat_Drag_Arm           = 23,   // Arm the Plant Trees action again
        Beat_Drag_Select        = 24,   // Hold-drag multi-select 3+ seeds (Ghost Drag + DragInset)
        Beat_Drag_Confirm       = 25,   // Arrow → Confirm; press it to commit

        Beat_2_2_TileInspector  = 22,   // Tile inspector discovered (tile selected in non-onboarding state)
        Beat_3_1_FirstRibbon    = 31,   // Region 2 unlocked → first ribbon
        Beat_3_2_LookAround     = 32,   // RMB pan nudge
        Beat_3_3_NewTool        = 33,   // New action card unlocked
        Beat_3_4_Graduation     = 34,   // Scaffolding retires — onboarding complete
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Coach-mark surface — B2 builds the visuals; the Director calls these.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Which coach-mark visual the Director is requesting.
    /// </summary>
    public enum CoachMarkKind
    {
        None,
        BlinkingTileMarker,     // Pulse highlight on a world-space tile
        FidgetArrow,            // Nudge arrow pointing toward a world-space target
        GhostMouseClick,        // LeanTween ghost-mouse performing a single LMB click
        GhostMouseDrag,         // LeanTween ghost-mouse performing a drag (beat 2.1)
        CornerReminder,         // Persistent screen-corner text reminder (e.g. "Select multiple")
    }

    /// <summary>
    /// Director → B2 request. Carries the kind plus an optional world-space anchor and
    /// optional label text (for CornerReminder). B2 is responsible for the lifetime; the
    /// Director calls <see cref="OnCoachMarkRequested"/> and does not track the widget.
    /// </summary>
    [Serializable]
    public struct CoachMarkRequest
    {
        public CoachMarkKind kind;

        /// <summary>World-space position the mark should point at or appear near.</summary>
        public Vector3 worldTarget;

        /// <summary>Optional Transform to track dynamically (e.g. an action card).</summary>
        public Transform trackTarget;

        /// <summary>
        /// When true, <see cref="trackTarget"/>'s position is already in screen space
        /// (a Screen-Space-Overlay UI RectTransform — its <c>position</c> is in screen
        /// pixels) and must NOT be run through Camera.WorldToScreenPoint. Used for marks
        /// that point at UI buttons (e.g. the Plant Trees card) rather than world tiles.
        /// </summary>
        public bool screenSpaceTarget;

        /// <summary>Optional label / reminder text (used by CornerReminder).</summary>
        public string labelText;

        /// <summary>True → hide this kind of mark (hide-request convention).</summary>
        public bool hide;
    }
}
