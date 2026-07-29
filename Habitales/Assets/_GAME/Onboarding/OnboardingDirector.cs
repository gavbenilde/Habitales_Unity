using System;
using System.Collections.Generic;
using UnityEngine;
using Habitales.UI;
using Habitales.UI.Actions;
using Habitales.Dialogue;
using UnityEngine.Serialization;
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
    //    • Passive phases (2,3,8,9,10,10.1,11,12,13,14,16,17,18): advance on the
    //      popup's onComplete callback.
    //    • Interactive phases advance on real gameplay events:
    //        4  → strip opened            (ActionBarUI.IsStripOpen)
    //        5  → Plant Trees armed        (ActionBarUI.OnActionArmed)
    //        6  → drag multi-select ≥ 3    (TileSelector.SelectedTileCount, polled)
    //        7  → action confirmed         (ActionBarUI.OnActionConfirmed)
    //        15 → next zone unlocked       (RunManager.OnRegionUnlocked)
    //    • Phase 1 (loading reveal): timer-authoritative — lasts exactly
    //      loadingRevealSeconds. The world's tile pop-in animates independently.
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
            [Tooltip("Which phase's FidgetArrow this tunes (phases 4, 5, 7, 8, 9, 10, 10.1 show one).")]
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
        [Tooltip("Exact duration (seconds) of phase 1. The world's tile pop-in animates " +
                 "independently and keeps playing past this, so lower = snappier hand-off to Azi.")]
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

        [Tooltip("Optional per-phase FidgetArrow orbit tuning. One entry per phase whose arrow " +
                 "needs a different seat/pivot than the widget default. Order is irrelevant — " +
                 "lookup is by phase id; phases with no entry use the FidgetArrow's own defaults.")]
        [SerializeField] private FidgetArrowTuning[] fidgetArrowTuning = new FidgetArrowTuning[0];

        [Header("Interactive targets")]
        [Tooltip("Stable ActionSO.actionId of the action phase 5 teaches — the card the coach-mark " +
                 "arrow points at and the arm that completes the phase. Matched on the data id (not a " +
                 "C# type) because every authored action is a GenericPlayerAction. Defaults to Plant Trees.")]
        [SerializeField] private string plantTreesActionId = "plant_trees";
        [SerializeField] private string cleanupTrashActionId = "remove_trash";

        [Header("References (optional — auto-found if null)")]
        [SerializeField] private ActionBarUI actionBarUI;
        [FormerlySerializedAs("objectiveBannerUI")]
        [Tooltip("PlayLandingReveal() plays at graduation (the objective 'lands in front of the player').")]
        [SerializeField] private ResourceDisplay resourceDisplay;
        [Tooltip("Used to resolve the newly-unlocked zone's centroid for the phase-16 camera pan.")]
        [SerializeField] private RegionManager regionManager;

        [Header("Hard Coded References for Ease")]
        [SerializeField] private ActionCategoryBar categoryBar;
        [SerializeField] private ActionStripView stripView;
        [SerializeField] private ActionEstimatePanel estimatePanel;
        
        [Header("Debug")]
        [Tooltip("Start from this index into the phase sequence (0 = normal start).")]
        [SerializeField] private int debugStartPhaseIndex = 0;

        // ─────────────────────────────────────────────────────────────────────
        // Phase sequence + preset table
        // ─────────────────────────────────────────────────────────────────────

        // The chronological order of the deck (the # column of the tutorial plan).
        private static readonly OnboardingBeatId[] s_Sequence =
        {
            // OnboardingBeatId.Phase_01_LoadingReveal,
            OnboardingBeatId.Phase_02_MeetAzi,
            OnboardingBeatId.Phase_03_Framing,
            OnboardingBeatId.Onboarding_Controls,
            OnboardingBeatId.Onboarding_Planting,
            OnboardingBeatId.Phase_04_ActionBar,
            OnboardingBeatId.Phase_05_PickCard,
            // OnboardingBeatId.Phase_06_SelectTiles,
            OnboardingBeatId.Phase_07_Confirm,
            OnboardingBeatId.Phase_WorkerSurprise,
            OnboardingBeatId.Onboarding_UI,
            // OnboardingBeatId.Phase_08_TimeStamina,
            // OnboardingBeatId.Phase_09_Weather,
            // OnboardingBeatId.Phase_10_ZoneHealth,
            // OnboardingBeatId.Phase_10_1_TraitPips,
            OnboardingBeatId.Phase_CleaningIntro,
            OnboardingBeatId.Onboarding_Cleaning,
            OnboardingBeatId.Phase_CleaningBar,
            OnboardingBeatId.Phase_CleaningCard,
            OnboardingBeatId.Phase_CleaningConfirm,
            OnboardingBeatId.Phase_EntityDeathIntro,
            OnboardingBeatId.Phase_EntityDeathPing,
            OnboardingBeatId.Phase_EndingIntro,
            OnboardingBeatId.Onboarding_Ending,
            OnboardingBeatId.Phase_11_GoalDeadline,
            // OnboardingBeatId.Phase_12_Stakes,
            // OnboardingBeatId.Phase_13_RoleAffirm,
            OnboardingBeatId.Phase_14_HelpAffordance,
            OnboardingBeatId.Phase_15_FreePlay,
            OnboardingBeatId.Phase_16_ZoneUnlock,
            OnboardingBeatId.Phase_ZoneUnlocked,
            OnboardingBeatId.Phase_17_Factory,
            OnboardingBeatId.Phase_FactoryIntro,
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
                case OnboardingBeatId.Phase_04_ActionBar:
                case OnboardingBeatId.Phase_05_PickCard:
                case OnboardingBeatId.Phase_06_SelectTiles:
                case OnboardingBeatId.Phase_07_Confirm:
                case OnboardingBeatId.Phase_08_TimeStamina:
                case OnboardingBeatId.Phase_09_Weather:
                case OnboardingBeatId.Phase_10_ZoneHealth:    
                case OnboardingBeatId.Phase_11_GoalDeadline:
                case OnboardingBeatId.Phase_12_Stakes:
                case OnboardingBeatId.Phase_13_RoleAffirm:
                case OnboardingBeatId.Phase_14_HelpAffordance:
                case OnboardingBeatId.Phase_15_FreePlay:
                case OnboardingBeatId.Phase_16_ZoneUnlock:
                case OnboardingBeatId.Phase_17_Factory:
                case OnboardingBeatId.Phase_18_Maintenance:
                case OnboardingBeatId.Phase_CleaningIntro:
                case OnboardingBeatId.Phase_CleaningBar:
                case OnboardingBeatId.Phase_CleaningCard:
                case OnboardingBeatId.Phase_CleaningConfirm:
                case OnboardingBeatId.Phase_EndingIntro:
                case OnboardingBeatId.Phase_WorkerSurprise:
                case OnboardingBeatId.Phase_EntityDeathIntro:
                    return PopupStyle.Dialog; // 1, 2, 3, 11, 12, 13

                // case OnboardingBeatId.Phase_04_ActionBar:
                // case OnboardingBeatId.Phase_05_PickCard:
                // case OnboardingBeatId.Phase_06_SelectTiles:
                // case OnboardingBeatId.Phase_07_Confirm:
                    // return PopupStyle.Text;
                
                case OnboardingBeatId.Onboarding_Controls:
                case OnboardingBeatId.Onboarding_Planting:
                case OnboardingBeatId.Onboarding_UI:
                case OnboardingBeatId.Onboarding_Cleaning:
                case OnboardingBeatId.Onboarding_Ending:
                case OnboardingBeatId.Phase_EntityDeathPing:
                case OnboardingBeatId.Phase_ZoneUnlocked:
                case OnboardingBeatId.Phase_FactoryIntro:
                    return PopupStyle.Handbook;
                    
                default: // 8, 9, 10, 10.1, 14, 16, 17, 18
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
        private bool _plantArmed;              // phase 5: Plant Trees armed (event)
        private bool _cleanupArmed;            // phase cleanup armed (event)
        private bool _plantConfirmed;          // phase 7: action confirmed (event)
        private bool _cleanConfirmed;     // phase cleanup confirmed (event)
        private bool _dragDone;           // phase 6: drag multi-select ≥ 3 (polled)
        private bool _regionUnlocked;     // phase 15: next zone unlocked (event)

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

            if (resourceDisplay == null) resourceDisplay = FindObjectOfType<ResourceDisplay>();
            if (resourceDisplay == null)
                Debug.LogWarning($"{name}: ResourceDisplay not found — graduation's landing-reveal tween will not play.", this);

            if (regionManager == null) regionManager = FindObjectOfType<RegionManager>();
            // regionManager is optional — phase 16's camera pan is best-effort.

            BuildPhaseLookup();

            if (!ok) { enabled = false; return; }

            // Meaning-event subscriptions (Law 2).
            if (RunManager.Instance != null)
                RunManager.Instance.OnRegionUnlocked += HandleRegionUnlocked;
            else
                Debug.LogError($"{name}: RunManager.Instance is null — phase 15 (free play) can never detect the zone unlock. " +
                               "Ensure RunManager is in the scene.", this);

            if (actionBarUI != null)
            {
                actionBarUI.OnActionArmed     += HandleActionArmed;
                actionBarUI.OnActionConfirmed += HandleActionConfirmed;
            }

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

            if (actionBarUI != null)
            {
                actionBarUI.OnActionArmed     -= HandleActionArmed;
                actionBarUI.OnActionConfirmed -= HandleActionConfirmed;
            }
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

            switch (CurrentBeat)
            {
                // case OnboardingBeatId.Phase_01_LoadingReveal:
                //     // Timer-authoritative: phase 1 lasts exactly loadingRevealSeconds. The world's
                //     // tile pop-in animates independently (RegionManager) and keeps playing past this.
                //     _loadingTimer += Time.unscaledDeltaTime;
                //     if (_loadingTimer >= loadingRevealSeconds) CompletePhase();
                //     break;

                case OnboardingBeatId.Phase_04_ActionBar:
                    // RefreshArmCueTarget();
                    if (actionBarUI != null && actionBarUI.IsStripOpen) CompletePhase();
                    break;

                case OnboardingBeatId.Phase_05_PickCard:
                    // RefreshArmCueTarget();     // re-point the arrow tab → card as the strip builds
                    if (_plantArmed) CompletePhase();
                    break;

                case OnboardingBeatId.Phase_06_SelectTiles:
                    // _dragGhostDelayTimer += Time.unscaledDeltaTime;
                    // if (_dragGhostDelayTimer >= dragGhostDelaySeconds) RefreshDragGhostByArmState();
                    // if (!_dragDone)
                    // {
                    //     TileSelector ts = GetTileSelector();
                    //     int need = Mathf.Max(3, _dragBaselineCount + 1);
                    //     if (ts != null && ts.IsFloodFillMode && ts.SelectedTileCount >= need)
                    //     {
                    //         _dragDone = true;
                    //         CompletePhase();
                    //     }
                    // }
                    break;

                case OnboardingBeatId.Phase_07_Confirm:
                    if (_plantConfirmed) CompletePhase();
                    break;
                
                case OnboardingBeatId.Phase_CleaningBar:
                    if (actionBarUI != null && actionBarUI.IsStripOpen) CompletePhase();
                    break;

                case OnboardingBeatId.Phase_CleaningCard:
                    if (stripView.AreButtonsInteractable()) stripView.SetButtonsUninteractable();
                    if (_cleanupArmed) CompletePhase();
                    break;
                
                case OnboardingBeatId.Phase_CleaningConfirm:
                    if (_cleanConfirmed) CompletePhase();
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
            _plantArmed = _cleanupArmed = _plantConfirmed = _cleanConfirmed = _dragDone = _regionUnlocked = false;
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
                // case OnboardingBeatId.Phase_01_LoadingReveal:
                //     // No popup — the world builds in front of the player (Update runs the timer).
                //     break;

                case OnboardingBeatId.Phase_04_ActionBar:
                    PresentPopup(id, advanceOnComplete: false);
                    categoryBar.DisableCleanup();
                    // _currentCueTarget = actionBarUI != null ? actionBarUI.GetArmCueRect(null) : null;
                    // ShowMark(new CoachMarkRequest
                    // {
                    //     kind              = CoachMarkKind.FidgetArrow,
                    //     trackTarget       = _currentCueTarget,   // Intervene tab
                    //     screenSpaceTarget = true,
                    // });
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
                        stripView.SetButtonsUninteractable();
                    }
                    // PresentPopup(id, advanceOnComplete: false);
                    // _currentCueTarget = actionBarUI != null ? actionBarUI.GetArmCueRect(plant) : null;
                    // ShowMark(new CoachMarkRequest
                    // {
                    //     kind              = CoachMarkKind.FidgetArrow,
                    //     trackTarget       = _currentCueTarget,   // Plant Trees card (or tab if not built yet)
                    //     screenSpaceTarget = true,
                    // });
                    break;
                }

                case OnboardingBeatId.Phase_06_SelectTiles:
                    // PresentPopup(id, advanceOnComplete: false);
                    // ShowMark(new CoachMarkRequest
                    // {
                    //     kind      = CoachMarkKind.CornerReminder,
                    //     labelText = OnboardingContent.Reminder_SelectMultiple,
                    // });
                    // GhostMouseDrag is arm-gated (and delayed dragGhostDelaySeconds after phase-enter)
                    // — RefreshDragGhostByArmState() shows it in Update.
                    break;

                case OnboardingBeatId.Phase_07_Confirm:
                    // PresentPopup(id, advanceOnComplete: false);
                    // ShowMark(new CoachMarkRequest
                    // {
                    //     kind              = CoachMarkKind.FidgetArrow,
                    //     trackTarget       = actionBarUI != null ? actionBarUI.GetConfirmButtonRect() : null,
                    //     screenSpaceTarget = true,
                    // });
                    estimatePanel.SetCancelInteractable(false);
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
                
                case OnboardingBeatId.Phase_CleaningBar:
                    PresentPopup(id, advanceOnComplete: false);
                    categoryBar.DisableIntervene();
                    break;

                case OnboardingBeatId.Phase_CleaningCard:
                    PlayerAction clean = FindCleanTrashAction();
                    if (actionBarUI != null && clean != null)
                    {
                        actionBarUI.RevealCategory(ActionCategory.Cleanup);
                        actionBarUI.LockCardsExcept(clean);
                    }
                    break;
                
                case OnboardingBeatId.Phase_CleaningConfirm:
                    estimatePanel.SetCancelInteractable(false);
                    break;
                
                case OnboardingBeatId.Phase_15_FreePlay:
                    // No UI. Free play until the player restores enough to unlock the next zone.
                    // (CornerReminder-on-stall is deferred — tutorial plan phase 15.)
                    break;

                case OnboardingBeatId.Phase_16_ZoneUnlock:
                    // Region framing is RegionRevealCameraFocus's job now (2026-07-28) — it fires on
                    // RegionManager.OnRegionGenerated, so EVERY unlock gets the pan, not just this
                    // one. Panning again here would restart the same tween mid-flight for no gain.
                    // The fallback stays so phase 16 still frames the zone if that component was
                    // never added to the camera.
                    if (!RegionRevealCameraFocus.IsActive
                        && EventCameraHandler.Instance != null
                        && TryGetNewRegionCentroid(out Vector3 centre))
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
            if (completed == OnboardingBeatId.Phase_CleaningCard)
                actionBarUI?.ClearCardLock(); 

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

            // if (objectiveBannerUI != null)
            //     objectiveBannerUI.PlayLandingReveal();
            // else
            //     Debug.LogWarning($"{name}: objectiveBannerUI is not wired — skipping the graduation landing-reveal tween.", this);

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
                _plantArmed = true;
            if (CurrentBeat == OnboardingBeatId.Phase_CleaningCard && IsCleanupAction(action))
                _cleanupArmed = true;
        }

        void HandleActionConfirmed()
        {
            // if(actionBarUI != null)
            //     actionBarUI.ShowCategories();
            
            if (CurrentBeat == OnboardingBeatId.Phase_07_Confirm)
                _plantConfirmed = true;
            if (CurrentBeat == OnboardingBeatId.Phase_CleaningConfirm)
            {
                Debug.Log("Action Confirmed");
                _cleanConfirmed = true;
            }
        }

        void HandleRegionUnlocked()
        {
            if (CurrentBeat == OnboardingBeatId.Phase_15_FreePlay)
                _regionUnlocked = true;
        }

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

        // Framing centre of the most-recently generated region — the centre of the AABB spanning its
        // tiles' extremes (2026-07-28: was the mean of tile positions, which pulled the camera toward
        // the densest lobe of a concave flood-fill region).
        // Best-effort: returns false (and the caller skips the pan) if it can't be resolved.
        // (Item F: formalised as the shared TileManager.TryGetRegionBounds helper; this just
        // resolves WHICH region is the just-generated one and delegates.)
        bool TryGetNewRegionCentroid(out Vector3 world)
        {
            world = Vector3.zero;
            TileManager tm = TileManager.Instance;
            if (tm == null || regionManager == null) return false;
            int newRegionId = regionManager.NextRegionID - 1;   // the just-generated region
            if (!tm.TryGetRegionBounds(newRegionId, out Bounds bounds)) return false;
            world = bounds.center;
            return true;
        }

        // The stable actionId phase 5 teaches. Falls back to the Plant Trees literal if a designer
        // clears the field, so the phase never silently soft-locks on an empty id.
        string TaughtActionId =>
            string.IsNullOrEmpty(plantTreesActionId) ? "plant_trees" : plantTreesActionId;
        
        string CleanupActionId =>
            string.IsNullOrEmpty(cleanupTrashActionId) ? "remove_trash" : cleanupTrashActionId;

        // Matches the taught action by its data-driven actionId (not a concrete C# type — every
        // authored action is a GenericPlayerAction, so `is PlantTreesAction` never matches).
        bool IsTaughtAction(PlayerAction action) =>
            action != null && action.ActionId == TaughtActionId;
        
        bool IsCleanupAction(PlayerAction action) =>
            action != null && action.ActionId == CleanupActionId;

        PlayerAction FindPlantTreesAction()
        {
            ActionManager am = ActionManager.Instance;
            if (am == null) return null;
            foreach (PlayerAction a in am.GetAvailableActions())
                if (IsTaughtAction(a)) return a;
            return null;
        }

        PlayerAction FindCleanTrashAction()
        {
            ActionManager am = ActionManager.Instance;
            if (am == null) return null;
            foreach (PlayerAction a in am.GetAvailableActions())
                if (IsCleanupAction(a)) return a;
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
//       Text phases:      4, 5, 6, 7
//       Character phases: 8, 9, 10, 10.1, 14, 16, 17, 18
//
// Highlight targets (optional coach marks)
//   • dayCounterTarget / weatherHexTarget / zoneHealthBarTarget / traitPipsTarget
//     — RectTransforms the phase-8/9/10/10.1 FidgetArrow points at. Leave null
//     to skip the arrow (the popup text still teaches the feature).
//   • fidgetArrowTuning — optional per-phase orbit overrides for the FidgetArrow
//     (angle 0-360 around the target + pivot / arrow-position x-y nudges). Add an
//     entry only for a phase whose arrow needs a different seat than the widget
//     default; phases with no entry use the FidgetArrow's own serialized values.
//
// References (auto-found if left null)
//   • actionBarUI       — required for interactive phases 4–7.
//   • objectiveBannerUI — graduation landing-reveal tween.
//   • regionManager     — phase-16 camera pan to the new zone (best-effort).
//
// DEFERRED (see ONBOARDING_HANDOFF.md, tracked separately):
//   • Phase 1: timer-authoritative — advances after loadingRevealSeconds. (Formerly gated on
//     RegionManager.OnInitialRegionRevealed / item D; that signal no longer drives advancement.)
//   • Phase 17 shows its popup only until item B guarantees the factory + adds the camera move.
//   • Phase 11 deadline text stays literal until item G adds token substitution.
//   • Skip control (item E — WIRED): SkipOnboarding() is the public entry point; the
//     hold-Space fill-bar UI lives in the separate OnboardingSkipControl component
//     (see its own Inspector-wiring block).
// =============================================================================
