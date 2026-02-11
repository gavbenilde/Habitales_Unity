using System;
using System.Collections.Generic;
using UnityEngine;

public class ActionManager : MonoBehaviour
{
    [SerializeField] private TileManager tileManager;
    
    public event Action<Tile> OnActionCompleted;
    
    private List<PlayerAction> availableActions = new List<PlayerAction>();
    
    void Start()
    {
        // Register all actions
        availableActions.Add(new PlantTreesAction());
        availableActions.Add(new FireSuppressionAction());
        // ... other actions
    }
    
    /// <summary>
    /// Executes an action with resource management.
    /// </summary>
    public void ExecuteAction(PlayerAction action, List<Tile> targetTiles)
    {
        if (!action.CanExecute(targetTiles))
        {
            Debug.LogWarning($"Action {action.ActionName} cannot be executed on selected tiles!");
            return;
        }
        
        ResourceManager rm = ResourceManager.Instance;
        int availablePeople = rm.AvailablePeople;
        int days = action.CalculateDays(availablePeople, targetTiles.Count);
        
        Debug.Log($"─── Executing {action.ActionName} ───");
        Debug.Log($"Tiles: {targetTiles.Count} | People: {availablePeople} | Days: {days}");
        
        // Execute action logic
        bool success = action.Execute(targetTiles, tileManager);
        
        if (success)
        {
            // Advance time
            rm.AdvanceTime(days);
            
            // Apply fatigue
            rm.ApplyFatigue(targetTiles.Count, days, action.FatigueMultiplierPerTile);
            
            // Trigger world update (cascade, entities, etc.)
            OnActionCompleted?.Invoke(targetTiles[0]);
        }
    }
    
    public List<PlayerAction> GetAvailableActions() => availableActions;
}