using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ActionUI : MonoBehaviour
{
    [Header("Panel References")]
    [SerializeField] private RectTransform actionPanel; // Main container for both panels
    [SerializeField] private Camera mainCamera;
    
    [Header("Header Section")]
    [SerializeField] private Button backButton;
    // [SerializeField] private TextMeshProUGUI categoryTitleText;
    [SerializeField] private TextMeshProUGUI tileHealthText;
    
    [Header("Category Selection")]
    [SerializeField] private GameObject categoryIconsPanel;
    [SerializeField] private CategoryButton examineButton;
    [SerializeField] private CategoryButton interveneButton;
    [SerializeField] private CategoryButton emergencyButton;
    [SerializeField] private CategoryButton cleanupButton;
    
    [Header("Action List")]
    [SerializeField] private GameObject actionListPanel;
    [SerializeField] private Transform actionListContent;
    [SerializeField] private GameObject actionCardPrefab;
    [SerializeField] private ActionIconConfig actionIconConfig;
    
    [Header("Multi-Select UI")]
    [SerializeField] private GameObject multiSelectPanel;
    [SerializeField] private TextMeshProUGUI tileCounterText;
    [SerializeField] private TextMeshProUGUI actionNameText;
    [SerializeField] private TextMeshProUGUI daysText;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private GameObject warningIcon;
    [SerializeField] private GameObject warningTooltipPanel;
    [SerializeField] private TextMeshProUGUI warningTooltipText;
    
    [Header("FloodFill Slider")]
    [SerializeField] private GameObject floodFillSliderContainer;
    [SerializeField] private Slider floodFillSlider;
    [SerializeField] private TextMeshProUGUI floodFillSliderLabel;
   
    [Header("Positioning")]
    [SerializeField] private Vector2 screenOffset = new Vector2(150f, 0f);
    
    [Header("System References")]
    [SerializeField] private ActionManager actionManager;
    [SerializeField] private TileSelector tileSelector;
    
    // State tracking
    private ActionPanelState currentState = ActionPanelState.Hidden;
    private Tile currentTile;
    private PlayerAction currentAction;
    private ActionCategory selectedCategory;
    private List<GameObject> spawnedActionCards = new List<GameObject>();
    
    void Awake()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
        
        if (actionManager == null)
        {
            actionManager = FindObjectOfType<ActionManager>();
        }
        
        if (tileSelector == null)
        {
            tileSelector = FindObjectOfType<TileSelector>();
        }
        
        // Wire up buttons
        if (backButton != null)
        {
            backButton.onClick.AddListener(OnBackButtonClicked);
        }
        
        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(OnConfirmClicked);
        }
        
        if (cancelButton != null)
        {
            cancelButton.onClick.AddListener(OnCancelClicked);
        }
        
        // FloodFill slider
        if (floodFillSlider != null)
            floodFillSlider.onValueChanged.AddListener(OnFloodFillSliderChanged);
        
        if (warningIcon != null)
        {
            HoverTooltip tooltip = warningIcon.GetComponent<HoverTooltip>();
            if (tooltip == null)
                tooltip = warningIcon.AddComponent<HoverTooltip>();

            tooltip.Configure(
                warningTooltipPanel,
                warningTooltipText,
                "Spreading your team too thin across a large area causes fatigue."
            );
        }
        
        if (warningTooltipPanel != null)
        {
            warningTooltipPanel.SetActive(false);
        }
        
        
        // Wire up category buttons
        if (examineButton != null)
        {
            examineButton.button.onClick.AddListener(() => OnCategoryButtonClicked(ActionCategory.Examine));
        }
        
        if (interveneButton != null)
        {
            interveneButton.button.onClick.AddListener(() => OnCategoryButtonClicked(ActionCategory.Intervene));
        }
        
        if (emergencyButton != null)
        {
            emergencyButton.button.onClick.AddListener(() => OnCategoryButtonClicked(ActionCategory.Emergency));
        }
        
        if (cleanupButton != null)
        {
            cleanupButton.button.onClick.AddListener(() => OnCategoryButtonClicked(ActionCategory.Cleanup));
        }
        
        // Initial state
        SetState(ActionPanelState.Hidden);
    }
    
    void OnEnable()
    {
        if (tileSelector != null)
        {
            tileSelector.OnMultiSelectionConfirmed += HandleMultiSelectionConfirmed;
        }
    }
    
    void OnDisable()
    {
        if (tileSelector != null)
        {
            tileSelector.OnMultiSelectionConfirmed -= HandleMultiSelectionConfirmed;
        }
    }
    
    void OnDestroy()
    
    {
        if (floodFillSlider != null)
            floodFillSlider.onValueChanged.RemoveListener(OnFloodFillSliderChanged);
    }

    
    void Update()
    {
        // Handle ESC key based on current state
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            HandleEscapeKey();
        }
        
        // Update tile counter in multi-select mode
        if (currentState == ActionPanelState.MultiSelect && tileSelector != null && tileSelector.IsMultiSelectMode)
        {
            UpdateTileCounter();
        }
    }
    
    // ═══════════════════════════════════════════════════════
    // PUBLIC API
    // ═══════════════════════════════════════════════════════
    
    /// <summary>
    /// Shows the category selection panel near the clicked tile.
    /// Called by GameManager when player clicks a tile.
    /// </summary>
    public void ShowActionsForTile(Tile tile, Vector3 worldPosition)
    {
        currentTile = tile;
        
        // Position panel near cursor/tile
        PositionPanel(worldPosition);
        
        // Update health display
        UpdateHealthDisplay(tile);
        
        // Update category button badges with action counts
        UpdateCategoryBadges();
        
        // Show category selection state
        SetState(ActionPanelState.CategorySelect);
    }
    
    /// <summary>
    /// Hides all UI panels and resets state.
    /// </summary>
    public void HideActions()
    {
        currentTile = null;
        currentAction = null;
        ClearActionCards();
        SetState(ActionPanelState.Hidden);
    }
    
    // ═══════════════════════════════════════════════════════
    // STATE MANAGEMENT
    // ═══════════════════════════════════════════════════════
    
    void SetState(ActionPanelState newState)
    {
        currentState = newState;
        
        // Hide everything first
        if (actionPanel != null) actionPanel.gameObject.SetActive(false);
        if (categoryIconsPanel != null) categoryIconsPanel.SetActive(false);
        if (actionListPanel != null) actionListPanel.SetActive(false);
        if (multiSelectPanel != null) multiSelectPanel.SetActive(false);
        if (backButton != null) backButton.gameObject.SetActive(false);
        // if (categoryTitleText != null) categoryTitleText.gameObject.SetActive(false);
        
        // Show relevant UI based on state
        switch (newState)
        {
            case ActionPanelState.Hidden:
                // Everything hidden
                break;
                
            case ActionPanelState.CategorySelect:
                // Show main panel with category icons
                actionPanel.gameObject.SetActive(true);
                categoryIconsPanel.SetActive(true);
                if (tileHealthText != null) tileHealthText.gameObject.SetActive(true);
                break;
                
            case ActionPanelState.ActionList:
                // Show main panel with action list (horizontal scroll)
                actionPanel.gameObject.SetActive(true);
                actionListPanel.SetActive(true);
                backButton.gameObject.SetActive(true);
                // categoryTitleText.gameObject.SetActive(true);
                // categoryTitleText.text = selectedCategory.GetDisplayName();
                break;
                
            case ActionPanelState.MultiSelect:
                // Hide action panel, show multi-select panel
                actionPanel.gameObject.SetActive(false);
                multiSelectPanel.SetActive(true);
                break;
        }
    }
    
    // ═══════════════════════════════════════════════════════
    // CATEGORY SELECTION
    // ═══════════════════════════════════════════════════════
    
    /// <summary>
    /// Called when player clicks a category button (Examine, Intervene, etc.)
    /// </summary>
    void OnCategoryButtonClicked(ActionCategory category)
    {
        selectedCategory = category;
        ShowActionList(category);
    }
    
    /// <summary>
    /// Spawns action cards into the horizontal scroll view.
    /// </summary>
    void ShowActionList(ActionCategory category)
    {
        // Clear any existing cards first
        ClearActionCards();
        
        // Get actions in this category
        List<PlayerAction> actions = GetActionsInCategory(category);
        
        if (actions.Count == 0)
        {
            Debug.LogWarning($"No actions available in category: {category}");
            return;
        }
        
        // Spawn action cards into the horizontal Content
        foreach (PlayerAction action in actions)
        {
            CreateActionCard(action);
        }
        
        // Show action list state
        SetState(ActionPanelState.ActionList);
    }
    
    /// <summary>
    /// Updates the badge counts on category buttons.
    /// Shows how many actions are available in each category.
    /// </summary>
    void UpdateCategoryBadges()
    {
        if (actionManager == null) return;
        
        List<PlayerAction> allActions = actionManager.GetAvailableActions();
        
        // Count actions per category
        int examineCount = allActions.Count(a => a.Category == ActionCategory.Examine);
        int interveneCount = allActions.Count(a => a.Category == ActionCategory.Intervene);
        int emergencyCount = allActions.Count(a => a.Category == ActionCategory.Emergency);
        int cleanupCount = allActions.Count(a => a.Category == ActionCategory.Cleanup);
        
        // Update badges
        if (examineButton != null) examineButton.SetBadgeCount(examineCount);
        if (interveneButton != null) interveneButton.SetBadgeCount(interveneCount);
        if (emergencyButton != null) emergencyButton.SetBadgeCount(emergencyCount);
        if (cleanupButton != null) cleanupButton.SetBadgeCount(cleanupCount);
        
    }
    
    /// <summary>
    /// Filters available actions by category.
    /// </summary>
    List<PlayerAction> GetActionsInCategory(ActionCategory category)
    {
        if (actionManager == null) return new List<PlayerAction>();
        
        List<PlayerAction> allActions = actionManager.GetAvailableActions();
        return allActions.Where(a => a.Category == category).ToList();
    }
    
    // ═══════════════════════════════════════════════════════
    // ACTION CARDS (Horizontal Scroll)
    // ═══════════════════════════════════════════════════════
    
    // ═══════════════════════════════════════════════════════
    // ACTION CARDS (UPDATED FOR SPRITES)
    // ═══════════════════════════════════════════════════════
    
    /// <summary>
    /// Creates a single action card with sprite icon.
    /// The Horizontal Layout Group on Content will automatically position it.
    /// </summary>
    void CreateActionCard(PlayerAction action)
    {
        if (actionCardPrefab == null || actionListContent == null)
        {
            Debug.LogError("ActionCardPrefab or ActionListContent is null! Check Inspector assignments.");
            return;
        }
        
        // Instantiate card as child of Content
        GameObject card = Instantiate(actionCardPrefab, actionListContent);
        spawnedActionCards.Add(card);
        
        // Get components from the prefab
        Button cardButton = card.GetComponent<Button>();
        Image iconImage = card.transform.Find("ActionIcon")?.GetComponent<Image>();
        
        // Set action sprite
        if (iconImage != null && actionIconConfig != null)
        {
            Sprite actionSprite = actionIconConfig.GetSpriteForAction(action.ActionName);
            
            if (actionSprite != null)
            {
                iconImage.sprite = actionSprite;
                iconImage.enabled = true;
            }
            else
            {
                Debug.LogWarning($"No sprite found for action '{action.ActionName}' in ActionIconConfig!");
                iconImage.enabled = false; // Hide if no sprite
            }
        }
        else
        {
            if (iconImage == null)
            {
                Debug.LogWarning("ActionCardPrefab is missing child named 'ActionIcon' with Image component!");
            }
            if (actionIconConfig == null)
            {
                Debug.LogWarning("ActionIconConfig is not assigned in ActionUI! Assign it in Inspector.");
            }
        }
        
        // Wire up click handler
        if (cardButton != null)
        {
            cardButton.onClick.AddListener(() => OnActionCardClicked(action));
        }
        else
        {
            Debug.LogWarning("ActionCardPrefab is missing Button component!");
        }
        
        // Optional: Set card name for debugging
        card.name = $"Card_{action.ActionName}";
    }
    
    /// <summary>
    /// Destroys all spawned action cards.
    /// Called when changing categories or closing panel.
    /// </summary>
    void ClearActionCards()
    {
        foreach (GameObject card in spawnedActionCards)
        {
            if (card != null)
            {
                Destroy(card);
            }
        }
        spawnedActionCards.Clear();
    }
    
    /// <summary>
    /// Called when player clicks an action card.
    /// Enters multi-select mode to let player choose tiles.
    /// </summary>
    void OnActionCardClicked(PlayerAction action)
    {
        if (currentTile == null)
        {
            Debug.LogWarning("No tile selected!");
            return;
        }
        
        currentAction = action;
        
        // Enter multi-select mode via TileSelector
        if (tileSelector != null)
        {
            if (action.selectionMode == SelectionMode.FloodFill)
                tileSelector.EnterFloodFillMode(action, currentTile);
            else
                tileSelector.EnterMultiSelectMode(action, currentTile);

            ShowMultiSelect();
        }
        else
        {
            Debug.LogError("TileSelector is null! Cannot enter multi-select mode.");
        }
    }
    
    // ═══════════════════════════════════════════════════════
    // MULTI-SELECT MODE
    // ═══════════════════════════════════════════════════════
    
    void ShowMultiSelect()
    {
        SetState(ActionPanelState.MultiSelect);

        if (actionNameText != null && currentAction != null)
            actionNameText.text = currentAction.ActionName;

        if (tileCounterText != null) tileCounterText.text = "";
        if (daysText != null) daysText.text = "";

        bool isFloodFill = tileSelector != null && tileSelector.IsFloodFillMode;

        if (floodFillSliderContainer != null)
            floodFillSliderContainer.SetActive(isFloodFill);

        if (isFloodFill && floodFillSlider != null)
        {
            int max = Mathf.Min(tileSelector.MaxSelectableTiles, tileSelector.FloodFillReachableCount);
            floodFillSlider.minValue = 1;
            floodFillSlider.maxValue = Mathf.Max(1, max);
            floodFillSlider.wholeNumbers = true;
            floodFillSlider.value = 1;
            UpdateFloodFillSliderLabel(1, max); // Resolves to "" anyway
        }

        UpdateTileCounter();
    }
    
    void OnFloodFillSliderChanged(float value)
    {
        if (tileSelector == null || !tileSelector.IsFloodFillMode) return;

        int count = Mathf.RoundToInt(value);
        tileSelector.SetFloodFillSize(count);
        UpdateFloodFillSliderLabel(count, tileSelector.FloodFillReachableCount);
        UpdateTileCounter();
    }

    void UpdateFloodFillSliderLabel(int current, int max)
    {
        if (floodFillSliderLabel != null)
            floodFillSliderLabel.text = "";
    }

    
    void UpdateTileCounter()
    {
        if (tileCounterText == null || tileSelector == null) return;

        int selected = tileSelector.SelectedTileCount;
        int max = tileSelector.MaxSelectableTiles;

        tileCounterText.text = "";
        if (daysText != null) daysText.text = "";

        if (confirmButton != null)
            confirmButton.interactable = selected > 0;

        if (selected == 0)
        {
            SetWarningIconActive(false);
            return;
        }

        if (currentAction != null)
        {
            int people = ResourceManager.Instance.AvailablePeople;
            int personsPerTile = people / selected;
            float range = Mathf.Max(1f, people - currentAction.MinPeoplePerTile);
            float t = Mathf.Clamp01((personsPerTile - currentAction.MinPeoplePerTile) / range);
            SetWarningIconActive(t < 0.33f);
        }
    }
    
    /// <summary>
    /// Maps t (0..1) to a green → yellow → orange → red gradient.
    /// t = 1.0 → green (plenty of people per tile)
    /// t = 0.0 → red (barely above minimum — caught as red above, but boundary-safe)
    /// </summary>
    private Color GetCounterGradientColor(float t)
    {
        // Color anchors
        Color green  = new Color(0.2f, 0.9f, 0.2f);
        Color yellow = new Color(1.0f, 0.9f, 0.0f);
        Color orange = new Color(1.0f, 0.45f, 0.0f);
        Color red    = new Color(0.9f, 0.2f, 0.2f);

        if (t >= 0.66f)
            return Color.Lerp(yellow, green, (t - 0.66f) / 0.34f);  // Yellow → Greenyeah
        
        else if (t >= 0.33f)
            return Color.Lerp(orange, yellow, (t - 0.33f) / 0.33f); // Orange → Yellow
        else
            return Color.Lerp(red, orange, t / 0.33f);               // Red → Orange
    }
    
    private void SetWarningIconActive(bool active)
    {
        if (warningIcon == null) return;
        warningIcon.SetActive(active);

        // If hiding the icon, also hide the tooltip immediately
        if (!active && warningTooltipPanel != null)
            warningTooltipPanel.SetActive(false);
    }
    
    void OnConfirmClicked()
    {
        if (tileSelector == null) return;
        tileSelector.ConfirmSelection();
    }
    
    void OnCancelClicked()
    {
        if (tileSelector == null) return;
        tileSelector.CancelSelection();
        
        // Return to action list
        ShowActionList(selectedCategory);
    }
    
    void HandleMultiSelectionConfirmed(List<Tile> tiles)
    {
        if (currentAction == null || actionManager == null) return;
        
        Debug.Log($"Executing {currentAction.ActionName} on {tiles.Count} tiles");
        actionManager.ExecuteAction(currentAction, tiles);
        
        // Hide everything after execution
        HideActions();
    }
    
    // ═══════════════════════════════════════════════════════
    // NAVIGATION
    // ═══════════════════════════════════════════════════════
    
    void OnBackButtonClicked()
    {
        if (currentState == ActionPanelState.ActionList)
        {
            // Clear action cards
            ClearActionCards();
            
            // Return to category selection
            SetState(ActionPanelState.CategorySelect);
        }
    }
    
    void HandleEscapeKey()
    {
        switch (currentState)
        {
            case ActionPanelState.CategorySelect:
                HideActions();
                break;
                
            case ActionPanelState.ActionList:
                OnBackButtonClicked();
                break;
                
            case ActionPanelState.MultiSelect:
                // ESC in multi-select returns to action list
                if (tileSelector != null)
                {
                    tileSelector.CancelSelection();
                }
                ShowActionList(selectedCategory);
                break;
        }
    }
    
    // ═══════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════
    
    void PositionPanel(Vector3 worldPosition)
    {
        Vector3 screenPos = mainCamera.WorldToScreenPoint(worldPosition);
        screenPos.x += screenOffset.x;
        screenPos.y += screenOffset.y;
        actionPanel.position = screenPos;
    }
    
    void UpdateHealthDisplay(Tile tile)
    {
        if (tileHealthText == null || tile == null) return;
        
        float health = tile.CalculateHealth();
        string state;
        Color healthColor;
        
        if (health < 33f)
        {
            healthColor = new Color(0.9f, 0.3f, 0.3f);
            state = "Critical";
        }
        else if (health < 67f)
        {
            healthColor = new Color(0.9f, 0.8f, 0.3f);
            state = "Degraded";
        }
        else
        {
            healthColor = new Color(0.3f, 0.9f, 0.3f);
            state = "Thriving";
        }
        
        tileHealthText.text = $"Health: {health:F1}% ({state})";
        tileHealthText.color = healthColor;
    }
}