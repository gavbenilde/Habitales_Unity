using System;
using System.Collections.Generic;
using UnityEngine;
using Habitales.Dialogue;

namespace Habitales.UI
{
    /// <summary>
    /// The front door for THREADED narrative popups. Two flavours remain:
    ///
    ///   • Dialog    — intrusive, dimmed, portrait, THREADED (DialoguePopupView).
    ///   • Character — non-intrusive side bubble with portrait (SideNarrativeBubble).
    ///   • Text      — non-intrusive side bubble, no portrait (SideNarrativeBubble).
    ///
    /// The former "Headline" (one-shot EventPopupUI) flavour has been retired (WO-2,
    /// 2026-06-30). EventManager now routes event popups through UIManager.ShowPopup →
    /// PopupController instead. Do not add ShowHeadline back here.
    ///
    /// Threaded calls accept either a ConversationSO (authored once, also renders in chat)
    /// or a raw list of ResolvedLines (hardcoded onboarding beats). Style presets live on
    /// <see cref="PopupStyle"/>. This is the only class the rest of the game talks to for
    /// threaded narrative; single-shot popups go through UIManager → PopupController (S2).
    /// </summary>
    public class NarrativePopupManager : MonoBehaviour
    {
        public static NarrativePopupManager Instance { get; private set; }

        [Header("Views (wire both)")]
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

        /// <summary>
        /// Hard-hide every threaded narrative surface (e.g. on game over).
        /// Does NOT touch event-popup dismissal — that goes through UIManager → PopupController.
        /// </summary>
        public void HideAll()
        {
            if (dialogueView != null) dialogueView.Hide();
            if (sideBubble != null) sideBubble.Hide();
        }
    }
}
