using UnityEngine;

/// <summary>
/// Categories for organizing player actions in the UI.
/// </summary>
public enum ActionCategory
{
    Examine,    // 🔍 Information gathering and analysis
    Intervene,  // 🛠️ Proactive long-term improvements
    Emergency,  // 🚨 Reactive crisis response
    Cleanup     // 🧹 Removal and maintenance tasks
}

/// <summary>
/// Helper methods for ActionCategory.
/// </summary>
public static class ActionCategoryExtensions
{
    public static string GetDisplayName(this ActionCategory category)
    {
        switch (category)
        {
            case ActionCategory.Examine:
                return "EXAMINE";
            case ActionCategory.Intervene:
                return "INTERVENE";
            case ActionCategory.Emergency:
                return "EMERGENCY";
            case ActionCategory.Cleanup:
                return "CLEANUP";
            default:
                return category.ToString().ToUpper();
        }
    }
    
    public static string GetDescription(this ActionCategory category)
    {
        switch (category)
        {
            case ActionCategory.Examine:
                return "Gather information about tile conditions";
            case ActionCategory.Intervene:
                return "Long-term rehabilitation actions";
            case ActionCategory.Emergency:
                return "Respond to immediate threats";
            case ActionCategory.Cleanup:
                return "Remove obstacles and contaminants";
            default:
                return "";
        }
    }
}
