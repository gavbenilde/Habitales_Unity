using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Habitales.Dialogue;
using Habitales.Meta;

namespace Habitales.UI
{
    /// <summary>
    /// The inescapable Azi check-in panel — replaces the retired Season Report Lite
    /// (archived at ARCHIVE/_GAME/Results/SeasonReportLiteUI.cs). Two columns:
    /// LEFT a group-chat-style conversation played on a blank slate (tap to reveal each
    /// bubble, choice rows for player replies), RIGHT a scrollable stats column built from
    /// <see cref="SeasonReportData"/> plus the significant tile deltas. The [Continue]
    /// button only appears once the conversation reaches its terminal node — the player
    /// must read the whole thing to leave.
    ///
    /// UNLIKE the archived report-lite this IS a pause-hijack: Show() calls
    /// <see cref="RunManager.PauseForEvent"/> and only [Continue] releases it (same
    /// owns-its-own-pause pattern as DialoguePopupView). There is deliberately no dim
    /// tap-out, no close button, no back — inescapable by design.
    ///
    /// The conversation is EPHEMERAL: nodes are resolved through DialogueManager's normal
    /// pipeline (profiles, expressions, EventContext tokens — S2, one resolution path) but
    /// nothing is delivered to a chat tab, nothing is logged to history, and
    /// OnConversationCompleted is NOT fired — this panel is its own completion gate.
    /// Bodies may use {days}, {improved}, {decayed}: Show() sets them as EventContext
    /// overrides, cleared again on Hide().
    ///
    /// Unscaled-time safe: the sim is event-paused the whole time it's open, so nothing in
    /// here may depend on Time.timeScale (there are no tweens/coroutines).
    ///
    /// Implements IUISubsystem so it can join UIManager's subsystems list (U-hub pattern).
    ///
    /// WIRING (human):
    ///   1. Build the CheckInPanel prefab: full-screen blocker root → two-column layout.
    ///      LEFT: a ScrollRect thread view (same bubble prefabs as ChatAppUI), a full-column
    ///      invisible Button as the tap catcher, a choice row container, and the [Continue]
    ///      button at the bottom of the column. RIGHT: a ScrollRect with a VerticalLayoutGroup
    ///      content for the stat rows.
    ///   2. Wire every [SerializeField] below — all loud-fail required at Awake (Law 3;
    ///      prefab-to-be-built, so hard-fail is the correct signal, same as the archived
    ///      report-lite).
    ///   3. Add this component to UIManager's serialized `subsystems` list.
    ///   4. Reuse ChatAppUI's npc/player bubble prefabs and choice button prefab directly.
    /// </summary>
    public class CheckInPanelUI : MonoBehaviour, IUISubsystem
    {
        public static CheckInPanelUI Instance { get; private set; }

        [Header("Panel Root")]
        [SerializeField] private GameObject panelRoot; // toggled by Show/Hide

        [Header("Header")]
        [SerializeField] private TextMeshProUGUI headerText;
        [SerializeField] private string headerTemplate = "Day {0} of {1} — Check-in";

        [Header("Chat Column")]
        [SerializeField] private ScrollRect bubbleScrollRect;
        [SerializeField] private Transform bubbleContent;
        [SerializeField] private ChatBubbleUI npcBubblePrefab;
        [SerializeField] private ChatBubbleUI playerBubblePrefab;
        [Tooltip("Invisible full-column button — each press reveals the next bubble. Disabled while a choice is pending and once the thread ends.")]
        [SerializeField] private Button tapCatcherButton;

        [Header("Choice Row")]
        [SerializeField] private GameObject choiceRow;
        [SerializeField] private Transform choiceRowContent;
        [SerializeField] private ChoiceButtonUI choiceButtonPrefab;

        [Header("Continue (hidden until the thread ends)")]
        [SerializeField] private Button continueButton;

