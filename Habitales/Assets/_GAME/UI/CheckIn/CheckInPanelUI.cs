using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Habitales.Dialogue;
using Habitales.Meta;

namespace Habitales.UI
{
    /// <summary>
    /// The inescapable Azi check-in panel (2026-07-15 rework: "Azi Messages You").
    /// Two columns: RIGHT a static generated report — four stat slots plus the windowed
    /// health graph — that lands complete in one frame the moment the panel opens; LEFT a
    /// messaging-app conversation that Azi INITIATES: a beat, a typing indicator, then
    /// bubbles arrive on their own, paced by text length. A tap hurries the current wait
    /// (one bubble per tap — never a dump); choice rows halt the walk until picked. The
    /// terminal bubble is followed by an authored "—End of Conversation—" marker, then
    /// [Continue] — the panel's one and only exit.
    ///
    /// This IS a pause-hijack: Show()/ShowConversationOnly() call
    /// <see cref="RunManager.PauseForEvent"/> and only [Continue] releases it. There is
    /// deliberately no dim tap-out, no close button, no back — inescapable by design.
    /// The auto-walk runs on UNSCALED time (the sim is event-paused throughout).
    ///
    /// The conversation is EPHEMERAL: nodes resolve through DialogueManager's normal
    /// pipeline (profiles, expressions, EventContext tokens — S2, one resolution path) but
    /// nothing is delivered to a chat tab, nothing is logged to history, and
    /// OnConversationCompleted is NOT fired — this panel is its own completion gate.
    /// Bodies may use {days}, {improved}, {decayed}: Show() sets them as EventContext
    /// overrides, cleared again on Hide().
    ///
    /// COLLISION RULE (end flow trumps — decision 12 of the 2026-07-15 rework): both entry
    /// points are safe to call while a session is already in progress. If the world
    /// collapses on a check-in day, RunManager's ShowConversationOnly() force-resets the
    /// midseason panel in place — coroutines stopped, bubbles/choices/marker cleared,
    /// tokens cleared — so the player gets the somber collapse conversation, never banter
    /// over a dead world. The pause counter is never double-incremented.
    ///
    /// Implements IUISubsystem so it can join UIManager's subsystems list (U-hub pattern).
    ///
    /// WIRING (human):
    ///   1. Put this component on an ALWAYS-ACTIVE GameObject; <c>panelRoot</c> is the
    ///      child it toggles (the auto-walk coroutine dies if this GameObject deactivates).
    ///   2. Build the CheckInPanel prefab: full-screen blocker root → two columns.
    ///      LEFT: a ScrollRect thread view (reuse ChatAppUI's npc/player bubble prefabs),
    ///      a full-column invisible Button as the tap catcher, a choice row container +
    ///      choice button prefab, a typing-indicator GameObject (parented into
    ///      <c>bubbleContent</c> so it rides the bottom of the thread), and the [Continue]
    ///      button. RIGHT (<c>statsColumnRoot</c>): four static TMP value slots
    ///      (days left · top action · improved · decayed — labels are prefab art, code
    ///      fills values only) and the graph: a <see cref="HealthSparklineUI"/> with its
    ///      two static boundary-line Images (solid black left, dashed black right) and the
    ///      two date TMP labels beneath them.
    ///   3. Author the "—End of Conversation—" marker prefab (any mix of TMP/Image) and
    ///      assign <c>endOfConversationMarkerPrefab</c> — it is instantiated into
    ///      <c>bubbleContent</c> and scrolls with the chat.
    ///   4. Wire every [SerializeField] below — loud-fail required at Awake (Law 3) except
    ///      <c>typingIndicator</c> and <c>statsColumnRoot</c> (optional-with-warning).
    ///   5. Add this component to UIManager's serialized `subsystems` list.
    ///   6. Tune the pacing trio (first-message beat, base + per-character bubble delay)
    ///      and the end beat.
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
        [Tooltip("Invisible full-column button — a tap skips the current typing wait and reveals the pending bubble immediately (one per tap). Idle while a choice is pending; disabled once [Continue] shows.")]
        [SerializeField] private Button tapCatcherButton;

