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
    [SerializeField] private AziSpeechBubbleUI   aziSpeechBubbleUI;
    [SerializeField] private Habitales.Meta.RunEndCoordinator runEndCoordinator; // owns XP/level-up/persistence

    
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
    /// Per-day change in world-average health (today − yesterday), in health-points.
    /// 0 until at least two days have resolved. Read-only (Law 1) — drives the HUD trend arrow.
    /// Sourced from the existing daily <c>healthHistory</c> push (heartbeat step "4b").
    /// </summary>
    public float WorldHealthDelta
    {
        get
        {
            int n = healthHistory.Count;
            return n >= 2 ? healthHistory[n - 1] - healthHistory[n - 2] : 0f;
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

    // The real meaning-event sink threaded into every per-day TickContext (arch §5.2 / §6.1).
    // Built once; entities raise through ctx.Events instead of grabbing EventManager (S1).
    private readonly Habitales.Core.IEntityEventSink entityEventSink = new Habitales.Core.EventManagerEntitySink();

    [Header("Victory/Loss Conditions")]
    [SerializeField] private float collapseThreshold = 90f; // Percentage of critical tiles
    [SerializeField] private float criticalHealthThreshold = 33f; // Critical state
    [SerializeField] private float thrivingHealthThreshold = 67f; // Thriving state
    private bool isGameOver = false;
    public bool IsEventPaused { get; private set; } = false;

    
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
    private readonly RunSnapshot snapshot = new();
    // XP / level-up / persistence moved to RunEndCoordinator (Habitales.Meta) — decoupled from the run sim.

    public int PeakThrivingCount => snapshot.peakThrivingCount;
    public Texture2D PeakScreenshot => snapshot.peakScreenshot;

    private HashSet<int> unlockedRegions = new HashSet<int>();
    
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
    
        // Subscribe to resource events (optional but useful)
        if (resourceManager != null)
        {
            resourceManager.OnTimeAdvanced += HandleTimeAdvanced;
            resourceManager.OnPeopleFatigued += HandlePeopleFatigued;
            resourceManager.OnPeopleRecovered += HandlePeopleRecovered;
            resourceManager.OnGameOver += HandleGameOver;
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
    /// Fired after an action's coroutine completes. The per-day heartbeat
    /// (HandleTimeAdvanced) now owns cascade, threshold/collapse/unlock checks, the
    /// thriving-peak snapshot, and the visual refresh — all run once per day (arch §2.1).
    /// This handler is intentionally a no-op now; kept for the OnActionCompleted wiring
    /// and any future end-of-action bookkeeping.
    /// </summary>
    void HandleActionCompleted(Tile targetTile, int daysElapsed)
    {
        // Intentionally empty — work moved to the per-day heartbeat (arch §2.1).
    }

    /// <summary>
    /// Flags the next region as unlockable when world health crosses the threshold.
    /// Does NOT generate the region — that waits on the player pressing the unlock
    /// button (see UnlockNextRegion). Guarded so the event fires at most once per
    /// pending unlock. Called from both the per-action path and the debug F1 path.
    /// </summary>
    void TryFlagRegionUnlock(float totalAverageHealth)
    {
        if (regionUnlockPending) return;
        if (totalAverageHealth < regionUnlockThreshold) return;
        if (unlockedRegions.Contains(regionManager.NextRegionID - 1)) return;

        regionUnlockPending = true;
        Debug.Log($"ZONE UNLOCK READY! World avg health {totalAverageHealth:F1} passed threshold {regionUnlockThreshold}. Awaiting player.");
        OnRegionUnlockReady?.Invoke();
    }

    /// <summary>
    /// Player-triggered region generation (wired to the "Unlock Next Zone" button).
    /// No-op unless a region is currently pending. Generates exactly one region,
    /// clears the pending flag, and fires OnRegionUnlocked.
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
    /// delegates to RunSnapshot to capture a screenshot.
    /// </summary>
    void EvaluateThrivingPeak()
    {
        if (isGameOver) return;
        int count = GetThrivingTileCount();
        int day   = resourceManager != null ? resourceManager.TotalDays : 0;
        if (snapshot.TryRecordPeak(count, day))
            StartCoroutine(snapshot.CaptureRoutine());
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
    /// Checks if ecosystem collapse has occurred (≥90% tiles critical).
    /// Called after each action completes.
    /// </summary>
    void CheckCollapseCondition()
    {
        if (isGameOver) return;
    
        List<Tile> allTiles = tileManager.GetAllTiles();
        if (allTiles.Count == 0) return;
    
        // Count tiles with health ≤33%
        int criticalCount = 0;
        foreach (Tile tile in allTiles)
        {
            if (tile.CalculateHealth() <= criticalHealthThreshold)
            {
                criticalCount++;
            }
        }
    
        // Calculate percentage
        float criticalPercent = ((float)criticalCount / allTiles.Count) * 100f;
    
        if (showDebugInfo)
        {
            Debug.Log($"Critical tiles: {criticalCount}/{allTiles.Count} ({criticalPercent:F1}%)");
        }
    
        // Trigger collapse if threshold exceeded
        if (criticalPercent >= collapseThreshold)
        {
            int thrivingTiles = GetThrivingTileCount();
            TriggerGameOver($"Ecosystem Collapse", thrivingTiles);
        }
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
    /// Triggers game over. Shows Azi speech bubble first (if wired), then run-end screen.
    /// </summary>
    void TriggerGameOver(string reason, int thrivingTiles)
    {
        isGameOver = true;
        Debug.Log($"GAME OVER: {reason} | Peak Thriving: {snapshot.peakThrivingCount}");

        // Meta layer (XP / level-up / persistence / Azi line) is owned by RunEndCoordinator.
        ComputeTileCounts(out int thriving, out int degraded, out int critical);
        Habitales.Meta.RunEndCoordinator.RunEndSummary summary = default;
        if (runEndCoordinator != null)
            summary = runEndCoordinator.ProcessRunEnd(thriving, degraded, critical, snapshot.peakThrivingCount);
        else
            Debug.LogError("[RunManager] runEndCoordinator is NOT wired — XP/level-up/persistence will not run and the Azi line will be blank. Wire it in the Inspector.");

        EndGameData data = BuildEndGameData(reason, summary);

        // Loud guards — silent null-conditional Shows were swallowing the whole run-end flow.
        if (endGameScreenUI == null)
            Debug.LogError("[RunManager] endGameScreenUI is NOT wired in the inspector — end-game UI will not appear. Drag the EndGameScreenUI GameObject into RunManager's serialized field.");

        if (aziSpeechBubbleUI != null)
        {
            Debug.Log("[RunManager] Showing Azi speech bubble → EndGameScreen on dismiss.");
            aziSpeechBubbleUI.Show(data.aziSummaryLine, () =>
            {
                if (endGameScreenUI != null) endGameScreenUI.Show(data);
                else Debug.LogError("[RunManager] Azi dismissed but endGameScreenUI is null — stuck. Wire the reference.");
            });
        }
        else
        {
            Debug.LogWarning("[RunManager] aziSpeechBubbleUI not wired — skipping speech bubble, showing EndGameScreen directly.");
            if (endGameScreenUI != null) endGameScreenUI.Show(data);
        }
    }

    
    
    // The ordered day-advance sequence (arch §2.1), run ONCE PER DAY. ResourceManager has
    // already advanced the clock + rolled weather before firing this. Cascade now lives here
    // (was per-action) so a multi-day action diffuses each day — see FinishAction, which
    // applies that day's effects BEFORE advancing so the cascade sees them.
    // NOTE: EventManager.DrainQueue (arch step 5) runs via EventManager's own OnTimeAdvanced
    // subscription, a separate subscriber — strict step-5 ordering awaits the control-inversion.
    void HandleTimeAdvanced(int days)
    {
        if (isGameOver) return;
        if (showDebugInfo)
            Debug.Log($"⏰ Time advanced by {days} days | Now: {resourceManager.GetFullTimeDisplay()}");

        for (int d = 0; d < days; d++)
        {
            if (isGameOver) break;

            // 2. Entities tick (fire spread, kaingin, tree growth, …). Assemble the per-day
            // TickContext ONCE here (reading weather via Law 1) and thread it down — entities
            // READ the resolved day instead of grabbing singletons mid-tick (arch §5.2 / S1).
            // The real EventManager-backed sink decouples entities from the EventManager singleton.
            var tickCtx = new Habitales.Entities.TickContext(
                tileManager,
                WeatherManager.Instance != null ? WeatherManager.Instance.GetFireBonusDamage()     : 0f,
                WeatherManager.Instance != null ? WeatherManager.Instance.GetFireSpreadMultiplier() : 1f,
                entityEventSink);
            tileManager.UpdateAllEntities(in tickCtx);

            // 2b. Natural neglect decay — every tile loses its escalating decayK from its soil
            // substats + vegetation cover, then that decay grows (k *= Tile.DecayGrowth). Player
            // interaction resets a tile's decay (ActionManager.FinishAction). Runs before cascade
            // so the day's loss diffuses with everything else.
            tileManager.ApplyDailyDecay();

            // 3. Cascade — neighbour diffusion, ONCE per day.
            CascadeTileUpdates();

            // 4. Threshold crossings (meaning-events) + world-state checks.
            EvaluateThresholds();
            CheckCollapseCondition();
            TryFlagRegionUnlock(regionManager.GetTotalAverageHealth());
            EvaluateThrivingPeak();
            healthHistory.Add(regionManager.GetTotalAverageHealth());

            // Prototype field-season cutoff — defers to the canonical run length.
            if (isPrototypeRun && resourceManager.TotalDays >= resourceManager.RunLengthDays)
            {
                TriggerGameOver("Field Season Complete", GetThrivingTileCount());
                return;
            }

            // 6. Visuals refresh LAST, after all data has settled.
            tileManager.RefreshAllVisuals();

            // 7. The universal push everyone subscribes to.
            OnDayResolved?.Invoke(resourceManager.TotalDays);
        }
    }
    
    public void PauseForEvent()
    {
        IsEventPaused = true;
        if (showDebugInfo) Debug.Log("GameManager: Paused for event.");
    }

    public void ResumeFromEvent()
    {
        IsEventPaused = false;
        if (showDebugInfo) Debug.Log("GameManager: Resumed from event.");
    }

    // Stub — flesh out in Step 4 when action interruption is implemented
    public void AbortCurrentAction()
    {
        if (showDebugInfo) Debug.Log("GameManager: Action aborted by event.");
        // TODO: apply partial tile progress here
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

    void HandleGameOver()
    {
        TriggerGameOver("Field Season Complete", GetThrivingTileCount());
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
            researchPoints = resourceManager.ResearchPoints,
        };

        // Tile counts — reuses existing threshold fields on this class
        ComputeTileCounts(out data.thrivingCount, out data.degradedCount, out data.criticalCount);
        data.snapshot       = snapshot;
        data.aziSummaryLine = summary.aziLine;
        data.xpEarned       = summary.xpEarned;
        data.xpBefore       = summary.xpBefore;

        // Top worker by actions participated
        var allWorkers = resourceManager.AllWorkers;
        if (allWorkers != null && allWorkers.Count > 0)
            data.topWorker = allWorkers.OrderByDescending(w => w.actionsParticipated).First();

        // Action stats — only actions the player actually used are in this dict,
        // so "most avoided" = least-used of tried actions (never-touched actions excluded)
        var counts = actionManager.actionUsageCounts;
        if (counts.Count > 0)
        {
            data.favouriteAction   = counts.OrderByDescending(kvp => kvp.Value).First().Key;
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
        CheckCollapseCondition();
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
            resourceManager.OnGameOver -= HandleGameOver;
        }

        snapshot.Dispose();
    }
    
    
    // Public properties
    public TileManager TileManager => tileManager;
    public float DiffusionRate => diffusionRate;
    public int CascadeIterations => cascadeIterations;
}