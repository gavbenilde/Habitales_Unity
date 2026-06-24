using System;
using System.Collections.Generic;
using UnityEngine;
using Habitales.Dialogue;

namespace Habitales.UI
{
    /// <summary>
    /// The single front door for narrative popups. One call site, four flavours:
    ///
    ///   • Headline  — the existing one-shot EventPopupUI (title + body + optional portrait).
    ///   • Dialog    — intrusive, dimmed, portrait, THREADED (DialoguePopupView).
    ///   • Character — non-intrusive side bubble with portrait (SideNarrativeBubble).
    ///   • Text      — non-intrusive side bubble, no portrait (SideNarrativeBubble).
    ///
    /// Threaded calls accept either a ConversationSO (authored once, also renders in chat)
    /// or a raw list of ResolvedLines (hardcoded onboarding beats). Style presets live on
    /// <see cref="PopupStyle"/>. This is the only class the rest of the game talks to;
    /// the three views are wiring details behind it (S2 — one concept, one place).
    /// </summary>
    public class NarrativePopupManager : MonoBehaviour
    {
        public static NarrativePopupManager Instance { get; private set; }

        [Header("Views (wire all three)")]
        [SerializeField] private EventPopupUI headlineView;        // existing one-shot
        [SerializeField] private DialoguePopupView dialogueView;   // intrusive threaded
        [SerializeField] private SideNarrativeBubble sideBubble;   // non-intrusive side

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

        /// <summary>Play a raw line list (hardcoded beats) in the given style.</summary>
        public void PlayLines(IList<ResolvedLine> lines, in PopupStyle style, Action onComplete = null)
        {
            if (style.intrusive)
            {
                if (dialogueView == null)
                {
                    Debug.LogError($"{name}: dialogueView is not wired — intrusive Dialog Popup can't show. Assign a DialoguePopupView in the Inspector.", this);
                    onComplete?.Invoke();
                    return;
                }
                dialogueView.Play(lines, style.showPortrait, onComplete);
            }
            else
            {
                if (sideBubble == null)
                {
                    Debug.LogError($"{name}: sideBubble is not wired — Character/Text popup can't show. Assign a SideNarrativeBubble in the Inspector.", this);
                    onComplete?.Invoke();
                    return;
                }
                sideBubble.Play(lines, style, onComplete);
            }
        }

        // ── Single-line convenience ────────────────────────────────────

        /// <summary>One line, one speaker. The common onboarding case (an Azi nudge).</summary>
        public void Say(string body, Sprite portrait, string speaker, in PopupStyle style, Action onComplete = null)
        {
            var line = new ResolvedLine { body = body, portrait = portrait, displayName = speaker };
            PlayLines(new List<ResolvedLine> { line }, style, onComplete);
        }

        // ── Headline one-shot (delegates to the existing EventPopupUI) ──

        /// <summary>The classic full-screen one-shot. EventManager routes its events here.</summary>
        public void ShowHeadline(GameEventSO ev, string headline, string body,
            Action onContinue = null, Action onAbort = null)
        {
            if (headlineView == null)
            {
                Debug.LogError($"{name}: headlineView (EventPopupUI) is not wired — headline event can't show. Assign it in the Inspector.", this);
                onContinue?.Invoke();   // don't strand EventManager's resume callback
                return;
            }
            headlineView.Show(ev, headline, body, onContinue, onAbort);
        }

        /// <summary>Hard-hide every surface (e.g. on game over).</summary>
        public void HideAll()
        {
            if (headlineView != null) headlineView.Hide();
            if (dialogueView != null) dialogueView.Hide();
            if (sideBubble != null) sideBubble.Hide();
        }
    }
}
