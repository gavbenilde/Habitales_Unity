using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Stores sprite mappings for all player actions.
/// Create via: Right-click in Project → Create → Habitales → Action Icon Config
/// </summary>
[CreateAssetMenu(fileName = "ActionIconConfig", menuName = "Habitales/Action Icon Config")]
public class ActionIconConfig : ScriptableObject
{
    [Header("Action Icon Mappings")]
    [SerializeField] private List<ActionIconMapping> iconMappings = new List<ActionIconMapping>();
    
    // Cache for faster lookups
    private Dictionary<string, Sprite> iconCache;
    
    /// <summary>
    /// Gets the sprite for a given action name.
    /// Returns null if no mapping found.
    /// </summary>
    public Sprite GetSpriteForAction(string actionName)
    {
        // Build cache on first access
        if (iconCache == null)
        {
            BuildCache();
        }
        
        if (iconCache.TryGetValue(actionName, out Sprite sprite))
        {
            return sprite;
        }
        
        Debug.LogWarning($"No sprite mapping found for action: {actionName}");
        return null;
    }
    
    /// <summary>
    /// Checks if a sprite exists for the given action.
    /// </summary>
    public bool HasSprite(string actionName)
    {
        if (iconCache == null)
        {
            BuildCache();
        }
        
        return iconCache.ContainsKey(actionName);
    }
    
    /// <summary>
    /// Builds the internal dictionary for fast lookups.
    /// </summary>
    private void BuildCache()
    {
        iconCache = new Dictionary<string, Sprite>();
        
        foreach (var mapping in iconMappings)
        {
            if (mapping.sprite != null && !string.IsNullOrEmpty(mapping.actionName))
            {
                iconCache[mapping.actionName] = mapping.sprite;
            }
        }
    }
    
    /// <summary>
    /// Editor helper to rebuild cache when values change.
    /// </summary>
    private void OnValidate()
    {
        iconCache = null; // Force rebuild on next access
    }
}

/// <summary>
/// Represents a single action name → sprite mapping.
/// </summary>
[Serializable]
public class ActionIconMapping
{
    [Tooltip("The exact name of the action (must match PlayerAction.ActionName)")]
    public string actionName;
    
    [Tooltip("The sprite to display on the action card")]
    public Sprite sprite;
}
