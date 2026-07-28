using System;
using System.Collections.Generic;
using UnityEngine;
using Habitales.Dialogue;

namespace Habitales.UI
{
    /// <summary>
    /// The front door for THREADED narrative popups. Two flavours remain:
    ///
    ///   • Dialog    — intrusive, dimmed, portrait, THREADED.
    ///   • Character — non-intrusive side bubble WITH portrait.
    ///   • Text      — non-intrusive side bubble, NO portrait.
    ///
    /// <para><b>This is now a thin facade over <see cref="PopupController"/> (routed through
    /// <see cref="UIManager"/>).</b> It owns no views of its own — it maps a
    /// <see cref="PopupStyle"/> + a list of <see cref="ResolvedLine"/> onto a
    /// <see cref="PopupRequest"/> and hands it to the hub, which drives the one properly-wired
    /// presenter. Presentation logic lives ONLY in PopupController (S2 — one concept, one place);
    /// this class exists so callers (onboarding beats, notes, nudges) keep a stable
    /// <c>Say</c>/<c>PlayThread</c>/<c>PlayLines</c> surface. Renamed from
    /// NarrativePopupManager 2026-07-15 (GUID preserved); the check-in system no longer
    /// touches it — this stays a dumb presentation router.</para>
    ///
    /// <para>The former self-driven <c>DialoguePopupView</c>/<c>SideNarrativeBubble</c> refs are
    /// gone — they were routinely mis-wired to prefab assets (not scene instances), so popups
    /// silently no-op'd. Do not re-add per-view refs here; add them to PopupController's scene
    /// wiring instead.</para>
    /// </summary>
    public class PopupManager : MonoBehaviour
    {
        public static PopupManager Instance { get; private set; }

        private bool _warnedNoHub;   // warn-once guard so a missing hub doesn't spam the log

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        // ── Threaded entry points ──────────────────────────────────────

        /// <summary>Play an authored conversation (also chat-renderable) in the given style.</summary>
        public void PlayThread(ConversationSO conversation, in PopupStyle style, Action onComplete = null)
        {
            if (conversation == null)
            {
                Debug.LogWarning($"{name}: PlayThread called with a null conversation.", this);
                onComplete?.Invoke();
                return;
            }
            var dm = DialogueManager.Instance;
            if (dm == null)
            {
                Debug.LogError($"{name}: DialogueManager.Instance is null — cannot resolve conversation '{conversation.name}'.", this);
                onComplete?.Invoke();
                return;
            }
            PlayLines(dm.ResolveConversationLines(conversation), style, onComplete);
        }

        /// <summary>
        /// Play a raw line list (hardcoded beats) in the given style. Builds a
        /// <see cref="PopupRequest"/> and routes it through <see cref="UIManager.ShowPopup"/>,
        /// which pauses the sim for intrusive popups and drives <see cref="PopupController"/>.
        /// </summary>
        public void PlayLines(IList<ResolvedLine> lines, in PopupStyle style, Action onComplete = null)
        {
            if (lines == null || lines.Count == 0) { onComplete?.Invoke(); return; }

            var hub = UIManager.Instance;
            if (hub == null || hub.Popups == null)
            {
                if (!_warnedNoHub)
                {
                    Debug.LogError($"{name}: UIManager / PopupController unavailable — cannot present narrative popup. " +
                                   "Firing onComplete so callers aren't softlocked.", this);
                    _warnedNoHub = true;
                }
                onComplete?.Invoke();
                return;
            }

            // Honour showPortrait: PopupController draws a portrait whenever a line carries one,
            // so for the portrait-less Text style we strip it. Always copy — never mutate the
            // caller's list — and carry every ResolvedLine field across on the stripped clone.
            var resolved = new List<ResolvedLine>(lines.Count);
            foreach (var src in lines)
            {
                if (src == null) continue;
                resolved.Add(style.showPortrait ? src : StripPortrait(src));
            }
            if (resolved.Count == 0) { onComplete?.Invoke(); return; }

            var request = new PopupRequest
            {
                intrusiveness      = style.intrusiveness,
                lines              = resolved,
                confirmLabel       = "OK",
                onConfirm          = onComplete,
                autoDismissSeconds = style.autoAdvanceSeconds,
                anchor             = style.anchor
            };
            
            // Route through the hub so intrusive popups pause the sim (manageSimState);
            // non-intrusive side bubbles leave the sim running.
            hub.ShowPopup(request, manageSimState: style.intrusive);
        }

        // ── Single-line convenience ────────────────────────────────────

        /// <summary>One line, one speaker. The common onboarding case (an Azi nudge).</summary>
        public void Say(string body, Sprite portrait, string speaker, in PopupStyle style, Action onComplete = null)
        {
            var line = new ResolvedLine { body = body, portrait = portrait, displayName = speaker };
            PlayLines(new List<ResolvedLine> { line }, style, onComplete);
        }

        /// <summary>
        /// Hard-hide every narrative surface (e.g. on game over / trigger-batch abort).
        /// Delegates to <see cref="PopupController.DismissAll"/> via the hub.
        /// </summary>
        public void HideAll()
        {
            UIManager.Instance?.Popups?.DismissAll();
        }

        // ── Helpers ────────────────────────────────────────────────────

        // Clone a line with its portrait cleared, preserving every other field so the
        // Text style loses only the portrait (not speaker name, sticker, expression, etc.).
        private static ResolvedLine StripPortrait(ResolvedLine src) => new ResolvedLine
        {
            speakerID       = src.speakerID,
            displayName     = src.displayName,
            portrait        = null,
            body            = src.body,
            expressionID    = src.expressionID,
            isPlayerBubble  = src.isPlayerBubble,
            isStickerBubble = src.isStickerBubble,
            stickerSprite   = src.stickerSprite
        };
    }
}
