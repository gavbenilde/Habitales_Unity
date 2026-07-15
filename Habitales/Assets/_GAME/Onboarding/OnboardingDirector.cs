using System;
using UnityEngine;
using Habitales.UI;
using Habitales.UI.Actions;
using Habitales.Dialogue;
using Habitales.Triggers;

namespace Habitales.Onboarding
{
    // =========================================================================
    //  OnboardingDirector — Work-Order B1
    //  The spine of the Alpha onboarding: an ordered 11-beat state machine with
    //  an input-idle (stall) detector that reveals explicit text after ~3 s of
    //  no input (Hodent implicit-first).
    //
    //  ARCHITECTURAL LAWS (arch §1):
    //    1. Getters, not setters — reads other systems via public read-only
    //       properties; never writes their state.
    //    2. Hooks fire on meaning — subscribes to meaning-events (OnDayResolved,
    //       OnRegionUnlocked, OnEntitySpawned, OnActionCompleted). Conditions that
    //       have no event are polled in Update (documented below).
    //    3. Loud failure for misconfig — every null [SerializeField] ref produces
    //       Debug.LogError and the component disables gracefully.
    // =========================================================================

    /// <summary>
    /// Drives the Alpha onboarding: ordered beat progression, per-beat Azi cue
    /// via PopupManager, stall detection, and the coach-mark contract
    /// that B2 / B3 / B4 subscribe to.
    ///
    /// <para><b>Scene wiring:</b> see the Inspector checklist at the bottom of this file.</para>
    /// </summary>
    [DefaultExecutionOrder(50)] // after all manager singletons
    public class OnboardingDirector : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // THE CONTRACT (copy-paste-ready for sibling agents)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Fires when the director enters a new beat (BEFORE the cue is shown).</summary>
        public event Action<OnboardingBeatId> OnBeatEntered;

        /// <summary>Fires when a beat's success condition is satisfied and we advance.</summary>
        public event Action<OnboardingBeatId> OnBeatCompleted;

        /// <summary>
        /// B2 subscribes here. The director calls this whenever a coach-mark should appear
        /// or be hidden. See <see cref="CoachMarkRequest.hide"/> for hide-request convention.
        /// </summary>
        public event Action<CoachMarkRequest> OnCoachMarkRequested;

        /// <summary>True while the onboarding sequence is running (not yet graduated).</summary>
        public bool IsActive { get; private set; }

        /// <summary>The beat currently in progress (None if inactive).</summary>
        public OnboardingBeatId CurrentBeat { get; private set; } = OnboardingBeatId.None;

        /// <summary>Singleton — one director per scene.</summary>
        public static OnboardingDirector Instance { get; private set; }

        // ─────────────────────────────────────────────────────────────────────
        // Inspector
        // ─────────────────────────────────────────────────────────────────────

        [Header("Content")]
        [Tooltip("Catalog id of the Priority-Zero PopupSO (beat 0), resolved via TriggerManager.Fire. " +
                 "The Director fires it here instead of waiting for OnboardingBootstrap — " +
                 "leave OnboardingBootstrap in the scene with the same id.")]
        [SerializeField] private string priorityZeroEventId = "priority_zero";

        [Tooltip("Azi portrait Sprite. Every Say() call passes this.")]
        [SerializeField] private Sprite aziPortrait;

        [Tooltip("Beat 2.0's forced group-chat kickoff (Azi & Bob, 2-3 choice points, under ~8 " +
                 "messages, channel = GroupChat). Delivered via DialogueManager.DeliverConversation " +
                 "on beat entry — badge/shake/ribbon fire for free off OnMessagesUpdated. Beat 2.0 " +
                 "completes when DialogueManager reports this conversation read to its end.")]
        [SerializeField] private ConversationSO groupChatKickoffConversation;

        [Tooltip("Screen-space RectTransform of the messaging app icon (the same object " +
                 "MessagingAppIconUI sits on / shakes). The FidgetArrow coach-mark for beat 2.0 " +
                 "points here. Assign explicitly — MessagingAppIconUI has no public accessor.")]
        [SerializeField] private RectTransform messagingIconCueTarget;

        [Header("Stall Detector")]
        [Tooltip("Seconds of input idle before the explicit stall-fallback text is shown.")]
        [SerializeField] private float stallSeconds = 3f;

        [Header("References (optional — auto-found if null)")]
        [Tooltip("The ActionBarUI in the scene. Auto-found if null; assign explicitly for determinism.")]
        [SerializeField] private ActionBarUI actionBarUI;

        [Tooltip("The ObjectiveBannerUI in the scene. Auto-found if null. Its PlayLandingReveal() " +
                 "is called at graduation (Beat_3_4) — the \"text lands in front of the player\" tween.")]
        [SerializeField] private ObjectiveBannerUI objectiveBannerUI;

        [Header("Skip / Debug")]
        [Tooltip("If true, skip Priority Zero (beat 0) — useful when the scene already fires it via OnboardingBootstrap.")]
        [SerializeField] private bool skipBeat0 = false;

        [Tooltip("Start from this beat index (0 = normal start). Debug only.")]
        [SerializeField] private int debugStartBeat = 0;

        // ─────────────────────────────────────────────────────────────────────
        // Beat definition
        // ─────────────────────────────────────────────────────────────────────

        private struct BeatDef
        {
            public OnboardingBeatId id;
            public string cueLine;           // shown immediately on enter
            public string stallFallback;     // shown after stallSeconds idle (null = no fallback)
            public CoachMarkKind coachMark;  // BlinkingTileMarker, etc. (None = no mark)
        }

