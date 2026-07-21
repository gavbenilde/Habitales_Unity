using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Habitales.Dialogue;
using Habitales.UI.Actions;

/// <summary>
/// Central game loop coordinator.  
/// Handles: Action execution → Tile cascading → Zone health checks → Zone generation triggers
/// </summary>
[DefaultExecutionOrder(-100)] // orchestrator — initializes after core services (arch §4 init order)
public class RunManager : MonoBehaviour {
    [Header("System References")]
    [SerializeField] private TileManager tileManager;
    [SerializeField] private TileSelector tileSelector;
    [SerializeField] private ActionBarUI actionBarUI;
    [SerializeField] private ActionManager actionManager;
    [UnityEngine.Serialization.FormerlySerializedAs("zoneManager")]
    [SerializeField] private RegionManager regionManager;
    [SerializeField] private DialogueManager dialogueManager;
    [SerializeField] private ResourceManager resourceManager;
    [SerializeField] private RegionOutlineRenderer regionOutlineRenderer;
    
    [SerializeField] private EndGameScreenUI     endGameScreenUI;
    [SerializeField] private Habitales.Meta.RunEndCoordinator runEndCoordinator; // owns XP/level-up/persistence

    [Header("Run End Conversation")]
    [Tooltip("The scripted Azi/Bob conversation shown when the field season completes, before the end report. " +
             "Variant selection reuses the check-in comparators with end-of-run semantics: 'improved' = final " +
             "thriving tile count, 'decayed' = final critical tile count, days-left is real (0 unless the run " +
             "ended past the cap). Unwired → LogError, End Report shows directly.")]
    [SerializeField] private CheckInConversationSO seasonEndConversation;
    [Tooltip("Same as above but for the Ecosystem Collapse ending. Unwired → LogError, End Report directly.")]
    [SerializeField] private CheckInConversationSO collapseConversation;

    
    [Header("Cascade Settings")]
    [SerializeField] [Range(0.05f, 0.5f)] private float diffusionRate = 0.15f;
    [SerializeField] [Tooltip("How many cascade iterations per action")]
    private int cascadeIterations = 3;
    
    [Header("Zone Progression")]
    [UnityEngine.Serialization.FormerlySerializedAs("zoneUnlockThreshold")]
    [SerializeField] [Range(0f, 100f)] private float regionUnlockThreshold = 80f;

    /// <summary>World-health % at which the next region becomes unlockable. Single source of truth for the health-bar marker and the unlock button.</summary>
    public float ZoneUnlockThreshold => regionUnlockThreshold;

    /// <summary>
    /// Length of the SHORT recent window the trend arrows average change over (world here,
    /// regions in RegionManager). This is a responsiveness knob for the arrow only — it is
    /// deliberately NOT the full-run <c>healthHistory</c> progress record. Keep it small so the
    /// arrow reacts to recent play; the progress graph reads the whole history separately.
    /// </summary>
    public const int TrendWindowDays = 3;

    /// <summary>
    /// Smoothed per-day change in world-average health, in health-points: the average daily
    /// change over the last <see cref="TrendWindowDays"/> days (or fewer early in the run —
    /// averaging daily deltas telescopes to (newest − oldest) / span). 0 until at least two
    /// days have resolved. Read-only (Law 1) — drives the HUD trend arrow. Peeks only the tail
    /// of <c>healthHistory</c>; the full list stays the run-progress record (see EndGameData).
    /// </summary>
    public float WorldHealthTrend
    {
        get
        {
            int n = healthHistory.Count;
            if (n < 2) return 0f;
            int span = Mathf.Min(TrendWindowDays, n - 1);
            return (healthHistory[n - 1] - healthHistory[n - 1 - span]) / span;
        }
    }

    /// <summary>Fired once when world health first crosses the threshold and a region is awaiting manual unlock. Subscribers: unlock button, objective banner.</summary>
    public event System.Action OnRegionUnlockReady;
    /// <summary>Fired after the player presses the button and the next region is generated.</summary>
    public event System.Action OnRegionUnlocked;

    // ── Heartbeat seams (arch §6 HOOK) ──────────────────────────────────────────
    /// <summary>The universal per-day push — fires after each day fully resolves (data settled, visuals refreshed). arg: the day that just resolved.</summary>
    public event System.Action<int> OnDayResolved;
    /// <summary>Fires when a tile CROSSES a health tier (Critical/Degraded/Thriving). Meaning-event (Law 2) — on crossing only, not every day. args: tile, old tier, new tier.</summary>
    public event System.Action<Tile, Tier, Tier> OnTileTierChanged;

    // True once the threshold is crossed and we are waiting on the player's button press.
    private bool regionUnlockPending = false;

    /// <summary>True while a region unlock is earned but not yet claimed (Law 1 read).
    /// The progress bar latches at 100% while this is set, even if health decays back
    /// below the threshold — mirrors the mechanic (the button stays available too).</summary>
    public bool RegionUnlockPending => regionUnlockPending;

    // The real meaning-event sink threaded into every per-day TickContext (arch §5.2 / §6.1).
    // Built once; entities raise through ctx.Events instead of grabbing a singleton (S1).
    private readonly Habitales.Core.IEntityEventSink entityEventSink = new Habitales.Core.TriggerManagerEntitySink();

