namespace Habitales.Onboarding
{
    /// <summary>
    /// The hardcoded Alpha onboarding script (the doc licenses hardcoding this). Content only —
    /// no triggers, no state. The OnboardingDirector (built by the swarm) owns WHEN each beat
    /// fires and which coach-mark it pairs with; this class owns WHAT Azi says.
    ///
    /// Priority Zero is NOT delivered from here — the live pipeline resolves it as a `PopupSO`
    /// (looked up by eventName in a `PopupCatalogSO`, fired via TriggerManager.Fire) — because
    /// it is the one licensed full-screen contextual-text beat and rides the event pipeline. The
    /// `Beat_0_*` constants below are the CONTRACT TEXT SOURCE OF TRUTH the human copies into that
    /// PopupSO's body lines when authoring/re-authoring it (arch ENDGAME_BUILD_PLAN §2.2) — this
    /// class still owns WHAT Azi says even when the asset, not this file, is what actually renders.
    /// Everything else below is an Azi narrative wrapper, shown as a non-blocking side popup
    /// (PopupStyle.Character) via NarrativePopup.Say(line, aziPortrait, "Azi", PopupStyle.Character).
    /// </summary>
    public static class OnboardingContent
    {
        // ─── Beat 0 / Priority Zero — the run's contract, stated in-fiction ────────
        // Three facts, per ENDGAME_BUILD_PLAN §2.2: (1) how long the season runs, (2) what
        // success looks like, (3) that a report is filed at the end. The PopupSO
        // pipeline (PopupLine.body is shown raw — see PopupSO.ResolveLines) does not
        // auto-substitute tokens in this content, so these are NOT
        // {token} strings — Beat_0_ContractLine is the generic, always-correct version. Where a
        // caller CAN resolve a live day count (the Director, or whoever authors the PopupSO from
        // this), Beat_0_ContractLineTemplate + ResolveContractLine mirror CheckInScheduler's
        // exact idiom (a "{days}" token replaced at show time) so the number never drifts from
        // ResourceManager.RunLengthDays.
        public const string Beat_0_Headline = "Welcome aboard, Captain.";

        public const string Beat_0_ContractLine =
            "Glad you're here! This land's been logged and left bare. Here's the job: " +
            "over this field season, we bring it back — HQ wants this region thriving by the " +
            "time we're done. I file our report the day the season ends, so make it a good one.";

        // Use when a live RunLengthDays value is available (see ResolveContractLine below) —
        // states the day count explicitly instead of "this field season".
        public const string Beat_0_ContractLineTemplate =
            "Glad you're here! This land's been logged and left bare. Here's the job: the field " +
            "season runs {days} days, and HQ wants this region thriving by the end of it. " +
            "I file our report the day the season ends, so make it a good one.";

        /// <summary>
        /// Substitutes the "{days}" token in <see cref="Beat_0_ContractLineTemplate"/> with
        /// <paramref name="runLengthDays"/> (read from <c>ResourceManager.RunLengthDays</c> by the
        /// caller — this class stays a pure content table and does not reach for managers itself).
        /// Mirrors <c>CheckInScheduler</c>'s "{days}" substitution idiom (historic — the check-in
        /// now resolves tokens through EventContext; this stays the raw-content variant).
        /// </summary>
        public static string ResolveContractLine(int runLengthDays)
            => Beat_0_ContractLineTemplate.Replace("{days}", runLengthDays.ToString());

        // The fallback explicit text appears ONLY on a ~3s input stall (Hodent implicit-first).
        // The "light" lines are the gentle nudges shown up front.

        public const string Beat_1_1_PickAction   = "Let's start by planting something.";
        public const string Beat_1_2_PlaceOne     = "There — that spot's begging for something green.";
        public const string Beat_1_2_StallText    = "Press Left Mouse Button to place.";

        // ─── Corner-reminder labels ──────────────────────────────────────────────
        // Persistent screen-corner text; shown by the CornerReminder coach-mark widget.
        public const string Reminder_SelectReselect  = "Select / Reselect";
        public const string Reminder_SelectMultiple  = "Select multiple";

        public const string Beat_1_3_CommitTime   = "Commit to it. Nothing here moves until you do.";
        public const string Beat_1_4_PassDay      = "Want things to settle? Let the team rest — pass a day, see what the morning brings.";

        // ─── Beat 2.0 — forced group-chat kickoff (Azi & Bob) ───────────────────────
        // Delivered as a ConversationSO through DialogueManager.DeliverConversation, NOT via
        // Say() — badge/shake/ribbon fire for free off OnMessagesUpdated. This cue line nudges
        // the player toward the messaging icon; the stall fallback fires per the Director's
        // existing ~3s idle pattern if they haven't opened it yet.
        public const string Beat_2_0_OpenChat     = "Azi and Bob want a word before you go further — check the group chat.";
        public const string Beat_2_0_StallText    = "Azi and Bob are waiting in the group chat…";

        public const string Beat_2_1_DragMany     = "You've got room now — drag across, don't place them one by one.";

        // Beat 3.1 is the FIRST RIBBON (fires when region 2 unlocks). Authored as a thread so it
        // can also surface in the chat app (one thread, two surfaces). See ribbon thread below.
        public const string Beat_3_1_FirstRibbon  = "You opened up new ground — come see what the team's spotting out there.";

        public const string Beat_3_2_LookAround   = "Take a look around.";
        public const string Beat_3_3_NewTool      = "Got something new for you — try it when you're ready.";
        public const string Beat_3_4_Graduation   = "The land's yours now. I'm here if you need me.";
    }
}