        // Hardcoded per the doc's license. This is a lookup TABLE (id → cue/coachMark); advance
        // order is owned by ResolveNext() so the click/drag phases can loop 3× each.
        private static readonly BeatDef[] s_Beats = new BeatDef[]
        {
            new BeatDef
            {
                id           = OnboardingBeatId.Beat_0_PriorityZero,
                cueLine      = null,   // Priority Zero = a PopupSO full-screen headline (via TriggerManager), not a Say()
                stallFallback = null,
                coachMark    = CoachMarkKind.None,
            },

            // ── Click-teaching cycle (repeats 3×; cue spoken on the 1st cycle only) ──
            new BeatDef
            {
                id            = OnboardingBeatId.Beat_Click_Arm,
                cueLine       = OnboardingContent.Beat_1_1_PickAction,
                stallFallback = null,  // no explicit fallback — coach mark handles it
                coachMark     = CoachMarkKind.FidgetArrow,
            },
            new BeatDef
            {
                id            = OnboardingBeatId.Beat_Click_Place,
                cueLine       = OnboardingContent.Beat_1_2_PlaceOne,
                stallFallback = OnboardingContent.Beat_1_2_StallText,
                coachMark     = CoachMarkKind.GhostMouseClick,
            },
            new BeatDef
            {
                id            = OnboardingBeatId.Beat_Click_Confirm,
                cueLine       = OnboardingContent.Beat_1_3_CommitTime,
                stallFallback = null,
                coachMark     = CoachMarkKind.FidgetArrow,   // arrow → Confirm button
            },
            new BeatDef
            {
                id            = OnboardingBeatId.Beat_1_4_PassDay,
                cueLine       = OnboardingContent.Beat_1_4_PassDay,
                stallFallback = null,
                coachMark     = CoachMarkKind.None,          // no coach-mark; cue text is enough
            },

            // ── Forced group-chat kickoff (Azi & Bob) — slots before the drag cycle ──
            new BeatDef
            {
                id            = OnboardingBeatId.Beat_2_0_GroupChat,
                cueLine       = OnboardingContent.Beat_2_0_OpenChat,
                stallFallback = OnboardingContent.Beat_2_0_StallText,
                coachMark     = CoachMarkKind.FidgetArrow,   // arrow → messaging app icon
            },

            // ── Drag-teaching cycle (repeats 3×; cue spoken on the 1st cycle only) ──
            new BeatDef
            {
                id            = OnboardingBeatId.Beat_Drag_Arm,
                cueLine       = null,
                stallFallback = null,
                coachMark     = CoachMarkKind.FidgetArrow,
            },
            new BeatDef
            {
                id            = OnboardingBeatId.Beat_Drag_Select,
                cueLine       = OnboardingContent.Beat_2_1_DragMany,
                stallFallback = null,  // B4 (drag-inset ghost) owns the visual fallback
                coachMark     = CoachMarkKind.GhostMouseDrag,
            },
            new BeatDef
            {
                id            = OnboardingBeatId.Beat_Drag_Confirm,
                cueLine       = null,
                stallFallback = null,
                coachMark     = CoachMarkKind.FidgetArrow,   // arrow → Confirm button
            },

            new BeatDef
            {
                id            = OnboardingBeatId.Beat_2_2_TileInspector,
                cueLine       = null,   // discovered naturally — no explicit cue
                stallFallback = null,
                coachMark     = CoachMarkKind.CornerReminder,
            },
            new BeatDef
            {
                id            = OnboardingBeatId.Beat_3_1_FirstRibbon,
                cueLine       = OnboardingContent.Beat_3_1_FirstRibbon,
                stallFallback = null,
                coachMark     = CoachMarkKind.None,
            },
            new BeatDef
            {
                id            = OnboardingBeatId.Beat_3_2_LookAround,
                cueLine       = OnboardingContent.Beat_3_2_LookAround,
                stallFallback = null,
                coachMark     = CoachMarkKind.None,
            },
            new BeatDef
            {
                id            = OnboardingBeatId.Beat_3_3_NewTool,
                cueLine       = OnboardingContent.Beat_3_3_NewTool,
                stallFallback = null,
                coachMark     = CoachMarkKind.FidgetArrow,
            },
            new BeatDef
            {
                id            = OnboardingBeatId.Beat_3_4_Graduation,
                cueLine       = OnboardingContent.Beat_3_4_Graduation,
                stallFallback = null,
                coachMark     = CoachMarkKind.None,
            },
        };

        // ─────────────────────────────────────────────────────────────────────
        // Runtime state
        // ─────────────────────────────────────────────────────────────────────

        private float _idleTimer       = 0f;
        private bool  _stallFired      = false;
        private bool  _beat0Fired      = false;

        // How many times each teaching cycle repeats before advancing to the next phase.
        private const int ClickCyclesTarget = 3;
        private const int DragCyclesTarget  = 3;
        private int _clickCyclesDone = 0;   // incremented when a Click_Confirm completes
        private int _dragCyclesDone  = 0;   // incremented when a Drag_Confirm completes

        // Per-beat success gate booleans (polled or event-driven).
        private bool _actionSelectedThisBeat   = false; // event: ActionBarUI.OnActionArmed
        private bool _seedClickedThisBeat      = false; // event: TileSelector.OnTileSelected (click phase)
        private bool _confirmedThisBeat        = false; // event: ActionBarUI.OnActionConfirmed
        private bool _passDayDoneThisBeat      = false; // event: RunManager.OnDayResolved (beat 1.4)
        private bool _groupChatReadThisBeat    = false; // event: DialogueManager.OnConversationCompleted (beat 2.0)
        private bool _dragDoneThisBeat         = false; // polled: SelectedTileCount >= 3
        private bool _tileInspected            = false; // event: TileSelector.OnTileSelected (after drag phase)
        private bool _regionUnlocked           = false; // event: RunManager.OnRegionUnlocked
        private bool _panDetected              = false; // polled: Input.GetMouseButton(1)
        // Beat 3.3 (new tool) — no runtime unlock signal exists; auto-advance after first day post-3.1.
        // Beat 3.4 graduation fires automatically.

