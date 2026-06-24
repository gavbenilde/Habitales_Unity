using System;
using System.Collections.Generic;
using UnityEngine;

namespace Habitales.Dialogue
{
    [DefaultExecutionOrder(-100)] // manager — initializes after core services (arch §4 init order)
    public class DialogueManager : MonoBehaviour
    {
        public static DialogueManager Instance { get; private set; }

        [Header("References")]
        public DialogueRegistry registry;
        public StickerLibrary stickerLibrary;

        // ─── Stable Tab ID Constants ──────────────────────────────────────────
        // These strings are the runtime keys for the three hard-installed tabs.
        // They do NOT need to match any asset name — they are opaque dictionary keys.
        private const string GROUP_TAB_ID = "group";
        private const string AZI_TAB_ID   = "azi";
        private const string BOB_TAB_ID   = "bob";

        // ─── Runtime State ────────────────────────────────────────────────────

        private Dictionary<string, List<RuntimeChatEntry>> _fixedTabEntries
            = new Dictionary<string, List<RuntimeChatEntry>>();
        private Dictionary<string, WorkerTabData> _workerTabs
            = new Dictionary<string, WorkerTabData>();
        private Dictionary<string, Worker> _workerRefMap
            = new Dictionary<string, Worker>();
        private HashSet<string> _unreadTabs
            = new HashSet<string>();
        private Dictionary<string, int> _birthdayStickerYearSent
            = new Dictionary<string, int>();
        private Dictionary<string, int> _birthdayShoutoutSentYear
            = new Dictionary<string, int>();
        public Dictionary<string, int> chatOpenCounts = new Dictionary<string, int>();

        // Pending sticker response — stored for Invoke delay
        private string   _pendingStickerTabID;
        private StickerSO _pendingStickerResponse;

        private const float MESSAGE_ROLL_CHANCE = 0.40f;
        private const int   COOLDOWN_DAYS       = 2;

        // ─── Public Read-Only State (Law 1: getters, not setters) ────────────

        /// <summary>Total count of tabs with at least one unread message.</summary>
        public int UnreadCount => _unreadTabs.Count;

        // ─── Events ───────────────────────────────────────────────────────────

        public event Action<string> OnMessagesUpdated;
        public event Action<string> OnUnreadChanged;

        // ─── Lifecycle ────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            InitializeFixedTabs();
        }

        private void OnEnable()
        {
            InitializeFixedTabs(); // safe, no external dependencies
            SubscribeToManagers();
        }

        // Idempotent subscription (-= then += guarantees exactly one handler). Fixes the
        // former OnEnable+Start double-subscription that made HandleTimeAdvanced fire twice
        // per day-advance. Start() retries this in case a manager wasn't ready at OnEnable.
        private void SubscribeToManagers()
        {
            if (ResourceManager.Instance != null)
            {
                ResourceManager.Instance.OnTimeAdvanced -= HandleTimeAdvanced;
                ResourceManager.Instance.OnTimeAdvanced += HandleTimeAdvanced;
            }
            else
                Debug.LogWarning("[DialogueManager] ResourceManager not ready — will retry subscription in Start.");

            if (RegionManager.Instance != null)
            {
                RegionManager.Instance.OnRegionGenerated -= HandleRegionGenerated;
                RegionManager.Instance.OnRegionGenerated += HandleRegionGenerated;
            }
            else
                Debug.LogWarning("[DialogueManager] RegionManager not ready — will retry subscription in Start.");
        }

        private void OnDisable()
        {
            if (ResourceManager.Instance != null)
                ResourceManager.Instance.OnTimeAdvanced -= HandleTimeAdvanced;
            if (RegionManager.Instance != null)
                RegionManager.Instance.OnRegionGenerated -= HandleRegionGenerated;
        }

        private void InitializeFixedTabs()
        {
            // Pre-create entry lists for all three hard-installed tabs so callers can
            // append before any conversation has arrived.
            GetOrCreateFixedTabEntries(GROUP_TAB_ID);
            GetOrCreateFixedTabEntries(AZI_TAB_ID);
            GetOrCreateFixedTabEntries(BOB_TAB_ID);
        }