    [Header("Victory/Loss Conditions")]
    [SerializeField] private float collapseThreshold = 90f; // Percentage of critical tiles
    [SerializeField] private float criticalHealthThreshold = 33f; // Critical state
    [SerializeField] private float thrivingHealthThreshold = 67f; // Thriving state
    private bool isGameOver = false;
    /// <summary>True once the run has ended (Law 1 read). TriggerManager reads this to drop
    /// a popup whose mid-fire action-abort just ended the run underneath it.</summary>
    public bool IsGameOver => isGameOver;
    /// <summary>True while ANY event holds the sim paused. Refcounted (2026-07-19): three
    /// systems pause independently — CheckInPanelUI, TriggerManager popup batches, and
    /// UIManager modal popups. As a plain bool, whichever one resumed first un-paused the
    /// others; the depth counter makes each Pause/Resume pair balance on its own.</summary>
    public bool IsEventPaused => eventPauseDepth > 0;
    private int eventPauseDepth = 0;

    /// <summary>Fires the moment the run ends, BEFORE any end-flow UI shows. Subscribers stand
    /// down pending presentation (TriggerManager clears its popup queue — game over trumps
    /// queued events). Replaces the deleted ResourceManager.OnGameOver as the game-over signal;
    /// RunManager is now the sole authority on when a run ends.</summary>
    public event System.Action OnGameOverTriggered;

    
    [Header("Initial Zone Setup")]
    [SerializeField] private bool spawnInitialZone = true;
    [SerializeField] private Vector2Int initialZoneOrigin = Vector2Int.zero;
    [SerializeField] private int initialZoneSize = 6; // 6x6 grid
    // Zone 1's profile is owned by RegionManager.zone1Profile (single source of truth).
    // RunManager just triggers generation; it no longer holds a duplicate profile field.

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;

    [SerializeField] private bool isPrototypeRun = true;
    // Run length is now canonical on ResourceManager.RunLengthDays (single source of
    // truth — see the locked run-length unification). The old prototypeRunDays field
    // (and its per-scene 60/61/90 overrides) is retired; this gate just defers to it.

    // Peak thriving — high-water mark across the run; drives run-end score + snapshot.
    // Capture is delegated to ScreenshotService so the saved shot is UI-free (was RunSnapshot,
    // which captured the HUD too — that class is retired).
    [SerializeField] private Habitales.UI.ScreenshotService screenshotService;
    private int       peakThrivingCount;
    private int       peakAtDay = -1; // day index into healthHistory; -1 until EvaluateThrivingPeak first fires
    private Texture2D peakScreenshot;
    // XP / level-up / persistence moved to RunEndCoordinator (Habitales.Meta) — decoupled from the run sim.

    public int PeakThrivingCount => peakThrivingCount;
    public Texture2D PeakScreenshot => peakScreenshot;

    private HashSet<int> unlockedRegions = new HashSet<int>();
    
    // Full-run world-health time series (one sample per resolved day) — the player-progress
    // record surfaced at run end via EndGameData.healthHistory. The trend arrow only peeks its
    // last TrendWindowDays samples (WorldHealthTrend); it does NOT define or shorten this list.
    private List<float> healthHistory = new List<float>();
    
    public static RunManager Instance { get; private set; }
    
    void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    
    void Start() {
        InitializeSystems();

        // Progression load now lives in RunEndCoordinator (single load point).

        if (spawnInitialZone && tileManager != null)
        {
            SpawnInitialZone();
        }
    }
    
    void Update()
    {
    #if UNITY_EDITOR
            if (Input.GetKeyDown(KeyCode.F1))
            {
                Debug.Log("[DEBUG] F1 pressed — attempting day advance.");
                DebugAdvanceOneDay();
            }

            if (Input.GetKeyDown(KeyCode.F10))
            {
                Debug.Log("[DEBUG] F10 pressed — resetting progression.");
                if (runEndCoordinator != null) runEndCoordinator.ResetProgression();
            }
    #endif
    }
    
    void InitializeSystems() {
        // Auto-find systems if not assigned
        if (tileManager == null) {
            tileManager = TileManager.Instance;
            if (tileManager == null) Debug.LogError("TileManager not found!");
        }
        
        if (tileSelector == null) {
            tileSelector = GetComponent<TileSelector>();
            if (tileSelector == null) Debug.LogError("TileSelector missing!");
        }
        
        if (actionBarUI == null)
            actionBarUI = FindObjectOfType<ActionBarUI>();
        
        if (actionManager == null) {
            actionManager = ActionManager.Instance;
            if (actionManager == null) Debug.LogError("ActionManager not found!");
        }
        
        if (regionManager == null) {
            regionManager = RegionManager.Instance;
            if (regionManager == null) Debug.LogError("RegionManager not found!");
        }
        
        if (resourceManager == null)
        {
            resourceManager = ResourceManager.Instance;
            if (resourceManager == null) Debug.LogError("ResourceManager not found!");
        }

        if (screenshotService == null)
        {
            screenshotService = FindObjectOfType<Habitales.UI.ScreenshotService>();
            if (screenshotService == null)
                Debug.LogWarning("[RunManager] ScreenshotService not found — peak-thriving snapshots will be skipped (end screen shows no image). Add one to the scene or wire it in the Inspector.");
        }

        // Subscribe to resource events (optional but useful)
        if (resourceManager != null)
        {
            resourceManager.OnTimeAdvanced += HandleTimeAdvanced;
            resourceManager.OnPeopleFatigued += HandlePeopleFatigued;
            resourceManager.OnPeopleRecovered += HandlePeopleRecovered;
            // ResourceManager.OnGameOver is gone — the clock never declares game over.
            // EvaluateGameOver (per resolved day when idle, and on OnActionCompleted) owns it.
        }
        
        // Connect events
        if (tileSelector != null) {
            tileSelector.OnTileSelected += HandleTileSelected;
            tileSelector.OnTileDeselected += HandleTileDeselected;
        }
        
        if (actionManager != null) {
            actionManager.OnActionCompleted += HandleActionCompleted;
        }
        
        if (regionOutlineRenderer == null)
            regionOutlineRenderer = FindObjectOfType<RegionOutlineRenderer>();
        if (regionOutlineRenderer == null)
            Debug.LogError("RegionOutlineRenderer not found!");

        
        if (showDebugInfo) {
            Debug.Log($"GameManager initialized | Systems: TileManager={tileManager != null}, TileSelector={tileSelector != null}, ActionBarUI={actionBarUI != null}, ActionManager={actionManager != null}, RegionManager={regionManager != null}");
        }
    }
    