        // Day count when we entered a beat (for beats that advance on "next day").
        private int _dayAtBeatEnter = 0;

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start()
        {
            // Loud-fail all required refs (Law 3).
            bool ok = true;

            if (PopupManager.Instance == null)
            {
                Debug.LogError($"{name}: PopupManager.Instance is null — OnboardingDirector cannot show cues. " +
                               "Ensure a PopupManager is in the scene and initialized before this component.", this);
                ok = false;
            }

            if (string.IsNullOrWhiteSpace(priorityZeroEventId) && !skipBeat0)
            {
                Debug.LogError($"{name}: priorityZeroEventId is blank — Beat 0 (Priority Zero) will not fire. " +
                               "Set the Priority-Zero catalog id in the Inspector, or enable skipBeat0.", this);
                // Non-fatal: we advance past beat 0 automatically.
            }

            if (aziPortrait == null)
                Debug.LogWarning($"{name}: aziPortrait is not assigned — Azi cues will show without a portrait. Wire it in the Inspector.", this);

            if (groupChatKickoffConversation == null)
                Debug.LogError($"{name}: groupChatKickoffConversation is not assigned — beat 2.0 will deliver nothing " +
                               "and its completion gate can never be satisfied (the player will stall forever). " +
                               "Author the Azi & Bob kickoff ConversationSO and assign it in the Inspector.", this);

            if (messagingIconCueTarget == null)
                Debug.LogWarning($"{name}: messagingIconCueTarget is not assigned — beat 2.0's FidgetArrow will have " +
                                 "no target. Assign the messaging icon's RectTransform in the Inspector.", this);

            if (DialogueManager.Instance == null)
                Debug.LogError($"{name}: DialogueManager.Instance is null — beat 2.0 cannot deliver the kickoff " +
                               "conversation or detect when it's been read. Ensure a DialogueManager is in the scene.", this);

            // Auto-find ActionBarUI if not assigned.
            if (actionBarUI == null)
                actionBarUI = FindObjectOfType<ActionBarUI>();
            if (actionBarUI == null)
                Debug.LogWarning($"{name}: ActionBarUI not found — beat 1.1 (Pick Action) polling will not work. Assign it in the Inspector.", this);

            // Auto-find ObjectiveBannerUI if not assigned.
            if (objectiveBannerUI == null)
                objectiveBannerUI = FindObjectOfType<ObjectiveBannerUI>();
            if (objectiveBannerUI == null)
                Debug.LogWarning($"{name}: ObjectiveBannerUI not found — graduation's landing-reveal tween will not play. Assign it in the Inspector.", this);

            if (!ok) { enabled = false; return; }

            // Subscribe to meaning-events (Law 2).
            if (RunManager.Instance != null)
            {
                RunManager.Instance.OnRegionUnlocked += HandleRegionUnlocked;
                RunManager.Instance.OnDayResolved    += HandleDayResolved;
            }
            else
                Debug.LogError($"{name}: RunManager.Instance is null — region-unlock and day-resolved beats will not trigger. Ensure RunManager is in the scene.", this);

            if (DialogueManager.Instance != null)
                DialogueManager.Instance.OnConversationCompleted += HandleConversationCompleted;
            // else already loud-failed above.

            // Subscribe to ActionBarUI events (replaces polling of CurrentArmedAction).
            if (actionBarUI != null)
            {
                actionBarUI.OnActionArmed     += HandleActionArmed;
                actionBarUI.OnActionConfirmed += HandleActionConfirmed;
            }
            else
                Debug.LogWarning($"{name}: ActionBarUI not found — arm and confirm beats will not fire via events.", this);

            // TileSelector now has a static Instance (added in this session).
            _cachedSelector = TileSelector.Instance;
            if (_cachedSelector == null)
                _cachedSelector = FindObjectOfType<TileSelector>(); // fallback if Awake order differs
            if (_cachedSelector != null)
            {
                _cachedSelector.OnTileSelected            += HandleTileSelected;
                _cachedSelector.OnMultiSelectionConfirmed += HandleSelectionConfirmed;
            }
            else
                Debug.LogError($"{name}: No TileSelector found — tile-inspector detection will not work.", this);

            // Start from debugStartBeat (clamped into the lookup table).
            int startIdx = Mathf.Clamp(debugStartBeat, 0, s_Beats.Length - 1);

            IsActive = true;
            EnterBeat(s_Beats[startIdx].id);
        }

