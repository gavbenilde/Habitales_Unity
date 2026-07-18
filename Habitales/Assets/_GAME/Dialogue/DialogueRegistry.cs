using System.Collections.Generic;
using UnityEngine;

namespace Habitales.Dialogue
{
    [CreateAssetMenu(fileName = "DialogueRegistry", menuName = "Habitales/Dialogue/Dialogue Registry")]
    public class DialogueRegistry : ScriptableObject
    {
        // ─── Authored Conversations ───────────────────────────────────────────

        [Header("Conversations")]
        public List<ConversationSO> conversations = new List<ConversationSO>();

        // ─── Fixed-Tab Config ─────────────────────────────────────────────────
        // DialogueTabSO is gone; fixed tab identity + portrait are stored directly here.

        [Header("Fixed Tab — Group Chat")]
        public string groupChatName          = "Group Chat";
        public Sprite groupChatPortrait;

        [Header("Fixed Tab — Azi")]
        public CharacterProfileSO aziProfile;

        [Header("Fixed Tab — Bob")]
        public CharacterProfileSO bobProfile;

        [Header("Worker Fallback")]
        public Sprite fallbackWorkerPortrait;

        // ─── Runtime Lookup Caches — built lazily on first access ─────────────

        private Dictionary<string, ConversationSO>        _conversationMap;
        private Dictionary<WorkerTrait, List<ConversationSO>> _workerDailyPool;

        // ─── Public Lookups ───────────────────────────────────────────────────

        /// <summary>
        /// Returns the conversation whose asset name matches, or null if not found.
        /// Equivalent to the old GetThread(threadID) but keyed by ScriptableObject.name.
        /// </summary>
        public ConversationSO GetConversation(string assetName)
        {
            if (_conversationMap == null) BuildCaches();
            _conversationMap.TryGetValue(assetName, out ConversationSO conv);
            return conv;
        }

        /// <summary>
        /// All conversations eligible for a daily worker roll for the given trait:
        /// trigger == DailyRoll &amp;&amp; channel == Worker &amp;&amp; (universal || personality == trait).
        /// </summary>
        public IReadOnlyList<ConversationSO> GetWorkerDailyPool(WorkerTrait trait)
        {
            if (_workerDailyPool == null) BuildCaches();
            if (_workerDailyPool.TryGetValue(trait, out var pool)) return pool;
            return System.Array.Empty<ConversationSO>();
        }

        // ─── Fixed-Tab Helpers ────────────────────────────────────────────────

        /// <summary>Display name for the GroupChat tab.</summary>
        public string GetGroupChatDisplayName() => groupChatName;

        /// <summary>Portrait for the GroupChat tab.</summary>
        public Sprite GetGroupChatPortrait() => groupChatPortrait;

        /// <summary>Display name for Azi's tab (from the CharacterProfileSO, with fallback).</summary>
        public string GetAziDisplayName()  => aziProfile != null ? aziProfile.displayName : "Azi";

        /// <summary>Portrait for Azi's tab (from the CharacterProfileSO).</summary>
        public Sprite GetAziPortrait()     => aziProfile != null ? aziProfile.portrait : null;

        /// <summary>Display name for Bob's tab (from the CharacterProfileSO, with fallback).</summary>
        public string GetBobDisplayName()  => bobProfile != null ? bobProfile.displayName : "Bob";

        /// <summary>Portrait for Bob's tab (from the CharacterProfileSO).</summary>
        public Sprite GetBobPortrait()     => bobProfile != null ? bobProfile.portrait : null;

        // ─── Cache Construction ───────────────────────────────────────────────

        private void BuildCaches()
        {
            _conversationMap = new Dictionary<string, ConversationSO>();
            _workerDailyPool = new Dictionary<WorkerTrait, List<ConversationSO>>();

            foreach (var conv in conversations)
            {
                if (conv == null) continue;

                // Keyed by asset name (SO.name) — matches what birthday code uses as ID string.
                _conversationMap[conv.name] = conv;

                // Worker daily pool — bucket by trait for each matching conversation.
                if (conv.trigger  == DialogueTrigger.DailyRoll
                 && conv.channel  == DialogueChannel.Worker)
                {
                    if (conv.universal)
                    {
                        // Universal — goes into every trait bucket.
                        foreach (WorkerTrait t in System.Enum.GetValues(typeof(WorkerTrait)))
                            AddToWorkerPool(t, conv);
                    }
                    else
                    {
                        AddToWorkerPool(conv.personality, conv);
                    }
                }
            }
        }

        private void AddToWorkerPool(WorkerTrait trait, ConversationSO conv)
        {
            if (!_workerDailyPool.TryGetValue(trait, out var list))
            {
                list = new List<ConversationSO>();
                _workerDailyPool[trait] = list;
            }
            list.Add(conv);
        }

        // ─── Cache Invalidation ───────────────────────────────────────────────
        // Called automatically by Unity in the Editor when the asset is reimported;
        // at runtime only ever fires if the registry SO is hot-reloaded (Addressables).
        private void OnValidate()
        {
            _conversationMap = null;
            _workerDailyPool = null;

#if UNITY_EDITOR
            // Manual conversations are played by direct reference (check-ins, reports);
            // listing one here is an authoring mistake — it can't be rolled or delivered.
            foreach (var conv in conversations)
            {
                if (conv != null && conv.trigger == DialogueTrigger.Manual)
                    Debug.LogWarning($"{name}: '{conv.name}' has Trigger = Manual and should not be in the registry — remove it (it's played by direct reference only).", this);
            }
#endif
        }
    }
}