        private void Start()
        {
            // Retry subscription in case a manager wasn't ready at OnEnable (init order).
            // SubscribeToManagers is idempotent, so this never double-subscribes.
            SubscribeToManagers();
        }

        // ─── Core Public Methods ──────────────────────────────────────────────

        public void IncrementChatOpen(string tabID)
        {
            chatOpenCounts.TryGetValue(tabID, out int n);
            chatOpenCounts[tabID] = n + 1;
        }

        // Appends a hardcoded inline message to Azi's tab (Tier 1 / Tier 2 Azi).
        // No ConversationSO required — body is stored directly on the entry.
        public void AppendAziMessage(string body)
        {
            var entry = RuntimeChatEntry.FromInline(body, "Azi");
            GetOrCreateFixedTabEntries(AZI_TAB_ID).Add(entry);
            MarkUnread(AZI_TAB_ID);
            OnMessagesUpdated?.Invoke(AZI_TAB_ID);
        }

        // ─── Conversation Append ──────────────────────────────────────────────

        /// <summary>
        /// Routes a ConversationSO to the correct fixed tab based on conversation.channel.
        /// For Worker-channel conversations, use AppendWorkerConversation instead.
        /// </summary>
        public void AppendConversation(ConversationSO conversation)
        {
            if (conversation == null) return;

            string tabID = ChannelToTabID(conversation.channel);
            if (tabID == null)
            {
                Debug.LogWarning($"[DialogueManager] AppendConversation: channel '{conversation.channel}' on '{conversation.name}' does not map to a fixed tab. Use AppendWorkerConversation for Worker-channel conversations.");
                return;
            }

            // If a Worker-sender node appears in a GroupChat conversation we pick ONE
            // temp worker at append time and bind it — see worker-binding rule (C §7).
            string workerName  = null;
            string workerTrait = null;
            if (ConversationHasWorkerSender(conversation))
            {
                var tempWorker = PickWeightedRandomWorker();
                if (tempWorker != null)
                {
                    workerName  = tempWorker.workerName;
                    workerTrait = tempWorker.trait.ToString();
                }
            }

            var entry = RuntimeChatEntry.FromConversation(conversation, workerName, workerTrait);
            GetOrCreateFixedTabEntries(tabID).Add(entry);
            MarkUnread(tabID);
            OnMessagesUpdated?.Invoke(tabID);
        }

        /// <summary>
        /// Convenience overload — looks up by asset name via registry.
        /// The birthday code calls AppendConversation("birthday_group_shoutout").
        /// </summary>
        public void AppendConversation(string assetName)
        {
            var conv = registry?.GetConversation(assetName);
            if (conv == null)
            {
                Debug.LogWarning($"[DialogueManager] Conversation not found in registry: '{assetName}'");
                return;
            }
            AppendConversation(conv);
        }

        /// <summary>
        /// Appends a worker conversation to that worker's personal DM tab.
        /// Records the conversation asset name in lastSentTemplateIDs for no-repeat logic.
        /// </summary>
        public void AppendWorkerConversation(ConversationSO conversation, Worker worker)
        {
            if (conversation == null || worker == null) return;

            _workerRefMap[worker.workerName] = worker;

            var tab   = GetOrCreateWorkerTab(worker);
            var entry = RuntimeChatEntry.FromConversation(
                conversation,
                worker.workerName,
                worker.trait.ToString()
            );

            tab.entries.Add(entry);

            // No-repeat tracking — keyed by conversation asset name.
            if (!tab.lastSentTemplateIDs.Contains(conversation.name))
                tab.lastSentTemplateIDs.Add(conversation.name);

            MarkUnread(worker.workerName);
            OnMessagesUpdated?.Invoke(worker.workerName);
        }

        private void AppendWorkerInlineMessage(Worker worker, string body)
        {
            if (worker == null) return;

            _workerRefMap[worker.workerName] = worker;

            var tab   = GetOrCreateWorkerTab(worker);
            var entry = RuntimeChatEntry.FromInline(body, worker.workerName);

            tab.entries.Add(entry);
            MarkUnread(worker.workerName);
            OnMessagesUpdated?.Invoke(worker.workerName);
        }

        // ─── Chat Line Resolution ─────────────────────────────────────────────

