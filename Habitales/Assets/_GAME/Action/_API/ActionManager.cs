using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class ActionManager : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;

    private TileManager tileManager;
    private List<PlayerAction> availableActions = new List<PlayerAction>();

    public event Action<Tile, int> OnActionCompleted;

    void Awake()
    {
        tileManager = FindObjectOfType<TileManager>();
        if (tileManager == null)
            Debug.LogError("ActionManager requires TileManager in scene!");

        RegisterActions();
    }

    void RegisterActions()
    {
        availableActions.Clear();
        availableActions.Add(new ApplyFertilizerAction());
        availableActions.Add(new PlantTreesAction());
        availableActions.Add(new FireSuppressionAction());
        availableActions.Add(new CreateFirebreakAction());
        availableActions.Add(new ClearTrashAction());
        availableActions.Add(new StumpDeadTreeRemovalAction());
        availableActions.Add(new AnalyzeSoilSampleAction());
        availableActions.Add(new InspectTrashAction());
        availableActions.Add(new EcologicalSurveyAction());
        availableActions.Add(new CoverCroppingAction { cropVariant = CoverCropEntity.CoverCropVariant.Legume });   // ← new
        availableActions.Add(new CoverCroppingAction { cropVariant = CoverCropEntity.CoverCropVariant.DeepRoot }); // ← new
        availableActions.Add(new CoverCroppingAction { cropVariant = CoverCropEntity.CoverCropVariant.General });  // ← new
        availableActions.Add(new PhytoremedationPlantingAction());                                                 // ← new
        availableActions.Add(new PlantNitrogenFixingSpeciesAction());                                              // ← new

        
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
        int availablePeople = rm.AvailablePeople;
        int maxTiles = action.GetMaxTiles(availablePeople);

        if (targetTiles.Count > maxTiles)
        {
            Debug.LogWarning($"Not enough people! Need {action.MinPeoplePerTile * targetTiles.Count}, have {availablePeople}");
            return;
        }

        // Preflight validation — Execute no longer touches tiles
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
        int days = Mathf.Max(1, Mathf.RoundToInt(baseDays * weatherMult));

        if (showDebugInfo)
            Debug.Log($"ACTION: {action.ActionName} | Tiles: {targetTiles.Count} | People: {availablePeople} | Days: {baseDays} → {days} (×{weatherMult:F2})");

        // Tell the day/night handler this is a fresh action so durations reset
        IsActionRunning = true;
        DayNightCycleHandler dayNight = FindObjectOfType<DayNightCycleHandler>();
        dayNight?.ResetForNewAction();

        StartCoroutine(FinishAction(rm, action, targetTiles, days));
    }

    private IEnumerator FinishAction(ResourceManager rm, PlayerAction action, List<Tile> targetTiles, int days)
    {
        int totalTiles     = targetTiles.Count;
        int baseTilesPerDay = totalTiles / days;
        int remainder       = totalTiles % days;
        int processedTiles  = 0;

        for (int day = 0; day < days; day++)
        {
            // Wait for one full day/night cycle to complete
            yield return StartCoroutine(rm.AdvanceTimeStepped(1));

            // Apply this day's tile batch — spreads remainder tiles across early days
            int tilesThisDay = baseTilesPerDay + (day < remainder ? 1 : 0);

            for (int i = 0; i < tilesThisDay && processedTiles < totalTiles; i++)
            {
                Tile tile = targetTiles[processedTiles];
                action.ExecuteOnTile(tile, tileManager);
                tileManager.UpdateTileVisual(tile);   // tiles light up progressively
                processedTiles++;
            }
        }

        rm.ApplyFatigue(targetTiles.Count, days, action.FatigueMultiplierPerTile);
        IsActionRunning = false;
        OnActionCompleted?.Invoke(targetTiles[0], days);

        if (showDebugInfo)
            Debug.Log($"✓ {action.ActionName} complete — {processedTiles}/{totalTiles} tiles processed.");
    }
}