        [Header("Choice Row")]
        [SerializeField] private GameObject choiceRow;
        [SerializeField] private Transform choiceRowContent;
        [SerializeField] private ChoiceButtonUI choiceButtonPrefab;

        [Header("Message Pacing (unscaled time)")]
        [Tooltip("Quiet beat after the stats land, before Azi starts typing the first message.")]
        [SerializeField] private float firstMessageDelaySeconds = 0.6f;
        [Tooltip("Minimum typing time before any bubble arrives.")]
        [SerializeField] private float baseBubbleDelaySeconds = 0.8f;
        [Tooltip("Extra typing time per character of the incoming message.")]
        [SerializeField] private float perCharacterDelaySeconds = 0.02f;
        [Tooltip("OPTIONAL: 'Azi is typing…' GameObject, shown during each inter-bubble wait. Parent it into bubbleContent so it rides the bottom of the thread. Unwired = pacing still works, the indicator just never shows (warned once).")]
        [SerializeField] private GameObject typingIndicator;

        [Header("End of Conversation")]
        [Tooltip("Authored '—End of Conversation—' marker (any mix of TMP/Image). Instantiated into bubbleContent after the terminal bubble; scrolls with the chat.")]
        [SerializeField] private GameObject endOfConversationMarkerPrefab;
        [Tooltip("Beat between the terminal bubble and the marker, and again between the marker and [Continue].")]
        [SerializeField] private float endBeatSeconds = 0.5f;

        [Header("Continue (hidden until the marker has shown)")]
        [SerializeField] private Button continueButton;

        [Header("Stats Column (4 static slots — labels are prefab art, code fills values)")]
        [Tooltip("OPTIONAL: root GameObject of the whole stats column. Conversation-only mode (the run-end conversation) hides it; unwired, that mode just shows a stale column (warned once). The normal check-in re-shows it.")]
        [SerializeField] private GameObject statsColumnRoot;
        [SerializeField] private TextMeshProUGUI daysLeftText;
        [SerializeField] private TextMeshProUGUI topActionText;
        [SerializeField] private TextMeshProUGUI improvedText;
        [SerializeField] private TextMeshProUGUI decayedText;

        [Header("Graph (windowed sparkline + its two boundary date labels)")]
        [SerializeField] private HealthSparklineUI graph;
        [Tooltip("Date under the LEFT boundary line — today − windowDays.")]
        [SerializeField] private TextMeshProUGUI graphLeftDateText;
        [Tooltip("Date under the RIGHT boundary line — today + windowDays.")]
        [SerializeField] private TextMeshProUGUI graphRightDateText;

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
        private Coroutine _walkRoutine;
        private bool _skipRequested;
        private GameObject _markerInstance;

        // Conversation-only mode (run-end conversation): invoked once after Hide() releases
        // the pause — RunManager chains the End Report off it. Null for a normal check-in.
        private System.Action _onContinue;
        private bool _warnedNoStatsColumnRoot;
        private bool _warnedNoTypingIndicator;
        private bool _erroredNoResourceManager;