        public List<ResolvedLine> GetChatLines(string tabID)
        {
            var result  = new List<ResolvedLine>();
            var entries = GetEntriesForTab(tabID);
            if (entries == null) return result;

            foreach (var entry in entries)
            {
                switch (entry.entryType)
                {
                    case ChatEntryType.Thread:
                        result.AddRange(ResolveEntry(entry));
                        break;

                    case ChatEntryType.Inline:
                        result.Add(new ResolvedLine
                        {
                            speakerID   = entry.resolvedWorkerName,
                            displayName = entry.resolvedWorkerName,
                            portrait    = GetWorkerPortrait(entry.resolvedWorkerName),
                            body        = entry.inlineBody
                        });
                        break;

                    case ChatEntryType.PlayerSticker:
                        result.Add(new ResolvedLine
                        {
                            speakerID       = "player",
                            displayName     = "You",
                            isPlayerBubble  = true,
                            isStickerBubble = true,
                            stickerSprite   = entry.stickerSprite
                        });
                        break;

                    case ChatEntryType.WorkerSticker:
                        result.Add(new ResolvedLine
                        {
                            speakerID       = entry.resolvedWorkerName,
                            displayName     = entry.resolvedWorkerName,
                            portrait        = GetWorkerPortrait(entry.resolvedWorkerName),
                            isStickerBubble = true,
                            stickerSprite   = entry.stickerSprite
                        });
                        break;
                }
            }

            return result;
        }

        public List<TabPreview> GetTabPreviews()
        {
            var result = new List<TabPreview>();

            // Fixed tabs — always first, Group Chat always index 0
            if (registry != null)
            {
                result.Add(new TabPreview
                {
                    tabID           = GROUP_TAB_ID,
                    displayName     = registry.GetGroupChatDisplayName(),
                    portrait        = registry.GetGroupChatPortrait(),
                    lastMessageBody = GetLastLineBody(GROUP_TAB_ID),
                    hasUnread       = _unreadTabs.Contains(GROUP_TAB_ID),
                    isBirthday      = false
                });
                result.Add(new TabPreview
                {
                    tabID           = AZI_TAB_ID,
                    displayName     = registry.GetAziDisplayName(),
                    portrait        = registry.GetAziPortrait(),
                    lastMessageBody = GetLastLineBody(AZI_TAB_ID),
                    hasUnread       = _unreadTabs.Contains(AZI_TAB_ID),
                    isBirthday      = false
                });
                result.Add(new TabPreview
                {
                    tabID           = BOB_TAB_ID,
                    displayName     = registry.GetBobDisplayName(),
                    portrait        = registry.GetBobPortrait(),
                    lastMessageBody = GetLastLineBody(BOB_TAB_ID),
                    hasUnread       = _unreadTabs.Contains(BOB_TAB_ID),
                    isBirthday      = false
                });
            }

            // Worker tabs below fixed tabs
            foreach (var kvp in _workerTabs)
            {
                var tab = kvp.Value;
                result.Add(new TabPreview
                {
                    tabID           = kvp.Key,
                    displayName     = tab.isBirthday ? $"🎂 {tab.workerName}" : tab.workerName,
                    portrait        = tab.portrait,
                    lastMessageBody = GetLastLineBody(kvp.Key),
                    hasUnread       = _unreadTabs.Contains(kvp.Key),
                    isBirthday      = tab.isBirthday
                });
            }

            return result;
        }

        public void HandleStickerSent(string tabID, StickerSO sticker)
        {
            if (sticker == null) return;

            // Append player sticker immediately
            var entries = GetEntriesForTab(tabID);
            entries?.Add(RuntimeChatEntry.FromPlayerSticker(sticker.sprite));
            OnMessagesUpdated?.Invoke(tabID);

            // Only worker tabs send a response
            if (!_workerTabs.ContainsKey(tabID)) return;
            if (!_workerRefMap.TryGetValue(tabID, out Worker worker)) return;

            StickerSO response = sticker.isBirthdaySticker
                ? stickerLibrary.GetBirthdayTraitResponse(worker.trait)
                : stickerLibrary.GetTraitResponse(worker.trait);

            if (response == null) return;

            if (sticker.isBirthdaySticker)
                _birthdayStickerYearSent[worker.workerName] = ResourceManager.Instance.CurrentYear;

            _pendingStickerTabID    = tabID;
            _pendingStickerResponse = response;
            Invoke(nameof(AppendPendingWorkerStickerResponse), UnityEngine.Random.Range(0.8f, 1.2f));
        }

