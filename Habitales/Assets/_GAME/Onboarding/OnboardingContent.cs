namespace Habitales.Onboarding
{
    /// <summary>
    /// The hardcoded Alpha onboarding script (the doc licenses hardcoding this). Content only —
    /// no triggers, no state. The OnboardingDirector (built by the swarm) owns WHEN each beat
    /// fires and which coach-mark it pairs with; this class owns WHAT Azi says.
    ///
    /// Priority Zero is NOT here — it lives as a `GameEventSO` asset (a headline event), because
    /// it is the one licensed full-screen contextual-text beat and rides the event pipeline.
    /// Everything below is an Azi narrative wrapper, shown as a non-blocking side popup
    /// (PopupStyle.Character) via NarrativePopup.Say(line, aziPortrait, "Azi", PopupStyle.Character).
    /// </summary>
    public static class OnboardingContent
    {
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

        public const string Beat_2_1_DragMany     = "You've got room now — drag across, don't place them one by one.";

        // Beat 3.1 is the FIRST RIBBON (fires when region 2 unlocks). Authored as a thread so it
        // can also surface in the chat app (one thread, two surfaces). See ribbon thread below.
        public const string Beat_3_1_FirstRibbon  = "You opened up new ground — come see what the team's spotting out there.";

        public const string Beat_3_2_LookAround   = "Take a look around.";
        public const string Beat_3_3_NewTool      = "Got something new for you — try it when you're ready.";
        public const string Beat_3_4_Graduation   = "The land's yours now. I'm here if you need me.";
    }
}