    void SpawnInitialZone()
    {
        Debug.Log("Generating initial Zone 1...");

        RegionGenerationResult result = regionManager.GenerateInitialRegion(initialZoneOrigin);

        if (result == null)
        {
            Debug.LogError("Failed to spawn initial zone!");
            return;
        }

        Debug.Log($"Zone 1 spawned — {result.tileCount} tiles at {initialZoneOrigin}");
    }

    
    void HandleTileSelected(Tile tile, Vector3 worldPosition)
    {
        if (isGameOver) return; 
        if (actionManager != null && actionManager.IsActionRunning) return;
        if (showDebugInfo) {
            Debug.Log($"Tile selected: {tile.gridPosition} | Health: {tile.CalculateHealth():F1}%");
        }
        
        regionOutlineRenderer?.ActivateRegion(tile.regionID);
    }
    
    void HandleTileDeselected() {
        if (showDebugInfo) {
            Debug.Log("Tile deselected");
        }
        
        regionOutlineRenderer?.ClearRegion();
    }
    
    /// <summary>
    /// Fired after an action's coroutine completes (clean finish OR abort). The per-day
    /// heartbeat owns all per-day work; the one thing that lives HERE is the game-over
    /// evaluation — the run can't end mid-action ("the actions dictate the length of the
    /// season"), so an action that crossed the final day, or one whose days pushed the
    /// world into collapse, gets its verdict the moment the crew comes home.
    /// </summary>
    void HandleActionCompleted(Tile targetTile, int daysElapsed)
    {
        EvaluateGameOver();
    }

    /// <summary>
    /// Flags the next region as unlockable when world health crosses the threshold, then
    /// AUTO-GENERATES it immediately (2026-07-21). The manual "Unlock Next Zone" button step
    /// was removed — crossing the threshold now spawns the next zone with no player press, so
    /// the button can be dormant/disabled in the scene. OnRegionUnlockReady still fires for any
    /// subscribers (the button, if left active, shows then hides again on OnRegionUnlocked in
    /// the same frame). Guarded so it fires at most once per pending unlock. Called from both
    /// the per-action path and the debug F1 path.
    /// </summary>
    void TryFlagRegionUnlock(float totalAverageHealth)
    {
        if (regionUnlockPending) return;
        if (totalAverageHealth < regionUnlockThreshold) return;
        if (unlockedRegions.Contains(regionManager.NextRegionID - 1)) return;

        regionUnlockPending = true;
        Debug.Log($"ZONE UNLOCK READY! World avg health {totalAverageHealth:F1} passed threshold {regionUnlockThreshold}. Auto-generating the next zone.");
        OnRegionUnlockReady?.Invoke();
        UnlockNextRegion();   // auto-unlock — no manual button press required
    }

    /// <summary>
    /// Generates the next region. Now invoked automatically from TryFlagRegionUnlock the moment
    /// the threshold is crossed (2026-07-21); still public so the debug F1 path and the legacy
    /// "Unlock Next Zone" button can call it too. No-op unless a region is currently pending.
    /// Generates exactly one region, clears the pending flag, and fires OnRegionUnlocked.
    /// </summary>
    public void UnlockNextRegion()
    {
        if (!regionUnlockPending || isGameOver) return;

        int newRegionFrom = regionManager.NextRegionID - 1;
        unlockedRegions.Add(newRegionFrom);
        regionManager.GenerateNewRegion(newRegionFrom);

        regionUnlockPending = false;
        OnRegionUnlocked?.Invoke();
    }

    /// <summary>
    /// Tracks the high-water mark of simultaneously-thriving tiles. On a new peak,
    /// asks ScreenshotService for a UI-free capture of the frame.
    /// </summary>
    void EvaluateThrivingPeak()
    {
        if (isGameOver) return;
        int count = GetThrivingTileCount();
        if (count <= peakThrivingCount) return;

        peakThrivingCount = count;
        // 0-based index into healthHistory — TotalDays is 1-based and this day's entry hasn't
        // been appended yet (EvaluateThrivingPeak runs immediately before healthHistory.Add in
        // HandleTimeAdvanced), so TotalDays - 1 is exactly the index that entry will land at.
        peakAtDay         = resourceManager != null ? resourceManager.TotalDays - 1 : -1;

        if (screenshotService != null)
            screenshotService.CaptureCleanTexture(HandlePeakCaptured);
        // else: peak count still recorded; the end screen just shows no snapshot.
    }