        public void MarkTabRead(string tabID)
        {
            if (_unreadTabs.Remove(tabID))
                OnUnreadChanged?.Invoke(tabID);
        }

        public bool IsBirthdayStickerLocked(string workerName)
        {
            return _birthdayStickerYearSent.TryGetValue(workerName, out int year)
                && year == ResourceManager.Instance.CurrentYear;
        }

        // ─── Event Hooks ──────────────────────────────────────────────────────

        private void HandleTimeAdvanced(int daysElapsed)
        {
            int totalDays = ResourceManager.Instance.TotalDays;
            RunWorkerMessageRandomizer(totalDays);
            RunBirthdayCheck(totalDays);
        }

        private void HandleRegionGenerated(RegionGenerationResult result)
        {
            // Narrative conversations tied to region events come through EventManager
            // via AppendConversation(linkedThread). Reserved for future direct triggers.
        }

        // ─── Worker Message Randomizer ────────────────────────────────────────

        private void RunWorkerMessageRandomizer(int totalDays)
        {
            // Step 1 — Chance gate
            if (UnityEngine.Random.value > MESSAGE_ROLL_CHANCE) return;

            // Step 2 — Build weighted pool
            var allWorkers = ResourceManager.Instance.AllWorkers;
            var eligible   = new List<(Worker worker, float weight)>();

            foreach (var worker in allWorkers)
            {
                if (worker.actionsParticipated <= 0) continue;

                int lastDay = _workerTabs.TryGetValue(worker.workerName, out WorkerTabData tab)
                    ? tab.lastMessagedDay
                    : -1;

                if (totalDays - lastDay < COOLDOWN_DAYS) continue;

                eligible.Add((worker, worker.actionsParticipated));
            }

            if (eligible.Count == 0) return;

            // Weighted random walk
            float total      = 0f;
            foreach (var e in eligible) total += e.weight;
            float roll       = UnityEngine.Random.value * total;
            float cumulative = 0f;
            Worker selected  = eligible[eligible.Count - 1].worker;

            foreach (var e in eligible)
            {
                cumulative += e.weight;
                if (cumulative >= roll) { selected = e.worker; break; }
            }

            // Step 3 — Fetch conversation pool for this worker's trait, exclude already-sent
            var workerTab = GetOrCreateWorkerTab(selected);
            var pool      = registry != null
                ? new List<ConversationSO>(registry.GetWorkerDailyPool(selected.trait))
                : new List<ConversationSO>();

            // Remove conversations already sent to this worker (no-repeat per worker).
            pool.RemoveAll(c => c == null || workerTab.lastSentTemplateIDs.Contains(c.name));

            if (pool.Count == 0)
            {
                // Pool exhausted for this worker this run — skip the roll silently.
                return;
            }

            var chosen = pool[UnityEngine.Random.Range(0, pool.Count)];
            AppendWorkerConversation(chosen, selected);
            workerTab.lastMessagedDay = totalDays;
        }

        // ─── Birthday Check ───────────────────────────────────────────────────

        private void RunBirthdayCheck(int totalDays)
        {
            int dayOfYear = totalDays % 365 == 0 ? 365 : totalDays % 365;

            foreach (var worker in ResourceManager.Instance.AllWorkers)
            {
                var tab        = GetOrCreateWorkerTab(worker);
                bool isBirthday = worker.birthdayDay == dayOfYear;
                tab.isBirthday = isBirthday;

                if (!isBirthday) continue;

                int currentYear = ResourceManager.Instance.CurrentYear;
                if (_birthdayShoutoutSentYear.TryGetValue(worker.workerName, out int sentYear)
                    && sentYear == currentYear) continue;

                EventContext.SetOverride("workerName", worker.workerName);
                AppendConversation("birthday_group_shoutout");
                _birthdayShoutoutSentYear[worker.workerName] = currentYear;
            }
        }

        // ─── Internal Helpers ─────────────────────────────────────────────────

