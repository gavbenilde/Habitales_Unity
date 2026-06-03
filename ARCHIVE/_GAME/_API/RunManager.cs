using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Habitales.Dialogue;

/// <summary>
/// Central game loop coordinator.  
/// Handles: Action execution → Tile cascading → Zone health checks → Zone generation triggers
/// </summary>
public class RunManager : MonoBehaviour {
    [Header("System References")]
    [SerializeField] private TileManager tileManager;
    [SerializeField] private TileSelector tileSelector;
    [SerializeField] private ActionUI actionUI; // DORMANT — replaced by actionBarUI; leave field but unwire in inspector.
    [SerializeField] private ActionBarUI actionBarUI;
    [SerializeField] private ActionManager actionManager;
    [SerializeField] private ZoneManager zoneManager;
    [SerializeField] private DialogueManager dialogueManager;
    [SerializeField] private ResourceManager resourceManager;
    [SerializeField] private RegionOutlineRenderer regionOutlineRenderer;
    
    [SerializeField] private EndGameScreenUI     endGameScreenUI;
    [SerializeField] private AziSpeechBubbleUI   aziSpeechBubbleUI;
    [SerializeField] private PlayerProgressionSO playerProgressionAsset;

    
    [Header("Cascade Settings")]
    [SerializeField] [Range(0.05f, 0.5f)] private float diffusionRate = 0.15f;
    [SerializeField] [Tooltip("How many cascade iterations per action")]
    private int cascadeIterations = 3;
    
    [Header("Zone Progression")]
    [SerializeField] [Range(0f, 100f)] private float zoneUnlockThreshold = 80f;

    /// <summary>World-health % at which the next zone becomes unlockable. Single source of truth for the health-bar marker and the unlock button.</summary>
    public float ZoneUnlockThreshold => zoneUnlockThreshold;

    /// <summary>Fired once when world health first crosses the threshold and a zone is awaiting manual unlock. Subscribers: unlock button, objective banner.</summary>
    public event System.Action OnZoneUnlockReady;
    /// <summary>Fired after the player presses the button and the next zone is generated.</summary>
    public event System.Action OnZoneUnlocked;

    // True once the threshold is crossed and we are waiting on the player's button press.
    private bool zoneUnlockPending = false;

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
    [SerializeField] private ZoneProfile zone1Profile;
    
    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;

    [SerializeField] private bool isPrototypeRun = true;
    [SerializeField] private int prototypeRunDays = 60;

    // Peak thriving — high-water mark across the run; drives run-end score + snapshot.
    private readonly RunSnapshot snapshot = new();
    private int runXpBefore = 0;
    private int runXpEarned = 0;

    [Header("XP Formula (per-tile weights, applied at run-end)")]
    [SerializeField] private float xpPerCriticalTile  = 0.1f;
    [SerializeField] private float xpPerDegradedTile  = 0.6f;
    [SerializeField] private float xpPerThrivingTile  = 2.4f;

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