        [Header("Stats Column")]
        [SerializeField] private Transform statsContent; // VerticalLayoutGroup under its own ScrollRect
        [SerializeField] private CheckInStatRowUI statRowPrefab;
        [Tooltip("OPTIONAL: root GameObject of the whole stats column (its ScrollRect/panel). " +
                 "Conversation-only mode (the run-end conversation) hides it; unwired, that mode " +
                 "just shows an empty column (warned once). The normal check-in re-shows it.")]
        [SerializeField] private GameObject statsColumnRoot;

        [Header("Stat Labels")]
        [SerializeField] private string daysLeftLabel   = "Days left";
        [SerializeField] private string improvedLabel   = "Tiles improved";
        [SerializeField] private string decayedLabel    = "Tiles decayed";
        [SerializeField] private string thrivingLabel   = "Thriving tiles";
        [SerializeField] private string degradedLabel   = "Degraded tiles";
        [SerializeField] private string criticalLabel   = "Critical tiles";
        [SerializeField] private string trendLabel      = "Health trend";
        [SerializeField] private string gradeLabel      = "On track for";
        [SerializeField] private string topActionLabel  = "Most-used tool";

        [Header("Stat Colors")]
        [SerializeField] private Color improvedColor = new Color(0.26f, 0.48f, 0.13f, 1f);
        [SerializeField] private Color decayedColor  = new Color(0.63f, 0.17f, 0.17f, 1f);
        [SerializeField] private Color thrivingColor = new Color(0.26f, 0.48f, 0.13f, 1f);
        [SerializeField] private Color degradedColor = new Color(0.85f, 0.44f, 0.10f, 1f);
        [SerializeField] private Color criticalColor = new Color(0.63f, 0.17f, 0.17f, 1f);

        // ── Runtime state ─────────────────────────────────────────────────────

        // One DFS frame of the conversation walk (mirrors DialogueManager's tree walk:
        // choice children are pushed as a sub-frame; an exhausted frame merges back).
        private class Frame
        {
            public List<MessageNode> nodes;
            public int index;
        }

        private readonly Stack<Frame> _walk = new Stack<Frame>();
        private ChoicePayload _pendingChoice;
        private bool _isOpen;

        // Conversation-only mode (run-end conversation): invoked once after Hide() releases
        // the pause — RunManager chains the End Report off it. Null for a normal check-in.
        private System.Action _onContinue;
        private bool _warnedNoStatsColumnRoot;

        private readonly List<ChatBubbleUI>   _bubbles       = new List<ChatBubbleUI>();
        private readonly List<ChoiceButtonUI> _choiceButtons = new List<ChoiceButtonUI>();
        private readonly List<CheckInStatRowUI> _statRows    = new List<CheckInStatRowUI>();

        // ── IUISubsystem ──────────────────────────────────────────────────────
        public string SubsystemId => "checkInPanel";
        public bool   IsVisible   => panelRoot != null && panelRoot.activeSelf;
        public void   SetVisible(bool visible)
        {
            // Passive visibility only (interface contract) — never touches the pause gate.
            if (panelRoot != null) panelRoot.SetActive(visible);
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError($"{name}: A second CheckInPanelUI instance was created. Only one is allowed in the scene. Destroying this duplicate.", this);
                Destroy(gameObject);
                return;
            }
            Instance = this;

            ValidateRefs();
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void OnEnable()
        {
            if (tapCatcherButton != null) tapCatcherButton.onClick.AddListener(HandleTap);
            if (continueButton   != null) continueButton.onClick.AddListener(HandleContinue);
        }

        private void OnDisable()
        {
            if (tapCatcherButton != null) tapCatcherButton.onClick.RemoveListener(HandleTap);
            if (continueButton   != null) continueButton.onClick.RemoveListener(HandleContinue);
        }

