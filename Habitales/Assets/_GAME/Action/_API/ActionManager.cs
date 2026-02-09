using UnityEngine;
using System;

/// <summary>
/// Executes player actions on individual tiles.
/// Does NOT handle cascading or game progression - that's GameManager's job.
/// </summary>
public class ActionManager : MonoBehaviour {
    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;
    
    private TileManager tileManager;
    
    // Event fired when action completes successfully
    public event Action<Tile> OnActionCompleted;
    
    void Awake() {
        tileManager = FindObjectOfType<TileManager>();
        if (tileManager == null) {
            Debug.LogError("ActionManager requires TileManager in scene!");
        }
    }
    
    /// <summary>
    /// Executes a single action on a single tile.
    /// Fires OnActionCompleted event if successful.
    /// </summary>
    public void ExecuteAction(PlayerAction action, Tile targetTile) {
        if (action == null || targetTile == null || tileManager == null) {
            Debug.LogError("Cannot execute action - missing components!");
            return;
        }
        
        if (showDebugInfo) {
            Debug.Log($"═══ ACTION: {action.ActionName} on {targetTile.gridPosition} ═══");
        }
        
        // Execute the action
        bool success = action.Execute(targetTile, tileManager);
        
        if (!success) {
            Debug.LogWarning($"Action '{action.ActionName}' failed!");
            return;
        }
        
        // Notify GameManager that action completed
        OnActionCompleted?.Invoke(targetTile);
    }
}