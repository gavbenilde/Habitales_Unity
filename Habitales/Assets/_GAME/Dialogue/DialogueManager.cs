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

        // Conversation asset names (ConversationSO.name) whose interactive walk has reached its
        // terminal node at least once — i.e. "read to the end", not merely delivered/opened.
        // General-purpose completion tracking (Law 2: fired on meaning, not on mutation) — any
        // consumer gating on "has the player finished reading X" uses this, not chatOpenCounts
        // (which only counts tab opens, not completion).
        private HashSet<string> _completedConversations = new HashSet<string>();

        // Pending sticker response — stored for Invoke delay
        private string   _pendingStickerTabID;
        private StickerSO _pendingStickerResponse;

        private const float MESSAGE_ROLL_CHANCE = 0.40f;
        private const int   COOLDOWN_DAYS       = 2;

        // ─── Public Read-Only State (Law 1: getters, not setters) ────────────

        /// <summary>Total count of tabs with at least one unread message.</summary>
        public int UnreadCount => _unreadTabs.Count;

        /// <summary>
        /// True once <paramref name="conversationName"/> (a ConversationSO's asset name) has been
        /// walked to its terminal node at least once, during an actual player read (GetChatLines)
        /// — every node visited, every choice (if any) answered. Delivering/appending a
        /// conversation does NOT satisfy this; merely opening its tab without reaching the end
        /// does NOT satisfy this either. Only reaching the end of the thread while the player is
        /// looking at it does (Law 2: meaning, not mutation). Backed by
        /// <see cref="_completedConversations"/>, populated by MarkConversationCompletedIfNeeded.
        /// </summary>
        public bool HasCompletedConversation(string conversationName)
            => !string.IsNullOrEmpty(conversationName) && _completedConversations.Contains(conversationName);

        // ─── Events ───────────────────────────────────────────────────────────

        public event Action<string> OnMessagesUpdated;
        public event Action<string> OnUnreadChanged;

        /// <summary>
        /// Fires once, the first time a delivered conversation's thread is walked to its terminal
        /// node (arg: the ConversationSO's asset name). This is a MEANING event (Law 2) — it does
        /// NOT fire on delivery (AppendConversation/DeliverConversation), tab-open, or intermediate
        /// choice answers; only on "the player has now read this conversation to its end". General
        /// on purpose — any beat/system gating on "conversation finished" subscribes here rather
        /// than inventing its own completion tracking.
        /// </summary>
        public event Action<string> OnConversationCompleted;

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
        /// General-purpose runtime injection point: any system that needs to push a scheduled or
        /// system-sent conversation into the chat at an arbitrary moment (not the daily-roll
        /// randomizer, not a player action) calls this. Routes by <see cref="ConversationSO.channel"/>
        /// exactly like <see cref="AppendConversation(ConversationSO)"/> — GroupChat/Azi/Bob land in
        /// their fixed tab and fire <see cref="OnMessagesUpdated"/> (badge/shake/ribbon already
        /// subscribe, so callers get that FX for free). Worker-channel conversations still need an
        /// explicit worker via <see cref="AppendWorkerConversation"/> (delivering to "a" worker tab
        /// requires choosing which worker — that decision belongs to the caller, not this method).
        /// Name is deliberately generic — this is the seam a scheduled sender (e.g. a mid-run report
        /// notifier) reuses; it is not onboarding-specific despite the first caller being one.
        /// </summary>
        public void DeliverConversation(ConversationSO conversation) => AppendConversation(conversation);

        /// <summary>Overload of <see cref="DeliverConversation(ConversationSO)"/> — looks up by registry asset name.</summary>
        public void DeliverConversation(string assetName) => AppendConversation(assetName);

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

        /// <summary>
        /// Returns the chat transcript visible for a tab. Interactive: walks each
        /// thread following the player's recorded choices and pauses (emits nothing
        /// further) at the first unanswered ChoicePayload. See ResolveTab.
        /// </summary>
        public List<ResolvedLine> GetChatLines(string tabID) => ResolveTab(tabID, isPlayerRead: true).lines;

        /// <summary>
        /// If the tab is currently paused on an unanswered choice, returns the
        /// options to render as a button row; otherwise null. The chat UI calls this
        /// after rebuilding bubbles to decide whether to show the choice row.
        /// </summary>
        public PendingChoice GetPendingChoice(string tabID)
        {
            var res = ResolveTab(tabID);
            if (res.pendingChoice == null) return null;

            var pending = new PendingChoice { tabID = tabID };
            foreach (var option in res.pendingChoice.options)
                pending.options.Add(BuildOptionView(option.label));
            return pending;
        }

        /// <summary>
        /// Player → runtime selection entry point. Records the chosen option on the
        /// paused entry and re-pushes the tab; the next resolve appends the chosen
        /// reply bubble, walks the option's sub-thread, then merges back to the parent.
        /// </summary>
        public void SelectChoice(string tabID, int optionIndex)
        {
            var res = ResolveTab(tabID);
            if (res.pendingChoice == null || res.pendingEntry == null)
            {
                Debug.LogWarning($"[DialogueManager] SelectChoice('{tabID}', {optionIndex}) — no pending choice on this tab.");
                return;
            }
            if (optionIndex < 0 || optionIndex >= res.pendingChoice.options.Count)
            {
                Debug.LogError($"[DialogueManager] SelectChoice: optionIndex {optionIndex} out of range (0..{res.pendingChoice.options.Count - 1}) on tab '{tabID}'.");
                return;
            }

            res.pendingEntry.chosenOptionIndices.Add(optionIndex);
            OnMessagesUpdated?.Invoke(tabID);
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
            // Lapse yesterday's unanswered choices BEFORE today's deliveries, so a fresh
            // daily-roll message can't be hidden behind a stale halt — and so the message
            // we're about to deliver isn't itself expired on arrival.
            ExpireUnansweredChoices();

            int totalDays = ResourceManager.Instance.TotalDays;
            RunWorkerMessageRandomizer(totalDays);
            RunBirthdayCheck(totalDays);
        }

        // ─── Choice Expiry ────────────────────────────────────────────────────

        // A day passed: seal every Thread entry that still has a reachable unanswered
        // choice (across all tabs). Sealing freezes that thread at its prompt and stops
        // it halting the tab. See RuntimeChatEntry.choicesExpired.
        private void ExpireUnansweredChoices()
        {
            var sealedTabs = new List<string>();

            if (SealTab(GetEntriesForTab(GROUP_TAB_ID))) sealedTabs.Add(GROUP_TAB_ID);
            if (SealTab(GetEntriesForTab(AZI_TAB_ID)))   sealedTabs.Add(AZI_TAB_ID);
            if (SealTab(GetEntriesForTab(BOB_TAB_ID)))   sealedTabs.Add(BOB_TAB_ID);
            foreach (var kvp in _workerTabs)
                if (SealTab(kvp.Value.entries)) sealedTabs.Add(kvp.Key);

            // Notify AFTER the walk so an OnMessagesUpdated subscriber can't mutate
            // _workerTabs mid-iteration. Refreshes any open view to drop stale buttons.
            foreach (var tabID in sealedTabs)
                OnMessagesUpdated?.Invoke(tabID);
        }

        private bool SealTab(List<RuntimeChatEntry> entries)
        {
            if (entries == null) return false;

            bool sealedAny = false;
            foreach (var entry in entries)
            {
                if (entry.entryType != ChatEntryType.Thread || entry.choicesExpired) continue;
                if (!EntryHasUnansweredChoice(entry)) continue;
                entry.choicesExpired = true;
                sealedAny = true;
            }
            return sealedAny;
        }

        // True if walking this entry (with its current chosen path) reaches a choice the
        // player hasn't answered yet. Probes a throwaway resolution — the entry is not
        // yet sealed, so an unanswered choice surfaces as pendingChoice.
        private bool EntryHasUnansweredChoice(RuntimeChatEntry entry)
        {
            var probe = new TabResolution();
            ResolveThreadEntry(entry, probe);
            return probe.pendingChoice != null;
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

        /// <summary>
        /// Resolves a ConversationSO into display-ready lines, independent of the
        /// chat-history path, so non-chat presenters (the Narrative Popup façade's
        /// Dialog / Character / Text views) render a conversation identically to how
        /// chat renders it (S2: one resolution path, two surfaces).
        ///
        /// LINEAR fallback for non-interactive surfaces: ChoicePayload nodes are
        /// skipped (the popup façade cannot pause for input). The interactive chat
        /// path goes through ResolveTab instead.
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
                if (node.payload is ChoicePayload) continue; // linear surfaces skip choices

                var line = ResolveContentOrSticker(node, workerName);
                if (line != null) result.Add(line);
            }

            return result;
        }

        /// <summary>
        /// Resolves ONE authored node into a display-ready line, for surfaces that walk a
        /// conversation themselves (e.g. CheckInPanelUI's tap-to-advance playback). Same
        /// pipeline as chat — profiles, expressions, EventContext tokens (S2: one resolution
        /// path). Returns null for ChoicePayload nodes; interactive callers handle choices
        /// via <see cref="ResolveChoiceReplyLine"/> / <see cref="BuildChoiceOptionView"/>.
        /// </summary>
        public ResolvedLine ResolveNodeLine(MessageNode node, string workerName = null)
            => ResolveContentOrSticker(node, workerName);

        /// <summary>Resolves a chosen option's label into the player's reply bubble line.</summary>
        public ResolvedLine ResolveChoiceReplyLine(MessagePayload label)
            => ResolveChoiceLabel(label);

        /// <summary>Resolves an option's label into button-display data for a choice row.</summary>
        public ChoiceOptionView BuildChoiceOptionView(MessagePayload label)
            => BuildOptionView(label);

        // ─── Interactive Choice Walker (Phase 2) ──────────────────────────────

        // Result of walking one tab interactively: the visible lines, plus the first
        // unanswered choice encountered (which pauses the whole tab).
        private sealed class TabResolution
        {
            public List<ResolvedLine> lines = new List<ResolvedLine>();
            public RuntimeChatEntry   pendingEntry;   // entry holding the unanswered choice
            public ChoicePayload      pendingChoice;  // the unanswered choice (null = none)
            public bool Halted => pendingChoice != null;
        }

        /// <summary>
        /// Walks every entry in a tab in order, resolving Thread entries via the
        /// interactive tree walk. The first unanswered ChoicePayload pauses the tab:
        /// no further lines (from that entry or later entries) are emitted until the
        /// player selects an option, so the transcript ends exactly where the choice
        /// buttons appear.
        /// </summary>
        /// <param name="tabID">Tab to resolve.</param>
        /// <param name="isPlayerRead">
        /// True when this resolution represents the player actually viewing the tab
        /// (GetChatLines) — only then do fully-walked Thread entries fire
        /// OnConversationCompleted. False for internal/background resolutions (e.g. the
        /// seal-probe in EntryHasUnansweredChoice), which must not credit the player with
        /// reading a thread they never opened.
        /// </param>
        private TabResolution ResolveTab(string tabID, bool isPlayerRead = false)
        {
            var res     = new TabResolution();
            var entries = GetEntriesForTab(tabID);
            if (entries == null) return res;

            foreach (var entry in entries)
            {
                if (res.Halted) break; // a pending choice pauses the whole tab

                switch (entry.entryType)
                {
                    case ChatEntryType.Thread:
                        bool reachedEnd = ResolveThreadEntry(entry, res);
                        if (isPlayerRead && reachedEnd)
                            MarkConversationCompletedIfNeeded(entry.conversation?.name);
                        break;

                    case ChatEntryType.Inline:
                        res.lines.Add(new ResolvedLine
                        {
                            speakerID   = entry.resolvedWorkerName,
                            displayName = entry.resolvedWorkerName,
                            portrait    = GetWorkerPortrait(entry.resolvedWorkerName),
                            body        = entry.inlineBody
                        });
                        break;

                    case ChatEntryType.PlayerSticker:
                        res.lines.Add(new ResolvedLine
                        {
                            speakerID       = "player",
                            displayName     = "You",
                            isPlayerBubble  = true,
                            isStickerBubble = true,
                            stickerSprite   = entry.stickerSprite
                        });
                        break;

                    case ChatEntryType.WorkerSticker:
                        res.lines.Add(new ResolvedLine
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

            return res;
        }

        // Resolves a Thread entry, following the player's recorded choice path. Returns true
        // when the walk ran off the end of conversation.thread without halting on an
        // unanswered/expired choice (i.e. every node visited, every choice answered) — "read to
        // the end". Callers that represent an actual player read (GetChatLines) use this to fire
        // OnConversationCompleted; the background seal-probe (EntryHasUnansweredChoice) discards
        // it deliberately — reaching the end of a walk the player never opened is not a read.
        private bool ResolveThreadEntry(RuntimeChatEntry entry, TabResolution res)
        {
            var conversation = entry.conversation;
            if (conversation == null) return false;

            if (!string.IsNullOrEmpty(entry.resolvedWorkerName))
            {
                EventContext.SetOverride("workerName",  entry.resolvedWorkerName);
                EventContext.SetOverride("workerTrait", entry.resolvedWorkerTrait ?? "");
            }

            int choiceCounter = 0;
            return WalkNodes(conversation.thread, entry, res, ref choiceCounter);
        }

        // Marks a conversation as fully read (Law 2 meaning event) and fires
        // OnConversationCompleted exactly once, the first time it happens. Called only from
        // read paths that represent the player actually viewing the tab (GetChatLines) — never
        // from the background seal-probe, which must not fire completion for unopened threads.
        private void MarkConversationCompletedIfNeeded(string conversationName)
        {
            if (string.IsNullOrEmpty(conversationName)) return;
            if (_completedConversations.Contains(conversationName)) return;
            _completedConversations.Add(conversationName);
            OnConversationCompleted?.Invoke(conversationName);
        }

        // DFS pre-order walk over a node list. choiceCounter is the encounter index of
        // each ChoicePayload across the whole entry — it keys into chosenOptionIndices,
        // so the same recorded path always reproduces the same transcript. Returns false
        // when the walk halts on an unanswered choice (so callers stop emitting).
        private bool WalkNodes(List<MessageNode> nodes, RuntimeChatEntry entry, TabResolution res, ref int choiceCounter)
        {
            if (nodes == null) return true;

            foreach (var node in nodes)
            {
                if (node == null || node.payload == null) continue;

                if (node.payload is ChoicePayload choice)
                {
                    // Malformed authored choice (no options) can never be answered —
                    // skip it transparently (no counter slot) so it can't soft-lock the tab.
                    if (choice.options == null || choice.options.Count == 0)
                    {
                        Debug.LogWarning($"[DialogueManager] Choice node with no options in '{entry.conversation?.name}'. Merging through.");
                        continue;
                    }

                    int idx = choiceCounter++;

                    if (idx < entry.chosenOptionIndices.Count)
                    {
                        // Already answered — replay the chosen branch.
                        int chosen = entry.chosenOptionIndices[idx];
                        if (chosen < 0 || chosen >= choice.options.Count)
                        {
                            Debug.LogWarning($"[DialogueManager] Recorded choice index {chosen} out of range for choice #{idx} in '{entry.conversation?.name}'. Merging through.");
                            continue; // empty-branch behaviour: fall through to parent's next node
                        }

                        var option    = choice.options[chosen];
                        var labelLine = ResolveChoiceLabel(option.label);
                        if (labelLine != null) res.lines.Add(labelLine);

                        // Walk the option's sub-thread; empty children merge straight through.
                        if (!WalkNodes(option.children, entry, res, ref choiceCounter))
                            return false;
                        // children exhausted → continue with the parent's next node (merge)
                    }
                    else if (entry.choicesExpired)
                    {
                        // Choice lapsed (a day passed with no reply). Freeze the thread
                        // at the prompt — no buttons — but DON'T halt the tab, so later
                        // entries / future days render normally.
                        return false;
                    }
                    else
                    {
                        // First unanswered choice in the tab → pause here.
                        res.pendingEntry  = entry;
                        res.pendingChoice = choice;
                        return false;
                    }
                }
                else
                {
                    var line = ResolveContentOrSticker(node, entry.resolvedWorkerName);
                    if (line != null) res.lines.Add(line);
                }
            }

            return true;
        }

        // ─── Shared Node Resolution ───────────────────────────────────────────

        // Resolves a Content/Sticker node into a display line (speaker identity +
        // body/sticker). Returns null for ChoicePayload (callers handle choices).
        // Shared by the linear fallback and the interactive walker (S2: one path).
        private ResolvedLine ResolveContentOrSticker(MessageNode node, string workerName)
        {
            if (node?.payload == null || node.payload is ChoicePayload) return null;

            bool   isPlayer = node.sender == DialogueSpeaker.Player;
            string displayName;
            Sprite portrait;

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

            if (node.payload is ContentPayload content)
            {
                return new ResolvedLine
                {
                    speakerID      = node.sender.ToString(),
                    displayName    = displayName,
                    portrait       = portrait,
                    body           = EventContext.Resolve(content.body),
                    expressionID   = node.expressionId,
                    isPlayerBubble = isPlayer
                };
            }

            var stickerPay = node.payload as StickerPayload;
            return new ResolvedLine
            {
                speakerID       = node.sender.ToString(),
                displayName     = displayName,
                portrait        = portrait,
                expressionID    = node.expressionId,
                isPlayerBubble  = isPlayer,
                isStickerBubble = true,
                stickerSprite   = stickerPay != null && stickerPay.sticker != null ? stickerPay.sticker.sprite : null
            };
        }

        // Resolves a chosen option's label into the player's reply bubble.
        private ResolvedLine ResolveChoiceLabel(MessagePayload label)
        {
            if (label is ContentPayload content)
            {
                return new ResolvedLine
                {
                    speakerID      = "player",
                    displayName    = "You",
                    isPlayerBubble = true,
                    body           = EventContext.Resolve(content.body)
                };
            }
            if (label is StickerPayload sticker)
            {
                return new ResolvedLine
                {
                    speakerID       = "player",
                    displayName     = "You",
                    isPlayerBubble  = true,
                    isStickerBubble = true,
                    stickerSprite   = sticker.sticker != null ? sticker.sticker.sprite : null
                };
            }
            return null;
        }

        // Resolves an option's label into button-display data.
        private ChoiceOptionView BuildOptionView(MessagePayload label)
        {
            if (label is StickerPayload sticker)
            {
                return new ChoiceOptionView
                {
                    isSticker     = true,
                    stickerSprite = sticker.sticker != null ? sticker.sticker.sprite : null
                };
            }

            var content = label as ContentPayload;
            return new ChoiceOptionView
            {
                isSticker = false,
                label     = content != null ? EventContext.Resolve(content.body) : ""
            };
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

        // Tab-preview teaser: the last *visible* text line (interactive walk honoured,
        // so a paused choice shows the last NPC prompt). Falls back to "[Sticker]" when
        // the only visible content is sticker bubbles.
        private string GetLastLineBody(string tabID)
        {
            var lines = ResolveTab(tabID).lines;
            if (lines.Count == 0) return "";

            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (!lines[i].isStickerBubble && !string.IsNullOrEmpty(lines[i].body))
                    return lines[i].body;
            }
            return "[Sticker]";
        }

        private void MarkUnread(string tabID)
        {
            _unreadTabs.Add(tabID);
            OnUnreadChanged?.Invoke(tabID);
        }
    }
}
