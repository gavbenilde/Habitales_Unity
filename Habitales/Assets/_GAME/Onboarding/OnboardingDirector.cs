using System;
using System.Collections.Generic;
using UnityEngine;
using Habitales.UI;
using Habitales.UI.Actions;
using Habitales.Dialogue;
using UTILITIES.Camera;

namespace Habitales.Onboarding
{
    // =========================================================================
    //  OnboardingDirector — 18-phase rebuild (ONBOARDING_HANDOFF §3, 2026-07-21)
    //
    //  Drives aki's 18-slide exposition deck (ONBOARDING_TUTORIAL_PLAN.md) as a
    //  linear phase runner. The legacy 11-beat model (s_Beats, click/drag loops,
    //  group-chat kickoff, Priority Zero) is retired; the PUBLIC SURFACE is kept
    //  intact so the two onboarding-juice systems keep COMPILING. Both are
    //  DORMANT for now (parked pending a design decision — ONBOARDING_HANDOFF §3);
    //  the main onboarding is built first. If revived, they re-point to:
    //    • DragGhostInset       → Phase_06_SelectTiles  (drag demo)
    //    • Beat1_3JuiceDirector → Phase_07_Confirm      (delta-tip / score juice)
    //
    //  CONTENT MODEL (decided 2026-07-21): each phase's words come from an
    //  authored PopupSO wired in the `phases` list below. The director resolves
    //  the SO's lines and presents them via PopupManager.PlayLines with a fixed
    //  per-phase PRESET (Dialog / Character / Text). PlayLines routes through
    //  UIManager.ShowPopup, so intrusive Dialog phases pause the sim on the same
    //  path the Pausing Bug Fix stabilised (handoff §7) — do NOT swap this for
    //  PopupController.Show(PopupSO), which skips that pause path.
    //
    //  ADVANCE MODEL:
    //    • Passive phases (2,3,7,7.3,8,9,10,10.1,11,12,13,14,16,17,18): advance on the
    //      popup's onComplete callback. (7 = ShowWorkers, 7.3 = FatigueBar.)
    //    • Interactive phases advance on real gameplay events:
    //        1   → Zone 1 tile pop-in finished (RegionManager.OnInitialRegionRevealed; loadingRevealSeconds
    //              is only a safety-net ceiling if that event never arrives)
    //        4   → strip opened            (ActionBarUI.IsStripOpen)
    //        5   → Plant Trees armed        (ActionBarUI.OnActionArmed)
    //        6   → drag multi-select ≥ 3    (TileSelector.SelectedTileCount, polled)
    //        7.1 → action confirmed         (ActionBarUI.OnActionConfirmed — the Confirm beat)
    //        7.2 → action lands             (ActionManager.OnActionCompleted; then red pings + popup)
    //        15  → next zone unlocked       (RunManager.OnRegionUnlocked)
    //
    //  ARCHITECTURAL LAWS (unchanged):
    //    1. Getters, not setters — reads other systems' read-only surface.
    //    2. Hooks fire on meaning — subscribes to meaning-events; only the few
    //       gates with no event are polled in Update.
    //    3. Loud failure for misconfig — required refs LogError; the director
    //       degrades without soft-locking (unwired popups auto-advance).
    //
    //  Scene wiring: see the Inspector checklist at the bottom of this file.
    // =========================================================================

    [DefaultExecutionOrder(50)] // after all manager singletons
    public class OnboardingDirector : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // THE CONTRACT (kept stable for DragGhostInset / Beat1_3JuiceDirector)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Fires when the director enters a new phase (BEFORE the popup/cue is shown).</summary>
        public event Action<OnboardingBeatId> OnBeatEntered;

        /// <summary>Fires when a phase's success condition is satisfied and we advance.</summary>
        public event Action<OnboardingBeatId> OnBeatCompleted;

        /// <summary>The coach-mark layer (B2) subscribes here. See <see cref="CoachMarkRequest.hide"/>.</summary>
        public event Action<CoachMarkRequest> OnCoachMarkRequested;

        /// <summary>True while the onboarding sequence is running (not yet graduated).</summary>
        public bool IsActive { get; private set; }

        /// <summary>The phase currently in progress (None if inactive).</summary>
        public OnboardingBeatId CurrentBeat { get; private set; } = OnboardingBeatId.None;

        /// <summary>Singleton — one director per scene.</summary>
        public static OnboardingDirector Instance { get; private set; }

        // ─────────────────────────────────────────────────────────────────────
        // Inspector
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Pairs a phase with the PopupSO that supplies its authored words.</summary>
        [Serializable]
        public struct PhaseContent
        {
            [Tooltip("Which onboarding phase this PopupSO supplies content for.")]
            public OnboardingBeatId phase;

            [Tooltip("Authored lines for the phase. The director picks the preset " +
                     "(Dialog/Character/Text); you supply the words + speakers.")]
            public PopupSO popup;
        }

        /// <summary>
        /// Per-phase FidgetArrow orbit override. Lets a designer seat the arrow at a different
        /// angle (and nudge its pivot / sprite) for each phase that shows a FidgetArrow — without
        /// touching the shared widget's defaults. Phases with no entry keep the widget defaults.
        /// </summary>
        [Serializable]
        public struct FidgetArrowTuning
        {
            [Tooltip("Which phase's FidgetArrow this tunes (phases 4, 5, 7, 7.3, 8, 9, 10, 10.1 show one).")]
            public OnboardingBeatId phase;

            [Tooltip("Where the arrow sits around the target, in degrees. 0 = right, 90 = up, " +
                     "180 = left, 270 = below. The arrowhead always points inward at the target.")]
            [Range(0f, 360f)]
            public float orbitAngleDeg;

            [Tooltip("Distance from pivot to arrow, in canvas pixels. 0 = keep the FidgetArrow's own radius.")]
            public float orbitRadius;

            [Tooltip("Nudge the pivot (the point the arrow orbits + points at) off the target, in canvas pixels.")]
            public Vector2 pivotOffsetPx;