        private void AppendPendingWorkerStickerResponse()
        {
            if (string.IsNullOrEmpty(_pendingStickerTabID) || _pendingStickerResponse == null) return;
            if (!_workerRefMap.TryGetValue(_pendingStickerTabID, out Worker worker)) return;

            var entry = RuntimeChatEntry.FromWorkerSticker(
                _pendingStickerResponse.sprite,
                worker.workerName,
                worker.trait.ToString()
            );

            _workerTabs[_pendingStickerTabID].entries.Add(entry);
            MarkUnread(_pendingStickerTabID);
            OnMessagesUpdated?.Invoke(_pendingStickerTabID);

            _pendingStickerTabID    = null;
            _pendingStickerResponse = null;
        }

        // Resolves a Thread-type RuntimeChatEntry via the bound conversation asset.
        private List<ResolvedLine> ResolveEntry(RuntimeChatEntry entry)
        {
            return ResolveConversationLines(
                entry.conversation,
                entry.resolvedWorkerName,
                entry.resolvedWorkerTrait
            );
        }

        /// <summary>
        /// Resolves a ConversationSO into display-ready lines, independent of the
        /// chat-history path, so non-chat presenters (the Narrative Popup façade's
        /// Dialog / Character / Text views) render a conversation identically to how
        /// chat renders it (S2: one resolution path, two surfaces).
        ///
        /// Phase 1 — linear walk only. ChoicePayload nodes are skipped; interactive
        /// choice walking is out of scope until Phase 2.
        /// </summary>
        public List<ResolvedLine> ResolveConversationLines(ConversationSO conversation, string workerName = null, string workerTrait = null)
        {
            var result = new List<ResolvedLine>();
            if (conversation == null) return result;

            if (!string.IsNullOrEmpty(workerName))
            {
                EventContext.SetOverride("workerName",  workerName);
                EventContext.SetOverride("workerTrait", workerTrait ?? "");
            }

            foreach (var node in conversation.thread)
            {
                if (node == null || node.payload == null) continue;

                // ── Phase 2: interactive choice walker ──
                // ChoicePayload nodes are intentionally skipped here. The interactive
                // choice runtime is out of scope for Phase 1. Add handling here in Phase 2.
                if (node.payload is ChoicePayload)
                    continue;

                // Resolve speaker identity for this node.
                string displayName;
                Sprite portrait;
                bool   isPlayer = node.sender == DialogueSpeaker.Player;

                if (isPlayer)
                {
                    displayName = "You";
                    portrait    = null;
                }
                else if (node.sender == DialogueSpeaker.Azi && registry?.aziProfile != null)
                {
                    displayName = registry.aziProfile.displayName;
                    portrait    = registry.aziProfile.GetExpression(node.expressionId);
                }
                else if (node.sender == DialogueSpeaker.Bob && registry?.bobProfile != null)
                {
                    displayName = registry.bobProfile.displayName;
                    portrait    = registry.bobProfile.GetExpression(node.expressionId);
                }
                else if (node.sender == DialogueSpeaker.Worker)
                {
                    // Worker-sender binding — name/portrait committed at append time.
                    displayName = workerName ?? "Worker";
                    portrait    = GetWorkerPortrait(workerName);
                }
                else
                {
                    // Fallback for Azi/Bob with missing profile.
                    displayName = node.sender.ToString();
                    portrait    = null;
                }

                // Emit one line per payload type.
                if (node.payload is ContentPayload content)
                {
                    result.Add(new ResolvedLine
                    {
                        speakerID      = node.sender.ToString(),
                        displayName    = displayName,
                        portrait       = portrait,
                        body           = EventContext.Resolve(content.body),
                        expressionID   = node.expressionId,
                        isPlayerBubble = isPlayer
                    });
                }
                else if (node.payload is StickerPayload stickerPay)
                {
                    result.Add(new ResolvedLine
                    {
                        speakerID       = node.sender.ToString(),
                        displayName     = displayName,
                        portrait        = portrait,
                        expressionID    = node.expressionId,
                        isPlayerBubble  = isPlayer,
                        isStickerBubble = true,
                        stickerSprite   = stickerPay.sticker != null ? stickerPay.sticker.sprite : null
                    });
                }
                // ChoicePayload already continued above.
            }

            return result;
        }

        // ─── Worker-Binding Helpers ───────────────────────────────────────────

