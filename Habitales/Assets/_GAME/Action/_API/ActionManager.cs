using UnityEngine;
using System;
using System.Collections.Generic;

public class ActionManager : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;

    private TileManager tileManager;
    private List<PlayerAction> availableActions = new List<PlayerAction>();

    // Event fired when action completes successfully
    public event Action<Tile, int> OnActionCompleted;

    void Awake()
    {
        tileManager = FindObjectOfType<TileManager>();
        if (tileManager == null)
        {
            Debug.LogError("ActionManager requires TileManager in scene!");
        }
        
        RegisterActions();
    }
    
    void RegisterActions()
    {
        availableActions.Clear();
        availableActions.Add(new ApplyFertilizerAction());
        availableActions.Add(new PlantTreesAction());
        availableActions.Add(new FireSuppressionAction());
        availableActions.Add(new CreateFirebreakAction()); 
        
        Debug.Log($"✓ ActionManager registered {availableActions.Count} actions");
    }
    
    public List<PlayerAction> GetAvailableActions()
    {
        return availableActions;
    }

    /// <summary>
    /// Executes a single action on multiple tiles.
    /// Fires OnActionCompleted event if successful.
    /// </summary>
    public void ExecuteAction(PlayerAction action, List<Tile> targetTiles)
    {
        if (action == null || targetTiles == null || targetTiles.Count == 0 || tileManager == null)
        {
            Debug.LogError("Cannot execute action - missing components!");
            return;
        }

        if (showDebugInfo)
            Debug.Log($"ACTION: {action.ActionName} on {targetTiles.Count} tiles");

        ResourceManager rm = ResourceManager.Instance;
        int availablePeople = rm.AvailablePeople;
        int maxTiles = action.GetMaxTiles(availablePeople);
        if (targetTiles.Count > maxTiles)
        {
            Debug.LogWarning($"Not enough people! Need {action.MinPeoplePerTile * targetTiles.Count}, have {availablePeople}");
            return;
        }

        // Calculate days, then apply weather work speed multiplier
        int baseDays = action.CalculateDays(availablePeople, targetTiles.Count);
        float weatherMult = WeatherManager.Instance != null
            ? WeatherManager.Instance.GetWorkSpeedMultiplier()
            : 1f;
        int days = Mathf.Max(1, Mathf.RoundToInt(baseDays * weatherMult));

        if (showDebugInfo)
            Debug.Log($"Tiles: {targetTiles.Count} | People: {availablePeople} | Days: {baseDays} → {days} (weather ×{weatherMult:F2})");

        bool success = action.Execute(targetTiles, tileManager);
        if (!success) { Debug.LogWarning($"Action {action.ActionName} failed!"); return; }

        rm.AdvanceTime(days);
        rm.ApplyFatigue(targetTiles.Count, days, action.FatigueMultiplierPerTile);
        OnActionCompleted?.Invoke(targetTiles[0], days);
    }
}
