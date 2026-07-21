using System;
using System.Collections.Generic;
using UnityEngine;
using UTILITIES.Camera;
using Habitales.Dialogue;
using Habitales.Entities;
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

        [Header("Azi Tier Content")]
        [Tooltip("Required to resolve tier1Intro (T1) / tier4JournalEntry (T4) AuthoredContent " +
                 "(FirstStageInChain / FromEntitySO). Unwired -> tier popups fall back to raw " +
                 "Default text only (no chain/borrow resolution) and the Journal falls back to a " +
                 "generic line.")]
        [SerializeField] private EntityRegistry _tierEntityRegistry;

        [Tooltip("Required to clear the window-scoped T3sp/T4 dedup store at each check-in " +
                 "boundary. Loud-fails in Start when null — without it, a species' non-intrusive " +
                 "death bubble and Journal entry only ever fire ONCE for the whole run.")]
        [SerializeField] private CheckInScheduler _checkInScheduler;

        [Tooltip("Auto-dismiss seconds for non-intrusive Azi tier bubbles (T1/T2/T3-later). Keeps " +
                 "the tier queue from stalling if the player never taps. Ignored for intrusive " +
                 "T3-first-of-cause popups (they always wait for the confirm button).")]
        [SerializeField] private float _tierNonIntrusiveAutoDismiss = 6f;

        // ─── Runtime state ────────────────────────────────────────────────────

        /// <summary>
        /// True while at least one popup is in flight (the queue has entries OR
        /// a popup is currently being displayed). Replacement for EventManager.IsShowingEvent.
        /// </summary>
        public bool IsBusy { get; private set; } = false;

        // IDs that have been fired at least once this run (for fireOnce dedup). Reused by the
        // Azi tier path for its run-scoped keys: "T1:{entityId}", "T2:{entityId}",
        // "T3cause:{cause}" — a plain HashSet<string>, so arbitrary prefixed keys coexist
        // safely with PopupSO.eventName entries.
        private readonly HashSet<string> _firedIds = new HashSet<string>();

        // Window-scoped dedup store — ONE mechanism, two key prefixes:
        // "T3sp:{entityId}" (later same-cause deaths, once per species per check-in window) and
        // "T4:{entityId}" (Journal logging, same cadence). Cleared by CheckInScheduler's
        // OnCheckInCompleted (see HandleCheckInCompleted) — never persisted.
        private readonly HashSet<string> _windowFiredIds = new HashSet<string>();

        // Pending popups that have not yet been presented.
        private readonly Queue<PopupSO> _queue = new Queue<PopupSO>();

        // Azi tier queue — a SECOND, independent dispatch path from the PopupSO/_queue path
        // above. Tier content is code-built (T2/T3 templates) or
        // SO-authored (T1/T4 AuthoredContent) directly into a PopupRequest, never a catalog
        // asset, so it does not share FirePopup's unconditional per-batch pause (which would
        // incorrectly pause the sim for T1/T2/T3-later's NonIntrusive bubbles). UIManager.ShowPopup
        // (manageSimState:true, the default) owns pause per-request instead — only Intrusive
        // requests (T3-first-of-cause) pause. Still rides the SAME _deferFires collection window
        // as the scripted path, so a tier popup never presents mid-day-resolution.
        private readonly Queue<PopupRequest> _tierQueue = new Queue<PopupRequest>();
        private bool _tierBusy = false;

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

            if (_tierEntityRegistry == null)
                Debug.LogWarning("TriggerManager: _tierEntityRegistry is not assigned — Azi tier content (T1 tier1Intro / T4 tier4JournalEntry) will fall back to raw Default text only, no FirstStageInChain/FromEntitySO resolution.", this);
            if (_checkInScheduler == null)
                Debug.LogError("TriggerManager: _checkInScheduler is not assigned — the window-scoped T3sp/T4 dedup store will never clear, so a species' later-death bubble and Journal entry only ever fire once per run instead of once per check-in window.", this);
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

            // ── Azi tier hooks ──────────────────────────────────────────────────
            TileSelector ts = TileSelector.Instance;
            if (ts != null)
                ts.OnTileSelected += HandleTileSelectedForTier;
            else
                Debug.LogError("TriggerManager: TileSelector.Instance is null in Start — T1 tier popups won't fire.", this);

            if (RunManager.Instance != null)
                RunManager.Instance.OnTileTierChanged += HandleTileTierChangedForTier;
            // (RunManager.Instance null already logged above for OnGameOverTriggered.)

            if (TileManager.Instance != null)
                TileManager.Instance.OnEntityDied += HandleEntityDiedForTier;
            else
                Debug.LogError("TriggerManager: TileManager.Instance is null in Start — T3 tier popups and Journal logging won't fire.", this);

            if (_checkInScheduler != null)
                _checkInScheduler.OnCheckInCompleted += HandleCheckInCompleted;
        }

        void OnDestroy()
        {
            if (ResourceManager.Instance != null)
                ResourceManager.Instance.OnTimeAdvanced -= RefreshGlobalContext;

            if (RunManager.Instance != null)
            {
                RunManager.Instance.OnGameOverTriggered -= HandleGameOver;
                RunManager.Instance.OnTileTierChanged -= HandleTileTierChangedForTier;
            }

            if (RegionManager.Instance != null)
                RegionManager.Instance.OnRegionGenerated -= HandleRegionGenerated;

            if (TileSelector.Instance != null)
                TileSelector.Instance.OnTileSelected -= HandleTileSelectedForTier;

            if (TileManager.Instance != null)
                TileManager.Instance.OnEntityDied -= HandleEntityDiedForTier;

            if (_checkInScheduler != null)
                _checkInScheduler.OnCheckInCompleted -= HandleCheckInCompleted;
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
            if (!_tierBusy && _tierQueue.Count > 0)
                ShowNextTierPopup();
        }

        /// <summary>
        /// Clears fired-ID history, the pending queue, and camera-pan state.
        /// Call this at the start of a new run.
        /// </summary>
        public void ResetForNewRun()
        {
            _firedIds.Clear();
            _windowFiredIds.Clear();
            _queue.Clear();
            _tierQueue.Clear();
            _tierBusy = false;
            _pannedThisBatch = false;
            _deferFires = false;
        }

        /// <summary>Window boundary — clears the T3sp/T4 dedup store so the next check-in window
        /// starts fresh. Subscribed to CheckInScheduler.OnCheckInCompleted.</summary>
        private void HandleCheckInCompleted()
        {
            _windowFiredIds.Clear();
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
                    anchor             = ScreenAnchor.BottomCenter,
                    position           = new Vector2(p.posX, p.posY)   // used only when intrusiveness == Positioned
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
                PopupManager.Instance?.HideAll();
            }

            IsBusy = false;
        }

        // ─── Azi tier popups (T1–T3) — second dispatch path ──────────────────────────────────

        private static readonly HashSet<string> DeathCauses = new HashSet<string> { "drought", "flood", "environment" };

        /// <summary>
        /// T1 — first inspect of a species (TileSelector.OnTileSelected). Non-intrusive Azi
        /// bubble with the SO's resolved tier1Intro. Dedup "T1:{entityId}" per run.
        /// </summary>
        private void HandleTileSelectedForTier(Tile tile, Vector3 worldPos)
        {
            TileEntitySO def = tile?.entity?.def;
            if (def == null) return;

            string key = $"T1:{def.EntityId}";
            if (_firedIds.Contains(key)) return;
            _firedIds.Add(key);

            string text = ResolveAuthoredText(def, so => so.tier1Intro);
            if (string.IsNullOrEmpty(text)) return; // unauthored — TileEntitySO.OnValidate already screamed for Plant SOs

            EnqueueTierPopup(BuildAziRequest(text, PopupIntrusiveness.NonIntrusive));
        }

        /// <summary>
        /// T2 — a tile carrying a Plant crosses DOWNWARD into Critical (RunManager.OnTileTierChanged).
        /// Non-intrusive Azi bubble, code-template text. Dedup "T2:{entityId}" per run.
        /// </summary>
        private void HandleTileTierChangedForTier(Tile tile, Tier oldTier, Tier newTier)
        {
            if (newTier != Tier.Critical) return; // only the downward crossing INTO Critical matters
            TileEntitySO def = tile?.entity?.def;
            if (def == null || def.category != EntityCategory.Plant) return;

            string key = $"T2:{tile.entity.entityId}";
            if (_firedIds.Contains(key)) return;
            _firedIds.Add(key);

            string text = $"This {def.displayName} is dying — it won't survive much longer without help.";
            EnqueueTierPopup(BuildAziRequest(text, PopupIntrusiveness.NonIntrusive));
        }

        /// <summary>
        /// T3 — a plant died with a tracked cause (TileManager.OnEntityDied). First death by that
        /// cause this run -> INTRUSIVE popup (dedup "T3cause:{cause}" per run; also stamps the
        /// species' window key so one death never produces two popups) + one-shot ping. Later
        /// deaths -> non-intrusive bubble (dedup "T3sp:{entityId}" per check-in window). Also
        /// logs the T4 Journal entry, gated by "T4:{entityId}" per check-in window, and pings
        /// the death tile regardless of the intrusive/later split (a later death gets no popup
        /// to draw the eye, so the ping is its only spatial signal).
        /// </summary>
        private void HandleEntityDiedForTier(Tile tile, string entityId, string cause)
        {
            if (tile == null || string.IsNullOrEmpty(entityId)) return;
            if (string.IsNullOrEmpty(cause) || !DeathCauses.Contains(cause)) return; // player removal / decomposition — never T3

            string speciesName = ResolveSpeciesNameForTier(tile, entityId);

            if (TileManager.Instance != null)
                PingDirector.Instance?.PingAt(TileManager.Instance.GridToWorldPosition(tile.gridPosition));

            string causeKey = $"T3cause:{cause}";
            if (!_firedIds.Contains(causeKey))
            {
                _firedIds.Add(causeKey);
                _windowFiredIds.Add($"T3sp:{entityId}"); // one death never makes two popups

                string text = BuildCauseExplanationText(cause, speciesName, tile);
                EnqueueTierPopup(BuildAziRequest(text, PopupIntrusiveness.Intrusive));
            }
            else
            {
                string spKey = $"T3sp:{entityId}";
                if (!_windowFiredIds.Contains(spKey))
                {
                    _windowFiredIds.Add(spKey);
                    string text = $"Another {speciesName} died — {CauseShortPhrase(cause)}.";
                    EnqueueTierPopup(BuildAziRequest(text, PopupIntrusiveness.NonIntrusive));
                }
            }

            string journalKey = $"T4:{entityId}";
            if (!_windowFiredIds.Contains(journalKey))
            {
                _windowFiredIds.Add(journalKey);
                LogJournalEntry(entityId, cause, speciesName);
            }
        }

        /// <summary>Best-effort species display name for a just-died entityId. Prefers the tile's
        /// still-attached entity (true for the KillAndTransform paths — drought/flood/TransformTo
        /// environment deaths fire OnEntityDied BEFORE the swap) and falls back to a registry
        /// lookup (needed for the RemoveEntity-outcome environment path, where tile.entity is
        /// already null by the time this fires) then the raw id as a last resort.</summary>
        private string ResolveSpeciesNameForTier(Tile tile, string entityId)
        {
            if (tile?.entity?.def != null && tile.entity.entityId == entityId)
                return tile.entity.def.displayName;

            if (_tierEntityRegistry != null)
            {
                TileEntitySO def = _tierEntityRegistry.Get(entityId);
                if (def != null) return def.displayName;
            }

            return entityId;
        }

        /// <summary>CODE-ONLY template text for the T3-intrusive popup (T2/T3 text is never
        /// authored). "environment" names the tile's FIRST issue via the small IssueType ->
        /// display-string map, falling back to a generic line when the issue list is empty.</summary>
        private string BuildCauseExplanationText(string cause, string speciesName, Tile tile)
        {
            switch (cause)
            {
                case "drought":
                    return $"The {speciesName} didn't make it — a long dry spell drained the soil past what it could " +
                           "survive. Drought builds slowly and often stays hidden until it's too late; keep an eye on " +
                           "long dry stretches. I've logged what happened in the Journal.";
                case "flood":
                    return $"The {speciesName} died in the storm — heavy rain and flooding can drown roots and wash " +
                           "away everything holding a tile together. I've logged what happened in the Journal.";
                case "environment":
                    return $"Oh no, the {speciesName} died fast. {ResolveFirstIssueText(tile)} I've written it up in " +
                           "the Journal so you can check back on it.";
                default:
                    return $"The {speciesName} died. I've made a note in the Journal.";
            }
        }

        private static string ResolveFirstIssueText(Tile tile)
        {
            if (tile?.issues == null || tile.issues.Count == 0)
                return "The tile there just couldn't support it anymore.";
            return $"The tile there seems to have {IssueDisplayName(tile.issues[0].type)}.";
        }

        /// <summary>Small IssueType -> display-string map (TileIssue has no display name today,
        /// so this is a separate small code map rather than a TileIssue edit).</summary>
        private static string IssueDisplayName(IssueType type) => type switch
        {
            IssueType.LoggedTrees             => "Logged Trees",
            IssueType.NutrientDepletion       => "Nutrient Depletion",
            IssueType.HeavyMetalContamination => "Heavy Metal Contamination",
            IssueType.ActiveErosion           => "Active Erosion",
            IssueType.DrainageCollapse        => "Drainage Collapse",
            IssueType.SoilCompaction          => "Soil Compaction",
            IssueType.ChemicalBurnout         => "Chemical Burnout",
            _                                 => "an unidentified issue"
        };

        private static string CauseShortPhrase(string cause) => cause switch
        {
            "drought"     => "drought",
            "flood"       => "flooding",
            "environment" => "the soil couldn't support it",
            _             => "unknown causes"
        };

        /// <summary>Resolves an AuthoredContent field with the registry when available (needed for
        /// FirstStageInChain/FromEntitySO); falls back to raw Default text only when unwired.</summary>
        private string ResolveAuthoredText(TileEntitySO def, System.Func<TileEntitySO, AuthoredContent> selector)
        {
            if (def == null || selector == null) return null;
            if (_tierEntityRegistry != null)
                return _tierEntityRegistry.ResolveContent(def, selector);

            AuthoredContent content = selector(def);
            return content != null && content.source == AuthoredContent.Source.Default ? content.text : null;
        }

        /// <summary>T4 — logs a Journal entry (stream B's JournalStore), gated by the caller's
        /// "T4:{entityId}" window key. Falls back to a generic line when tier4JournalEntry can't
        /// resolve (unauthored non-Plant SO, missing registry, …) so the Journal never shows a
        /// blank card.</summary>
        private void LogJournalEntry(string entityId, string cause, string speciesName)
        {
            if (JournalStore.Instance == null)
            {
                Debug.LogWarning("TriggerManager: JournalStore.Instance is null — T4 journal entry dropped (no JournalStore in the scene).", this);
                return;
            }

            TileEntitySO def = _tierEntityRegistry != null ? _tierEntityRegistry.Get(entityId) : null;
            string text = def != null ? ResolveAuthoredText(def, so => so.tier4JournalEntry) : null;
            if (string.IsNullOrEmpty(text))
                text = $"{speciesName} didn't make it this time — {CauseShortPhrase(cause)}.";

            int day = ResourceManager.Instance != null ? ResourceManager.Instance.TotalDays : 0;

            JournalStore.Instance.LogEntry(new JournalEntry
            {
                entityId    = entityId,
                displayName = speciesName,
                text        = text,
                cause       = cause,
                day         = day
            });
        }

        /// <summary>Builds a single-line Azi PopupRequest. NonIntrusive gets the tunable
        /// auto-dismiss (so the tier queue can't stall on an ignored bubble); Intrusive ignores it
        /// (always waits for the confirm button, matching PopupController's contract).</summary>
        private PopupRequest BuildAziRequest(string body, PopupIntrusiveness intrusiveness)
        {
            var line = new ResolvedLine
            {
                speakerID   = "Azi",
                displayName = _registry != null ? _registry.GetAziDisplayName() : "Azi",
                portrait    = _registry != null ? _registry.GetAziPortrait() : null,
                body        = body
            };

            return new PopupRequest
            {
                intrusiveness      = intrusiveness,
                lines              = new List<ResolvedLine> { line },
                confirmLabel       = "OK",
                onConfirm          = null,
                autoDismissSeconds = intrusiveness == PopupIntrusiveness.NonIntrusive ? _tierNonIntrusiveAutoDismiss : 0f,
                anchor             = ScreenAnchor.BottomCenter
            };
        }

        /// <summary>Enqueues a tier PopupRequest. Respects the SAME day-resolution defer window
        /// (_deferFires) as the scripted PopupSO path — presents immediately only when outside a
        /// day resolution AND no other tier popup is currently showing.</summary>
        private void EnqueueTierPopup(in PopupRequest request)
        {
            _tierQueue.Enqueue(request);
            if (!_tierBusy && !_deferFires)
                ShowNextTierPopup();
        }

        private void ShowNextTierPopup()
        {
            if (_tierQueue.Count == 0)
            {
                _tierBusy = false;
                return;
            }

            PopupRequest req = _tierQueue.Dequeue();
            _tierBusy = true;

            var ui = UIManager.Instance;
            if (ui == null)
            {
                Debug.LogError("TriggerManager: UIManager.Instance is null — cannot present a tier popup. Dropping it and continuing the tier queue.", this);
                _tierBusy = false;
                ShowNextTierPopup();
                return;
            }

            // Wrap onConfirm so the tier queue advances once THIS popup is dismissed — mirrors
            // FirePopup/ResumeAfterEvent for the scripted path, but scoped to _tierBusy only.
            Action originalConfirm = req.onConfirm;
            req.onConfirm = () =>
            {
                originalConfirm?.Invoke();
                _tierBusy = false;
                ShowNextTierPopup();
            };

            // manageSimState left at its default (true) — UIManager paces the pause itself, only
            // for Intrusive requests. This is the second-dispatch-path distinction from FirePopup's
            // unconditional per-batch pause (see the _tierQueue field doc above).
            ui.ShowPopup(req);
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