    /// <summary>Receives the UI-free peak capture. We own the texture (see CaptureCleanTexture).</summary>
    void HandlePeakCaptured(Texture2D captured)
    {
        if (captured == null) return;
        if (peakScreenshot != null) Destroy(peakScreenshot);
        peakScreenshot = captured;
    }

    /// <summary>
    /// Fires OnTileTierChanged when a tile CROSSES a health tier (arch §2.1b step 4).
    /// Meaning-event (Law 2): fires on change only. First evaluation seeds lastTier silently.
    /// </summary>
    void EvaluateThresholds()
    {
        foreach (Tile tile in tileManager.GetAllTiles())
        {
            Tier current = TierOf(tile);
            if (!tile.tierSeeded)
            {
                tile.lastTier   = current;
                tile.tierSeeded = true;
                continue;
            }
            if (current != tile.lastTier)
            {
                Tier previous = tile.lastTier;
                tile.lastTier = current;
                OnTileTierChanged?.Invoke(tile, previous, current);
            }
        }
    }

    /// <summary>Tile health → tier, using the same serialized cutoffs as the thriving-count / collapse logic.</summary>
    Tier TierOf(Tile tile)
    {
        float h = tile.CalculateHealth();
        if (h <= criticalHealthThreshold) return Tier.Critical;
        if (h >= thrivingHealthThreshold) return Tier.Thriving;
        return Tier.Degraded;
    }


    
    /// <summary>
    /// Pure detector: has ecosystem collapse occurred (≥90% tiles critical)? Detection
    /// only — TRIGGERING the collapse ending is EvaluateGameOver's job, which also knows
    /// not to end the run mid-action.
    /// </summary>
    bool IsCollapsed()
    {
        List<Tile> allTiles = tileManager.GetAllTiles();
        if (allTiles.Count == 0) return false;

        int criticalCount = 0;
        foreach (Tile tile in allTiles)
        {
            if (tile.CalculateHealth() <= criticalHealthThreshold)
                criticalCount++;
        }

        float criticalPercent = ((float)criticalCount / allTiles.Count) * 100f;

        if (showDebugInfo)
            Debug.Log($"Critical tiles: {criticalCount}/{allTiles.Count} ({criticalPercent:F1}%)");

        return criticalPercent >= collapseThreshold;
    }

    /// <summary>
    /// The single game-over decision point (2026-07-08 run-end rework). Called at the end
    /// of every resolved day AND from HandleActionCompleted. The run never ends mid-action:
    /// whatever the player committed to plays out — even past the run-length cap — and the
    /// verdict lands when no action is running. Collapse is checked FIRST: if the world is
    /// collapsed when the action ends, the ending is Ecosystem Collapse even on/after the
    /// final day (user decision 2026-07-08).
    /// </summary>
    void EvaluateGameOver()
    {
        if (isGameOver) return;
        if (actionManager != null && actionManager.IsActionRunning) return;

        if (IsCollapsed())
        {
            TriggerGameOver("Ecosystem Collapse", GetThrivingTileCount(), collapsed: true);
            return;
        }

        if (isPrototypeRun && resourceManager.TotalDays >= resourceManager.RunLengthDays)
            TriggerGameOver("Field Season Complete", GetThrivingTileCount());
    }

    /// <summary>
    /// Counts tiles with health ≥67% (Thriving state).
    /// Used for final score calculation.
    /// </summary>
    int GetThrivingTileCount()
    {
        List<Tile> allTiles = tileManager.GetAllTiles();
        int thrivingCount = 0;

        foreach (Tile tile in allTiles)
        {
            if (tile.CalculateHealth() >= thrivingHealthThreshold)
            {
                thrivingCount++;
            }
        }
        return thrivingCount;
    }

    // XP calc, level-up application, persistence, Azi-line, and the (now-removed) cube
    // plant-unlock all moved to RunEndCoordinator (Habitales.Meta). XP unlocks nothing
    // functional in Alpha — level-up is a cosmetic/feel reward.




    /// <summary>
    /// Triggers game over. Flow (2026-07-08 rework, simplified 2026-07-15): (1)
    /// OnGameOverTriggered — pending presentation stands down (TriggerManager clears its
    /// popup queue); (2) End Conversation — the scripted Azi/Bob wrap-up via
    /// CheckInPanelUI's conversation-only mode; (3) End Report — EndGameScreenUI. When the
    /// conversation path isn't wired/authored: LogError → End Report directly (the old
    /// Azi-speech-bubble middle tier is deleted) — an unwired scene still ends.
    /// If the world collapses on a check-in day, ShowConversationOnly force-resets any
    /// in-progress midseason panel (collision rule — end flow trumps).
    /// </summary>
    void TriggerGameOver(string reason, int thrivingTiles, bool collapsed = false)
    {
        isGameOver = true;
        Debug.Log($"GAME OVER: {reason} | Peak Thriving: {peakThrivingCount}");

        // Game over trumps queued events — TriggerManager dismisses/clears on this signal.
        OnGameOverTriggered?.Invoke();

        // Meta layer (XP / level-up / persistence / Azi line / grade) is owned by RunEndCoordinator.
        // collapsed forces SeasonGrade.Collapse regardless of the tile mix — RunManager is the one
        // that knows WHY the run ended (Ecosystem Collapse vs Field Season Complete).
        ComputeTileCounts(out int thriving, out int degraded, out int critical);
        Habitales.Meta.RunEndCoordinator.RunEndSummary summary = default;
        if (runEndCoordinator != null)
            summary = runEndCoordinator.ProcessRunEnd(thriving, degraded, critical, peakThrivingCount, collapsed);
        else
            Debug.LogError("[RunManager] runEndCoordinator is NOT wired — XP/level-up/persistence will not run and the Azi line will be blank. Wire it in the Inspector.");

        EndGameData data = BuildEndGameData(reason, summary);

        // Loud guards — silent null-conditional Shows were swallowing the whole run-end flow.
        if (endGameScreenUI == null)
            Debug.LogError("[RunManager] endGameScreenUI is NOT wired in the inspector — end-game UI will not appear. Drag the EndGameScreenUI GameObject into RunManager's serialized field.");

        if (TryShowEndConversation(collapsed, data))
            return; // End Report shows on the conversation's [Continue]

        // No middle tier: the End Conversation either plays or the End Report shows now.
        // TryShowEndConversation already LogError'd the specific missing piece.
        // A midseason check-in may still be open under the End Report (its [Continue] —
        // the only ResumeFromEvent for its pause — is about to be covered); force-close
        // it so the event-pause can't be held for the rest of the process.
        Habitales.UI.CheckInPanelUI.Instance?.ForceClose();
        ShowEndReport(data);
    }