            [Tooltip("Final fine-tune of the arrow sprite's position after orbit, in canvas pixels.")]
            public Vector2 arrowPosOffsetPx;
        }

        [Header("Content")]
        [Tooltip("Resolves Azi/Bob display names + portraits when presenting a PopupSO. " +
                 "Assign the same DialogueRegistry PopupController uses. Falls back to literal names if null.")]
        [SerializeField] private DialogueRegistry dialogueRegistry;

        [Tooltip("One entry per phase that shows a popup (phases 1 and 15 show none). " +
                 "Order in this list is irrelevant — lookup is by phase id.")]
        [SerializeField] private PhaseContent[] phases = new PhaseContent[0];

        [Header("Phase 1 — loading reveal")]
        [Tooltip("Safety-net ceiling (seconds) for phase 1. Phase 1 normally advances the moment " +
                 "RegionManager finishes animating Zone 1's tiles in (OnInitialRegionRevealed); this " +
                 "only fires if that event never arrives (regionManager unwired/misconfigured), so it " +
                 "never soft-locks. Set it above the worst-case reveal time (zone1RevealStagger × the " +
                 "top of Zone 1's profile sizeRange), not below it.")]
        [SerializeField] private float loadingRevealSeconds = 2.5f;

        [Header("Phase 6 — select tiles")]
        [Tooltip("Seconds after entering phase 6 before the GhostMouseDrag coach mark may show, even " +
                 "if the action is already armed. Without this, an action armed on phase 5 carries " +
                 "straight into phase 6 and the ghost drag jumps out on the very first frame.")]
        [SerializeField] private float dragGhostDelaySeconds = 3f;

        [Header("Highlight targets (optional — coach marks for phases 8–10.1)")]
        [Tooltip("Day counter / stamina cluster. Phase 8 points a FidgetArrow here (skipped if null).")]
        [SerializeField] private RectTransform dayCounterTarget;
        [Tooltip("Weather hex icon. Phase 9 highlight (skipped if null).")]
        [SerializeField] private RectTransform weatherHexTarget;
        [Tooltip("Zone 1 health bar. Phase 10 highlight (skipped if null).")]
        [SerializeField] private RectTransform zoneHealthBarTarget;
        [Tooltip("Trait icon pips. Phase 10.1 highlight (skipped if null).")]
        [SerializeField] private RectTransform traitPipsTarget;
        [Tooltip("Workforce / fatigue bar (ResourceDisplay's people bar — drops as workers tire). " +
                 "Phase 7.3 points a FidgetArrow here (skipped if null).")]
        [SerializeField] private RectTransform fatigueBarTarget;

        [Tooltip("Optional per-phase FidgetArrow orbit tuning. One entry per phase whose arrow " +
                 "needs a different seat/pivot than the widget default. Order is irrelevant — " +
                 "lookup is by phase id; phases with no entry use the FidgetArrow's own defaults.")]
        [SerializeField] private FidgetArrowTuning[] fidgetArrowTuning = new FidgetArrowTuning[0];

        [Header("Worker pings (phases 7 / 7.2)")]
        [Tooltip("Color of the 'here's your crew' pings shown on every worker in phase 7 (Show Workers).")]
        [SerializeField] private Color workerRevealPingColor = Color.white;
        [Tooltip("Color of the 'these workers are tired' pings shown on the fatigued crew after the " +
                 "action lands in phase 7.2 (Workers Tired).")]
        [SerializeField] private Color workerFatiguePingColor = new Color(0.9f, 0.15f, 0.15f, 1f);

        [Tooltip("Seconds between worker-ping bursts. The reveal (white) and fatigue (red) pings re-fire " +
                 "on this cadence for as long as their phase is on screen, then stop when it advances.")]
        [SerializeField] private float workerPingIntervalSeconds = 1.25f;

        [Tooltip("World-space horizontal offset (left/right) from each worker's anchor position where its " +
                 "ping is centered. WalkerManager positions anchor at the worker's base/pivot — this nudges " +
                 "the ring onto a specific point on the sprite (e.g. off-center art) instead of the raw pivot.")]
        [SerializeField] private float workerPingOffsetX = 0f;
        [Tooltip("World-space vertical offset (height) from each worker's anchor position where its ping is " +
                 "centered. Positive raises the ring above the worker's base/pivot, e.g. toward chest/head " +
                 "height instead of the feet.")]
        [SerializeField] private float workerPingOffsetY = 1f;

        // The worker pings render on PingDirector's OVERLAY surface (the lens/UI quad glued to the
        // camera). Projecting the worker onto that quad is PingDirector's job (PingOverlay → the quad's
        // own local plane), so there is no camera/layer reference here — wire the overlay quad on
        // PingDirector instead. If it isn't wired, the worker pings simply no-op (art-pending, §T).

        [Header("Interactive targets")]
        [Tooltip("Stable ActionSO.actionId of the action phase 5 teaches — the card the coach-mark " +
                 "arrow points at and the arm that completes the phase. Matched on the data id (not a " +
                 "C# type) because every authored action is a GenericPlayerAction. Defaults to Plant Trees.")]
        [SerializeField] private string plantTreesActionId = "plant_trees";

        [Header("References (optional — auto-found if null)")]
        [SerializeField] private ActionBarUI actionBarUI;
        [Tooltip("PlayLandingReveal() plays at graduation (the objective 'lands in front of the player').")]
        [SerializeField] private ObjectiveBannerUI objectiveBannerUI;
        [Tooltip("Used to resolve the newly-unlocked zone's centroid for the phase-16 camera pan.")]
        [SerializeField] private RegionManager regionManager;

        [Header("Debug")]
        [Tooltip("Start from this index into the phase sequence (0 = normal start).")]
        [SerializeField] private int debugStartPhaseIndex = 0;

        // ─────────────────────────────────────────────────────────────────────
        // Phase sequence + preset table
        // ─────────────────────────────────────────────────────────────────────

