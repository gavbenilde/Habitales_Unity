using UnityEngine;

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
    /// pacing. One presenter, parameterised — see <see cref="PopupManager"/>.
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

        // ── Explicit placement (non-intrusive only) ──────────────────────────
        // A preset names a FLAVOUR (dim/portrait/pacing); placement is orthogonal to it.
        // These two let a caller keep a preset's flavour while overriding WHERE the
        // bubble lands — the path PopupSO's Positioned + Pos X / Pos Y travels through
        // PopupManager.PlayLines. Ignored when intrusive (modals centre + dim).

        /// <summary>
        /// When true (and <see cref="intrusive"/> is false), the bubble is placed at
        /// <see cref="position"/> instead of the <see cref="anchor"/> corner —
        /// i.e. <see cref="PopupIntrusiveness.Positioned"/> rather than NonIntrusive.
        /// </summary>
        public bool positioned;

        /// <summary>
        /// Canvas position used when <see cref="positioned"/> is true: an offset from
        /// the canvas centre (0,0 = centre, +X right, +Y up), fed to the bubble's
        /// <c>RectTransform.anchoredPosition</c>.
        /// </summary>
        public Vector2 position;

        /// <summary>
        /// Returns a copy of this style pinned to an explicit canvas position — the
        /// flavour (dim / portrait / pacing) is preserved, only the placement changes.
        /// No-ops on intrusive styles, which always centre.
        /// </summary>
        public PopupStyle At(Vector2 canvasPosition)
        {
            PopupStyle copy = this;
            copy.positioned = true;
            copy.position   = canvasPosition;
            return copy;
        }

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
