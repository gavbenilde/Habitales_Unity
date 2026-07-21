namespace Habitales.Onboarding
{
    /// <summary>
    /// Coach-mark micro-labels for the onboarding (the tiny bits of UI text that are NOT
    /// authored as PopupSO content).
    ///
    /// <para><b>2026-07-21 rebuild:</b> the legacy per-beat Azi script lived here. It is retired —
    /// all spoken/dialogue content now lives in authored <c>PopupSO</c> assets resolved by
    /// <see cref="OnboardingDirector"/> (see ONBOARDING_HANDOFF.md). Only the persistent
    /// CornerReminder labels remain, because they are shown by the CoachMark widget rather than a
    /// popup. Do not re-add beat scripts here — author a PopupSO instead.</para>
    /// </summary>
    public static class OnboardingContent
    {
        // ─── Corner-reminder labels ──────────────────────────────────────────────
        // Persistent screen-corner text; shown by the CornerReminder coach-mark widget.
        public const string Reminder_SelectReselect = "Select / Reselect";
        public const string Reminder_SelectMultiple = "Select multiple";
    }
}
