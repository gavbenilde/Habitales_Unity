using System;
using System.Collections.Generic;
using UnityEngine;
using UTILITIES.Camera;
using Habitales.Dialogue;
using Habitales.UI;

namespace Habitales.Triggers
{
    // ─────────────────────────────────────────────────────────────────────────
    // TriggerManager.cs — code-subscription trigger layer (WO-3).
    //
    // Supersedes EventManager's data-driven trigger switch. All real event fires
    // in the current build are Manual (by-ID via Fire(string)), so TriggerManager
    // exposes Fire(id) as its primary entry point. Reactive hook stubs (§4 in
    // the handoff doc) are wired in Start/OnDestroy but have no auto-fire logic
    // yet — they are extension points for future authored triggers.
    //
    // IMPORTANT: Do NOT add a TriggerManager GameObject to any scene until WO-4
    // ships the EventManager shim. Running both managers simultaneously is benign
    // (TriggerManager has no data-driven auto-fire, so there is no double-fire
    // risk), but it is confusing and wastes resources. Keep TriggerManager inert
    // (no scene GameObject) until WO-4 explicitly wires it in.
    //
    // Parallel class — does NOT modify EventManager.cs.
    //
    // Batch-pause contract (mirrors EventManager / WO-2):
    //   • One PauseForEvent on the first Fire of a batch.
    //   • ShowPopup called with manageSimState:false (TriggerManager owns the pause).
    //   • One ResumeFromEvent when the queue drains.
    //
    // Added 2026-06-30 (WO-3).
    // ─────────────────────────────────────────────────────────────────────────

    [DefaultExecutionOrder(-100)] // manager — initializes after core services (arch §4 init order)
    public class TriggerManager : MonoBehaviour
    {
        // ─── Singleton ────────────────────────────────────────────────────────

        public static TriggerManager Instance { get; private set; }

        // ─── Serialized refs ──────────────────────────────────────────────────

        [Header("Data")]
        [Tooltip("Required. PopupSO catalog keyed by eventName. Loud-fails in Awake when null.")]
        [SerializeField] private PopupCatalogSO _catalog;

        [Tooltip("Required. Used to resolve Azi/Bob speaker identity for each popup's lines. Loud-fails in Awake when null.")]
        [SerializeField] private DialogueRegistry _registry;

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = true;

        [Header("Action Interrupt")]
        [Tooltip("When a popup batch starts while an action is mid-flight (e.g. the 'first_kaingin' " +
                 "village-fire event raised during day resolution), abort the running action at the " +
                 "day boundary before pausing for the event. Days already resolved keep their effects " +
                 "(see ActionManager.AbortCurrentAction). Uncheck to let the action keep running " +
                 "underneath the popup instead.")]
        [SerializeField] private bool abortRunningActionOnPopup = true;

        // ─── Runtime state ────────────────────────────────────────────────────

        /// <summary>
        /// True while at least one popup is in flight (the queue has entries OR
        /// a popup is currently being displayed). Replacement for EventManager.IsShowingEvent.
        /// </summary>
        public bool IsBusy { get; private set; } = false;

        // IDs that have been fired at least once this run (for fireOnce dedup).
        private readonly HashSet<string> _firedIds = new HashSet<string>();

        // Pending popups that have not yet been presented.
        private readonly Queue<PopupSO> _queue = new Queue<PopupSO>();

        // True while RunManager is resolving a day (2026-07-08 control-inversion): Fire()
        // calls collect into _queue instead of presenting mid-tick; RunManager drains them
        // as an explicit ordered heartbeat step (after visuals + the game-over check).
        private bool _deferFires = false;

        // Track whether we panned the camera during this batch so we can return.
        private bool _pannedThisBatch = false;

        // Handle for the popup currently on screen (so HandleGameOver can dismiss it).
        private PopupHandle _currentPopup = PopupHandle.None;

        // ─── Lifecycle ────────────────────────────────────────────────────────

        void Awake()
        {
            // Singleton guard.
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Law 3 loud-fail for serialized refs.
            if (_catalog == null)
                Debug.LogError("TriggerManager: _catalog is not assigned in the Inspector — Fire() will always fail.", this);
            if (_registry == null)
                Debug.LogError("TriggerManager: _registry is not assigned in the Inspector — Azi/Bob names will fall back to literals.", this);
        }

        void Start()
        {
            // ── Reactive hook subscriptions ────────────────────────────────────
            // Subscribe to ResourceManager for token refresh (RefreshGlobalContext)
            // and game-over cleanup (HandleGameOver).
            ResourceManager rm = ResourceManager.Instance;
            if (rm != null)
                rm.OnTimeAdvanced += RefreshGlobalContext;
            else
                Debug.LogError("TriggerManager: ResourceManager.Instance is null in Start — context tokens won't refresh.", this);

            // Game-over signal moved to RunManager (2026-07-08 run-end rework) — RunManager
            // is now the sole authority on when a run ends; ResourceManager.OnGameOver is gone.
            if (RunManager.Instance != null)
                RunManager.Instance.OnGameOverTriggered += HandleGameOver;
            else
                Debug.LogError("TriggerManager: RunManager.Instance is null in Start — the popup queue won't clear on game over.", this);

            // HOOK: OnRegionGenerated — subscribe for future reactive triggers tied
            // to region unlock events. No auto-fire logic here; author triggers in code.
            RegionManager zm = RegionManager.Instance;
            if (zm != null)
                zm.OnRegionGenerated += HandleRegionGenerated;
            else
                Debug.LogWarning("TriggerManager: RegionManager.Instance is null — OnRegionGenerated hook won't fire.", this);

            // Prime the global token set immediately so tokens are available on day 0.
            RefreshGlobalContext(0);
        }

