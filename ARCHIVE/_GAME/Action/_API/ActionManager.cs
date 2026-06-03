using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class ActionManager : MonoBehaviour
{
    // Global flat offset added to every action's computed duration. Prototype
    // pacing knob — every action takes +N days longer than its CalculateDays result.
    private const int kActionDurationBonusDays = 2;

    [Header("Planting")]
    [SerializeField] private PlantingProfileSO[] starterProfiles;
    [SerializeField] private PlayerProgressionSO playerProgression;
    // Base material applied to every planted cube (starters + procedural).
    // Assigned as a real asset reference so Unity's build dependency scanner
    // bundles its shader — the runtime CreatePrimitive default is not tracked
    // and renders magenta/invisible in builds. Use a URP/Lit material.
    [SerializeField] private Material plantCubeMaterial;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;

    private TileManager tileManager;
    private List<PlayerAction> availableActions = new List<PlayerAction>();

    public event Action<Tile, int> OnActionCompleted;

    public Dictionary<string, int> actionUsageCounts = new Dictionary<string, int>();

    void Awake()
    {
        tileManager = FindObjectOfType<TileManager>();
        if (tileManager == null)
            Debug.LogError("ActionManager requires TileManager in scene!");

        // Eagerly load progression so the GeneratedPlantRegistry is hydrated before
        // RegisterActions() walks it. RunManager.Start() also calls Load() — the call
        // is idempotent (just reads JSON), so the duplicate is harmless and avoids
        // an Awake/Start ordering dependency between the two MonoBehaviours.
        ProgressionPersistence.Load(playerProgression);

        RegisterActions();
    }

    void RegisterActions()
    {
        availableActions.Clear();

        // ── ACTIVE (prototype) ────────────────────────────────────────────────
        availableActions.Add(new FireSuppressionAction());                 // Emergency

        if (starterProfiles != null)
            foreach (var p in starterProfiles)
                if (p != null) availableActions.Add(new PlantingAction(p, plantCubeMaterial, playerProgression));  // Intervene (4 starters)

        // Procedurally-unlocked plants from prior runs (re-hydrated on Load).
        foreach (var generated in GeneratedPlantRegistry.All)
            availableActions.Add(new PlantingAction(generated, plantCubeMaterial, playerProgression));

        availableActions.Add(new RemoveWitheredAction());                  // Cleanup

        // ── DORMANT (post-prototype — kept in code per CLAUDE.md §7) ─────────
        // availableActions.Add(new ApplyFertilizerAction());
        // availableActions.Add(new PlantTreesAction());
        // availableActions.Add(new CreateFirebreakAction());
        // availableActions.Add(new ClearTrashAction());
        // availableActions.Add(new StumpDeadTreeRemovalAction());
        // availableActions.Add(new AnalyzeSoilSampleAction());
        // availableActions.Add(new InspectTrashAction());
        // availableActions.Add(new EcologicalSurveyAction());
        // availableActions.Add(new CoverCroppingAction { cropVariant = CoverCroppingAction.Variant.Legume });
        // availableActions.Add(new CoverCroppingAction { cropVariant = CoverCroppingAction.Variant.Grass });
        // availableActions.Add(new CoverCroppingAction { cropVariant = CoverCroppingAction.Variant.Phyto });

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
            yield return StartCoroutine(rm.AdvanceTimeStepped(1));
            actualDaysPassed++;

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