        // The chronological order of the deck (the # column of the tutorial plan).
        private static readonly OnboardingBeatId[] s_Sequence =
        {
            OnboardingBeatId.Phase_01_LoadingReveal,
            OnboardingBeatId.Phase_02_MeetAzi,
            OnboardingBeatId.Phase_03_Framing,
            OnboardingBeatId.Phase_07_ShowWorkers,
            OnboardingBeatId.Phase_04_ActionBar,
            OnboardingBeatId.Phase_05_PickCard,
            OnboardingBeatId.Phase_06_SelectTiles,
            OnboardingBeatId.Phase_07_Confirm,
            OnboardingBeatId.Phase_07_WorkersTired,
            OnboardingBeatId.Phase_07_FatigueBar,
            OnboardingBeatId.Phase_08_TimeStamina,
            OnboardingBeatId.Phase_09_Weather,
            OnboardingBeatId.Phase_10_ZoneHealth,
            OnboardingBeatId.Phase_10_1_TraitPips,
            OnboardingBeatId.Phase_11_GoalDeadline,
            OnboardingBeatId.Phase_12_Stakes,
            OnboardingBeatId.Phase_13_RoleAffirm,
            OnboardingBeatId.Phase_14_HelpAffordance,
            OnboardingBeatId.Phase_15_FreePlay,
            OnboardingBeatId.Phase_16_ZoneUnlock,
            OnboardingBeatId.Phase_17_Factory,
            OnboardingBeatId.Phase_18_Maintenance,
        };

