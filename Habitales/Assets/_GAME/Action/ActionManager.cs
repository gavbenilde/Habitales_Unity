using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using Habitales.Actions;
using Random = UnityEngine.Random;

[DefaultExecutionOrder(-100)] // manager — initializes after core services (arch §4 init order)
public class ActionManager : MonoBehaviour
{
    // Global flat offset added to every action's computed duration. Prototype
    // pacing knob — every action takes +N days longer than its CalculateDays result.
    private const int kActionDurationBonusDays = 2;

    [Header("Authored Actions")]
    [Tooltip("Every data-driven ActionSO (via the Action Creator) is registered from here.")]
    [SerializeField] private ActionRegistry actionRegistry;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;

    private TileManager tileManager;
    private List<PlayerAction> availableActions = new List<PlayerAction>();

    public event Action<Tile, int> OnActionCompleted;

    /// <summary>
    /// Fires at the TOP of each day-loop iteration in FinishAction, before that day's per-tile
    /// effects run — carries the batch of tiles being worked THIS day (Walkers handoff §5). The
    /// first fire doubles as the avalanche-start signal; IsActionRunning already covers plain
    /// state checks, so there is no separate "action started" event. Never fires again on abort —
    /// OnActionCompleted (fired on finish AND abort) covers the exit in both cases.
    /// </summary>
    public event Action<IReadOnlyList<Tile>> OnActionDayStarted;

    public Dictionary<string, int> actionUsageCounts = new Dictionary<string, int>();