    /// <summary>
    /// Step 2 of the end flow: the End Conversation. Returns false (with a LogError naming
    /// the missing piece) so TriggerGameOver can show the End Report directly — the end
    /// flow must never dead-end. Variant selection reuses the check-in comparators with
    /// end-of-run semantics: 'improved' = final thriving count, 'decayed' = final critical count.
    /// </summary>
    bool TryShowEndConversation(bool collapsed, EndGameData data)
    {
        CheckInConversationSO source = collapsed ? collapseConversation : seasonEndConversation;
        if (source == null)
        {
            Debug.LogError($"[RunManager] {(collapsed ? "collapseConversation" : "seasonEndConversation")} is not wired — skipping the End Conversation, showing the End Report directly.");
            return false;
        }

        var panel = Habitales.UI.CheckInPanelUI.Instance;
        if (panel == null)
        {
            Debug.LogError("[RunManager] CheckInPanelUI is not in the scene — skipping the End Conversation, showing the End Report directly.");
            return false;
        }

        int daysLeft = resourceManager != null ? resourceManager.DaysRemaining : 0;
        ConversationSO conversation = source.Select(data.thrivingCount, data.criticalCount, daysLeft);
        if (conversation == null)
            return false; // Select() already logged the authoring error

        return panel.ShowConversationOnly(conversation, data.endReason, () => ShowEndReport(data));
    }

    /// <summary>Step 3 of the end flow: the End Report.</summary>
    void ShowEndReport(EndGameData data)
    {
        if (endGameScreenUI != null) endGameScreenUI.Show(data);
        else Debug.LogError("[RunManager] endGameScreenUI is null — end report cannot show. Wire the reference.");
    }

    
    
    // The ordered day-advance sequence (arch §2.1), run ONCE PER DAY. ResourceManager has
    // already advanced the clock + rolled weather before firing this. Cascade now lives here
    // (was per-action) so a multi-day action diffuses each day — see FinishAction, which
    // applies that day's effects BEFORE advancing so the cascade sees them.
    // Popup drain is now a strict ordered step (2026-07-08 control-inversion): fires raised
    // during the day (entity sink, hooks) are COLLECTED by TriggerManager and drained here
    // as the last step, after visuals + OnDayResolved + the game-over check.
    void HandleTimeAdvanced(int days)
    {
        if (isGameOver) return;
        if (showDebugInfo)
            Debug.Log($"⏰ Time advanced by {days} days | Now: {resourceManager.GetFullTimeDisplay()}");

        for (int d = 0; d < days; d++)
        {
            if (isGameOver) break;

            // 1. Open the trigger-collection window: Fire() calls made while the day resolves
            // (kaingin, entity deaths, …) enqueue silently instead of popping mid-tick.
            Habitales.Triggers.TriggerManager.Instance?.BeginDayResolution();

            // 2. Entities tick (fire spread, kaingin, tree growth, …). Assemble the per-day
            // TickContext ONCE here (reading weather via Law 1) and thread it down — entities
            // READ the resolved day instead of grabbing singletons mid-tick (arch §5.2 / S1).
            // The real EventManager-backed sink decouples entities from the EventManager singleton.
            // Spell depth = days past the activation threshold (≥1 while active, 0 otherwise) —
            // plants combine it with their tile's VegCover + species resistance for stall/regression.
            var wm = WeatherManager.Instance;
            int droughtStreakDays = (wm != null && wm.IsDroughtActive) ? wm.DrySpellDays - wm.StreakThreshold + 1 : 0;
            int delugeStreakDays  = (wm != null && wm.IsDelugeActive)  ? wm.WetSpellDays - wm.StreakThreshold + 1 : 0;
            var tickCtx = new Habitales.Entities.TickContext(
                tileManager,
                wm != null ? wm.GetFireBonusDamage()     : 0f,
                wm != null ? wm.GetFireSpreadMultiplier() : 1f,
                entityEventSink,
                droughtStreakDays,
                delugeStreakDays,
                tileManager.GrowthStallPoint,
                tileManager.GrowthRegressPoint);
            tileManager.UpdateAllEntities(in tickCtx);

            // 2b. Natural neglect decay — every tile loses its escalating decayK from its soil
            // substats + vegetation cover, then that decay grows (k *= Tile.DecayGrowth). Player
            // interaction resets a tile's decay (ActionManager.FinishAction). Runs before cascade
            // so the day's loss diffuses with everything else.
            tileManager.ApplyDailyDecay();

            // 2c. Weather streak stress — once a dry/wet streak hits 3+ days, active weather
            // starts actively draining tiles (skewed by low VegetationCover) and can wither
            // plants outright. No-ops if WeatherManager isn't present or no streak is active.
            tileManager.ApplyWeatherStress();

            // 3. Cascade — neighbour diffusion, ONCE per day.
            CascadeTileUpdates();

            // 4. Threshold crossings (meaning-events) + world-state checks.
            EvaluateThresholds();
            TryFlagRegionUnlock(regionManager.GetTotalAverageHealth());
            EvaluateThrivingPeak();
            healthHistory.Add(regionManager.GetTotalAverageHealth());

            // 5. Visuals refresh after all data has settled.
            tileManager.RefreshAllVisuals();

            // 6. The universal push everyone subscribes to.
            OnDayResolved?.Invoke(resourceManager.TotalDays);

            // 7. Game-over evaluation — BEFORE the popup drain, so a run that ends today
            // clears the queue (game over trumps queued events) and the end flow owns the
            // screen. No-ops while an action is mid-flight (verdict waits for completion).
            EvaluateGameOver();

            // 8. Drain the popups collected during the day — the player sees them over a
            // fully-updated world. Mid-action this is where an event interrupt lands
            // (FirePopup aborts the running action at this day boundary).
            Habitales.Triggers.TriggerManager.Instance?.DrainDeferredFires();
        }
    }
    