        private readonly List<ChatBubbleUI>   _bubbles       = new List<ChatBubbleUI>();
        private readonly List<ChoiceButtonUI> _choiceButtons = new List<ChoiceButtonUI>();

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
            if (typingIndicator != null) typingIndicator.SetActive(false);
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
            ok &= Require(endOfConversationMarkerPrefab, nameof(endOfConversationMarkerPrefab));
            ok &= Require(daysLeftText,       nameof(daysLeftText));
            ok &= Require(topActionText,      nameof(topActionText));
            ok &= Require(improvedText,       nameof(improvedText));
            ok &= Require(decayedText,        nameof(decayedText));
            ok &= Require(graph,              nameof(graph));
            ok &= Require(graphLeftDateText,  nameof(graphLeftDateText));
            ok &= Require(graphRightDateText, nameof(graphRightDateText));

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
        /// Opens the midseason check-in: pauses the sim, lands the stats + windowed graph
        /// complete in one frame (Phase A — "data drawn-in, not UI"), then starts the
        /// Azi-initiated auto-walk. Caller (CheckInScheduler) picks the conversation
        /// variant and computes the deltas, slope, and window.
        /// Safe to call mid-session (collision rule — see class doc): resets in place
        /// without double-pausing.
        /// </summary>
        public void Show(SeasonReportData data, int improvedCount, int decayedCount,
                         float slopePerDay, int windowDays, ConversationSO conversation)
        {
            if (!enabled)
            {
                Debug.LogError($"{name}: Show() called but component is disabled (missing refs) — the check-in is skipped.", this);
                return;
            }
            if (data == null || conversation == null || DialogueManager.Instance == null)
            {
                Debug.LogError($"{name}: Show() called with data={(data != null)}, conversation={(conversation != null)}, DialogueManager={(DialogueManager.Instance != null)} — the check-in is skipped. The scheduler should have caught this.", this);
                return;
            }

            OpenSession();

            int daysLeft = Mathf.Max(0, data.runLengthDays - data.currentDay);

            // Token overrides — resolved inside DialogueManager's EventContext.Resolve pass,
            // so "{days} days left" works in any variant body or choice label. Cleared on Hide.
            EventContext.SetOverride("days",     daysLeft.ToString());
            EventContext.SetOverride("improved", improvedCount.ToString());
            EventContext.SetOverride("decayed",  decayedCount.ToString());

            headerText.text = string.Format(headerTemplate, data.currentDay, data.runLengthDays);

            _onContinue = null; // normal check-in — nothing chains off Continue
            if (statsColumnRoot != null) statsColumnRoot.SetActive(true); // undo conversation-only hide
            FillStats(data, improvedCount, decayedCount, daysLeft, slopePerDay, windowDays);

            StartConversation(conversation);
        }

        /// <summary>
        /// Conversation-only mode (run-end rework): plays a conversation with the stats
        /// column hidden — the run-end Azi/Bob wrap-up ("End Conversation") uses this, with
        /// the End Report chained off <paramref name="onContinue"/>. Same inescapable
        /// pause-hijack contract as the check-in; <paramref name="onContinue"/> fires exactly
        /// once, after [Continue] closes the panel and releases the pause. Returns false
        /// (without side effects) when the panel can't play, so the caller can loud-fail.
        /// Safe to call mid-session (collision rule — see class doc): a collapse on a
        /// check-in day stomps the midseason panel in place, without double-pausing.
        /// </summary>
        public bool ShowConversationOnly(ConversationSO conversation, string header, System.Action onContinue)
        {
            if (!enabled)
            {
                Debug.LogError($"{name}: ShowConversationOnly() called but component is disabled (missing refs) — caller must skip to the End Report.", this);
                return false;
            }
            if (conversation == null || DialogueManager.Instance == null)
            {
                Debug.LogError($"{name}: ShowConversationOnly() called with conversation={(conversation != null)}, DialogueManager={(DialogueManager.Instance != null)} — caller must skip to the End Report.", this);
                return false;
            }

            OpenSession();

            _onContinue = onContinue;
            headerText.text = header ?? string.Empty;

            if (statsColumnRoot != null)
            {
                statsColumnRoot.SetActive(false);
            }
            else if (!_warnedNoStatsColumnRoot)
            {
                _warnedNoStatsColumnRoot = true;
                Debug.LogWarning($"{name}: statsColumnRoot is not wired — conversation-only mode shows a stale stats column. Wire the column root to hide it.", this);
            }

            StartConversation(conversation);
            return true;
        }

        // Shared head of Show / ShowConversationOnly: take the pause exactly once and
        // reset the previous session's traces. The reset lives HERE — before the caller
        // stores _onContinue / tokens / header — never later in the open path, or it
        // would wipe the very state the caller just set (collision rule, decision 12).
        private void OpenSession()
        {
            if (_isOpen)
            {
                Debug.LogWarning($"{name}: opened while a session is in progress — force-resetting in place (end flow trumps).", this);
            }
            else
            {
                RunManager.Instance?.PauseForEvent();
                _isOpen = true;
            }
            ResetSession();
        }