    /// <summary>
    /// Singleton — exactly one ActionManager per scene. Read anywhere via ActionManager.Instance
    /// (Law 1 / S4). RunManager already reads it this way in ExecuteAction's pause-guard.
    /// </summary>
    public static ActionManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Progression load now has a single owner: RunEndCoordinator.Start(). The cube-era
        // eager Load here (to hydrate GeneratedPlantRegistry before RegisterActions) is gone.
        RegisterActions();
    }

    void Start()
    {
        // TileManager is a singleton now — resolve it after all Awakes have run (order-safe),
        // replacing the old FindObjectOfType. Only used at action time, so Start is early enough.
        tileManager = TileManager.Instance;
        if (tileManager == null)
            Debug.LogError("ActionManager requires a TileManager in the scene!", this);
    }

    void RegisterActions()
    {
        availableActions.Clear();

        // ── DORMANT (post-prototype — kept in code per CLAUDE.md §7) ─────────
        // All hardcoded code-defined actions are now superseded by data-driven ActionSOs
        // (the Action Creator). Classes stay on disk; registrations are off so only authored
        // SO actions show in the UI. Re-enable a line to revive that legacy action.
        // availableActions.Add(new PlantTreesAction());                   // Intervene (superseded by SO actions)
        // availableActions.Add(new ApplyFertilizerAction());             // Intervene
        // availableActions.Add(new ClearTrashAction());                  // Cleanup
        // availableActions.Add(new StumpDeadTreeRemovalAction());        // Cleanup
        // availableActions.Add(new InspectTrashAction());                // Examine
        // Emergency category retired to 3 groups; the two fire actions are dormant for now.
        // availableActions.Add(new FireSuppressionAction());
        // availableActions.Add(new CreateFirebreakAction());
        // availableActions.Add(new AnalyzeSoilSampleAction());
        // availableActions.Add(new EcologicalSurveyAction());

        // ── AUTHORED (data-driven via the Action Creator) ───────────────────
        RegisterAuthoredActions();

        Debug.Log($"✓ ActionManager registered {availableActions.Count} actions");
    }

    // Wraps every ActionSO in the registry as a GenericPlayerAction so author-created
    // actions become playable with no bespoke C# subclass.
    private void RegisterAuthoredActions()
    {
        if (actionRegistry == null) return;
        if (!actionRegistry.ValidateAll())
            Debug.LogError("ActionManager: ActionRegistry failed validation — see errors above.", this);

        foreach (ActionSO def in actionRegistry.actions)
        {
            if (def == null) continue;
            availableActions.Add(new GenericPlayerAction(def));
        }
    }

    public bool IsActionRunning { get; private set; } = false;

    // Handle + bookkeeping for the in-flight FinishAction coroutine — needed so
    // AbortCurrentAction can both StopCoroutine it and run the same finalize path
    // FinishAction would have run on a clean finish (fields captured at ExecuteAction time).
    private Coroutine currentActionCoroutine;
    private PlayerAction currentAction;
    private List<Tile> currentTargetTiles;
    private int currentAssignedPeople;
    private int currentDaysPassed; // updated as each day resolves; read by an in-flight abort

    public List<PlayerAction> GetAvailableActions() => availableActions;

    /// <summary>
    /// Starts <paramref name="action"/> on <paramref name="targetTiles"/>. Returns false when
    /// the action was REFUSED (missing components, event pause, workforce, CanExecute) so the
    /// caller can react — ActionBarUI must not reset its UI as if the action ran (the old void
    /// signature made every refusal a silent swallow; 2026-07-19 fix).
    /// </summary>
    public bool ExecuteAction(PlayerAction action, List<Tile> targetTiles)
    {
        if (action == null || targetTiles == null || targetTiles.Count == 0 || tileManager == null)
        {
            Debug.LogError("Cannot execute action — missing components!");
            return false;
        }
        
        // Pause entry-guard (arch §3.4): do not START a new action while an active event
        // popup has the simulation paused. Previously only HandleActionCompleted was guarded,
        // not action entry — this closes the documented interrupt-before-action gap.
        // (References IsEventPaused today; becomes IsSimulationPaused when RunManager is ported in Phase 3.)
        if (RunManager.Instance != null && RunManager.Instance.IsEventPaused)
        {
            Debug.LogWarning($"Cannot execute action {action.ActionName} — simulation is paused by an active event.");
            return false;
        }

        ResourceManager rm = ResourceManager.Instance;
        int availablePeople = rm.AvailablePeople; // Capture exact workforce
        int maxTiles = action.GetMaxTiles(availablePeople);

        if (targetTiles.Count > maxTiles)
        {
            Debug.LogWarning($"Not enough people! Need {action.MinPeoplePerTile * targetTiles.Count}, have {availablePeople}");
            return false;
        }

        bool success = action.Execute(targetTiles, tileManager);
        if (!success)
        {
            Debug.LogWarning($"Action {action.ActionName} blocked by CanExecute.");
            return false;
        }

        int baseDays = action.CalculateDays(availablePeople, targetTiles.Count);
        float weatherMult = WeatherManager.Instance != null
            ? WeatherManager.Instance.GetWorkSpeedMultiplier()
            : 1f;
        int days = Mathf.Max(1, Mathf.RoundToInt(baseDays * weatherMult)); 
                   // + kActionDurationBonusDays;

        if (showDebugInfo)
            Debug.Log($"ACTION: {action.ActionName} | Tiles: {targetTiles.Count} | People: {availablePeople} | Days: {baseDays} → {days}");

        // action SFX
        if (action.ActionId == "remove_trash")
        {
            FMODUnity.EventReference ev = FMODEvents.instance.cleanup;
            
            float randomPitch = Random.Range(0.9f, 1.1f);

            AudioManager.instance.PlayOneShot(ev, Vector3.zero, randomPitch);
        }
        
        IsActionRunning = true;
        DayNightCycleHandler dayNight = DayNightCycleHandler.Instance;
        dayNight?.ResetForNewAction();

        // Captured so an abort (which enters via a separate public method, not the coroutine
        // itself) can run the same finalize path FinishAction uses on a clean finish.
        currentAction         = action;
        currentTargetTiles    = targetTiles;
        currentAssignedPeople = availablePeople;
        currentDaysPassed     = 0;

        // Pass assigned people into the Coroutine
        currentActionCoroutine = StartCoroutine(FinishAction(rm, action, targetTiles, days, availablePeople));
        return true;
    }

    private IEnumerator FinishAction(ResourceManager rm, PlayerAction action, List<Tile> targetTiles, int days, int assignedPeople)
    {
        int totalTiles       = targetTiles.Count;
        int baseTilesPerDay  = totalTiles / days;
        int remainder        = totalTiles % days;
        int processedTiles   = 0;

        for (int day = 0; day < days; day++)
        {
            // Apply THIS day's batch of tile effects FIRST, so the day-advance that follows
            // cascades stats that already include the action's work (arch §2.1 ordering).
            int tilesThisDay = baseTilesPerDay + (day < remainder ? 1 : 0);

            // Slice out today's batch before any per-tile effect runs, so subscribers (the
            // Walker system's Working avalanche) see the same batch ExecuteOnTile is about to
            // process (Walkers handoff §5 — fired before per-tile effects).
            int batchCount = Mathf.Min(tilesThisDay, totalTiles - processedTiles);
            List<Tile> dayBatch = targetTiles.GetRange(processedTiles, Mathf.Max(0, batchCount));
            OnActionDayStarted?.Invoke(dayBatch);

            for (int i = 0; i < tilesThisDay && processedTiles < totalTiles; i++)
            {
                Tile tile = targetTiles[processedTiles];
                action.ExecuteOnTile(tile, tileManager);
                tileManager.ResetTileDecay(tile); // working a tile resets its neglect decay to DecayStart

                Vector3 pos = tileManager.GridToWorldPosition(tile.gridPosition);
                VFXManager.Instance.SpawnVFX("Default", pos);

                tileManager.UpdateTileVisual(tile);
                processedTiles++;
            }

            // Then advance one day → heartbeat runs: entities tick, cascade (which now sees
            // the effects applied above), thresholds, visuals refresh, OnDayResolved (arch §2.1).
            yield return StartCoroutine(rm.AdvanceTimeStepped(1));
            currentDaysPassed++; // also read by AbortCurrentAction if it fires between days
        }

        FinalizeAction(action, targetTiles, days, assignedPeople, currentDaysPassed, processedTiles, totalTiles, aborted: false);
    }

    /// <summary>
    /// Shared tail of a clean finish and an abort: fatigue application, usage-count bookkeeping,
    /// state reset, and the OnActionCompleted notification. Examine-result popups are
    /// finish-only (see call sites) — showing "results" for tiles the action never touched
    /// mid-abort would be misleading, so that block only runs from the clean-finish path.
    /// </summary>
    private void FinalizeAction(PlayerAction action, List<Tile> targetTiles, int days, int assignedPeople,
        int actualDaysPassed, int processedTiles, int totalTiles, bool aborted)
    {
        if (actualDaysPassed > 0)
        {
            int minRequiredTotal = targetTiles.Count * action.MinPeoplePerTile;
            float exertion = (float)minRequiredTotal / assignedPeople;
            ResourceManager.Instance?.ApplyFatigue(assignedPeople, actualDaysPassed, action.FatigueMultiplierPerTile, exertion);
        }

        // Keyed off Category (not concrete class) so authored Examine ActionSOs fire the
        // popup too; the action itself declares which summary to build (ExamineReport).
        if (!aborted && action.Category == ActionCategory.Examine && ExamineResultPopupUI.Instance != null)
            ExamineResultPopupUI.Instance.ShowExamineResult(targetTiles, action.ExamineReport);

        if (!actionUsageCounts.ContainsKey(action.ActionName))
            actionUsageCounts[action.ActionName] = 0;
        actionUsageCounts[action.ActionName]++;

        IsActionRunning         = false;
        currentActionCoroutine  = null;
        currentAction           = null;
        currentTargetTiles      = null;
        // Fired on abort too: every current subscriber tolerates it (RunManager's handler is a
        // documented no-op; GameplayHudGate drives off IsActionRunning, not this event;
        // DragGhostInset just decrements an onboarding replay counter — treating an aborted
        // demo action as "completed" there is harmless). Firing it keeps UI/selection unblocked
        // via the exact same path as a clean finish instead of a second bespoke one.
        OnActionCompleted?.Invoke(targetTiles[0], days);

        if (showDebugInfo)
        {
            string tag = aborted ? "aborted" : "complete";
            Debug.Log($"✓ {action.ActionName} {tag} — {processedTiles}/{totalTiles} tiles processed, {actualDaysPassed} day(s) resolved.");
        }
    }

    /// <summary>
    /// Aborts the in-flight multi-day action. Days already resolved keep their applied effects
    /// (fatigue included, via the same FinalizeAction tail a clean finish uses); the remaining
    /// days are cancelled. Intended for event interrupts that must reclaim the workforce
    /// mid-action.
    ///
    /// Safety note: FinishAction only ever yields inside ResourceManager.AdvanceTimeStepped's
    /// WaitUntil(DayNightCycleHandler.IsIdle) — a wait on a coroutine owned by
    /// DayNightCycleHandler, not by this one. StopCoroutine here only stops OUR coroutine;
    /// DayNightCycleHandler's own RunCycles keeps running independently and still flips IsIdle
    /// back to true on its own, so nothing is left waiting forever and the day/night visuals
    /// don't get stuck. Because FinishAction never yields mid-tile-processing (only between
    /// days), stopping it can only ever land on a day boundary — exactly matching the
    /// "days already resolved keep their effects" contract; there is no partial-day state to
    /// unwind, so abort takes effect immediately rather than deferring to the next day boundary.
    /// </summary>
    public void AbortCurrentAction()
    {
        if (!IsActionRunning || currentActionCoroutine == null)
        {
            Debug.LogWarning("ActionManager.AbortCurrentAction: no action is currently running — nothing to abort.");
            return;
        }

        StopCoroutine(currentActionCoroutine);

        PlayerAction abortedAction = currentAction;
        List<Tile> abortedTiles    = currentTargetTiles;
        int assignedPeople         = currentAssignedPeople;
        int daysPassed             = currentDaysPassed;

        currentActionCoroutine = null;

        FinalizeAction(abortedAction, abortedTiles, daysPassed, assignedPeople, daysPassed,
            processedTiles: 0, totalTiles: abortedTiles.Count, aborted: true);
    }
}