        // The popup preset each phase presents with. Dialog = intrusive + portrait + pause;
        // Character = side bubble + portrait; Text = side bubble, no portrait (speaker-less MC box).
        private static PopupStyle PresetFor(OnboardingBeatId id)
        {
            switch (id)
            {
                case OnboardingBeatId.Phase_02_MeetAzi:
                case OnboardingBeatId.Phase_03_Framing:
                case OnboardingBeatId.Phase_11_GoalDeadline:
                case OnboardingBeatId.Phase_12_Stakes:
                case OnboardingBeatId.Phase_13_RoleAffirm:
                    return PopupStyle.Dialog;

                case OnboardingBeatId.Phase_04_ActionBar:
                case OnboardingBeatId.Phase_05_PickCard:
                case OnboardingBeatId.Phase_06_SelectTiles:
                case OnboardingBeatId.Phase_07_Confirm:
                    return PopupStyle.Text;

                default: // 7 (ShowWorkers), 7.2 (WorkersTired), 7.3 (FatigueBar), 8, 9, 10, 10.1, 14, 16, 17, 18
                    return PopupStyle.Character;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Runtime state
        // ─────────────────────────────────────────────────────────────────────

        private readonly Dictionary<OnboardingBeatId, PopupSO> _phasePopups =
            new Dictionary<OnboardingBeatId, PopupSO>();

        private readonly Dictionary<OnboardingBeatId, FidgetArrowTuning> _arrowTuning =
            new Dictionary<OnboardingBeatId, FidgetArrowTuning>();

        private int  _phaseIndex;
        private bool _advanceRequested;   // set by a passive popup's onComplete; drained in Update

        // Per-phase success gates (reset on enter).
        private bool _armed;              // phase 5: Plant Trees armed (event)
        private bool _confirmed;          // phase 7: action confirmed (event)
        private bool _dragDone;           // phase 6: drag multi-select ≥ 3 (polled)
        private bool _regionUnlocked;     // phase 15: next zone unlocked (event)
        private bool _regionRevealed;     // phase 1: Zone 1 tile pop-in tween finished (event)
        private bool _awaitingActionComplete; // phase 7.2: red pings + popup held until OnActionCompleted
        // Sticky across the ShowWorkers→Confirm hand-off (NOT reset in the per-phase gate reset): the
        // reveal beat is non-blocking, so an eager player can press Confirm during it. If they do, the
        // Confirm beat (7.1) auto-advances instead of waiting for a re-confirm that can never come.
        private bool _confirmSeenEarly;

        // Worker-ping repeat driver (phases 7 / 7.2). While the mode is set, Update re-fires the pings
        // every workerPingIntervalSeconds; EnterPhase clears it so the repeat stops the moment the phase
        // advances.
        private enum WorkerPingMode { None, AllWorkers, FatiguedWorkers }
        private WorkerPingMode _workerPingMode = WorkerPingMode.None;
        private float          _workerPingTimer;

        private float _loadingTimer;      // phase 1 reveal timer (authoritative duration)

        // Phase 6 drag ghost — shown only while an action is armed; needs growth past this baseline.
        private bool  _dragGhostShown;
        private int   _dragBaselineCount;
        private float _dragGhostDelayTimer;   // counts up from phase-enter; ghost gated until it clears dragGhostDelaySeconds

        // Last UI cue target sent to the arrow — re-issue only when it changes (tab → card).
        private Transform _currentCueTarget;

        // Coach marks shown this phase, hidden together on phase complete.
        private readonly HashSet<CoachMarkKind> _activeMarks = new HashSet<CoachMarkKind>();

        // Reused scratch so tile-target resolution allocates nothing per phase.
        private readonly List<Tile> _openTileScratch = new List<Tile>();

        private TileSelector _cachedSelector;

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
            bool ok = true;

            if (PopupManager.Instance == null)
            {
                Debug.LogError($"{name}: PopupManager.Instance is null — OnboardingDirector cannot present phases. " +
                               "Ensure a PopupManager is in the scene before this component.", this);
                ok = false;
            }

            if (dialogueRegistry == null)
                Debug.LogError($"{name}: dialogueRegistry is not assigned — Azi/Bob names fall back to literals and " +
                               "portraits resolve null. Assign the same DialogueRegistry PopupController uses.", this);

            if (actionBarUI == null) actionBarUI = FindObjectOfType<ActionBarUI>();
            if (actionBarUI == null)
                Debug.LogWarning($"{name}: ActionBarUI not found — interactive phases 4–7 cannot advance. Assign it in the Inspector.", this);

            if (objectiveBannerUI == null) objectiveBannerUI = FindObjectOfType<ObjectiveBannerUI>();
            if (objectiveBannerUI == null)
                Debug.LogWarning($"{name}: ObjectiveBannerUI not found — graduation's landing-reveal tween will not play.", this);

            if (regionManager == null) regionManager = FindObjectOfType<RegionManager>();
            // Also used for phase 16's camera pan (best-effort there).

            BuildPhaseLookup();

            if (!ok) { enabled = false; return; }

            // Meaning-event subscriptions (Law 2).
            if (RunManager.Instance != null)
                RunManager.Instance.OnRegionUnlocked += HandleRegionUnlocked;
            else
                Debug.LogError($"{name}: RunManager.Instance is null — phase 15 (free play) can never detect the zone unlock. " +
                               "Ensure RunManager is in the scene.", this);

            if (regionManager != null)
                regionManager.OnInitialRegionRevealed += HandleInitialRegionRevealed;
            else
                Debug.LogWarning($"{name}: RegionManager not found — phase 1 (loading reveal) can't detect the tile " +
                                 "pop-in finishing; it will fall back to the loadingRevealSeconds timer.", this);

            if (actionBarUI != null)
            {
                actionBarUI.OnActionArmed     += HandleActionArmed;
                actionBarUI.OnActionConfirmed += HandleActionConfirmed;
            }

            // Phase 7.2 (fatigued-worker reveal) fires only once the confirmed action finishes.
            if (ActionManager.Instance != null)
                ActionManager.Instance.OnActionCompleted += HandleActionCompleted;
            else
                Debug.LogWarning($"{name}: ActionManager.Instance is null — phase 7.2 (fatigued-worker " +
                                 "reveal) can't detect the action finishing; it will fire immediately instead.", this);

            _cachedSelector = TileSelector.Instance ?? FindObjectOfType<TileSelector>();
            if (_cachedSelector == null)
                Debug.LogWarning($"{name}: No TileSelector found — phase 6 (drag select) cannot advance.", this);

            _phaseIndex = Mathf.Clamp(debugStartPhaseIndex, 0, s_Sequence.Length - 1);
            IsActive = true;
            EnterPhase(s_Sequence[_phaseIndex]);
        }

        void OnDestroy()
        {
            if (RunManager.Instance != null)
                RunManager.Instance.OnRegionUnlocked -= HandleRegionUnlocked;

            if (regionManager != null)
                regionManager.OnInitialRegionRevealed -= HandleInitialRegionRevealed;

            if (actionBarUI != null)
            {
                actionBarUI.OnActionArmed     -= HandleActionArmed;
                actionBarUI.OnActionConfirmed -= HandleActionConfirmed;
            }

            if (ActionManager.Instance != null)
                ActionManager.Instance.OnActionCompleted -= HandleActionCompleted;
        }

        void BuildPhaseLookup()
        {
            _phasePopups.Clear();
            if (phases != null)
            {
                foreach (PhaseContent pc in phases)
                {
                    if (pc.popup == null) continue;
                    _phasePopups[pc.phase] = pc.popup;
                }
            }

            _arrowTuning.Clear();
            if (fidgetArrowTuning != null)
            {
                foreach (FidgetArrowTuning t in fidgetArrowTuning)
                    _arrowTuning[t.phase] = t;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Update — polling-only gates
        // ─────────────────────────────────────────────────────────────────────

        void Update()
        {
            if (!IsActive) return;

            // A passive popup finished → advance (drained here to avoid re-entrancy in the callback).
            if (_advanceRequested)
            {
                _advanceRequested = false;
                CompletePhase();
                return;
            }

            // Worker-ping repeat (phases 7 / 7.2): re-fire the burst on cadence until the phase clears
            // the mode. Runs alongside the per-beat switch below, not inside it.
            if (_workerPingMode != WorkerPingMode.None)
            {
                _workerPingTimer += Time.unscaledDeltaTime;
                if (_workerPingTimer >= workerPingIntervalSeconds)
                {
                    _workerPingTimer = 0f;
                    FireWorkerPings();
                }
            }

            switch (CurrentBeat)
            {
                case OnboardingBeatId.Phase_01_LoadingReveal:
                    // Event-authoritative: advances the moment RegionManager finishes animating Zone
                    // 1's tiles in (OnInitialRegionRevealed). loadingRevealSeconds is only a safety-net
                    // ceiling in case that event never arrives (regionManager unwired/misconfigured).
                    _loadingTimer += Time.unscaledDeltaTime;
                    if (_regionRevealed || _loadingTimer >= loadingRevealSeconds) CompletePhase();
                    break;

                case OnboardingBeatId.Phase_04_ActionBar:
                    RefreshArmCueTarget();
                    if (actionBarUI != null && actionBarUI.IsStripOpen) CompletePhase();
                    break;

                case OnboardingBeatId.Phase_05_PickCard:
                    RefreshArmCueTarget();     // re-point the arrow tab → card as the strip builds
                    if (_armed) CompletePhase();
                    break;

                case OnboardingBeatId.Phase_06_SelectTiles:
                    _dragGhostDelayTimer += Time.unscaledDeltaTime;
                    if (_dragGhostDelayTimer >= dragGhostDelaySeconds) RefreshDragGhostByArmState();
                    if (!_dragDone)
                    {
                        TileSelector ts = GetTileSelector();
                        int need = Mathf.Max(3, _dragBaselineCount + 1);
                        if (ts != null && ts.IsFloodFillMode && ts.SelectedTileCount >= need)
                        {
                            _dragDone = true;
                            CompletePhase();
                        }
                    }
                    break;

                case OnboardingBeatId.Phase_07_Confirm:
                    if (_confirmed) CompletePhase();
                    break;

                case OnboardingBeatId.Phase_15_FreePlay:
                    if (_regionUnlocked) CompletePhase();
                    break;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Phase management
        // ─────────────────────────────────────────────────────────────────────

        void EnterPhase(OnboardingBeatId id)
        {
            // Reset per-phase gates.
            _armed = _confirmed = _dragDone = _regionUnlocked = _regionRevealed = false;
            _awaitingActionComplete = false;
            _workerPingMode      = WorkerPingMode.None;   // stop any repeating worker pings from the prior beat
            _workerPingTimer     = 0f;
            _dragGhostShown      = false;
            _dragBaselineCount   = 0;
            _dragGhostDelayTimer = 0f;
            _currentCueTarget  = null;
            _loadingTimer      = 0f;
            _advanceRequested  = false;

            CurrentBeat = id;
            OnBeatEntered?.Invoke(id);

            switch (id)
            {
                case OnboardingBeatId.Phase_01_LoadingReveal:
                    // No popup — the world builds in front of the player (Update runs the timer).
                    break;

                case OnboardingBeatId.Phase_04_ActionBar:
                    PresentPopup(id, advanceOnComplete: false);
                    _currentCueTarget = actionBarUI != null ? actionBarUI.GetArmCueRect(null) : null;
                    ShowMark(new CoachMarkRequest
                    {
                        kind              = CoachMarkKind.FidgetArrow,
                        trackTarget       = _currentCueTarget,   // Intervene tab
                        screenSpaceTarget = true,
                    });
                    break;

                case OnboardingBeatId.Phase_05_PickCard:
                {
                    PlayerAction plant = FindPlantTreesAction();
                    if (actionBarUI != null && plant != null)
                    {
                        // Reveal the Intervene strip idempotently, then dim all but Plant Trees.
                        // NOT SelectCategory: the player just opened Intervene to clear phase 4, and
                        // SelectCategory is a user TOGGLE — re-calling it on the open category collapses
                        // the strip (and SetCategoriesExpanded would replay the flower reveal tween),
                        // which read as "the action bar resets". RevealCategory is the non-toggling,
                        // no-replay force-open.
                        actionBarUI.RevealCategory(ActionCategory.Intervene);
                        actionBarUI.LockCardsExcept(plant);
                    }
                    PresentPopup(id, advanceOnComplete: false);
                    _currentCueTarget = actionBarUI != null ? actionBarUI.GetArmCueRect(plant) : null;
                    ShowMark(new CoachMarkRequest
                    {
                        kind              = CoachMarkKind.FidgetArrow,
                        trackTarget       = _currentCueTarget,   // Plant Trees card (or tab if not built yet)
                        screenSpaceTarget = true,
                    });
                    break;
                }

                case OnboardingBeatId.Phase_06_SelectTiles:
                    PresentPopup(id, advanceOnComplete: false);
                    ShowMark(new CoachMarkRequest
                    {
                        kind      = CoachMarkKind.CornerReminder,
                        labelText = OnboardingContent.Reminder_SelectMultiple,
                    });
                    // GhostMouseDrag is arm-gated (and delayed dragGhostDelaySeconds after phase-enter)
                    // — RefreshDragGhostByArmState() shows it in Update.
                    break;

                case OnboardingBeatId.Phase_07_ShowWorkers:
                    // The crew has been spawned hidden (turned away) since run start — this beat is
                    // their entrance: show them and lerp each into facing the camera, then white pings
                    // on the whole crew — "here's who does the work" — then a passive popup that
                    // advances on dismissal. The pings repeat on workerPingIntervalSeconds while the
                    // beat is up; the popup text carries the beat. Non-intrusive (Character), so the
                    // sim isn't paused and the ping rings animate while the bubble is up.
                    WalkerManager.Instance?.RevealInitialWorkers();
                    _confirmSeenEarly = false;   // open the reveal→confirm window (see the field's note)
                    BeginWorkerPings(WorkerPingMode.AllWorkers);   // repeats until the beat advances
                    PresentPopup(id, advanceOnComplete: true);
                    break;

                case OnboardingBeatId.Phase_07_Confirm:
                    // If the player already confirmed during the (non-blocking) reveal, the action is
                    // already committed and disarmed — asking again would soft-lock. Auto-advance.
                    if (_confirmSeenEarly) { RequestAdvance(); break; }
                    PresentPopup(id, advanceOnComplete: false);
                    ShowMark(new CoachMarkRequest
                    {
                        kind              = CoachMarkKind.FidgetArrow,
                        trackTarget       = actionBarUI != null ? actionBarUI.GetConfirmButtonRect() : null,
                        screenSpaceTarget = true,
                    });
                    break;

                case OnboardingBeatId.Phase_07_WorkersTired:
                    // Red pings on the fatigued crew, shown only AFTER the confirmed action finishes.
                    // If the action is still running (the normal case — it plays out over several
                    // days), defer the pings + popup to HandleActionCompleted; if it already finished
                    // (e.g. an instant action, so OnActionCompleted fired during the confirm beat and
                    // we missed it), fire now so the beat never stalls waiting on a past event.
                    if (ActionManager.Instance != null && ActionManager.Instance.IsActionRunning)
                        _awaitingActionComplete = true;   // HandleActionCompleted will reveal
                    else
                        RevealFatiguedWorkers();
                    break;

                case OnboardingBeatId.Phase_07_FatigueBar:
                    ShowHighlight(fatigueBarTarget);   // FidgetArrow at the workforce / fatigue bar
                    PresentPopup(id, advanceOnComplete: true);
                    break;

                case OnboardingBeatId.Phase_08_TimeStamina:
                    ShowHighlight(dayCounterTarget);   // day counter + stamina cluster
                    PresentPopup(id, advanceOnComplete: true);
                    break;

                case OnboardingBeatId.Phase_09_Weather:
                    ShowHighlight(weatherHexTarget);
                    PresentPopup(id, advanceOnComplete: true);
                    break;

                case OnboardingBeatId.Phase_10_ZoneHealth:
                    ShowHighlight(zoneHealthBarTarget);
                    PresentPopup(id, advanceOnComplete: true);
                    break;

                case OnboardingBeatId.Phase_10_1_TraitPips:
                    ShowHighlight(traitPipsTarget);
                    PresentPopup(id, advanceOnComplete: true);
                    break;

                case OnboardingBeatId.Phase_15_FreePlay:
                    // No UI. Free play until the player restores enough to unlock the next zone.
                    // (CornerReminder-on-stall is deferred — tutorial plan phase 15.)
                    break;

                case OnboardingBeatId.Phase_16_ZoneUnlock:
                    if (EventCameraHandler.Instance != null && TryGetNewRegionCentroid(out Vector3 centre))
                        EventCameraHandler.Instance.PanTo(centre);
                    PresentPopup(id, advanceOnComplete: true);
                    break;

                case OnboardingBeatId.Phase_17_Factory:
                    // TODO(item B): once RegionManager guarantees the factory in an ordered early
                    // region, zoom/pan the camera to it here (EventCameraHandler.ZoomTo + PanTo).
                    PresentPopup(id, advanceOnComplete: true);
                    break;

                default: // 2, 3, 11, 12, 13, 14, 18 — passive Dialog / Character phases
                    PresentPopup(id, advanceOnComplete: true);
                    break;
            }
        }

        void CompletePhase()
        {
            if (!IsActive) return;

            OnboardingBeatId completed = CurrentBeat;

            HideActiveMarks();
            if (completed == OnboardingBeatId.Phase_05_PickCard)
                actionBarUI?.ClearCardLock();   // never leave a card dimmed (handoff §6-E)

            OnBeatCompleted?.Invoke(completed);

            _phaseIndex++;
            if (_phaseIndex >= s_Sequence.Length) { Graduate(); return; }
            EnterPhase(s_Sequence[_phaseIndex]);
        }

        /// <summary>
        /// Abort the tutorial and land the player in the fully graduated state (used by the
        /// hold-Space skip control, handoff §6-E). Dismisses any in-flight popup and releases
        /// its sim pause BEFORE graduating — Graduate() then clears every card lock and coach
        /// mark, so no scaffolding is left behind (a dimmed card left over is worse than none).
        /// </summary>
        public void SkipOnboarding()
        {
            if (!IsActive) return;
            PopupManager.Instance?.HideAll();   // release the intrusive-popup pause + clear side bubbles
            Graduate();
        }

        void Graduate()
        {
            IsActive    = false;
            CurrentBeat = OnboardingBeatId.None;

            HideAllMarks();
            actionBarUI?.ClearCardLock();   // safety — no residual lock after onboarding
            // Safety net for a skip landing before Phase_07_ShowWorkers ever ran — the crew must
            // never stay hidden once onboarding is over. Idempotent if it already ran.
            WalkerManager.Instance?.RevealInitialWorkers();

            if (objectiveBannerUI != null)
                objectiveBannerUI.PlayLandingReveal();
            else
                Debug.LogWarning($"{name}: objectiveBannerUI is not wired — skipping the graduation landing-reveal tween.", this);

            Debug.Log("[OnboardingDirector] Graduation — scaffolding retired.");
            enabled = false;   // no more Update polling
        }

        // ─────────────────────────────────────────────────────────────────────
        // Popup presentation
        // ─────────────────────────────────────────────────────────────────────

        // Presents the phase's authored PopupSO with the phase's fixed preset. When
        // advanceOnComplete is true, the popup's dismissal advances the sequence.
        // An unwired popup logs a warning and (if passive) auto-advances so the run never soft-locks.
        void PresentPopup(OnboardingBeatId id, bool advanceOnComplete)
        {
            PopupSO so = GetPopup(id);
            if (so == null)
            {
                Debug.LogWarning($"{name}: no PopupSO wired for {id} — skipping its popup" +
                                 (advanceOnComplete ? " and auto-advancing." : "."), this);
                if (advanceOnComplete) RequestAdvance();
                return;
            }

            if (PopupManager.Instance == null)
            {
                Debug.LogError($"{name}: PopupManager.Instance is null — cannot present {id}.", this);
                if (advanceOnComplete) RequestAdvance();
                return;
            }

            List<ResolvedLine> lines = so.ResolveLines(dialogueRegistry);
            PopupStyle preset = PresetFor(id);

            OnboardingBeatId captured = id;
            Action onDone = advanceOnComplete
                ? (Action)(() => { if (IsActive && CurrentBeat == captured) RequestAdvance(); })
                : null;

            PopupManager.Instance.PlayLines(lines, preset, onDone);
        }

        void RequestAdvance() => _advanceRequested = true;

        PopupSO GetPopup(OnboardingBeatId id) =>
            _phasePopups.TryGetValue(id, out PopupSO so) ? so : null;

        // ─────────────────────────────────────────────────────────────────────
        // Meaning-event handlers (Law 2)
        // ─────────────────────────────────────────────────────────────────────

        void HandleActionArmed(PlayerAction action)
        {
            if (CurrentBeat == OnboardingBeatId.Phase_05_PickCard && IsTaughtAction(action))
                _armed = true;
        }

        void HandleActionConfirmed()
        {
            if (CurrentBeat == OnboardingBeatId.Phase_07_Confirm)
                _confirmed = true;
            else if (CurrentBeat == OnboardingBeatId.Phase_07_ShowWorkers)
                _confirmSeenEarly = true;   // early confirm during the reveal — Confirm beat will auto-advance
        }

        void HandleRegionUnlocked()
        {
            if (CurrentBeat == OnboardingBeatId.Phase_15_FreePlay)
                _regionUnlocked = true;
        }

        void HandleInitialRegionRevealed()
        {
            if (CurrentBeat == OnboardingBeatId.Phase_01_LoadingReveal)
                _regionRevealed = true;
        }

        // Phase 7.2 waits here: the red-ping reveal + its popup fire the moment the confirmed action
        // finishes (clean finish OR abort — both settle Worker.isFatigued before this fires).
        void HandleActionCompleted(Tile tile, int daysElapsed)
        {
            if (CurrentBeat == OnboardingBeatId.Phase_07_WorkersTired && _awaitingActionComplete)
            {
                _awaitingActionComplete = false;
                RevealFatiguedWorkers();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Worker pings (phases 7 / 7.2)
        // ─────────────────────────────────────────────────────────────────────
        //
        // Both routines are best-effort: if the Walker system or PingDirector isn't wired in this
        // scene (both are still art-pending — WALKERS / VFX §S/§T), the pings simply no-op and the
        // popup text still teaches the beat. No soft-lock, matching Law 3's degrade-don't-die.

        // Reveals the fatigued crew: red pings + the phase-7.2 popup (advances on dismissal).
        void RevealFatiguedWorkers()
        {
            BeginWorkerPings(WorkerPingMode.FatiguedWorkers);   // repeats until the beat advances
            PresentPopup(OnboardingBeatId.Phase_07_WorkersTired, advanceOnComplete: true);
        }

        // Starts (or restarts) the repeating worker-ping burst for the current beat: fires once now,
        // then Update re-fires every workerPingIntervalSeconds until EnterPhase clears the mode.
        void BeginWorkerPings(WorkerPingMode mode)
        {
            _workerPingMode  = mode;
            _workerPingTimer = 0f;
            FireWorkerPings();
        }

        // One burst for the active mode — invoked immediately by BeginWorkerPings and then on cadence
        // by Update. No-op when the mode is None.
        void FireWorkerPings()
        {
            switch (_workerPingMode)
            {
                case WorkerPingMode.AllWorkers:      PingAllWorkers(workerRevealPingColor);       break;
                case WorkerPingMode.FatiguedWorkers: PingFatiguedWorkers(workerFatiguePingColor); break;
            }
        }

        void PingAllWorkers(Color color)
        {
            if (PingDirector.Instance == null || WalkerManager.Instance == null) return;
            foreach (Vector3 pos in WalkerManager.Instance.WorkerWalkerPositions)
                PingDirector.Instance.PingOverlay(WorkerPingAnchor(pos), color);   // lens/UI surface — projection is PingDirector's job
        }

        void PingFatiguedWorkers(Color color)
        {
            if (PingDirector.Instance == null || WalkerManager.Instance == null) return;

            bool any = false;
            foreach (Vector3 pos in WalkerManager.Instance.FatiguedWorkerWalkerPositions)
            {
                PingDirector.Instance.PingOverlay(WorkerPingAnchor(pos), color);
                any = true;
            }

            // The taught action normally tires several workers, but if a tuning change leaves none
            // fatigued, ping the whole crew instead so the beat never shows zero pings.
            if (!any)
                foreach (Vector3 pos in WalkerManager.Instance.WorkerWalkerPositions)
                    PingDirector.Instance.PingOverlay(WorkerPingAnchor(pos), color);
        }

        /// <summary>WalkerManager's positions anchor at each worker's base/pivot — offsets by
        /// workerPingOffsetX/Y (world-space X/height) so the ring can land on a specific point on the
        /// sprite (e.g. chest height) instead of the raw pivot. Applied before PingOverlay's own
        /// world→overlay-quad-local projection, so it stays put through the reprojection PingDirector
        /// re-runs every frame as the overlay quad rides the camera.</summary>
        Vector3 WorkerPingAnchor(Vector3 workerPos) =>
            workerPos + new Vector3(workerPingOffsetX, workerPingOffsetY, 0f);

        // ─────────────────────────────────────────────────────────────────────
        // Coach-mark helpers
        // ─────────────────────────────────────────────────────────────────────

        void ShowMark(CoachMarkRequest req)
        {
            req.hide = false;
            ApplyArrowTuning(ref req);
            _activeMarks.Add(req.kind);
            OnCoachMarkRequested?.Invoke(req);
        }

        // Stamps the current phase's FidgetArrow orbit override onto the request (if one is wired).
        // No-op for non-arrow marks and for phases with no tuning entry (the widget uses its defaults).
        void ApplyArrowTuning(ref CoachMarkRequest req)
        {
            if (req.kind != CoachMarkKind.FidgetArrow) return;
            if (!_arrowTuning.TryGetValue(CurrentBeat, out FidgetArrowTuning t)) return;

            req.applyOrbit       = true;
            req.orbitAngleDeg    = t.orbitAngleDeg;
            req.orbitRadius      = t.orbitRadius;
            req.pivotOffsetPx    = t.pivotOffsetPx;
            req.arrowPosOffsetPx = t.arrowPosOffsetPx;
        }

        void HideMark(CoachMarkKind kind)
        {
            _activeMarks.Remove(kind);
            OnCoachMarkRequested?.Invoke(new CoachMarkRequest { kind = kind, hide = true });
        }

        void HideActiveMarks()
        {
            foreach (CoachMarkKind kind in _activeMarks)
                OnCoachMarkRequested?.Invoke(new CoachMarkRequest { kind = kind, hide = true });
            _activeMarks.Clear();
        }

        void HideAllMarks()
        {
            foreach (CoachMarkKind kind in Enum.GetValues(typeof(CoachMarkKind)))
            {
                if (kind == CoachMarkKind.None) continue;
                OnCoachMarkRequested?.Invoke(new CoachMarkRequest { kind = kind, hide = true });
            }
            _activeMarks.Clear();
        }

        // FidgetArrow → an optional UI highlight target (phases 8–10.1). Skips silently if unwired
        // (the popup text still teaches the feature).
        void ShowHighlight(RectTransform target)
        {
            if (target == null) return;
            ShowMark(new CoachMarkRequest
            {
                kind              = CoachMarkKind.FidgetArrow,
                trackTarget       = target,
                screenSpaceTarget = true,
            });
        }

        // Re-issues the arm-cue arrow only when its target changes (tab → card as the strip opens).
        void RefreshArmCueTarget()
        {
            Transform t = CurrentArmCueTarget();
            if (t == _currentCueTarget) return;
            _currentCueTarget = t;

            ShowMark(new CoachMarkRequest
            {
                kind              = CoachMarkKind.FidgetArrow,
                trackTarget       = t,
                screenSpaceTarget = true,
            });
        }

        Transform CurrentArmCueTarget()
        {
            if (actionBarUI == null) return null;
            switch (CurrentBeat)
            {
                case OnboardingBeatId.Phase_04_ActionBar:
                    return actionBarUI.GetArmCueRect(null);                    // Intervene tab
                case OnboardingBeatId.Phase_05_PickCard:
                    return actionBarUI.GetArmCueRect(FindPlantTreesAction());  // card (or tab until built)
                default:
                    return null;
            }
        }

        // Phase 6: the drag ghost shows while an action is armed and hides when disarmed.
        void RefreshDragGhostByArmState()
        {
            bool armed = actionBarUI != null && actionBarUI.CurrentArmedAction != null;
            if (armed == _dragGhostShown) return;
            _dragGhostShown = armed;

            if (armed)
            {
                // Capture the auto-blob size so the phase completes only after the player DRAGS to
                // grow it — not the instant arming creates the minimum selection.
                TileSelector ts = GetTileSelector();
                _dragBaselineCount = ts != null ? ts.SelectedTileCount : 0;

                var req = new CoachMarkRequest { kind = CoachMarkKind.GhostMouseDrag };
                if (TryGetRandomOpenTileWorld(out Vector3 world))
                    req.worldTarget = world;
                ShowMark(req);
            }
            else
            {
                HideMark(CoachMarkKind.GhostMouseDrag);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Resolution helpers
        // ─────────────────────────────────────────────────────────────────────

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

        // Centroid of the most-recently generated region — the mean of its tiles' world positions.
        // Best-effort: returns false (and the caller skips the pan) if it can't be resolved.
        // (Item F: formalised as the shared TileManager.TryGetRegionCentroid helper; this just
        // resolves WHICH region is the just-generated one and delegates.)
        bool TryGetNewRegionCentroid(out Vector3 world)
        {
            world = Vector3.zero;
            TileManager tm = TileManager.Instance;
            if (tm == null || regionManager == null) return false;
            int newRegionId = regionManager.NextRegionID - 1;   // the just-generated region
            return tm.TryGetRegionCentroid(newRegionId, out world);
        }

        // The stable actionId phase 5 teaches. Falls back to the Plant Trees literal if a designer
        // clears the field, so the phase never silently soft-locks on an empty id.
        string TaughtActionId =>
            string.IsNullOrEmpty(plantTreesActionId) ? "plant_trees" : plantTreesActionId;

        // Matches the taught action by its data-driven actionId (not a concrete C# type — every
        // authored action is a GenericPlayerAction, so `is PlantTreesAction` never matches).
        bool IsTaughtAction(PlayerAction action) =>
            action != null && action.ActionId == TaughtActionId;

        PlayerAction FindPlantTreesAction()
        {
            ActionManager am = ActionManager.Instance;
            if (am == null) return null;
            foreach (PlayerAction a in am.GetAvailableActions())
                if (IsTaughtAction(a)) return a;
            return null;
        }

        TileSelector GetTileSelector()
        {
            if (_cachedSelector == null)
                _cachedSelector = TileSelector.Instance ?? FindObjectOfType<TileSelector>();
            return _cachedSelector;
        }
    }
}

// =============================================================================
// INSPECTOR WIRING CHECKLIST
// =============================================================================
//
// Content
//   • dialogueRegistry — the same DialogueRegistry asset PopupController uses
//     (resolves Azi/Bob names + portraits).
//   • phases           — one entry per popup phase. Author a PopupSO per phase
//     (words + speaker only; the director picks Dialog/Character/Text). Phases
//     that show NO popup and need no entry: 1 (loading) and 15 (free play).
//       Dialog phases:    2, 3, 11, 12, 13
//       Text phases:      4, 5, 6, 7.1 (Confirm)
//       Character phases: 7 (ShowWorkers), 7.2 (WorkersTired), 7.3 (FatigueBar),
//                         8, 9, 10, 10.1, 14, 16, 17, 18
//
// Highlight targets (optional coach marks)
//   • dayCounterTarget / weatherHexTarget / zoneHealthBarTarget / traitPipsTarget /
//     fatigueBarTarget — RectTransforms the phase-8/9/10/10.1/7.3 FidgetArrow points
//     at. Leave null to skip the arrow (the popup text still teaches the feature).
//
// Worker pings (phases 7 / 7.2)
//   • workerRevealPingColor (white) / workerFatiguePingColor (red) — the ping hue for
//     the crew-reveal and fatigued-crew beats. Pings no-op unless the Walker system AND
//     PingDirector are wired in the scene (both are still art-pending — §S/§T).
//   • workerPingOffsetX / workerPingOffsetY — world-space offset from each worker's
//     WalkerManager anchor (base/pivot) to where its ping is centered, e.g. raised to
//     chest height instead of the feet. Applied before PingDirector's world→overlay
//     projection, so it stays correct as the overlay quad rides the camera.
//   • fidgetArrowTuning — optional per-phase orbit overrides for the FidgetArrow
//     (angle 0-360 around the target + pivot / arrow-position x-y nudges). Add an
//     entry only for a phase whose arrow needs a different seat than the widget
//     default; phases with no entry use the FidgetArrow's own serialized values.
//
// References (auto-found if left null)
//   • actionBarUI       — required for interactive phases 4–7.
//   • objectiveBannerUI — graduation landing-reveal tween.
//   • regionManager     — phase-1 tile-pop-in advance (falls back to the loadingRevealSeconds
//     timer if unwired) and the phase-16 camera pan to the new zone (best-effort).
//
// DEFERRED (see ONBOARDING_HANDOFF.md, tracked separately):
//   • Phase 17 shows its popup only until item B guarantees the factory + adds the camera move.
//   • Phase 11 deadline text stays literal until item G adds token substitution.
//   • Skip control (item E — WIRED): SkipOnboarding() is the public entry point; the
//     hold-Space fill-bar UI lives in the separate OnboardingSkipControl component
//     (see its own Inspector-wiring block).
// =============================================================================