        ProgressionPersistence.Load(playerProgressionAsset);

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
                ProgressionPersistence.Reset(playerProgressionAsset);
            }
    #endif
    }
    
    void InitializeSystems() {
        // Auto-find systems if not assigned
        if (tileManager == null) {
            tileManager = FindObjectOfType<TileManager>();
            if (tileManager == null) Debug.LogError("TileManager not found!");
        }
        
        if (tileSelector == null) {
            tileSelector = GetComponent<TileSelector>();
            if (tileSelector == null) Debug.LogError("TileSelector missing!");
        }
        
        // actionUI is dormant; skip auto-find to suppress the "not found" warning.

        if (actionBarUI == null)
            actionBarUI = FindObjectOfType<ActionBarUI>();
        
        if (actionManager == null) {
            actionManager = FindObjectOfType<ActionManager>();
            if (actionManager == null) Debug.LogError("ActionManager not found!");
        }
        
        if (zoneManager == null) {
            zoneManager = FindObjectOfType<ZoneManager>();
            if (zoneManager == null) Debug.LogError("ZoneManager not found!");
        }
        
        if (resourceManager == null)
        {
            resourceManager = FindObjectOfType<ResourceManager>();
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
            Debug.Log($"GameManager initialized | Systems: TileManager={tileManager != null}, TileSelector={tileSelector != null}, ActionBarUI={actionBarUI != null}, ActionManager={actionManager != null}, ZoneManager={zoneManager != null}");
        }
    }
    
    void SpawnInitialZone()
    {
        Debug.Log("Generating initial Zone 1...");

        ZoneGenerationResult result = zoneManager.GenerateInitialZone(initialZoneOrigin, zone1Profile);

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
    /// CORE GAME LOOP: Called after any action completes.
    /// 1. Cascade tile updates in the region
    /// 2. Check zone health
    /// 3. Trigger zone generation if threshold met
    /// </summary>
    void HandleActionCompleted(Tile targetTile, int daysElapsed)
    {
        if (targetTile == null || isGameOver || IsEventPaused) return;

        if (showDebugInfo)
            Debug.Log($"─── Action Complete — Finalizing World ───");

        // Cascade runs once per action (stat diffusion between neighbours)
        CascadeTileUpdates();

        // Final visual pass after cascade has adjusted stats
        foreach (Tile tile in tileManager.GetAllTiles())
            tileManager.UpdateTileVisual(tile);

        CheckCollapseCondition();

        float totalAverageHealth = zoneManager.GetTotalAverageHealth();
        if (showDebugInfo)
            Debug.Log($"Total World Health: {totalAverageHealth:F1} / Unlock Threshold: {zoneUnlockThreshold}");

        TryFlagZoneUnlock(totalAverageHealth);

        EvaluateThrivingPeak();

        if (showDebugInfo)
            Debug.Log($"═══ UPDATE COMPLETE ═══\n");
    }

    /// <summary>
    /// Flags the next zone as unlockable when world health crosses the threshold.
    /// Does NOT generate the zone — that waits on the player pressing the unlock
    /// button (see UnlockNextZone). Guarded so the event fires at most once per
    /// pending unlock. Called from both the per-action path and the debug F1 path.
    /// </summary>
    void TryFlagZoneUnlock(float totalAverageHealth)
    {
        if (zoneUnlockPending) return;
        if (totalAverageHealth < zoneUnlockThreshold) return;
        if (unlockedRegions.Contains(zoneManager.NextRegionID - 1)) return;

        zoneUnlockPending = true;
        Debug.Log($"ZONE UNLOCK READY! World avg health {totalAverageHealth:F1} passed threshold {zoneUnlockThreshold}. Awaiting player.");
        OnZoneUnlockReady?.Invoke();
    }

    /// <summary>
    /// Player-triggered zone generation (wired to the "Unlock Next Zone" button).
    /// No-op unless a zone is currently pending. Generates exactly one zone,
    /// clears the pending flag, and fires OnZoneUnlocked.
    /// </summary>
    public void UnlockNextZone()
    {
        if (!zoneUnlockPending || isGameOver) return;

        int newRegionFrom = zoneManager.NextRegionID - 1;
        unlockedRegions.Add(newRegionFrom);
        zoneManager.GenerateNewZone(newRegionFrom);

        zoneUnlockPending = false;
        OnZoneUnlocked?.Invoke();
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

    // Grants exactly one plant unlock per level-up. Always generates a procedural
    // plant via PlantingProfileGenerator and persists it as a seed/name pair.
    // ActionManager re-hydrates these into PlantingActions via GeneratedPlantRegistry.All.
    //
    // NOTE: PlayerProgressionSO.unlockPool is DORMANT (see PlayerProgressionSO.cs §field
    // comment + CLAUDE.md §7) — its 4 entries are the same starter SOs already wired
    // into ActionManager.starterProfiles, so popping it would only re-add starter IDs to
    // unlockedPlantIds (cosmetic level-up card, no new action-bar entry). Skip it.
    private void GrantOnePlantUnlock(PlayerProgressionSO progression)
    {
        if (progression == null) return;

        // Mix UTC ticks with a Random int so consecutive level-ups in the same frame
        // still diverge.
        int seed = unchecked((int)(System.DateTime.UtcNow.Ticks ^ UnityEngine.Random.Range(int.MinValue, int.MaxValue)));

        string name = GeneratedPlantRegistry.PickUnusedName(progression.unlockedPlantNames) ?? $"Plant {seed}";
        var profile = GeneratedPlantRegistry.RegisterFromSeed(seed, name);

        progression.unlockedPlantSeeds.Add(seed);
        progression.unlockedPlantNames.Add(name);
        progression.unlockedPlantIds.Add(profile.profileID);
    }

    /// <summary>
    /// XP from a finished run = sum of per-tile-state weights. Critical tiles still grant a
    /// little (you tried), degraded tiles are the meat (the typical end-state), thriving tiles
    /// are the big reward.
    /// </summary>
    private int CalculateRunXp()
    {
        ComputeTileCounts(out int thriving, out int degraded, out int critical);
        float raw = critical * xpPerCriticalTile + degraded * xpPerDegradedTile + thriving * xpPerThrivingTile;
        int xp = Mathf.RoundToInt(raw);
        Debug.Log($"[XP] thriving={thriving}×{xpPerThrivingTile} + degraded={degraded}×{xpPerDegradedTile} + critical={critical}×{xpPerCriticalTile} → {xp} XP");
        return xp;
    }

    private string GenerateAziLine(int peak)
    {
        if (peak == 0)  return "Tough run. We didn't quite get there. Next time?";
        if (peak < 5)   return "We made a small dent. Felt like the start of something.";
        if (peak < 12)  return "Solid run, Cap. The land remembers what we did.";
        return "Look at what we built. I'm proud of us.";
    }




    /// <summary>
    /// Triggers game over. Shows Azi speech bubble first (if wired), then run-end screen.
    /// </summary>
    void TriggerGameOver(string reason, int thrivingTiles)
    {
        isGameOver = true;
        Debug.Log($"GAME OVER: {reason} | Peak Thriving: {snapshot.peakThrivingCount}");

        runXpEarned = CalculateRunXp();

        if (playerProgressionAsset != null)
        {
            runXpBefore = playerProgressionAsset.totalXp;
            int levelUps = playerProgressionAsset.AddXp(runXpEarned);
            // One procedural plant unlock per level-up; persists via seed/name pair.
            for (int i = 0; i < levelUps; i++)
                GrantOnePlantUnlock(playerProgressionAsset);
            // The level-up overlay reads lastSeen* vs current on the next fresh
            // main-menu visit, so this Save is the only handoff needed.
            ProgressionPersistence.Save(playerProgressionAsset);
        }

        EndGameData data = BuildEndGameData(reason);

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

    
    
    void HandleTimeAdvanced(int days)
    {
        if (isGameOver) return;
        if (showDebugInfo)
            Debug.Log($"⏰ Time advanced by {days} days | Now: {resourceManager.GetFullTimeDisplay()}");

        // Entity ticks happen every day — fire spread, kaingin, tree growth, etc.
        for (int d = 0; d < days; d++)
            tileManager.UpdateAllEntities();

        if (isPrototypeRun && resourceManager.TotalDays >= prototypeRunDays)
        {
            TriggerGameOver("Field Season Complete", GetThrivingTileCount());
            return;
        }

        healthHistory.Add(zoneManager.GetTotalAverageHealth());

        // update visuals
        foreach (Tile tile in tileManager.GetAllTiles())
            tileManager.UpdateTileVisual(tile);
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

    private EndGameData BuildEndGameData(string reason)
    {
        var data = new EndGameData
        {
            endReason      = reason,
            currentYear    = resourceManager.CurrentYear,
            totalDays      = resourceManager.TotalDays,
            worldHealth    = zoneManager.GetTotalAverageHealth(),
            healthHistory  = new List<float>(healthHistory),
            researchPoints = resourceManager.ResearchPoints,
        };

        // Tile counts — reuses existing threshold fields on this class
        ComputeTileCounts(out data.thrivingCount, out data.degradedCount, out data.criticalCount);
        data.snapshot       = snapshot;
        data.aziSummaryLine = GenerateAziLine(snapshot.peakThrivingCount);
        data.xpEarned       = runXpEarned;
        data.xpBefore       = runXpBefore;

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
            data.zoneHealths[regionID] = zoneManager.GetRegionHealth(regionID);

        return data;
    }

    private void ComputeTileCounts(out int thriving, out int degraded, out int critical)
    {
        thriving = degraded = critical = 0;
        foreach (var tile in tileManager.GetAllTiles())
        {
            float h = tile.CalculateHealth();
            if      (h > thrivingHealthThreshold)  thriving++;
            else if (h > criticalHealthThreshold)  degraded++;
            else                                   critical++;
        }
    }

    

    
    
    
    /// <summary>
    /// Cascades tile stat changes across a region using diffusion.
    /// Each tile lerps toward the average of its 4 neighbors.
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

        // Fires OnTimeAdvanced → HandleTimeAdvanced handles entity ticks + visuals
        resourceManager.AdvanceTime(1);

        // Cascade + endgame are per-action, not per-day — simulate them manually here
        CascadeTileUpdates();
        foreach (Tile tile in tileManager.GetAllTiles())
            tileManager.UpdateTileVisual(tile);
        CheckCollapseCondition();

        float totalAverageHealth = zoneManager.GetTotalAverageHealth();
        if (showDebugInfo)
            Debug.Log($"[DEBUG] Day passed. World Health: {totalAverageHealth:F1} | {resourceManager.GetFullTimeDisplay()}");

        TryFlagZoneUnlock(totalAverageHealth);

        EvaluateThrivingPeak();
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