using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

[DefaultExecutionOrder(-100)] // manager — initializes after core services (arch §4 init order)
public class ActionManager : MonoBehaviour
{
    // Global flat offset added to every action's computed duration. Prototype
    // pacing knob — every action takes +N days longer than its CalculateDays result.
    private const int kActionDurationBonusDays = 2;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;

    private TileManager tileManager;
    private List<PlayerAction> availableActions = new List<PlayerAction>();

    public event Action<Tile, int> OnActionCompleted;

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

        // ── ACTIVE (prototype) ────────────────────────────────────────────────
        availableActions.Add(new FireSuppressionAction());                 // Emergency
        availableActions.Add(new PlantTreesAction());                      // Intervene (replaces the 4 cube planting actions)
        availableActions.Add(new ApplyFertilizerAction());                 // Intervene  (Phase 5)
        availableActions.Add(new ClearTrashAction());                      // Cleanup    (Phase 5)
        availableActions.Add(new StumpDeadTreeRemovalAction());            // Cleanup    (Phase 5)
        availableActions.Add(new InspectTrashAction());                    // Examine    (Phase 5)

        // ── DORMANT (post-prototype — kept in code per CLAUDE.md §7) ─────────
        // availableActions.Add(new CreateFirebreakAction());
        // availableActions.Add(new AnalyzeSoilSampleAction());
        // availableActions.Add(new EcologicalSurveyAction());

        Debug.Log($"✓ ActionManager registered {availableActions.Count} actions");
    }

    public bool IsActionRunning { get; private set; } = false;
    public List<PlayerAction> GetAvailableActions() => availableActions;

    public void ExecuteAction(PlayerAction action, List<Tile> targetTiles)
    {
        if (action == null || targetTiles == null || targetTiles.Count == 0 || tileManager == null)
        {
            Debug.LogError("Cannot execute action — missing components!");
            return;
        }

        // Pause entry-guard (arch §3.4): do not START a new action while an active event
        // popup has the simulation paused. Previously only HandleActionCompleted was guarded,
        // not action entry — this closes the documented interrupt-before-action gap.
        // (References IsEventPaused today; becomes IsSimulationPaused when RunManager is ported in Phase 3.)
        if (RunManager.Instance != null && RunManager.Instance.IsEventPaused)
        {
            Debug.LogWarning($"Cannot execute action {action.ActionName} — simulation is paused by an active event.");
            return;
        }

        ResourceManager rm = ResourceManager.Instance;
        int availablePeople = rm.AvailablePeople; // Capture exact workforce
        int maxTiles = action.GetMaxTiles(availablePeople);

        if (targetTiles.Count > maxTiles)
        {
            Debug.LogWarning($"Not enough people! Need {action.MinPeoplePerTile * targetTiles.Count}, have {availablePeople}");
            return;
        }

        bool success = action.Execute(targetTiles, tileManager);
        if (!success)
        {
            Debug.LogWarning($"Action {action.ActionName} blocked by CanExecute.");
            return;
        }

        int baseDays = action.CalculateDays(availablePeople, targetTiles.Count);
        float weatherMult = WeatherManager.Instance != null
            ? WeatherManager.Instance.GetWorkSpeedMultiplier()
            : 1f;
        int days = Mathf.Max(1, Mathf.RoundToInt(baseDays * weatherMult)) + kActionDurationBonusDays;

        if (showDebugInfo)
            Debug.Log($"ACTION: {action.ActionName} | Tiles: {targetTiles.Count} | People: {availablePeople} | Days: {baseDays} → {days}");

        IsActionRunning = true;
        DayNightCycleHandler dayNight = FindObjectOfType<DayNightCycleHandler>();
        dayNight?.ResetForNewAction();

        // Pass assigned people into the Coroutine
        StartCoroutine(FinishAction(rm, action, targetTiles, days, availablePeople));
    }

    private IEnumerator FinishAction(ResourceManager rm, PlayerAction action, List<Tile> targetTiles, int days, int assignedPeople)
    {
        int totalTiles       = targetTiles.Count;
        int baseTilesPerDay  = totalTiles / days;
        int remainder        = totalTiles % days;
        int processedTiles   = 0;
        int actualDaysPassed = 0; // Track days actually elapsed

        for (int day = 0; day < days; day++)
        {
            // Apply THIS day's batch of tile effects FIRST, so the day-advance that follows
            // cascades stats that already include the action's work (arch §2.1 ordering).
            int tilesThisDay = baseTilesPerDay + (day < remainder ? 1 : 0);

            for (int i = 0; i < tilesThisDay && processedTiles < totalTiles; i++)
            {
                Tile tile = targetTiles[processedTiles];
                action.ExecuteOnTile(tile, tileManager);

                Vector3 pos = tileManager.GridToWorldPosition(tile.gridPosition);
                VFXManager.Instance.SpawnVFX("Default", pos);

                tileManager.UpdateTileVisual(tile);
                processedTiles++;
            }

            // Then advance one day → heartbeat runs: entities tick, cascade (which now sees
            // the effects applied above), thresholds, visuals refresh, OnDayResolved (arch §2.1).
            yield return StartCoroutine(rm.AdvanceTimeStepped(1));
            actualDaysPassed++;
        }
        
        int minRequiredTotal = targetTiles.Count * action.MinPeoplePerTile;
        float exertion = (float)minRequiredTotal / assignedPeople;
        
        if (actualDaysPassed > 0)
        {
            rm.ApplyFatigue(assignedPeople, actualDaysPassed, action.FatigueMultiplierPerTile, exertion);
        }
        
        if (ExamineResultPopupUI.Instance != null)
        {
            if (action is EcologicalSurveyAction)
                ExamineResultPopupUI.Instance.ShowExamineResult(targetTiles, ExamineActionType.EcologicalSurvey);
            else if (action is AnalyzeSoilSampleAction)
                ExamineResultPopupUI.Instance.ShowExamineResult(targetTiles, ExamineActionType.SoilAnalysis);
            else if (action is InspectTrashAction)
                ExamineResultPopupUI.Instance.ShowExamineResult(targetTiles, ExamineActionType.InspectTrash);
        }
        
        if (!actionUsageCounts.ContainsKey(action.ActionName))
            actionUsageCounts[action.ActionName] = 0;
        actionUsageCounts[action.ActionName]++;

        IsActionRunning = false;
        OnActionCompleted?.Invoke(targetTiles[0], days);

        if (showDebugInfo)
            Debug.Log($"✓ {action.ActionName} complete — {processedTiles}/{totalTiles} tiles processed.");
    }
}
