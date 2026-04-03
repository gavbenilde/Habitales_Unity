/// <summary>
/// States for the action panel UI navigation.
/// </summary>
public enum ActionPanelState
{
    Hidden,          // Panel not visible
    CategorySelect,  // Showing 4 category icons
    ActionList,      // Showing actions in selected category
    VariantSelect,  // sub-panel showing variants for a grouped action
    MultiSelect      // Multi-select mode active
}