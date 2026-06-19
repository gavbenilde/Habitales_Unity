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
        private string _pendingStickerTabID;
        private StickerSO _pendingStickerResponse;

        private const float MESSAGE_ROLL_CHANCE = 0.40f;
        private const int   COOLDOWN_DAYS       = 25;

        private static readonly string[] s_flavorLines =
        {
            "Tough day out there.",
            "Felt good to swing the spade today.",
            "The tilapia were jumping in the river.",
            "Sun's brutal but the soil's responding.",
            "I think I felt the wind change today.",
            "Saw a kingfisher near the bend.",
            "My back hurts but my heart feels good.",
            "Cap, the air smells different out here."
        };

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
            if (registry == null) return;
            foreach (var tab in registry.tabs)
                if (tab != null && !_fixedTabEntries.ContainsKey(tab.tabID))
                    _fixedTabEntries[tab.tabID] = new List<RuntimeChatEntry>();
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
        
        // Tab ID for Azi's fixed chat tab. The DialogueRegistry asset must include a
        // DialogueTabSO with tabID = "azi" and displayName = "Azi".
        private const string AZI_TAB_ID = "azi";

        // Appends a hardcoded inline message to Azi's tab (Tier 1 / Tier 2 Azi).
        // No DialogueThreadSO required — body is stored directly on the entry.
        public void AppendAziMessage(string body)
        {
            var entry = RuntimeChatEntry.FromInline(body, "Azi");
            GetOrCreateFixedTabEntries(AZI_TAB_ID).Add(entry);
            MarkUnread(AZI_TAB_ID);
            OnMessagesUpdated?.Invoke(AZI_TAB_ID);
        }

        public void AppendThread(DialogueThreadSO thread)
        {
            if (thread == null) return;
            var entry = RuntimeChatEntry.FromThread(thread.threadID);
            GetOrCreateFixedTabEntries(thread.tabID).Add(entry);
            MarkUnread(thread.tabID);
            OnMessagesUpdated?.Invoke(thread.tabID);
        }

        public void AppendThread(string threadID)
        {
            var thread = registry.GetThread(threadID);
            if (thread == null)
            {
                Debug.LogWarning($"[DialogueManager] Thread not found: {threadID}");
                return;
            }
            AppendThread(thread);
        }

        public void AppendWorkerThread(WorkerMessageTemplateSO template, Worker worker)
        {
            if (template == null || worker == null) return;

            _workerRefMap[worker.workerName] = worker;

            var tab   = GetOrCreateWorkerTab(worker);
            var entry = RuntimeChatEntry.FromThread(
                template.threadID,
                worker.workerName,
                worker.trait.ToString()
            );

            tab.entries.Add(entry);
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
                        result.AddRange(ResolveThread(entry));
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
                foreach (var tab in registry.tabs)
                {
                    if (tab == null) continue;
                    result.Add(new TabPreview
                    {
                        tabID           = tab.tabID,
                        displayName     = tab.displayName,
                        portrait        = tab.tabPortrait,
                        lastMessageBody = GetLastLineBody(tab.tabID),
                        hasUnread       = _unreadTabs.Contains(tab.tabID),
                        isBirthday      = false
                    });
                }
            }

            // Worker tabs below
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

            _pendingStickerTabID      = tabID;
            _pendingStickerResponse   = response;
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
            // Narrative threads tied to region events come through EventManager
            // via AppendThread(linkedThread). Reserved for future direct triggers.
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

            // Step 3 — Inline flavor selection
            var workerTab = GetOrCreateWorkerTab(selected);

            string body = s_flavorLines[UnityEngine.Random.Range(0, s_flavorLines.Length)];
            AppendWorkerInlineMessage(selected, body);

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
                AppendThread("birthday_group_shoutout");
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

        private List<ResolvedLine> ResolveThread(RuntimeChatEntry entry)
        {
            var thread = registry.GetThread(entry.threadID);
            return ResolveThreadLines(thread, entry.resolvedWorkerName, entry.resolvedWorkerTrait);
        }

        /// <summary>
        /// Resolves a DialogueThreadSO directly into display-ready lines, independent of the
        /// chat-history path, so non-chat presenters (the Narrative Popup façade's Dialog /
        /// Character / Text views) render a thread identically to how chat renders it. Takes
        /// the SO directly — the thread need not live in the registry (S2: one resolution path,
        /// two surfaces).
        /// </summary>
        public List<ResolvedLine> ResolveThreadLines(DialogueThreadSO thread, string workerName = null, string workerTrait = null)
        {
            var result = new List<ResolvedLine>();
            if (thread == null) return result;

            if (!string.IsNullOrEmpty(workerName))
            {
                EventContext.SetOverride("workerName",  workerName);
                EventContext.SetOverride("workerTrait", workerTrait ?? "");
            }

            foreach (var line in thread.lines)
            {
                bool isPlayer       = line.speakerID == "player";
                SpeakerProfile spk  = thread.GetSpeaker(line.speakerID);

                Sprite portrait     = null;
                string displayName  = line.speakerID;

                if (isPlayer)
                {
                    displayName = "You";
                }
                else if (spk != null)
                {
                    portrait    = spk.GetExpression(line.expressionID);
                    displayName = spk.displayName;
                }
                else if (!string.IsNullOrEmpty(workerName))
                {
                    portrait    = GetWorkerPortrait(workerName);
                    displayName = workerName;
                }

                result.Add(new ResolvedLine
                {
                    speakerID      = line.speakerID,
                    displayName    = displayName,
                    portrait       = portrait,
                    body           = EventContext.Resolve(line.body),
                    expressionID   = line.expressionID,
                    isPlayerBubble = isPlayer
                });
            }

            return result;
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

            var thread = registry.GetThread(last.threadID);
            if (thread == null || thread.lines.Count == 0) return "";

            return thread.lines[thread.lines.Count - 1].body;
        }

        private void MarkUnread(string tabID)
        {
            _unreadTabs.Add(tabID);
            OnUnreadChanged?.Invoke(tabID);
        }
    }
}