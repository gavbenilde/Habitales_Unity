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

        // Used when entryType == Thread
        public string threadID;

        // Used when entryType == PlayerSticker or WorkerSticker
        public Sprite stickerSprite;

        // Used when entryType == Inline
        public string inlineBody;

        // Worker context — stored at append time, used at resolve time
        public string resolvedWorkerName;
        public string resolvedWorkerTrait;

        public static RuntimeChatEntry FromThread(string threadID, string workerName = null, string workerTrait = null)
        {
            return new RuntimeChatEntry
            {
                entryType    = ChatEntryType.Thread,
                threadID     = threadID,
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

        // Used for hardcoded inline messages (e.g. Tier 1/2 Azi) — no DialogueThreadSO required.
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
        public List<RuntimeChatEntry> entries  = new List<RuntimeChatEntry>();
        public int lastMessagedDay             = -1;
        public List<string> lastSentTemplateIDs = new List<string>();
        public bool isBirthday;
    }
}