        void OnDestroy()
        {
            if (RunManager.Instance != null)
            {
                RunManager.Instance.OnRegionUnlocked -= HandleRegionUnlocked;
                RunManager.Instance.OnDayResolved    -= HandleDayResolved;
            }

            if (DialogueManager.Instance != null)
                DialogueManager.Instance.OnConversationCompleted -= HandleConversationCompleted;

            if (actionBarUI != null)
            {
                actionBarUI.OnActionArmed     -= HandleActionArmed;
                actionBarUI.OnActionConfirmed -= HandleActionConfirmed;
            }

            if (_cachedSelector != null)
            {
                _cachedSelector.OnTileSelected            -= HandleTileSelected;
                _cachedSelector.OnMultiSelectionConfirmed -= HandleSelectionConfirmed;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Update — input stall timer + polling-only success gates
        // ─────────────────────────────────────────────────────────────────────

        void Update()
        {
            if (!IsActive) return;

            // ── Stall detector ────────────────────────────────────────────────
            // Reset idle timer on ANY meaningful input.
            bool inputThisFrame =
                Input.anyKey ||
                Input.GetMouseButton(0) ||
                Input.GetMouseButton(1) ||
                Input.GetMouseButton(2) ||
                Input.mouseScrollDelta.sqrMagnitude > 0.01f ||
                Input.GetAxis("Mouse X") != 0f ||
                Input.GetAxis("Mouse Y") != 0f;

            if (inputThisFrame)
            {
                _idleTimer = 0f;
                _stallFired = false;
            }
            else
            {
                _idleTimer += Time.unscaledDeltaTime;
                if (!_stallFired && _idleTimer >= stallSeconds)
                {
                    _stallFired = true;
                    TryShowStallFallback();
                }
            }

            // ── Per-beat polling gates ─────────────────────────────────────────
            OnboardingBeatId beat = CurrentBeat;

            // Arm beats: the arm-cue target changes mid-beat (strip opens → card appears, or the
            // card rebuilds). Re-point the FidgetArrow when it switches.
            if (beat == OnboardingBeatId.Beat_Click_Arm ||
                beat == OnboardingBeatId.Beat_Drag_Arm  ||
                beat == OnboardingBeatId.Beat_3_3_NewTool)
                RefreshArmCueTarget();

            // Drag-select: show the drag ghost only while an action is armed.
            if (beat == OnboardingBeatId.Beat_Drag_Select)
                RefreshDragGhostByArmState();

            switch (beat)
            {
                case OnboardingBeatId.Beat_0_PriorityZero:
                    // Beat 0 advances once Priority Zero has fired (i.e. this frame after EnterBeat).
                    // EnterBeat fires it immediately; we advance on the next frame.
                    if (_beat0Fired)
                    {
                        _beat0Fired = false;
                        CompleteBeat();
                    }
                    break;

                case OnboardingBeatId.Beat_Click_Arm:
                case OnboardingBeatId.Beat_Drag_Arm:
                    // Event-driven: HandleActionArmed sets _actionSelectedThisBeat when Plant Trees is armed.
                    if (_actionSelectedThisBeat) CompleteBeat();
                    break;

                case OnboardingBeatId.Beat_1_4_PassDay:
                    // Event-driven: HandleDayResolved sets _passDayDoneThisBeat when a day resolves.
                    if (_passDayDoneThisBeat) CompleteBeat();
                    break;

                case OnboardingBeatId.Beat_2_0_GroupChat:
                    // Event-driven: HandleConversationCompleted sets _groupChatReadThisBeat when
                    // DialogueManager reports the kickoff conversation read to its END (not merely
                    // opened — see DialogueManager.OnConversationCompleted).
                    if (_groupChatReadThisBeat) CompleteBeat();
                    break;

                case OnboardingBeatId.Beat_Click_Place:
                    // Event-driven (HandleTileSelected sets _seedClickedThisBeat). Using the click
                    // event — not a GetSelectedTile() poll — so a stale selection from a prior cycle
                    // (arming auto-enters flood-fill) can't auto-complete this beat.
                    if (_seedClickedThisBeat) CompleteBeat();
                    break;

                case OnboardingBeatId.Beat_Drag_Select:
                    // POLLED: SelectedTileCount grew past the arm baseline (and ≥3) in FloodFill,
                    // so arming's auto-blob alone never satisfies the beat.
                    if (!_dragDoneThisBeat)
                    {
                        var ts = GetTileSelector();
                        int need = Mathf.Max(3, _dragBaselineCount + 1);
                        if (ts != null && ts.IsFloodFillMode && ts.SelectedTileCount >= need)
                        {
                            _dragDoneThisBeat = true;
                            CompleteBeat();
                        }
                    }
                    break;

                case OnboardingBeatId.Beat_Click_Confirm:
                case OnboardingBeatId.Beat_Drag_Confirm:
                    // Event-driven: HandleActionConfirmed (ActionBarUI.OnActionConfirmed) sets _confirmedThisBeat.
                    if (_confirmedThisBeat) CompleteBeat();
                    break;

                case OnboardingBeatId.Beat_2_2_TileInspector:
                    // Event-driven (HandleTileSelected fires after the drag phase completes).
                    if (_tileInspected) CompleteBeat();
                    break;

                case OnboardingBeatId.Beat_3_1_FirstRibbon:
                    // Event-driven (HandleRegionUnlocked).
                    if (_regionUnlocked) CompleteBeat();
                    break;

                case OnboardingBeatId.Beat_3_2_LookAround:
                    // POLLED: any right-mouse-button input = pan detected.
                    if (!_panDetected && Input.GetMouseButton(1))
                    {
                        _panDetected = true;
                        CompleteBeat();
                    }
                    break;

                case OnboardingBeatId.Beat_3_3_NewTool:
                    // No runtime unlock signal exists yet. Auto-advance on next day after entering
                    // (documented as a missing signal below). _dayAtBeatEnter was captured on enter.
                    if (ResourceManager.Instance != null &&
                        ResourceManager.Instance.TotalDays > _dayAtBeatEnter)
                        CompleteBeat();
                    break;

                case OnboardingBeatId.Beat_3_4_Graduation:
                    // Graduation: auto-advance one frame after the cue (retire scaffolding).
                    CompleteBeat();
                    break;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Beat management
        // ─────────────────────────────────────────────────────────────────────

        void EnterBeat(OnboardingBeatId id)
        {
            if (!TryGetBeatDef(id, out BeatDef def)) return;

            _idleTimer  = 0f;
            _stallFired = false;

            // Reset per-beat flags.
            _actionSelectedThisBeat = false;
            _seedClickedThisBeat    = false;
            _confirmedThisBeat      = false;
            _passDayDoneThisBeat    = false;
            _groupChatReadThisBeat  = false;
            _dragDoneThisBeat       = false;
            _tileInspected          = false;
            _regionUnlocked         = false;
            _panDetected            = false;
            _dragGhostShown         = false;
            _dragBaselineCount      = 0;

            CurrentBeat = def.id;

            // Capture day for time-based gates.
            _dayAtBeatEnter = ResourceManager.Instance != null ? ResourceManager.Instance.TotalDays : 0;

            OnBeatEntered?.Invoke(def.id);

            // Fire the coach-mark request (with a per-beat target — see BuildCoachMarkRequest).
            // Beat_Drag_Select is excluded from the normal path because its GhostMouseDrag is
            // arm-gated (see RefreshDragGhostByArmState). However, its CornerReminder IS sent
            // immediately on entry via the special case below.
            if (def.coachMark != CoachMarkKind.None && def.id != OnboardingBeatId.Beat_Drag_Select)
                OnCoachMarkRequested?.Invoke(BuildCoachMarkRequest(def));

            // Beat_Drag_Select: fire the CornerReminder immediately on enter even though the
            // GhostMouseDrag waits for arm state. The CornerReminder has no arm dependency.
            if (def.id == OnboardingBeatId.Beat_Drag_Select)
            {
                OnCoachMarkRequested?.Invoke(new CoachMarkRequest
                {
                    kind      = CoachMarkKind.CornerReminder,
                    hide      = false,
                    labelText = OnboardingContent.Reminder_SelectMultiple,
                });
            }

            // Show the cue line (or fire Priority Zero for beat 0). Repeating teaching beats only
            // speak on their first cycle (ShouldSpeakThisCycle) so Azi doesn't repeat the same line.
            if (def.id == OnboardingBeatId.Beat_0_PriorityZero)
            {
                if (!skipBeat0)
                    FirePriorityZero();
                else
                    _beat0Fired = true; // flag so Update advances next frame
            }
            else if (!string.IsNullOrEmpty(def.cueLine) && ShouldSpeakThisCycle(def.id))
            {
                ShowAziLine(def.cueLine);
            }
            // Beat 2.2 has no cue — it's discovered naturally.

            if (def.id == OnboardingBeatId.Beat_2_0_GroupChat)
                DeliverGroupChatKickoff();

            // Graduation — the objective banner's "text lands in front of the player" reveal
            // (arch ENDGAME_BUILD_PLAN §6.3). Fire-and-forget: PlayLandingReveal is cancel-safe
            // and self-contained, the Director doesn't track its lifetime.
            if (def.id == OnboardingBeatId.Beat_3_4_Graduation)
            {
                if (objectiveBannerUI != null)
                    objectiveBannerUI.PlayLandingReveal();
                else
                    Debug.LogWarning($"{name}: objectiveBannerUI is not wired — skipping the graduation landing-reveal tween.", this);
            }
        }

        // Delivers the forced group-chat kickoff the same way any other message arrives —
        // through DialogueManager, so MessagingAppIconUI's badge/shake and RibbonUI's preview
        // fire for free off OnMessagesUpdated (they already subscribe; no onboarding-specific FX
        // needed here). Beat 2.0's completion gate (Update's Beat_2_0_GroupChat case) is driven
        // separately by DialogueManager.OnConversationCompleted / HasCompletedConversation.
        void DeliverGroupChatKickoff()
        {
            if (groupChatKickoffConversation == null)
            {
                Debug.LogError($"{name}: groupChatKickoffConversation is not assigned — beat 2.0 cannot deliver " +
                               "the kickoff conversation. Assign the Azi & Bob ConversationSO in the Inspector.", this);
                return;
            }

            if (DialogueManager.Instance == null)
            {
                Debug.LogError($"{name}: DialogueManager.Instance is null — cannot deliver the beat 2.0 kickoff conversation.", this);
                return;
            }

            // Already read in a prior session/graduation-skip edge case (debugStartBeat, etc.) —
            // don't re-deliver a duplicate copy into the chat; just let Update's already-satisfied
            // check complete the beat next frame.
            if (DialogueManager.Instance.HasCompletedConversation(groupChatKickoffConversation.name))
            {
                _groupChatReadThisBeat = true;
                return;
            }

            DialogueManager.Instance.DeliverConversation(groupChatKickoffConversation);
        }

        void CompleteBeat()
        {
            if (!IsActive) return;

            OnboardingBeatId completedId = CurrentBeat;

            // Hide any coach mark for this beat.
            if (TryGetBeatDef(completedId, out BeatDef def) && def.coachMark != CoachMarkKind.None)
            {
                OnCoachMarkRequested?.Invoke(new CoachMarkRequest
                {
                    kind = def.coachMark,
                    hide = true,
                });
            }

            OnBeatCompleted?.Invoke(completedId);

            OnboardingBeatId next = ResolveNext(completedId);
            if (next == OnboardingBeatId.None)
                Graduate();   // all beats done
            else
                EnterBeat(next);
        }

        // Advance order — owns the 3× click/drag teaching loops. Returns None to graduate.
        OnboardingBeatId ResolveNext(OnboardingBeatId completed)
        {
            switch (completed)
            {
                case OnboardingBeatId.Beat_0_PriorityZero:    return OnboardingBeatId.Beat_Click_Arm;

                case OnboardingBeatId.Beat_Click_Arm:         return OnboardingBeatId.Beat_Click_Place;
                case OnboardingBeatId.Beat_Click_Place:       return OnboardingBeatId.Beat_Click_Confirm;
                case OnboardingBeatId.Beat_Click_Confirm:
                    _clickCyclesDone++;
                    // After the FIRST click confirm: show the "pass a day" nudge.
                    // After subsequent confirms: loop straight back to arm.
                    if (_clickCyclesDone == 1)
                        return OnboardingBeatId.Beat_1_4_PassDay;
                    return _clickCyclesDone < ClickCyclesTarget
                        ? OnboardingBeatId.Beat_Click_Arm
                        : OnboardingBeatId.Beat_2_0_GroupChat;

                case OnboardingBeatId.Beat_1_4_PassDay:
                    return _clickCyclesDone < ClickCyclesTarget
                        ? OnboardingBeatId.Beat_Click_Arm
                        : OnboardingBeatId.Beat_2_0_GroupChat;

                // Forced group-chat kickoff — slots after the click-teaching cycle finishes,
                // before the drag-teaching cycle starts (arch ENDGAME_BUILD_PLAN §6.2).
                case OnboardingBeatId.Beat_2_0_GroupChat:     return OnboardingBeatId.Beat_Drag_Arm;

                case OnboardingBeatId.Beat_Drag_Arm:          return OnboardingBeatId.Beat_Drag_Select;
                case OnboardingBeatId.Beat_Drag_Select:       return OnboardingBeatId.Beat_Drag_Confirm;
                case OnboardingBeatId.Beat_Drag_Confirm:
                    _dragCyclesDone++;
                    return _dragCyclesDone < DragCyclesTarget
                        ? OnboardingBeatId.Beat_Drag_Arm
                        : OnboardingBeatId.Beat_2_2_TileInspector;

                case OnboardingBeatId.Beat_2_2_TileInspector: return OnboardingBeatId.Beat_3_1_FirstRibbon;
                case OnboardingBeatId.Beat_3_1_FirstRibbon:   return OnboardingBeatId.Beat_3_2_LookAround;
                case OnboardingBeatId.Beat_3_2_LookAround:    return OnboardingBeatId.Beat_3_3_NewTool;
                case OnboardingBeatId.Beat_3_3_NewTool:       return OnboardingBeatId.Beat_3_4_Graduation;
                case OnboardingBeatId.Beat_3_4_Graduation:    return OnboardingBeatId.None;

                default:                                      return OnboardingBeatId.None;
            }
        }

        // Lookup the BeatDef table by id.
        bool TryGetBeatDef(OnboardingBeatId id, out BeatDef def)
        {
            for (int i = 0; i < s_Beats.Length; i++)
            {
                if (s_Beats[i].id == id) { def = s_Beats[i]; return true; }
            }
            def = default;
            return false;
        }

        // The Azi cue for a repeating teaching beat is spoken on the FIRST cycle only — coach marks
        // still play every cycle, but we don't repeat the same spoken line three times.
        bool ShouldSpeakThisCycle(OnboardingBeatId id)
        {
            switch (id)
            {
                case OnboardingBeatId.Beat_Click_Arm:
                case OnboardingBeatId.Beat_Click_Place:
                case OnboardingBeatId.Beat_Click_Confirm:
                    return _clickCyclesDone == 0;
                case OnboardingBeatId.Beat_Drag_Arm:
                case OnboardingBeatId.Beat_Drag_Select:
                case OnboardingBeatId.Beat_Drag_Confirm:
                    return _dragCyclesDone == 0;
                default:
                    return true;
            }
        }

        void Graduate()
        {
            IsActive    = false;
            CurrentBeat = OnboardingBeatId.None;

            Debug.Log($"[OnboardingDirector] Graduation — scaffolding retired.");

            // Fire hide-all coach marks.
            foreach (CoachMarkKind kind in Enum.GetValues(typeof(CoachMarkKind)))
            {
                if (kind == CoachMarkKind.None) continue;
                OnCoachMarkRequested?.Invoke(new CoachMarkRequest { kind = kind, hide = true });
            }

            // Disable self — no more Update polling.
            enabled = false;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Stall fallback
        // ─────────────────────────────────────────────────────────────────────

        void TryShowStallFallback()
        {
            if (!TryGetBeatDef(CurrentBeat, out BeatDef def)) return;
            if (string.IsNullOrEmpty(def.stallFallback)) return;
            if (!ShouldSpeakThisCycle(def.id)) return;   // don't repeat the fallback every cycle

            // Only show the fallback if the beat is still unsatisfied (checked by being still active).
            ShowAziLine(def.stallFallback);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Narrative helpers
        // ─────────────────────────────────────────────────────────────────────

        void ShowAziLine(string line)
        {
            var popups = PopupManager.Instance;
            if (popups == null)
            {
                Debug.LogError($"{name}: PopupManager.Instance is null — cannot show Azi line.", this);
                return;
            }
            popups.Say(line, aziPortrait, "Azi", PopupStyle.Character);
        }

        void FirePriorityZero()
        {
            if (string.IsNullOrWhiteSpace(priorityZeroEventId))
            {
                Debug.LogError($"{name}: priorityZeroEventId is blank — Priority Zero will not fire. Set the catalog id in the Inspector.", this);
                _beat0Fired = true; // still advance
                return;
            }

            if (TriggerManager.Instance == null)
            {
                Debug.LogError($"{name}: TriggerManager.Instance is null — cannot fire Priority Zero.", this);
                _beat0Fired = true;
                return;
            }

            TriggerManager.Instance.Fire(priorityZeroEventId);
            _beat0Fired = true;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Meaning-event handlers (Law 2)
        // ─────────────────────────────────────────────────────────────────────

        void HandleActionArmed(PlayerAction action)
        {
            // Fired by ActionBarUI.OnActionArmed. Satisfies the arm beats when PlantTreesAction is selected.
            if ((CurrentBeat == OnboardingBeatId.Beat_Click_Arm ||
                 CurrentBeat == OnboardingBeatId.Beat_Drag_Arm) &&
                action is PlantTreesAction)
                _actionSelectedThisBeat = true;
        }

        void HandleActionConfirmed()
        {
            // Fired by ActionBarUI.OnActionConfirmed (Confirm pressed and action executing).
            if (CurrentBeat == OnboardingBeatId.Beat_Click_Confirm ||
                CurrentBeat == OnboardingBeatId.Beat_Drag_Confirm)
                _confirmedThisBeat = true;
        }

        void HandleSelectionConfirmed(System.Collections.Generic.List<Tile> tiles)
        {
            // Kept for symmetry — ActionBarUI.OnActionConfirmed is now the primary signal.
            // This handler fires from TileSelector.OnMultiSelectionConfirmed and covers the
            // edge case where another code path calls ConfirmSelection directly.
            if (CurrentBeat == OnboardingBeatId.Beat_Click_Confirm ||
                CurrentBeat == OnboardingBeatId.Beat_Drag_Confirm)
                _confirmedThisBeat = true;
        }

        void HandleDayResolved(int day)
        {
            // Fired by RunManager.OnDayResolved after each day fully settles.
            if (CurrentBeat == OnboardingBeatId.Beat_1_4_PassDay)
                _passDayDoneThisBeat = true;
        }

        void HandleRegionUnlocked()
        {
            if (CurrentBeat == OnboardingBeatId.Beat_3_1_FirstRibbon)
                _regionUnlocked = true;
        }

        void HandleConversationCompleted(string conversationName)
        {
            // Fired by DialogueManager.OnConversationCompleted when a thread is walked to its
            // terminal node during an actual player read — not on delivery, not on tab-open.
            if (CurrentBeat != OnboardingBeatId.Beat_2_0_GroupChat) return;
            if (groupChatKickoffConversation == null) return;
            if (conversationName != groupChatKickoffConversation.name) return;

            _groupChatReadThisBeat = true;
        }

        void HandleTileSelected(Tile tile, Vector3 worldPos)
        {
            if (CurrentBeat == OnboardingBeatId.Beat_Click_Place)
                _seedClickedThisBeat = true;

            if (CurrentBeat == OnboardingBeatId.Beat_2_2_TileInspector)
                _tileInspected = true;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Coach-mark target resolution
        // ─────────────────────────────────────────────────────────────────────
        //
        // The widgets are dumb — they point at whatever target we hand them. We resolve
        // targets dynamically here so nothing has to be wired per-tile in the Inspector:
        //   • Tile beats (click / drag / marker) aim at a RANDOM unoccupied tile in the
        //     current board (entity == null). The ghost mouse flies from the live cursor
        //     to that tile, so any first-tileset layout is handled tastefully.
        //   • UI beats (pick-action) aim at the Plant Trees arm-cue (the card if the strip
        //     is open, else the Intervene tab) as a screen-space target.

        // Reused scratch list so target resolution allocates nothing per beat.
        private readonly System.Collections.Generic.List<Tile> _openTileScratch =
            new System.Collections.Generic.List<Tile>();

        CoachMarkRequest BuildCoachMarkRequest(BeatDef def)
        {
            var req = new CoachMarkRequest { kind = def.coachMark, hide = false };

            switch (def.id)
            {
                case OnboardingBeatId.Beat_Click_Arm:
                case OnboardingBeatId.Beat_Drag_Arm:
                case OnboardingBeatId.Beat_3_3_NewTool:
                    // FidgetArrow → the Plant Trees arm-cue (UI, screen-space).
                    req.trackTarget       = ResolveArmCueTarget();
                    req.screenSpaceTarget = true;
                    _currentCueTarget     = req.trackTarget;   // baseline for mid-beat refresh
                    break;

                case OnboardingBeatId.Beat_Click_Confirm:
                case OnboardingBeatId.Beat_Drag_Confirm:
                    // FidgetArrow → the Confirm button (UI, screen-space). The button is live once a
                    // selection exists, which it is by the time we reach the confirm beat.
                    req.trackTarget       = actionBarUI != null ? actionBarUI.GetConfirmButtonRect() : null;
                    req.screenSpaceTarget = true;
                    _currentCueTarget     = req.trackTarget;
                    break;

                case OnboardingBeatId.Beat_Click_Place:   // GhostMouseClick + CornerReminder (select / reselect)
                    if (TryGetRandomOpenTileWorld(out Vector3 tileWorld))
                        req.worldTarget = tileWorld;
                    // Also fire a CornerReminder to persist the "Select / Reselect" label.
                    OnCoachMarkRequested?.Invoke(new CoachMarkRequest
                    {
                        kind      = CoachMarkKind.CornerReminder,
                        hide      = false,
                        labelText = OnboardingContent.Reminder_SelectReselect,
                    });
                    break;

                // Beat_Drag_Select is excluded from the EnterBeat coach-mark path (arm-gated via
                // RefreshDragGhostByArmState). Its CornerReminder is sent directly in EnterBeat above.
                // This case is never reached from BuildCoachMarkRequest but left for documentation.

                case OnboardingBeatId.Beat_2_2_TileInspector: // CornerReminder
                    req.labelText = "Select a tile to inspect it";
                    break;

                case OnboardingBeatId.Beat_2_0_GroupChat:
                    // FidgetArrow → the messaging app icon (UI, screen-space). No auto-resolve
                    // path exists (MessagingAppIconUI has no public rect accessor) — this is a
                    // dedicated serialized ref the human wires directly.
                    req.trackTarget       = messagingIconCueTarget;
                    req.screenSpaceTarget = true;
                    break;
            }

            return req;
        }

        /// <summary>Picks a random unoccupied tile (entity == null) and returns its world position.</summary>
        bool TryGetRandomOpenTileWorld(out Vector3 world)
        {
            world = Vector3.zero;

            TileManager tm = TileManager.Instance;
            if (tm == null) return false;

            var all = tm.GetAllTiles();
            if (all == null || all.Count == 0) return false;

            _openTileScratch.Clear();
            foreach (Tile t in all)
                if (t != null && t.entity == null)
                    _openTileScratch.Add(t);

            if (_openTileScratch.Count == 0) return false;

            Tile pick = _openTileScratch[UnityEngine.Random.Range(0, _openTileScratch.Count)];
            world = tm.GridToWorldPosition(pick.gridPosition);
            return true;
        }

        /// <summary>The UI rect the FidgetArrow should point at to guide arming Plant Trees.</summary>
        Transform ResolveArmCueTarget()
        {
            if (actionBarUI == null) return null;
            return actionBarUI.GetArmCueRect(FindPlantTreesAction());
        }

        // Last UI cue target sent to the arrow; re-issues the request only when it changes
        // (e.g. tab → card when the strip opens), so the arrow re-points without restarting
        // every frame.
        private Transform _currentCueTarget;

        void RefreshArmCueTarget()
        {
            Transform t = ResolveArmCueTarget();
            if (t == _currentCueTarget) return;
            _currentCueTarget = t;

            OnCoachMarkRequested?.Invoke(new CoachMarkRequest
            {
                kind              = CoachMarkKind.FidgetArrow,
                hide              = false,
                trackTarget       = t,
                screenSpaceTarget = true,
            });
        }

        // Beat 2.1: the drag ghost demo shows while an action is armed and hides when disarmed.
        private bool _dragGhostShown;

        // Selection size captured when the drag ghost is shown; the beat needs growth past this.
        private int  _dragBaselineCount;

        void RefreshDragGhostByArmState()
        {
            bool armed = actionBarUI != null && actionBarUI.CurrentArmedAction != null;
            if (armed == _dragGhostShown) return;
            _dragGhostShown = armed;

            if (armed)
            {
                // Capture the initial blob size so the beat completes only after the player
                // DRAGS to grow it — not the instant arming creates the minimum blob (which
                // can already be ≥3 and would otherwise auto-complete and hide the ghost).
                TileSelector ts = GetTileSelector();
                _dragBaselineCount = ts != null ? ts.SelectedTileCount : 0;

                var req = new CoachMarkRequest { kind = CoachMarkKind.GhostMouseDrag, hide = false };
                if (TryGetRandomOpenTileWorld(out Vector3 world))
                    req.worldTarget = world;
                OnCoachMarkRequested?.Invoke(req);
            }
            else
            {
                OnCoachMarkRequested?.Invoke(new CoachMarkRequest
                {
                    kind = CoachMarkKind.GhostMouseDrag,
                    hide = true,
                });
            }
        }

        PlayerAction FindPlantTreesAction()
        {
            ActionManager am = ActionManager.Instance;
            if (am == null) return null;
            foreach (PlayerAction a in am.GetAvailableActions())
                if (a is PlantTreesAction) return a;
            return null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers — avoid FindObjectOfType spam
        // ─────────────────────────────────────────────────────────────────────

        private TileSelector _cachedSelector;
        TileSelector GetTileSelector()
        {
            if (_cachedSelector == null)
                _cachedSelector = TileSelector.Instance ?? FindObjectOfType<TileSelector>();
            return _cachedSelector;
        }
    }
}

// =============================================================================
// SIGNAL STATUS (updated — all four gaps from the original list addressed):
//
// 1. OnActionArmed ✓ WIRED
//    ActionBarUI.OnActionArmed fires inside ArmAction() the moment an action is selected.
//    Director subscribes in Start(), unsubscribes in OnDestroy(). No more polling.
//
// 2. OnActionConfirmed ✓ WIRED
//    ActionBarUI.OnActionConfirmed fires inside HandleConfirmed() before ExecuteAction().
//    Director subscribes in Start(), unsubscribes in OnDestroy().
//    TileSelector.OnMultiSelectionConfirmed is kept as a secondary/fallback handler.
//
// 3. OnNewToolUnlocked — INTENTIONALLY LEFT AS-IS (no clean runtime source)
//    Beat 3.3 still auto-advances after the first day-resolve post-entry (ResourceManager.TotalDays).
//    There is no ActionManager.OnActionUnlocked or similar signal in the codebase.
//    If a real unlock system is added later, subscribe here and remove the day-count poll.
//
// 4. TileSelector.Instance ✓ WIRED
//    TileSelector.Instance is now a static property set in Awake (with Law-3 duplicate warning).
//    Director uses TileSelector.Instance in Start() and GetTileSelector(). FindObjectOfType
//    is retained only as a fallback in case of execution-order edge cases.
//
// =============================================================================