    public void PauseForEvent()
    {
        eventPauseDepth++;
        if (showDebugInfo) Debug.Log($"GameManager: Paused for event (depth {eventPauseDepth}).");
    }

    public void ResumeFromEvent()
    {
        if (eventPauseDepth <= 0)
        {
            Debug.LogWarning("GameManager: ResumeFromEvent called with no matching PauseForEvent — ignored (depth already 0).");
            return;
        }
        eventPauseDepth--;
        if (showDebugInfo) Debug.Log($"GameManager: Resumed from event (depth {eventPauseDepth}).");
    }

    /// <summary>
    /// Aborts the in-flight multi-day action. Days already resolved keep their applied effects;
    /// the remaining days are cancelled. Intended for event interrupts that must reclaim the
    /// workforce mid-action. Thin delegation — ActionManager owns the coroutine handle and the
    /// finalize/cleanup path (see ActionManager.AbortCurrentAction for the full contract,
    /// including why this is safe to call mid-day-cycle).
    /// </summary>
    public void AbortCurrentAction()
    {
        if (ActionManager.Instance == null)
        {
            Debug.LogWarning("[RunManager] AbortCurrentAction: ActionManager.Instance is null — cannot abort.");
            return;
        }

        if (showDebugInfo) Debug.Log("GameManager: Action aborted by event.");
        ActionManager.Instance.AbortCurrentAction();
    }

    void HandlePeopleFatigued(int count, int returnDay)
    {
        if (showDebugInfo)
            Debug.Log($"{count} worker(s) fatigued. Return by day {returnDay}.");
    }

    void HandlePeopleRecovered(int count)
    {
        if (showDebugInfo)
            Debug.Log($"{count} worker(s) recovered! Available: {resourceManager.AvailablePeople}/{resourceManager.TotalPeople}");
    }

    private EndGameData BuildEndGameData(string reason, Habitales.Meta.RunEndCoordinator.RunEndSummary summary)
    {
        var data = new EndGameData
        {
            endReason      = reason,
            currentYear    = resourceManager.CurrentYear,
            totalDays      = resourceManager.TotalDays,
            worldHealth    = regionManager.GetTotalAverageHealth(),
            healthHistory  = new List<float>(healthHistory),
        };

        // Tile counts — reuses existing threshold fields on this class
        ComputeTileCounts(out data.thrivingCount, out data.degradedCount, out data.criticalCount);
        data.peakThrivingCount = peakThrivingCount;
        data.peakScreenshot    = peakScreenshot;
        data.peakAtDay         = peakAtDay;
        data.aziSummaryLine = summary.aziLine;
        // DORMANT (2026-07-18): level-ups cut — EndGameData no longer carries xpEarned/xpBefore.
        data.seasonGrade    = summary.grade;

        // Top worker by actions participated
        var allWorkers = resourceManager.AllWorkers;
        if (allWorkers != null && allWorkers.Count > 0)
            data.topWorker = allWorkers.OrderByDescending(w => w.actionsParticipated).First();

        // Action stats — only actions the player actually used are in this dict,
        // so "most avoided" = least-used of tried actions (never-touched actions excluded)
        var counts = actionManager.actionUsageCounts;
        if (counts.Count > 0)
        {
            data.favouriteAction   = GetFavouriteActionName();
            data.mostAvoidedAction = counts.OrderBy(kvp => kvp.Value).First().Key;
        }

        // Most chatted worker — falls back to top worker if nobody was ever chatted with
        var chatCounts = DialogueManager.Instance?.chatOpenCounts;
        if (chatCounts != null && chatCounts.Count > 0)
            data.mostChattedWorker = chatCounts.OrderByDescending(kvp => kvp.Value).First().Key;
        else if (data.topWorker != null)
            data.mostChattedWorker = data.topWorker.workerName;

        // Zone healths — iterate the existing unlockedRegions HashSet on this class
        data.zoneHealths = new Dictionary<int, float>();
        foreach (int regionID in unlockedRegions)
            data.zoneHealths[regionID] = regionManager.GetRegionHealth(regionID);

        return data;
    }