        /// <summary>
        /// Returns true if any node in the conversation has sender == Worker.
        /// Used to decide whether to bind a temp worker at append time for GroupChat conversations.
        /// </summary>
        private bool ConversationHasWorkerSender(ConversationSO conversation)
        {
            if (conversation == null) return false;
            foreach (var node in conversation.thread)
                if (node != null && node.sender == DialogueSpeaker.Worker) return true;
            return false;
        }

        /// <summary>
        /// Picks one worker by actionsParticipated weight (same distribution as the
        /// randomizer's eligibility walk, but without the cooldown gate — this is
        /// used for a one-off binding, not a daily delivery).
        /// Returns null if no workers have participated.
        /// </summary>
        private Worker PickWeightedRandomWorker()
        {
            var allWorkers = ResourceManager.Instance?.AllWorkers;
            if (allWorkers == null) return null;

            var candidates = new List<(Worker w, float weight)>();
            foreach (var worker in allWorkers)
            {
                if (worker.actionsParticipated > 0)
                    candidates.Add((worker, worker.actionsParticipated));
            }

            if (candidates.Count == 0) return null;

            float total      = 0f;
            foreach (var c in candidates) total += c.weight;
            float roll       = UnityEngine.Random.value * total;
            float cumulative = 0f;
            Worker result    = candidates[candidates.Count - 1].w;

            foreach (var c in candidates)
            {
                cumulative += c.weight;
                if (cumulative >= roll) { result = c.w; break; }
            }

            return result;
        }

        // ─── Tab/Entry Helpers ────────────────────────────────────────────────

        /// <summary>Maps a DialogueChannel to its runtime tab ID string. Returns null for Worker.</summary>
        private string ChannelToTabID(DialogueChannel channel)
        {
            return channel switch
            {
                DialogueChannel.GroupChat => GROUP_TAB_ID,
                DialogueChannel.Azi       => AZI_TAB_ID,
                DialogueChannel.Bob       => BOB_TAB_ID,
                _                         => null
            };
        }

        private WorkerTabData GetOrCreateWorkerTab(Worker worker)
        {
            if (!_workerTabs.TryGetValue(worker.workerName, out WorkerTabData tab))
            {
                tab = new WorkerTabData
                {
                    workerName = worker.workerName,
                    portrait   = worker.stockPhoto
                };
                _workerTabs[worker.workerName]  = tab;
                _workerRefMap[worker.workerName] = worker;
            }
            return tab;
        }

        private List<RuntimeChatEntry> GetOrCreateFixedTabEntries(string tabID)
        {
            if (!_fixedTabEntries.TryGetValue(tabID, out var list))
            {
                list = new List<RuntimeChatEntry>();
                _fixedTabEntries[tabID] = list;
            }
            return list;
        }

        private List<RuntimeChatEntry> GetEntriesForTab(string tabID)
        {
            if (_fixedTabEntries.TryGetValue(tabID, out var fixedList)) return fixedList;
            if (_workerTabs.TryGetValue(tabID, out var workerTab))      return workerTab.entries;
            return null;
        }

        private Sprite GetWorkerPortrait(string workerName)
        {
            if (string.IsNullOrEmpty(workerName)) return registry?.fallbackWorkerPortrait;
            if (_workerTabs.TryGetValue(workerName, out WorkerTabData tab) && tab.portrait != null)
                return tab.portrait;
            return registry?.fallbackWorkerPortrait;
        }

        private string GetLastLineBody(string tabID)
        {
            var entries = GetEntriesForTab(tabID);
            if (entries == null || entries.Count == 0) return "";

            var last = entries[entries.Count - 1];

            if (last.entryType == ChatEntryType.Inline)
                return last.inlineBody;

            if (last.entryType != ChatEntryType.Thread) return "[Sticker]";

            // Resolve the last content line from the conversation.
            var lines = ResolveConversationLines(last.conversation, last.resolvedWorkerName, last.resolvedWorkerTrait);
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (!lines[i].isStickerBubble && !string.IsNullOrEmpty(lines[i].body))
                    return lines[i].body;
            }
            return "";
        }

        private void MarkUnread(string tabID)
        {
            _unreadTabs.Add(tabID);
            OnUnreadChanged?.Invoke(tabID);
        }
    }
}
