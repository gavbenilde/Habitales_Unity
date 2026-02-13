using UnityEngine;
using System;
using System.Collections.Generic;

public class ActionManager : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;

    private TileManager tileManager;
    private List<PlayerAction> availableActions = new List<PlayerAction>(); // ← ADD THIS

    // Event fired when action completes successfully
    public event Action<Tile> OnActionCompleted;

    void Awake()
    {
        tileManager = FindObjectOfType<TileManager>();
        if (tileManager == null)
        {
            Debug.LogError("ActionManager requires TileManager in scene!");
        }
        
        // ← ADD THIS: Register all actions
        RegisterActions();
    }
    
    // ← ADD THIS METHOD
    void RegisterActions()
    {
        availableActions.Clear();
        availableActions.Add(new ApplyFertilizerAction());
        availableActions.Add(new PlantTreesAction());
        availableActions.Add(new FireSuppressionAction());
        
        Debug.Log($"✓ ActionManager registered {availableActions.Count} actions");
    }
    
    // ← ADD THIS: Public getter
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
        {
            Debug.Log($"═══ ACTION: {action.ActionName} on {targetTiles.Count} tiles ═══");
        }

        // Check if we can afford it
        ResourceManager rm = ResourceManager.Instance;
        int availablePeople = rm.AvailablePeople;
        int maxTiles = action.GetMaxTiles(availablePeople);
        
        if (targetTiles.Count > maxTiles)
        {
            Debug.LogWarning($"Not enough people! Need {action.MinPeoplePerTile * targetTiles.Count}, have {availablePeople}");
            return;
        }
        
        // Calculate time cost
        int days = action.CalculateDays(availablePeople, targetTiles.Count);
        
        if (showDebugInfo)
        {
            Debug.Log($"Tiles: {targetTiles.Count} | People: {availablePeople} | Days: {days}");
        }

        // Execute the action
        bool success = action.Execute(targetTiles, tileManager);
        
        if (!success)
        {
            Debug.LogWarning($"Action '{action.ActionName}' failed!");
            return;
        }

        // Spend resources
        rm.AdvanceTime(days);
        rm.ApplyFatigue(targetTiles.Count, days, action.FatigueMultiplierPerTile);

        // Notify GameManager that action completed
        OnActionCompleted?.Invoke(targetTiles[0]);
    }
}
