using UnityEngine;

/// <summary>
/// Visual states for tile selection and interaction.
/// </summary>
public enum TileVisualState
{
    Default,            // Normal health-based color
    Hover,              // Faint white glow (hover in single-select)
    Selected,           // Translucent cyan (multi-select selected)
    Adjacent,           // Yellowish-white (adjacent available tile)
    AdjacentHover,       // Adjacent + hover combined (brighter)
    RegionHighlight,
    RegionDimmed
}

/// <summary>
/// Manages tile visual appearance based on health and selection state.
/// Uses color tinting on a single material instance.
/// </summary>
public class TileVisualizer : MonoBehaviour 
{
    [Header("Rendering")]
    [SerializeField] private MeshRenderer meshRenderer;
    
    [Header("Firebreak Visual")]
    [SerializeField] private GameObject firebreakPrefab;
    private GameObject firebreakInstance;
    
    private Tile tile;
    private TileVisualState currentState = TileVisualState.Default;
    private Material materialInstance;
    
    void Awake() 
    {
        if (meshRenderer == null) 
        {
            meshRenderer = GetComponent<MeshRenderer>();
        }
        
        // Create material instance to avoid shared material pollution
        if (meshRenderer != null)
        {
            materialInstance = meshRenderer.material; // This creates instance
        }
    }
    
    public void Initialize(Tile tileData) 
    {
        tile = tileData;
        SetVisualState(TileVisualState.Default);
        UpdateVisuals();
    }
    
    /// <summary>
    /// Updates the tile's color based on health (for Default state).
    /// </summary>
    public void UpdateVisuals() 
    {
        if (tile == null || materialInstance == null) return;
        
        // If in default state, update to current health color
        if (currentState == TileVisualState.Default       ||
            currentState == TileVisualState.RegionHighlight ||
            currentState == TileVisualState.RegionDimmed)
        {
            UpdateMaterial();
        }
    }
    
    /// <summary>
    /// Sets the visual state and updates the material accordingly.
    /// </summary>
    public void SetVisualState(TileVisualState state)
    {
        currentState = state;
        UpdateMaterial();
    }
    
    /// <summary>
    /// Applies the correct color based on current state.
    /// </summary>
    void UpdateMaterial()
    {
        if (meshRenderer == null || materialInstance == null || tile == null) return;
        
        Color baseColor = GetHealthColor(tile.CalculateHealth());
        Color finalColor;
        
        switch (currentState)
        {
            case TileVisualState.Hover:
                // Faint white glow (single-select hover)
                finalColor = Color.Lerp(baseColor, Color.white, 0.8f);
                break;
                
            case TileVisualState.Selected:
                // Cyan highlight
                finalColor = Color.Lerp(baseColor, new Color(0.8f, 0.3f, 0.2f, 1f), 0.7f);
                break;
                
            case TileVisualState.Adjacent:
                // Yellowish-white for adjacent available tiles
                finalColor = Color.Lerp(baseColor, new Color(1f, 1f, 1f), 0.5f);
                break;
                
            case TileVisualState.AdjacentHover:
                // Brighter yellowish-white when hovering over adjacent tile
                finalColor = Color.Lerp(baseColor, new Color(1f, 1f, 1f), 0.7f);
                break;
            
            case TileVisualState.RegionHighlight:
                // Brightened version of health color — pop the region tiles forward
                finalColor = Color.Lerp(baseColor, Color.white, 0.35f);
                break;

            case TileVisualState.RegionDimmed:
                // Heavily darkened — push non-region tiles to background
                finalColor = Color.Lerp(baseColor, Color.black, 0.6f);
                break;
                
            default: // TileVisualState.Default
                finalColor = baseColor;
                break;
        }
        
        materialInstance.color = finalColor;
    }
    
    /// <summary>
    /// Shows or hides the firebreak model on this tile.
    /// Called automatically when tile.stats.hasFirebreak changes.
    /// </summary>
    public void UpdateFirebreakVisual(bool hasFirebreak)
    {
        if (hasFirebreak && firebreakInstance == null)
        {
            // Spawn firebreak model
            if (firebreakPrefab != null)
            {
                firebreakInstance = Instantiate(firebreakPrefab, transform);
                firebreakInstance.transform.localPosition = new Vector3(0, 2, 0); // Sits on tile surface
                firebreakInstance.transform.localRotation = Quaternion.identity;
                firebreakInstance.name = "Firebreak";
            }
            else
            {
                Debug.LogWarning("Firebreak prefab not assigned in TileVisualizer!");
            }
        }
        else if (!hasFirebreak && firebreakInstance != null)
        {
            // Destroy firebreak model
            Destroy(firebreakInstance);
            firebreakInstance = null;
        }
    }
    
    /// <summary>
    /// Calculates health-based color (red < 33% < yellow < 67% < green).
    /// </summary>
    Color GetHealthColor(float health)
    {
        if (health < 33f)
        {
            return new Color(0.8f, 0.2f, 0.2f); // Red (Critical)
        }
        else if (health < 67f)
        {
            return new Color(0.9f, 0.8f, 0.3f); // Yellow (Degraded)
        }
        else
        {
            return new Color(0.3f, 0.8f, 0.3f); // Green (Thriving)
        }
    }
    
    /// <summary>
    /// Gets the base health color (for compatibility).
    /// </summary>
    public Color GetBaseColor()
    {
        if (tile == null) return Color.white;
        return GetHealthColor(tile.CalculateHealth());
    }
    
    // Getters
    public Tile GetTileData() => tile;
    public TileVisualState GetCurrentState() => currentState;
    
    void OnDestroy() 
    {
        if (materialInstance != null) 
        {
            Destroy(materialInstance);
        }
        
        if (firebreakInstance != null)
        {
            Destroy(firebreakInstance);
        }
    }
}