    private void ComputeTileCounts(out int thriving, out int degraded, out int critical)
    {
        // Delegate to TierOf so the run-end counts can never diverge from the tier classifier
        // that drives OnTileTierChanged (S2 — one concept, one place). Previously this used its
        // own boundary comparators and disagreed with TierOf at exactly 33 / 67.
        thriving = degraded = critical = 0;
        foreach (var tile in tileManager.GetAllTiles())
        {
            switch (TierOf(tile))
            {
                case Tier.Thriving: thriving++; break;
                case Tier.Critical: critical++; break;
                default:            degraded++; break;
            }
        }
    }

    // Shared by BuildEndGameData (favouriteAction) and BuildSeasonReportData (topActionName) so
    // the two reports can never disagree on "most-used action" (S2 — one concept, one place).
    // Null when no actions have been used yet.
    private string GetFavouriteActionName()
    {
        var counts = actionManager.actionUsageCounts;
        if (counts == null || counts.Count == 0) return null;
        return counts.OrderByDescending(kvp => kvp.Value).First().Key;
    }

    /// <summary>
    /// Mid-run trajectory snapshot for the Azi check-in panel (CheckInPanelUI; originally the
    /// archived Season Report Lite, arch ENDGAME_BUILD_PLAN §4). Law-1 read-only assembly —
    /// every field is a subset read of state this class already accumulates for
    /// BuildEndGameData, taken mid-run instead of at end.
    /// </summary>
    public Habitales.Meta.SeasonReportData BuildSeasonReportData()
    {
        if (runEndCoordinator == null)
        {
            Debug.LogError("[RunManager] BuildSeasonReportData: runEndCoordinator is NOT wired — cannot project a season grade. Wire it in the Inspector.");
            return null;
        }

        ComputeTileCounts(out int thriving, out int degraded, out int critical);

        return new Habitales.Meta.SeasonReportData(
            currentDay:      resourceManager.TotalDays,
            runLengthDays:   resourceManager.RunLengthDays,
            healthHistory:   new List<float>(healthHistory),
            worldHealthTrend: WorldHealthTrend,
            thrivingCount:   thriving,
            degradedCount:   degraded,
            criticalCount:   critical,
            projectedGrade:  runEndCoordinator.ComputeGrade(thriving, degraded, critical),
            topActionName:   GetFavouriteActionName());
    }



    
    
    
    /// <summary>
    /// Cascades tile stat changes across ALL tiles (global, not region-scoped) using diffusion.
    /// Each tile lerps toward the average of its 4 neighbors, snapshot-then-apply per iteration.
    /// </summary>
    void CascadeTileUpdates()
    {
        List<Tile> allTiles = tileManager.GetAllTiles();
        if (allTiles.Count == 0) return;

        for (int iteration = 0; iteration < cascadeIterations; iteration++)
        {
            Dictionary<Tile, TileStats> newStats = new Dictionary<Tile, TileStats>();

            foreach (Tile tile in allTiles)
            {
                TileStats targetStats = CalculateTargetStats(tile);
                newStats[tile] = LerpStats(tile.stats, targetStats, diffusionRate);
            }

            foreach (var kvp in newStats)
            {
                Tile tile = kvp.Key;
                TileStats ns = kvp.Value;
                tile.stats.nutrientBalance    = ns.nutrientBalance;
                tile.stats.soilOrganicMatter  = ns.soilOrganicMatter;
                tile.stats.soilStructure      = ns.soilStructure;
                tile.stats.biologicalActivity = ns.biologicalActivity;
                tile.stats.waterDynamics      = ns.waterDynamics;
                tile.stats.erosionResistance  = ns.erosionResistance;
                tile.stats.vegetationCover    = ns.vegetationCover;
                tile.stats.contamination      = ns.contamination;

                // Sync contamination overlay
                if (tile.stats.contamination > 60f && !tile.tv.Contains(TileOverlayType.Contaminated))
                    tile.tv.Add(TileOverlayType.Contaminated);
                else if (tile.stats.contamination <= 60f && tile.tv.Contains(TileOverlayType.Contaminated))
                    tile.tv.Remove(TileOverlayType.Contaminated);
            }
        }

        if (showDebugInfo)
            Debug.Log($"Cascade complete — {cascadeIterations} iterations, {allTiles.Count} tiles.");
    }

    
    /// <summary>
    /// Calculates target stats by averaging 4-directional neighbors.
    /// </summary>
    TileStats CalculateTargetStats(Tile tile)
    {
        List<Tile> neighbors = tileManager.GetAdjacentTiles(tile);
        if (neighbors.Count == 0) return CloneStats(tile.stats);

        float n = 0, o = 0, s = 0, b = 0, w = 0, e = 0, v = 0, c = 0;
        foreach (Tile nb in neighbors)
        {
            n += nb.stats.nutrientBalance;    o += nb.stats.soilOrganicMatter;
            s += nb.stats.soilStructure;      b += nb.stats.biologicalActivity;
            w += nb.stats.waterDynamics;      e += nb.stats.erosionResistance;
            v += nb.stats.vegetationCover;    c += nb.stats.contamination;
        }
        int count = neighbors.Count;
        return new TileStats {
            nutrientBalance    = n / count, soilOrganicMatter  = o / count,
            soilStructure      = s / count, biologicalActivity = b / count,
            waterDynamics      = w / count, erosionResistance  = e / count,
            vegetationCover    = v / count, contamination      = c / count
        };
    }

    
    /// <summary>
    /// Lerps from current stats toward target stats by diffusionRate.
    /// </summary>
    TileStats LerpStats(TileStats cur, TileStats tgt, float rate) => new TileStats
    {
        nutrientBalance    = Mathf.Lerp(cur.nutrientBalance,    tgt.nutrientBalance,    rate),
        soilOrganicMatter  = Mathf.Lerp(cur.soilOrganicMatter,  tgt.soilOrganicMatter,  rate),
        soilStructure      = Mathf.Lerp(cur.soilStructure,      tgt.soilStructure,      rate),
        biologicalActivity = Mathf.Lerp(cur.biologicalActivity, tgt.biologicalActivity, rate),
        waterDynamics      = Mathf.Lerp(cur.waterDynamics,      tgt.waterDynamics,      rate),
        erosionResistance  = Mathf.Lerp(cur.erosionResistance,  tgt.erosionResistance,  rate),
        vegetationCover    = Mathf.Lerp(cur.vegetationCover,    tgt.vegetationCover,    rate),
        contamination      = Mathf.Lerp(cur.contamination,      tgt.contamination,      rate)
    };

    
    /// <summary>
    /// Helper to clone TileStats.
    /// </summary>
    TileStats CloneStats(TileStats o) => new TileStats
    {
        nutrientBalance    = o.nutrientBalance,   soilOrganicMatter  = o.soilOrganicMatter,
        soilStructure      = o.soilStructure,     biologicalActivity = o.biologicalActivity,
        waterDynamics      = o.waterDynamics,     erosionResistance  = o.erosionResistance,
        vegetationCover    = o.vegetationCover,   contamination      = o.contamination
    };

    
    [ContextMenu("Test Collapse")]
    void TestCollapse()
    {
        List<Tile> allTiles = tileManager.GetAllTiles();
        int targetCount = Mathf.CeilToInt(allTiles.Count * 0.91f);
        for (int i = 0; i < targetCount; i++)
        {
            allTiles[i].stats.nutrientBalance    = 10f; allTiles[i].stats.soilOrganicMatter  = 10f;
            allTiles[i].stats.soilStructure      = 10f; allTiles[i].stats.biologicalActivity = 10f;
            allTiles[i].stats.waterDynamics      = 10f; allTiles[i].stats.erosionResistance  = 10f;
            allTiles[i].stats.vegetationCover    = 10f; allTiles[i].stats.contamination      = 90f;
            tileManager.UpdateTileVisual(allTiles[i]);
        }
        Debug.Log($"Set {targetCount}/{allTiles.Count} tiles to critical");
        EvaluateGameOver();
    }
    
