namespace Habitales.UI
{
    /// <summary>
    /// Contract every UI subsystem implements so the hub can drive visibility
    /// uniformly (hide-all-UI, restore-visibility) and so registration is consistent.
    ///
    /// <para><b>Direction (Law 2):</b> implementations are passive to the hub — they expose
    /// <c>SetVisible</c> for the hub to call downward; they raise <c>event Action</c>
    /// upward on meaning. No game-state writes from inside <c>SetVisible</c>.</para>
    /// </summary>
    public interface IUISubsystem
    {
        /// <summary>
        /// Stable identifier used in logs and debug lookups — e.g. "hud", "popups", "actionbar".
        /// </summary>
        string SubsystemId { get; }

        /// <summary>Whether the subsystem's root canvas / GameObject is currently visible.</summary>
        bool IsVisible { get; }

        /// <summary>
        /// Show or hide the subsystem's own root canvas / CanvasGroup / GameObject.
        /// Passive: no game-state writes, no cross-UI side-effects.
        /// </summary>
        void SetVisible(bool visible);
    }
}
