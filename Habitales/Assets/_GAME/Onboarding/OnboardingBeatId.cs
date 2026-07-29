using System;
using UnityEngine;

namespace Habitales.Onboarding
{
    // ─────────────────────────────────────────────────────────────────────────
    // THE CONTRACT — enums and data types that B2 / B3 / B4 build against.
    // Do NOT rename these values without updating sibling agents.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every phase in aki's 18-slide onboarding exposition (the # column of
    /// ONBOARDING_TUTORIAL_PLAN.md). Values are stable integers — safe to store in save data.
    ///
    /// <para>Rebuilt 2026-07-21 (ONBOARDING_HANDOFF §3): the legacy 11-beat cut
    /// (Beat_Click_*, Beat_Drag_*, Beat_2_0_GroupChat, Beat_3_*) is retired. The two juice
    /// systems re-point here: <c>DragGhostInset</c> → <see cref="Phase_06_SelectTiles"/>,
    /// <c>Beat1_3JuiceDirector</c> → <see cref="Phase_07_Confirm"/>.</para>
    /// </summary>
    public enum OnboardingBeatId
    {
        None                    = -1,
        
        Phase_01_LoadingReveal  = 1,    // Loading screen spectating live Zone-1 tile generation
        Phase_02_MeetAzi        = 2,    // Dialog (worried Azi): "Oh yikes. It's really bad out here…"
        Phase_03_Framing        = 3,    // Dialog: framing device — "Let's recall what we're here to do"
        Phase_04_ActionBar      = 4,    // Text + arrow → Intervene icon; teach the Action Bar
        Phase_05_PickCard       = 5,    // Text; cards dimmed except Plant Trees (arm the plant_trees action)
        Phase_06_SelectTiles    = 6,    // Text + GhostMouseDrag; hold-drag multi-select 3+ (DragGhostInset)

        // ── Worker beats (added 2026-07-23) — the 7-family, in sequence order:
        //   ShowWorkers → Confirm → WorkersTired → FatigueBar. Confirm keeps its stable id 7
        //   (its confirm gate + the dormant Beat1_3JuiceDirector re-point both key off it); the
        //   three new beats take fresh ids so nothing else shifts. Author-facing numbering:
        //   7 = ShowWorkers, 7.1 = Confirm (unchanged), 7.2 = WorkersTired, 7.3 = FatigueBar.
        Phase_07_ShowWorkers    = 70,   // Character: white pings on every worker — "here's your crew" (before confirm)
        Phase_07_Confirm        = 7,    // Text + arrow → Confirm; commit the action (Beat1_3JuiceDirector)
        Phase_07_WorkersTired   = 71,   // Character: red pings on the fatigued crew, AFTER the action lands (OnActionCompleted)
        Phase_07_FatigueBar     = 72,   // Character: FidgetArrow at the workforce / fatigue bar

        Phase_08_TimeStamina    = 8,    // Character: actions cost days; workers rest — highlight counters
        Phase_09_Weather        = 9,    // Character: weather affects growth — highlight weather hex
        Phase_10_ZoneHealth     = 10,   // Character: zone health bar + dropdown
        Phase_10_1_TraitPips    = 101,  // Character: trait icon pips (Indicators)
        Phase_11_GoalDeadline   = 11,   // Dialog: HQ report deadline + Azi Day-60 check-in (parametric later)
        Phase_12_Stakes         = 12,   // Dialog (worried Azi): stakes / motivation
        Phase_13_RoleAffirm     = 13,   // Dialog (sparkly Azi): player-role affirmation — LAST intrusive
        Phase_14_HelpAffordance = 14,   // Character (single bubble, one thread): Azi will proactively notify
        Phase_15_FreePlay       = 15,   // No UI — free play toward the zone-unlock threshold
        Phase_16_ZoneUnlock     = 16,   // Camera pan to the new zone + Character announcement
        Phase_17_Factory        = 17,   // Camera zoom/pan to the factory (persistent pollution source)
        Phase_18_Maintenance    = 18,   // Character/CornerReminder: maintain earlier zones — then graduate
        Onboarding_Controls     = 19,   
        Onboarding_Planting     = 20,   
        Onboarding_UI           = 21,   
        Onboarding_Cleaning     = 22,   
        Onboarding_Ending       = 23,   
        Phase_CleaningIntro     = 24,
        Phase_CleaningBar       = 25,
        Phase_CleaningCard      = 26,
        Phase_CleaningConfirm   = 27,
        Phase_EndingIntro       = 28,
        Phase_WorkerSurprise    = 29,
        Phase_EntityDeathPing   = 30,
        Phase_FactoryIntro      = 31,
        // Phase_WorkerFatigue     = 32,
        Phase_ZoneUnlocked      = 33,
        Phase_EntityDeathIntro  = 34,
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

        // ── FidgetArrow orbit override (ignored by every other kind) ──────────
        // The director stamps these per-phase (see OnboardingDirector.FidgetArrowTuning) so one
        // shared FidgetArrow can sit at a different angle around each target. When applyOrbit is
        // false the widget keeps its own serialized orbit defaults.

        /// <summary>True → the orbit fields below replace the FidgetArrow's serialized defaults for this Show.</summary>
        public bool applyOrbit;

        /// <summary>Where the arrow sits around the target, in degrees (0 = right, 90 = up, 180 = left, 270 = below).</summary>
        [Range(0f, 360f)] public float orbitAngleDeg;

        /// <summary>Distance from the pivot to the arrow, in canvas pixels. &lt;= 0 keeps the widget's serialized radius.</summary>
        public float orbitRadius;

        /// <summary>Nudges the pivot (the point the arrow orbits and points at) off the tracked target, in canvas pixels.</summary>
        public Vector2 pivotOffsetPx;

        /// <summary>Final fine-tune of the arrow sprite's position after the orbit is applied, in canvas pixels.</summary>
        public Vector2 arrowPosOffsetPx;
    }
}