    [ContextMenu("Test Year Complete")]
    void TestYearComplete()
    {
        // Fast-forward to day 364
        int daysToAdvance = 364 - resourceManager.TotalDays;
        if (daysToAdvance > 0)
        {
            resourceManager.AdvanceTime(daysToAdvance);
        }
    }
    
    [ContextMenu("Debug: Advance One Day")]
    void DebugAdvanceOneDay()
    {
        // Loud guards so a silent no-op is impossible — most common F1 culprit
        // is isGameOver latched true from an earlier collapse / 60-day cutoff.
        if (isGameOver)
        {
            Debug.LogWarning("[DEBUG] F1 ignored — isGameOver is true. Restart the scene to re-enable day advance.");
            return;
        }
        if (resourceManager == null)
        {
            Debug.LogWarning("[DEBUG] F1 ignored — resourceManager reference is null on RunManager.");
            return;
        }
        if (tileManager == null)
        {
            Debug.LogWarning("[DEBUG] F1 ignored — tileManager reference is null on RunManager.");
            return;
        }

        Debug.Log("[DEBUG] Advancing 1 day via shortcut.");

        // AdvanceTime(1) → OnTimeAdvanced → HandleTimeAdvanced now runs the FULL per-day
        // heartbeat (entities, cascade, thresholds, collapse, unlock, peak, visuals). The
        // manual cascade/endgame block that used to live here is gone — it would double-run
        // now that cascade is per-day.
        resourceManager.AdvanceTime(1);

        if (showDebugInfo)
            Debug.Log($"[DEBUG] Day passed. World Health: {regionManager.GetTotalAverageHealth():F1} | {resourceManager.GetFullTimeDisplay()}");
    }

    void OnDestroy() {
        if (tileSelector != null) {
            tileSelector.OnTileSelected -= HandleTileSelected;
            tileSelector.OnTileDeselected -= HandleTileDeselected;
        }
        
        if (actionManager != null) {
            actionManager.OnActionCompleted -= HandleActionCompleted;
        }
        
        if (resourceManager != null)
        {
            resourceManager.OnTimeAdvanced -= HandleTimeAdvanced;
            resourceManager.OnPeopleFatigued -= HandlePeopleFatigued;
            resourceManager.OnPeopleRecovered -= HandlePeopleRecovered;
        }

        if (peakScreenshot != null)
        {
            Destroy(peakScreenshot);
            peakScreenshot = null;
        }
    }
    
    
    // Public properties
    public TileManager TileManager => tileManager;
    public float DiffusionRate => diffusionRate;
    public int CascadeIterations => cascadeIterations;
}