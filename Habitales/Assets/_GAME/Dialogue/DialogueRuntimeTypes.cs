using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Habitales.Dialogue
{
    public enum ChatEntryType
    {
        Thread,
        PlayerSticker,
        WorkerSticker,
        Inline
    }

    public class RuntimeChatEntry
    {
        public ChatEntryType entryType;

        // Used when entryType == Thread — direct asset reference, no ID round-trip
        public ConversationSO conversation;

        // Used when entryType == PlayerSticker or WorkerSticker
        public Sprite stickerSprite;

        // Used when entryType == Inline
        public string inlineBody;

        // Worker context — stored at append time, used at resolve time.
        // For worker-tab conversations the bound worker name/trait live here.
        // For Inline entries resolvedWorkerName doubles as the speaker display name.
        public string resolvedWorkerName;
        public string resolvedWorkerTrait;

        // Phase 2 — interactive choice path. Element i holds the option index the
        // player picked for the i-th ChoicePayload encountered during the (DFS pre-order)
        // interactive walk of this entry's conversation. Persisting it here keeps the
        // chat re-flatten (GetChatLines) a pure function of state, so re-opening a tab
        // replays the same branch the player chose.
        public List<int> chosenOptionIndices = new List<int>();

        // Phase 2 — choice expiry. Set true when a day passes with this entry's choice
        // still unanswered: the prompt stays as chat history but the buttons vanish and
        // the thread freezes at the choice (the player "ghosted" the reply). Crucially it
        // un-halts the tab so the next day's messages aren't hidden behind a stale choice.
        public bool choicesExpired;

        public static RuntimeChatEntry FromConversation(ConversationSO conversation, string workerName = null, string workerTrait = null)
        {
            return new RuntimeChatEntry
            {
                entryType           = ChatEntryType.Thread,
                conversation        = conversation,
                resolvedWorkerName  = workerName,
                resolvedWorkerTrait = workerTrait
            };
        }

        public static RuntimeChatEntry FromPlayerSticker(Sprite sprite)
        {
            return new RuntimeChatEntry
            {
                entryType     = ChatEntryType.PlayerSticker,
                stickerSprite = sprite
            };
        }

        public static RuntimeChatEntry FromWorkerSticker(Sprite sprite, string workerName, string workerTrait)
        {
            return new RuntimeChatEntry
            {
                entryType           = ChatEntryType.WorkerSticker,
                stickerSprite       = sprite,
                resolvedWorkerName  = workerName,
                resolvedWorkerTrait = workerTrait
            };
        }

        // Used for hardcoded inline messages (e.g. Tier 1/2 Azi) — no ConversationSO required.
        public static RuntimeChatEntry FromInline(string body, string speakerDisplayName)
        {
            return new RuntimeChatEntry
            {
                entryType          = ChatEntryType.Inline,
                inlineBody         = body,
                resolvedWorkerName = speakerDisplayName
            };
        }
    }

    public class ResolvedLine
    {
        public string speakerID;
        public string displayName;
        public string title;
        public Sprite portrait;
        public RuntimeAnimatorController portraitAnimator;
        public string body;
        public string expressionID;

        public bool isPlayerBubble;
        public bool isStickerBubble;
        public Sprite stickerSprite;
    }

    public class TabPreview
    {
        public string tabID;
        public string displayName;
        public Sprite portrait;
        public string lastMessageBody;
        public bool hasUnread;
        public bool isBirthday;
    }

    // Pushed to the chat UI when an interactive walk pauses on an unanswered
    // ChoicePayload. A pure view object (Law 1) — carries no asset/entry refs.
    // The player's tap resolves to an index into 'options', sent back via
    // DialogueManager.SelectChoice(tabID, index).
    public class PendingChoice
    {
        public string tabID;
        public List<ChoiceOptionView> options = new List<ChoiceOptionView>();
    }

    // Display data for one selectable choice button (the option's label).
    public class ChoiceOptionView
    {
        public bool   isSticker;
        public string label;          // resolved text when !isSticker
        public Sprite stickerSprite;  // bubble sprite when isSticker
    }

    public class WorkerTabData
    {
        public string workerName;
        public Sprite portrait;
        public List<RuntimeChatEntry> entries   = new List<RuntimeChatEntry>();
        public int lastMessagedDay              = -1;
        // Keyed by conversation asset name — prevents sending the same conversation twice.
        public List<string> lastSentTemplateIDs = new List<string>();
        public bool isBirthday;
    }
}
