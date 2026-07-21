using System.Collections.Generic;
using UnityEngine;
using Habitales.Dialogue;
using ArtificeToolkit.Attributes;

namespace Habitales.UI
{
    // ─────────────────────────────────────────────────────────────────────────
    // PopupSO.cs — designer-authorable content asset for PopupController (U2).
    //
    // A PopupSO is a self-contained "what to say" spec: a sequence of lines
    // (each attributed to Azi, Bob, or a custom speaker), plus the intrusiveness
    // setting and catalog metadata (fireOnce, linkedThread, focusCameraOnTarget).
    // Pass it to PopupController.Show(PopupSO) and the controller resolves
    // Azi/Bob identity from DialogueRegistry before presenting, or call
    // ResolveLines(registry) to obtain the ResolvedLine list directly.
    //
    // S2: line resolution lives ONLY in ResolveLines — PopupController delegates
    // to it rather than duplicating the logic.
    //
    // Added 2026-06-30 (WO-1, EventManager → TriggerManager rework).
    // Edited 2026-06-30 (WO-3): added fireOnce / linkedThread / focusCameraOnTarget
    // metadata; extracted ResolveLines from PopupController.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Which built-in speaker slot to use for a <see cref="PopupLine"/>.
    /// Azi and Bob resolve their display name + portrait from the
    /// <c>DialogueRegistry</c> at runtime, so character-profile changes
    /// automatically propagate. Custom lets the designer specify a free-form
    /// speaker name (may be empty) with no portrait unless overridden.
    /// </summary>
    public enum PopupSpeaker
    {
        Azi,
        Bob,
        Custom
    }

    /// <summary>
    /// One line of dialogue in a <see cref="PopupSO"/> sequence.
    /// The property drawer hides <see cref="customName"/> unless
    /// <see cref="speaker"/> is <c>Custom</c>.
    /// </summary>
    [System.Serializable]
    public class PopupLine
    {
        /// <summary>Which built-in character slot speaks this line.</summary>
        public PopupSpeaker speaker;

        /// <summary>
        /// Display name shown when <see cref="speaker"/> is <c>Custom</c>.
        /// Blank is allowed (renders no speaker label). Ignored for Azi/Bob.
        /// </summary>
        public string customName;

        /// <summary>
        /// Optional portrait override. When non-null this sprite wins over
        /// any profile portrait resolved for Azi or Bob.
        /// </summary>
        public Sprite portraitOverride;

        /// <summary>The line of body text shown in the popup card / side bubble.</summary>
        [UnityEngine.TextArea(2, 5)]
        public string body;
    }

    /// <summary>
    /// Designer-authorable content asset for the popup presentation layer.
    /// Create via <em>Assets ▶ Create ▶ Habitales ▶ Popup</em>.
    ///
    /// Wire a <c>PopupSO</c> to any caller that has access to
    /// <c>UIManager.Instance</c> and call
    /// <c>UIManager.Instance.Popups.Show(myPopupSO, onConfirm)</c>
    /// — or, if you hold a direct reference to the controller,
    /// <c>popupController.Show(myPopupSO, onConfirm)</c>.
    ///
    /// Character portraits and display names for Azi and Bob are resolved at
    /// runtime from the <c>DialogueRegistry</c> assigned to
    /// <c>PopupController._registry</c> (Law S2 — identity lives in one place).
    /// </summary>
    [CreateAssetMenu(fileName = "Popup_New", menuName = "Habitales/Popup")]
    public class PopupSO : ScriptableObject
    {
        [Tooltip("Catalog ID — the string callers pass to TriggerManager.Fire(id) and PopupCatalogSO.GetById(id). " +
                 "Also shows in the Inspector header and error logs.")]
        public string eventName;

        [Tooltip("Intrusive = modal dim + input block (sim paused by UIManager). " +
                 "NonIntrusive = corner side bubble, game keeps running. " +
                 "Positioned = non-intrusive bubble placed at an explicit Pos X / Pos Y on the canvas.")]
        public PopupIntrusiveness intrusiveness;