        // Stops the auto-walk and clears every trace of the current session — bubbles,
        // choices, marker, typing indicator, tokens. Never touches the pause counter.
        private void ResetSession()
        {
            if (_walkRoutine != null)
            {
                StopCoroutine(_walkRoutine);
                _walkRoutine = null;
            }
            SetTyping(false);
            ClearBubbles();
            ClearChoiceRow();
            choiceRow.SetActive(false);
            if (_markerInstance != null)
            {
                Destroy(_markerInstance);
                _markerInstance = null;
            }
            _walk.Clear();
            _pendingChoice = null;
            _skipRequested = false;
            _onContinue = null;
            EventContext.ClearOverrides();
        }

        // Shared tail of Show / ShowConversationOnly: hand the thread to the auto-walk.
        // No reset here — OpenSession already did it, and resetting now would wipe the
        // _onContinue / token state the caller set in between. panelRoot activates BEFORE
        // the coroutine starts (a coroutine can't start on an inactive hierarchy if this
        // component sits under panelRoot).
        private void StartConversation(ConversationSO conversation)
        {
            _walk.Push(new Frame { nodes = conversation.thread, index = 0 });

            continueButton.gameObject.SetActive(false);
            tapCatcherButton.interactable = true;
            panelRoot.SetActive(true);

            _walkRoutine = StartCoroutine(AutoWalkRoutine());
        }

        // ── Phase A: stats ────────────────────────────────────────────────────

        // Fills the four static slots + the windowed graph + its two boundary dates —
        // all in one frame, before the first bubble exists (generated-report aesthetic).
        private void FillStats(SeasonReportData data, int improved, int decayed, int daysLeft,
                               float slopePerDay, int windowDays)
        {
            daysLeftText.text = daysLeft.ToString();
            topActionText.text = string.IsNullOrEmpty(data.topActionName) ? "—" : data.topActionName;
            improvedText.text = improved.ToString();
            decayedText.text  = decayed.ToString();

            graph.SetWindowed(data.healthHistory, windowDays, slopePerDay, data.currentDay, data.runLengthDays);

            var rm = ResourceManager.Instance;
            if (rm != null)
            {
                // The 360-day calendar wraps years correctly on its own (DayOfYearFor
                // normalizes negatives and overflow).
                graphLeftDateText.text  = GameCalendar.GetShortDate(rm.DayOfYearFor(data.currentDay - windowDays));
                graphRightDateText.text = GameCalendar.GetShortDate(rm.DayOfYearFor(data.currentDay + windowDays));
            }
            else
            {
                if (!_erroredNoResourceManager)
                {
                    _erroredNoResourceManager = true;
                    Debug.LogError($"{name}: ResourceManager.Instance is null — the graph's boundary dates cannot resolve.", this);
                }
                graphLeftDateText.text  = string.Empty;
                graphRightDateText.text = string.Empty;
            }
        }

        // ── Phases B–D: the auto-walk ─────────────────────────────────────────

        // Azi initiates and the thread delivers itself: beat → typing → bubble, paced by
        // text length, tap-skippable one bubble at a time; choices suspend the walk; the
        // terminal bubble is followed by the end marker, then [Continue]. Unscaled time
        // throughout — the sim is event-paused while the panel is open.
        private IEnumerator AutoWalkRoutine()
        {
            // Phase B: a quiet beat after the stats land — the player never summons Azi.
            yield return PacedWait(firstMessageDelaySeconds, showTyping: false);

            while (TryResolveNextStep(out ResolvedLine line, out ChoicePayload choice))
            {
                if (choice != null)
                {
                    // The choice row IS the input — suspend until the player picks.
                    // HandleChoiceSelected spawns their reply bubble and clears the halt.
                    SetTyping(false);
                    ShowChoices(choice);
                    while (_pendingChoice != null) yield return null;
                    continue;
                }

                // Typing beat scaled by the incoming message's length; a tap cuts it short.
                float wait = baseBubbleDelaySeconds
                             + perCharacterDelaySeconds * (line.body != null ? line.body.Length : 0);
                yield return PacedWait(wait, showTyping: true);
                SetTyping(false);
                SpawnBubble(line);
            }

            // Phase D: terminal bubble shown, nowhere left to walk → marker → Continue.
            SetTyping(false);
            yield return PacedWait(endBeatSeconds, showTyping: false);
            SpawnEndMarker();
            yield return PacedWait(endBeatSeconds, showTyping: false);

            _pendingChoice = null;
            tapCatcherButton.interactable = false;
            choiceRow.SetActive(false);
            continueButton.gameObject.SetActive(true);
            ScrollToBottom();
            _walkRoutine = null;
        }

