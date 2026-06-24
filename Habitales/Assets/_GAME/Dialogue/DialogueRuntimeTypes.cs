using System.Collections.Generic;
using UnityEngine;

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
        public Sprite portrait;
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