        void OnDestroy()
        {
            if (ResourceManager.Instance != null)
                ResourceManager.Instance.OnTimeAdvanced -= RefreshGlobalContext;

            if (RunManager.Instance != null)
                RunManager.Instance.OnGameOverTriggered -= HandleGameOver;

            if (RegionManager.Instance != null)
                RegionManager.Instance.OnRegionGenerated -= HandleRegionGenerated;
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Fire a popup by its catalog ID (<c>PopupSO.eventName</c>).
        /// Replaces <c>EventManager.FireEventByID</c>. Safe to call while a popup
        /// is already showing — the new one is queued and plays after the current one dismisses.
        /// </summary>
        public void Fire(string id)
        {
            if (_catalog == null)
            {
                Debug.LogWarning($"TriggerManager.Fire('{id}'): _catalog is null — cannot look up popup.", this);
                return;
            }

            PopupSO popup = _catalog.GetById(id);
            if (popup == null)
                return; // GetById already logged the warning

            // fireOnce dedup — skip if already fired this run.
            if (popup.fireOnce && _firedIds.Contains(id))
            {
                if (showDebugInfo)
                    Debug.Log($"TriggerManager: '{id}' skipped (fireOnce already fired).");
                return;
            }

            _queue.Enqueue(popup);

            // While a day is resolving, fires only COLLECT — RunManager drains the queue as
            // an explicit heartbeat step once the day has settled (visuals refreshed,
            // game-over checked). Outside that window, present immediately as before.
            if (!IsBusy && !_deferFires)
                ShowNextInQueue();
            // If IsBusy, the new entry sits in the queue and will play after the current one.
        }

        /// <summary>
        /// Opens the per-day collection window (called by RunManager at the top of each
        /// resolved day). Until <see cref="DrainDeferredFires"/>, Fire() enqueues silently.
        /// </summary>
        public void BeginDayResolution()
        {
            _deferFires = true;
        }

        /// <summary>
        /// Closes the collection window and presents whatever the day queued (called by
        /// RunManager as the last heartbeat step). No-ops when the queue is empty or a
        /// batch is already on screen — and naturally no-ops after a game over, because
        /// HandleGameOver has already cleared the queue.
        /// </summary>
        public void DrainDeferredFires()
        {
            _deferFires = false;
            if (!IsBusy && _queue.Count > 0)
                ShowNextInQueue();
        }

        /// <summary>
        /// Clears fired-ID history, the pending queue, and camera-pan state.
        /// Call this at the start of a new run.
        /// </summary>
        public void ResetForNewRun()
        {
            _firedIds.Clear();
            _queue.Clear();
            _pannedThisBatch = false;
            _deferFires = false;
        }

        // ─── Queue management ─────────────────────────────────────────────────

        private void ShowNextInQueue()
        {
            if (_queue.Count == 0)
            {
                IsBusy = false;

                if (showDebugInfo)
                    Debug.Log("TriggerManager: Queue empty — resuming game.");

                if (_pannedThisBatch && EventCameraHandler.Instance != null)
                {
                    _pannedThisBatch = false;
                    EventCameraHandler.Instance.ReturnToOrigin(
                        onComplete: () => RunManager.Instance?.ResumeFromEvent()
                    );
                }
                else
                {
                    RunManager.Instance?.ResumeFromEvent();
                }

                return;
            }

            FirePopup(_queue.Dequeue());
        }

        private void FirePopup(PopupSO p)
        {
            if (showDebugInfo)
                Debug.Log($"TriggerManager: Firing '{p.eventName}' ({_queue.Count} remaining in queue).");

            // Record fireOnce before we show (consistent with EventManager's approach).
            if (p.fireOnce)
                _firedIds.Add(p.eventName);

            // Batch pause — one pause for the whole queue, not per-popup.
            if (!IsBusy)
            {
                // Mid-action interrupt: a popup batch starting while an action is running (e.g. the
                // "first_kaingin" village fire event raised during day resolution) must reclaim the
                // workforce before the pause takes hold, or ActionManager's entry-gate just blocks the
                // NEXT action while this one keeps ticking underneath the popup. Abort at the day
                // boundary — days already resolved keep their applied effects (see
                // ActionManager.AbortCurrentAction for the full contract).
                if (abortRunningActionOnPopup && ActionManager.Instance != null && ActionManager.Instance.IsActionRunning)
                {
                    Debug.Log("TriggerManager: event popup interrupted the running action — aborted at the day boundary.");
                    if (RunManager.Instance != null)
                        RunManager.Instance.AbortCurrentAction();
                    else
                        Debug.LogError("TriggerManager: RunManager.Instance is null — cannot abort the running action before this popup pauses the sim.", this);

                    // The abort fires OnActionCompleted → RunManager.EvaluateGameOver — which can
                    // end the run RIGHT HERE (an aborted final-day/collapsed-world action). Game
                    // over trumps this popup: HandleGameOver (via OnGameOverTriggered) has already
                    // cleared the queue; drop the dequeued popup and let the end flow own the screen.
                    if (RunManager.Instance != null && RunManager.Instance.IsGameOver)
                    {
                        Debug.Log($"TriggerManager: '{p.eventName}' dropped — the aborted action ended the run; the end flow takes over.");
                        return;
                    }
                }

                if (RunManager.Instance != null)
                    RunManager.Instance.PauseForEvent();
                else
                    Debug.LogError("TriggerManager: RunManager.Instance is null — cannot pause the simulation for this popup batch.", this);
            }
            IsBusy = true;

            // Resolve lines and apply EventContext token substitution.
            List<ResolvedLine> lines = p.ResolveLines(_registry);
            foreach (ResolvedLine l in lines)
                l.body = EventContext.Resolve(l.body);

            // Read and clear the focus target BEFORE deciding whether to pan.
            Vector3? focusTarget = EventContext.GetFocusTarget();
            EventContext.ClearOverrides(); // clears token overrides AND focus target

            // Append the linked dialogue thread (if any) to DialogueManager.
            if (p.linkedThread != null)
                DialogueManager.Instance?.AppendConversation(p.linkedThread);

            // Build the presentation action as a local closure so it can be called
            // directly OR from the camera pan's onComplete callback.
            Action showPopup = () =>
            {
                var ui = UIManager.Instance;
                if (ui == null)
                {
                    Debug.LogError("TriggerManager: UIManager.Instance is null — cannot present popup. " +
                                   "Resuming queue so it doesn't stall.", this);
                    ResumeAfterEvent();
                    return;
                }

                var request = new PopupRequest
                {
                    intrusiveness      = p.intrusiveness,
                    lines              = lines,
                    confirmLabel       = "OK",
                    onConfirm          = ResumeAfterEvent,   // advances the queue on dismiss
                    autoDismissSeconds = 0f,
                    anchor             = ScreenAnchor.BottomCenter
                };

                // manageSimState:false — TriggerManager owns the batch pause,
                // exactly like EventManager. The hub must NOT double-toggle it.
                _currentPopup = ui.ShowPopup(request, manageSimState: false);
            };

            // Camera pan (optional).
            if (p.focusCameraOnTarget && focusTarget.HasValue && EventCameraHandler.Instance != null)
            {
                _pannedThisBatch = true;
                EventCameraHandler.Instance.PanTo(focusTarget.Value, onComplete: showPopup);
            }
            else
            {
                showPopup();
            }
        }

        private void ResumeAfterEvent()
        {
            if (showDebugInfo)
                Debug.Log("TriggerManager: Popup dismissed — checking queue...");
            ShowNextInQueue();
        }

        // ─── Hook handlers ────────────────────────────────────────────────────

        // HOOK: OnRegionGenerated — wired in Start.
        // Author reactive popup triggers here in future work-orders (code-subscription model).
        // Do NOT auto-fire from the old data-driven trigger-collection loop.
        private void HandleRegionGenerated(RegionGenerationResult result)
        {
            // HOOK: reactive triggers for region events go here.
        }

        private void HandleGameOver()
        {
            _queue.Clear();
            _pannedThisBatch = false;

            if (IsBusy)
            {
                // Dismiss the active popup through the hub (keeps PopupController state clean).
                UIManager.Instance?.Popups?.Dismiss(_currentPopup);
                _currentPopup = PopupHandle.None;

                // Also hide any lingering threaded-dialogue surfaces.
                NarrativePopupManager.Instance?.HideAll();
            }

            IsBusy = false;
        }

        // ─── Context token refresh ────────────────────────────────────────────

        // Keeps global EventContext tokens fresh each day. Ported verbatim from
        // EventManager.RefreshGlobalContext so token-interpolated popup bodies
        // work identically under TriggerManager.
        private void RefreshGlobalContext(int _ = 0)
        {
            ResourceManager rm = ResourceManager.Instance;
            if (rm == null) return;

            int year = rm.TotalDays / rm.DaysPerYear + 1;
            int day  = rm.TotalDays % rm.DaysPerYear + 1;

            EventContext.Set("current_year",       year.ToString());
            EventContext.Set("current_day",        day.ToString());
            EventContext.Set("available_workers",  rm.AvailablePeople.ToString());
            EventContext.Set("total_workers",      rm.TotalPeople.ToString());
            EventContext.Set("recovering_workers", rm.RecoveringPeopleCount.ToString());

            RegionManager zm = RegionManager.Instance;
            if (zm != null)
            {
                EventContext.Set("zone_count",   (zm.NextRegionID - 1).ToString());
                EventContext.Set("world_health", zm.GetTotalAverageHealth().ToString("F1"));
            }
        }
    }
}