        // One tap-skippable unscaled wait, optionally under the typing indicator.
        private IEnumerator PacedWait(float seconds, bool showTyping)
        {
            _skipRequested = false;
            if (showTyping) SetTyping(true);

            float elapsed = 0f;
            while (elapsed < seconds && !_skipRequested)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            _skipRequested = false;
        }

        /// <summary>
        /// Advances the DFS walk to the next renderable step: a resolved line OR a pending
        /// choice (exactly one of the two out-params is set). Returns false when the walk
        /// is exhausted — the current bubble was the conversation's terminal node.
        /// </summary>
        private bool TryResolveNextStep(out ResolvedLine line, out ChoicePayload choice)
        {
            line = null;
            choice = null;

            while (true)
            {
                while (_walk.Count > 0 && _walk.Peek().index >= _walk.Peek().nodes.Count)
                    _walk.Pop(); // frame exhausted — merge back to the parent

                if (_walk.Count == 0) return false;

                var frame = _walk.Peek();
                var node  = frame.nodes[frame.index++];
                if (node == null || node.payload == null) continue;

                if (node.payload is ChoicePayload pending)
                {
                    if (pending.options == null || pending.options.Count == 0)
                    {
                        Debug.LogWarning($"{name}: check-in conversation has a ChoicePayload with no options — skipping it.", this);
                        continue;
                    }
                    choice = pending;
                    return true;
                }

                var resolved = DialogueManager.Instance.ResolveNodeLine(node);
                if (resolved == null) continue;

                line = resolved;
                return true;
            }
        }

        private void HandleTap()
        {
            if (_pendingChoice != null) return; // the choice row IS the input right now
            _skipRequested = true;              // cut the current wait — one bubble per tap
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
            _pendingChoice = null; // releases the auto-walk's halt
            ClearChoiceRow();
            choiceRow.SetActive(false);

            // The player's own message lands instantly — no typing indicator for yourself.
            var reply = DialogueManager.Instance.ResolveChoiceReplyLine(option.label);
            if (reply != null) SpawnBubble(reply);

            if (option.children != null && option.children.Count > 0)
                _walk.Push(new Frame { nodes = option.children, index = 0 });
        }

        private void SpawnBubble(ResolvedLine line)
        {
            var prefab = line.isPlayerBubble ? playerBubblePrefab : npcBubblePrefab;
            var bubble = Instantiate(prefab, bubbleContent);
            bubble.Setup(line);
            _bubbles.Add(bubble);
            ScrollToBottom();
        }

        private void SpawnEndMarker()
        {
            _markerInstance = Instantiate(endOfConversationMarkerPrefab, bubbleContent);
            ScrollToBottom();
        }

        private void SetTyping(bool on)
        {
            if (typingIndicator == null)
            {
                if (on && !_warnedNoTypingIndicator)
                {
                    _warnedNoTypingIndicator = true;
                    Debug.LogWarning($"{name}: typingIndicator is not wired — pacing still runs, the indicator just never shows. Wire an 'Azi is typing…' GameObject to show it.", this);
                }
                return;
            }

            typingIndicator.SetActive(on);
            if (on)
            {
                // Ride the bottom of the thread (no-op if it isn't parented into bubbleContent).
                typingIndicator.transform.SetAsLastSibling();
                ScrollToBottom();
            }
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

            var onContinue = _onContinue;
            ResetSession(); // also clears tokens and _onContinue — hence the local copy

            panelRoot.SetActive(false);
            RunManager.Instance?.ResumeFromEvent();

            onContinue?.Invoke();
        }
    }
}