        private void ValidateRefs()
        {
            bool ok = true;
            ok &= Require(panelRoot,          nameof(panelRoot));
            ok &= Require(headerText,         nameof(headerText));
            ok &= Require(bubbleScrollRect,   nameof(bubbleScrollRect));
            ok &= Require(bubbleContent,      nameof(bubbleContent));
            ok &= Require(npcBubblePrefab,    nameof(npcBubblePrefab));
            ok &= Require(playerBubblePrefab, nameof(playerBubblePrefab));
            ok &= Require(tapCatcherButton,   nameof(tapCatcherButton));
            ok &= Require(choiceRow,          nameof(choiceRow));
            ok &= Require(choiceRowContent,   nameof(choiceRowContent));
            ok &= Require(choiceButtonPrefab, nameof(choiceButtonPrefab));
            ok &= Require(continueButton,     nameof(continueButton));
            ok &= Require(statsContent,       nameof(statsContent));
            ok &= Require(statRowPrefab,      nameof(statRowPrefab));

            if (!ok) enabled = false;
        }

        private bool Require(Object field, string fieldName)
        {
            if (field != null) return true;
            Debug.LogError($"{name}: CheckInPanelUI.{fieldName} missing — wire it in the Inspector.", this);
            return false;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Opens the check-in: pauses the sim, binds the stats column, and starts the
        /// conversation playback with its first bubble already revealed. Caller
        /// (AziCheckInNotifier) picks the conversation variant and computes the deltas.
        /// </summary>
        public void Show(SeasonReportData data, int improvedCount, int decayedCount, ConversationSO conversation)
        {
            if (!enabled)
            {
                Debug.LogError($"{name}: Show() called but component is disabled (missing refs) — ignoring.", this);
                return;
            }
            if (data == null || conversation == null || DialogueManager.Instance == null)
            {
                Debug.LogWarning($"{name}: Show() called with data={(data != null)}, conversation={(conversation != null)}, DialogueManager={(DialogueManager.Instance != null)} — ignoring. The notifier should have fallen back to a side-bubble.", this);
                return;
            }

            if (_isOpen)
            {
                // Re-arm without double-pausing — shouldn't happen at a 60-day cadence,
                // but a rebuilt panel is better than a stuck pause counter.
                Debug.LogWarning($"{name}: Show() called while already open — rebuilding in place.", this);
            }
            else
            {
                RunManager.Instance?.PauseForEvent();
                _isOpen = true;
            }

            int daysLeft = Mathf.Max(0, data.runLengthDays - data.currentDay);

            // Token overrides — resolved inside DialogueManager's EventContext.Resolve pass,
            // so "{days} days left" works in any variant body or choice label. Cleared on Hide.
            EventContext.SetOverride("days",     daysLeft.ToString());
            EventContext.SetOverride("improved", improvedCount.ToString());
            EventContext.SetOverride("decayed",  decayedCount.ToString());

            headerText.text = string.Format(headerTemplate, data.currentDay, data.runLengthDays);

            _onContinue = null; // normal check-in — nothing chains off Continue
            if (statsColumnRoot != null) statsColumnRoot.SetActive(true); // undo conversation-only hide
            BuildStats(data, improvedCount, decayedCount, daysLeft);

            StartConversation(conversation);
        }

        /// <summary>
        /// Conversation-only mode (2026-07-08 run-end rework): plays a conversation with the
        /// stats column hidden — the run-end Azi/Bob wrap-up ("End Conversation") uses this,
        /// with the End Report chained off <paramref name="onContinue"/>. Same inescapable
        /// pause-hijack contract as the check-in; <paramref name="onContinue"/> fires exactly
        /// once, after [Continue] closes the panel and releases the pause. Returns false
        /// (without side effects) when the panel can't play, so the caller can fall back.
        /// </summary>
        public bool ShowConversationOnly(ConversationSO conversation, string header, System.Action onContinue)
        {
            if (!enabled)
            {
                Debug.LogError($"{name}: ShowConversationOnly() called but component is disabled (missing refs) — caller should fall back.", this);
                return false;
            }
            if (conversation == null || DialogueManager.Instance == null)
            {
                Debug.LogWarning($"{name}: ShowConversationOnly() called with conversation={(conversation != null)}, DialogueManager={(DialogueManager.Instance != null)} — caller should fall back.", this);
                return false;
            }

            if (_isOpen)
            {
                Debug.LogWarning($"{name}: ShowConversationOnly() called while already open — rebuilding in place.", this);
            }
            else
            {
                RunManager.Instance?.PauseForEvent();
                _isOpen = true;
            }

            _onContinue = onContinue;
            headerText.text = header ?? string.Empty;

            ClearStatRows();
            if (statsColumnRoot != null)
            {
                statsColumnRoot.SetActive(false);
            }
            else if (!_warnedNoStatsColumnRoot)
            {
                _warnedNoStatsColumnRoot = true;
                Debug.LogWarning($"{name}: statsColumnRoot is not wired — conversation-only mode shows an empty stats column. Wire the column root to hide it.", this);
            }

            StartConversation(conversation);
            return true;
        }

        // Shared tail of Show / ShowConversationOnly: reset the walk and reveal bubble one.
        private void StartConversation(ConversationSO conversation)
        {
            ClearBubbles();
            ClearChoiceRow();
            _walk.Clear();
            _pendingChoice = null;
            _walk.Push(new Frame { nodes = conversation.thread, index = 0 });

            continueButton.gameObject.SetActive(false);
            tapCatcherButton.interactable = true;
            panelRoot.SetActive(true);

            RevealNext(); // first bubble shows immediately — never open on an empty column
        }

        // ── Conversation playback ─────────────────────────────────────────────

        private void HandleTap()
        {
            if (_pendingChoice != null) return; // the choice row IS the input right now
            RevealNext();
        }

        /// <summary>
        /// Advances the DFS walk one visible step: spawns the next bubble, or halts on a
        /// player choice, or — when the walk is exhausted — swaps the tap catcher for the
        /// [Continue] button (the panel's one and only exit).
        /// </summary>
        private void RevealNext()
        {
            while (true)
            {
                while (_walk.Count > 0 && _walk.Peek().index >= _walk.Peek().nodes.Count)
                    _walk.Pop(); // frame exhausted — merge back to the parent

                if (_walk.Count == 0)
                {
                    FinishConversation();
                    return;
                }

                var frame = _walk.Peek();
                var node  = frame.nodes[frame.index++];
                if (node == null || node.payload == null) continue;

                if (node.payload is ChoicePayload choice)
                {
                    if (choice.options == null || choice.options.Count == 0)
                    {
                        Debug.LogWarning($"{name}: check-in conversation has a ChoicePayload with no options — skipping it.", this);
                        continue;
                    }
                    ShowChoices(choice);
                    return;
                }

                var line = DialogueManager.Instance.ResolveNodeLine(node);
                if (line == null) continue;

                SpawnBubble(line);

                // If that was the last visible node, don't make the player tap into
                // nothing — surface [Continue] right away, under the final bubble.
                if (!WalkHasMore()) FinishConversation();
                return;
            }
        }

        // Peeks the remaining walk (top frame down) for any renderable node, so the
        // Continue button can appear together with the final bubble.
        private bool WalkHasMore()
        {
            foreach (var frame in _walk)
                for (int i = frame.index; i < frame.nodes.Count; i++)
                    if (frame.nodes[i] != null && frame.nodes[i].payload != null)
                        return true;
            return false;
        }

        private void FinishConversation()
        {
            _pendingChoice = null;
            tapCatcherButton.interactable = false;
            choiceRow.SetActive(false);
            continueButton.gameObject.SetActive(true);
        }

        private void ShowChoices(ChoicePayload choice)
        {
            _pendingChoice = choice;
            ClearChoiceRow();

            for (int i = 0; i < choice.options.Count; i++)
            {
                var view = DialogueManager.Instance.BuildChoiceOptionView(choice.options[i].label);
                var btn  = Instantiate(choiceButtonPrefab, choiceRowContent);
                btn.Setup(view, i, HandleChoiceSelected);
                _choiceButtons.Add(btn);
            }

            choiceRow.SetActive(true);
        }

        private void HandleChoiceSelected(int optionIndex)
        {
            if (_pendingChoice == null) return;
            if (optionIndex < 0 || optionIndex >= _pendingChoice.options.Count) return;

            var option = _pendingChoice.options[optionIndex];
            _pendingChoice = null;
            ClearChoiceRow();
            choiceRow.SetActive(false);

            var reply = DialogueManager.Instance.ResolveChoiceReplyLine(option.label);
            if (reply != null) SpawnBubble(reply);

            if (option.children != null && option.children.Count > 0)
                _walk.Push(new Frame { nodes = option.children, index = 0 });

            // Auto-reveal the response to the player's pick — replying and then having to
            // tap to hear the answer reads as a stall.
            RevealNext();
        }

        private void SpawnBubble(ResolvedLine line)
        {
            var prefab = line.isPlayerBubble ? playerBubblePrefab : npcBubblePrefab;
            var bubble = Instantiate(prefab, bubbleContent);
            bubble.Setup(line);
            _bubbles.Add(bubble);
            ScrollToBottom();
        }

        private void ScrollToBottom()
        {
            Canvas.ForceUpdateCanvases();
            bubbleScrollRect.verticalNormalizedPosition = 0f;
        }

        private void ClearBubbles()
        {
            foreach (var bubble in _bubbles)
                Destroy(bubble.gameObject);
            _bubbles.Clear();
        }

        private void ClearChoiceRow()
        {
            foreach (var btn in _choiceButtons)
                Destroy(btn.gameObject);
            _choiceButtons.Clear();
        }

        // ── Stats column ──────────────────────────────────────────────────────

        private void ClearStatRows()
        {
            foreach (var row in _statRows)
                Destroy(row.gameObject);
            _statRows.Clear();
        }

        // Adding a stat = one AddRow call; the layout group + scroll view absorb any count.
        private void BuildStats(SeasonReportData data, int improved, int decayed, int daysLeft)
        {
            ClearStatRows();

            AddRow(daysLeftLabel, daysLeft.ToString());
            AddRow(improvedLabel, improved.ToString(), improvedColor);
            AddRow(decayedLabel,  decayed.ToString(),  decayedColor);
            AddRow(thrivingLabel, data.thrivingCount.ToString(), thrivingColor);
            AddRow(degradedLabel, data.degradedCount.ToString(), degradedColor);
            AddRow(criticalLabel, data.criticalCount.ToString(), criticalColor);
            AddRow(trendLabel,    $"{data.worldHealthTrend:+0.0;-0.0;0.0}/day");

            // Collapse reads absurd mid-run — same omission rule as the end screen's sibling.
            if (data.projectedGrade != SeasonGrade.Collapse)
                AddRow(gradeLabel, data.projectedGrade.ToString());

            if (!string.IsNullOrEmpty(data.topActionName))
                AddRow(topActionLabel, data.topActionName);
        }

        private void AddRow(string label, string value)
        {
            var row = Instantiate(statRowPrefab, statsContent);
            row.Set(label, value);
            _statRows.Add(row);
        }

        private void AddRow(string label, string value, Color valueColor)
        {
            var row = Instantiate(statRowPrefab, statsContent);
            row.Set(label, value, valueColor);
            _statRows.Add(row);
        }

        // ── Exit ──────────────────────────────────────────────────────────────

        private void HandleContinue()
        {
            Hide();
        }

        /// <summary>Closes the panel and releases the pause. Only reachable via [Continue].
        /// In conversation-only mode, the caller's onContinue then fires exactly once —
        /// AFTER the resume, so the chained UI (the End Report) opens over a released sim
        /// (harmless at game over: isGameOver keeps the sim halted regardless).</summary>
        private void Hide()
        {
            if (!_isOpen) return;
            _isOpen = false;

            panelRoot.SetActive(false);
            EventContext.ClearOverrides();
            RunManager.Instance?.ResumeFromEvent();

            var onContinue = _onContinue;
            _onContinue = null;
            onContinue?.Invoke();
        }
    }
}
