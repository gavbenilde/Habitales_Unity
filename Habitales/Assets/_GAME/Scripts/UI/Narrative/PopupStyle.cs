namespace Habitales.UI
{
    /// <summary>
    /// Where a non-intrusive popup anchors on screen. Ignored for intrusive popups
    /// (they centre + dim). Drives the side bubble's screen corner.
    /// </summary>
    public enum ScreenAnchor
    {
        BottomLeft,
        BottomRight,
        TopLeft,
        TopRight,
        BottomCenter
    }

    /// <summary>
    /// The two independent axes that distinguish every narrative-popup flavour, plus
    /// pacing. One presenter, parameterised — see <see cref="NarrativePopupManager"/>.
    ///
    ///   • intrusive    — dim the background, block input, pause the sim (Dialog Popup).
    ///                    false = a small side bubble that never stops the game.
    ///   • showPortrait — draw the character portrait box. false = bare text box.
    ///
    /// The three presets below name the user-facing types. Author them by preset;
    /// reach for the raw struct only when you want something off the menu.
    /// </summary>
    public struct PopupStyle
    {
        public bool intrusive;
        public bool showPortrait;
        public ScreenAnchor anchor;

        /// <summary>
        /// Seconds before a side bubble auto-advances to the next line. 0 = wait for a
        /// tap. Intrusive popups ignore this — they always wait for the Next button.
        /// </summary>
        public float autoAdvanceSeconds;

        /// <summary>Intrusive, portrait, dims + pauses. The "Dialog Popup UI".</summary>
        public static PopupStyle Dialog => new PopupStyle
        {
            intrusive = true,
            showPortrait = true,
            anchor = ScreenAnchor.BottomCenter,
            autoAdvanceSeconds = 0f
        };

        /// <summary>Side bubble with a portrait, non-blocking. The "Character Popup".</summary>
        public static PopupStyle Character => new PopupStyle
        {
            intrusive = false,
            showPortrait = true,
            anchor = ScreenAnchor.BottomLeft,
            autoAdvanceSeconds = 0f
        };

        /// <summary>Side bubble, no portrait, non-blocking. The "Text Popup".</summary>
        public static PopupStyle Text => new PopupStyle
        {
            intrusive = false,
            showPortrait = false,
            anchor = ScreenAnchor.BottomLeft,
            autoAdvanceSeconds = 0f
        };
    }
}