        // ── Explicit placement (Positioned only) ──────────────────────────────
        // Shown ONLY when intrusiveness == Positioned (Artifice EnableIf hides these
        // fields for Intrusive / NonIntrusive). Offset from the canvas centre:
        // (0, 0) = dead centre, +X = right, +Y = up.
        [EnableIf(nameof(intrusiveness), PopupIntrusiveness.Positioned)]
        [Tooltip("Positioned only: horizontal offset from the canvas centre, in canvas units (+ = right).")]
        public float posX;

        [EnableIf(nameof(intrusiveness), PopupIntrusiveness.Positioned)]
        [Tooltip("Positioned only: vertical offset from the canvas centre, in canvas units (+ = up).")]
        public float posY;

        [Tooltip("The sequence of lines to page through. At least one line required.")]
        public List<PopupLine> lines = new List<PopupLine>();

        // ── TriggerManager metadata ───────────────────────────────────────────

        [Tooltip("When true, this popup fires at most once per run. TriggerManager tracks fired IDs by eventName.")]
        public bool fireOnce;

        [Tooltip("Optional ConversationSO to append to DialogueManager when this popup fires. " +
                 "The conversation is routed to its declared channel (GroupChat/Azi/Bob).")]
        public ConversationSO linkedThread;

        [Tooltip("When true, TriggerManager pans the camera to the focus target set via EventContext.SetFocusTarget " +
                 "before showing the popup. No-ops if EventContext has no focus target or EventCameraHandler is absent.")]
        public bool focusCameraOnTarget;

        // ── Line resolution (S2 — one place) ─────────────────────────────────

        /// <summary>
        /// Resolves every <see cref="PopupLine"/> in this asset into a <see cref="ResolvedLine"/>
        /// list, looking up Azi/Bob identity from <paramref name="registry"/>.
        ///
        /// <para>
        /// Resolution order per line:
        /// <list type="number">
        ///   <item><c>portraitOverride</c> wins over any profile portrait when non-null.</item>
        ///   <item>Azi/Bob → resolved from <paramref name="registry"/> (or literal name + null portrait if registry is null).</item>
        ///   <item>Custom → <c>customName</c> (may be empty) + null portrait.</item>
        /// </list>
        /// </para>
        ///
        /// <para>
        /// If <paramref name="registry"/> is <c>null</c>, Azi falls back to literal "Azi"
        /// and Bob to "Bob" with null portraits — no exception is thrown (Law 3 tolerant path).
        /// </para>
        /// </summary>
        public List<ResolvedLine> ResolveLines(DialogueRegistry registry)
        {
            bool hasRegistry = registry != null;
            var resolved = new List<ResolvedLine>(lines != null ? lines.Count : 0);

            if (lines == null) return resolved;

            foreach (PopupLine pl in lines)
            {
                string displayName;
                Sprite portrait;

                switch (pl.speaker)
                {
                    case PopupSpeaker.Azi:
                        displayName = hasRegistry ? registry.GetAziDisplayName() : "Azi";
                        portrait    = hasRegistry ? registry.GetAziPortrait()    : null;
                        break;
                    case PopupSpeaker.Bob:
                        displayName = hasRegistry ? registry.GetBobDisplayName() : "Bob";
                        portrait    = hasRegistry ? registry.GetBobPortrait()    : null;
                        break;
                    default: // Custom
                        displayName = pl.customName ?? string.Empty;
                        portrait    = null;
                        break;
                }

                // portraitOverride always wins over the profile portrait.
                if (pl.portraitOverride != null)
                    portrait = pl.portraitOverride;

                resolved.Add(new ResolvedLine
                {
                    speakerID    = pl.speaker.ToString(),
                    displayName  = displayName,
                    portrait     = portrait,
                    // §5: token substitution lives here (the single resolution path) so no caller can bypass it.
                    body         = PopupTokens.Resolve(pl.body) ?? string.Empty,
                    expressionID = string.Empty
                });
            }

            return resolved;
        }
    }
}
