using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.VisualScripting;
using UTILITIES.Camera;

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
    [SerializeField] private GameObject actionCardsPanel;
    [SerializeField] private Transform actionListContent;
    [SerializeField] private GameObject actionCardPrefab;
    [SerializeField] private ActionIconConfig actionIconConfig;
    [SerializeField] private Image selectedCardImage;
    [SerializeField] private TextMeshProUGUI selectedCardDesc;
    
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
    
    [Header("UI Estimations")]
    [SerializeField] private TMP_Text tileCountText;
    [SerializeField] private TMP_Text daysEstimateText;
    [SerializeField] private TMP_Text fatigueEstimateText;
    
    [Header("Lock Card")]
    [SerializeField] private GameObject lockModal;
    [SerializeField] private Button     lockModalContinueButton;

    [Header("FloodFill Slider")]
    [SerializeField] private GameObject floodFillSliderContainer;
    [SerializeField] private Slider floodFillSlider;
    [SerializeField] private TextMeshProUGUI floodFillSliderLabel;
   
    [Header("Inspect Mode")]
    [SerializeField] private InspectPanelUI inspectPanel;
    
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
    public ActionPanelState CurrentState => currentState;

    // Action panning & zooming
    private Action<Tile, Vector3> selectedTileHandler;
    private Tile selectedTile;
    private Vector3 selectedTilePosition;
    private float originZoom;
    
    private float xPosOffset = 7.5f;
    private float zPosOffset = 8f;
    
    void Awake()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
        
        if (actionManager == null)
        {
            actionManager = ActionManager.Instance;
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
        
        // FloodFill is now drag-driven (trail); the brush-size slider is retired.
        if (floodFillSliderContainer != null)
            floodFillSliderContainer.SetActive(false);

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
        
        // Emergency is retired to 3 authorable groups; its two actions are dormant. Hide the
        // tab so the empty category does not show. Re-activate to revive Emergency later.
        if (emergencyButton != null)
        {
            emergencyButton.gameObject.SetActive(false);
        }
        
        if (cleanupButton != null)
        {
            cleanupButton.button.onClick.AddListener(() => OnCategoryButtonClicked(ActionCategory.Cleanup));
        }
        
        if (lockModal != null) lockModal.SetActive(false);

        // Initial state
        SetState(ActionPanelState.Hidden);
    }
    
    void OnEnable()
    {
        if (tileSelector != null)
        {
            tileSelector.OnMultiSelectionConfirmed += HandleMultiSelectionConfirmed;
            selectedTileHandler = (tile, position) =>
            {
                selectedTile = tile;
                selectedTilePosition = position;
            };
            tileSelector.OnTileSelected += selectedTileHandler;
        }

        if (lockModalContinueButton != null)
            lockModalContinueButton.onClick.AddListener(HideLockModal);
    }

    void OnDisable()
    {
        if (tileSelector != null)
        {
            tileSelector.OnMultiSelectionConfirmed -= HandleMultiSelectionConfirmed;
            tileSelector.OnTileSelected -= selectedTileHandler;
        }

        if (lockModalContinueButton != null)
            lockModalContinueButton.onClick.RemoveListener(HideLockModal);
    }
    
    void OnDestroy()
    {
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
        if (CurrentState == ActionPanelState.InspectMode)
            return;
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
    
    /// <summary>
    /// Enters Inspect Mode. Collapses any open action flow first.
    /// Called by InspectModeManager.
    /// </summary>
    public void EnterInspectMode()
    {
        // Cancel any in-progress tile selection cleanly
        if (currentState == ActionPanelState.MultiSelect && tileSelector != null)
            tileSelector.CancelSelection();
 
        currentTile   = null;
        currentAction = null;
        ClearActionCards();
        SetState(ActionPanelState.InspectMode);
    }
 
    /// <summary>
    /// Exits Inspect Mode and returns to Hidden.
    /// Called by InspectModeManager.
    /// </summary>
    public void ExitInspectMode()
    {
        if (currentState != ActionPanelState.InspectMode) return;
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
        if (actionCardsPanel != null) actionCardsPanel.SetActive(false);
        if (selectedCardImage != null) selectedCardImage.gameObject.SetActive(false);
        if (multiSelectPanel != null) multiSelectPanel.SetActive(false);
        if (backButton != null) backButton.gameObject.SetActive(false);
        // Prototype: InspectPanelUI is always on — it listens to TileSelector
        // directly and owns its own visibility. Do not toggle it from here.
        // if (categoryTitleText != null) categoryTitleText.gameObject.SetActive(false);
        
        // Show relevant UI based on state
        switch (newState)
        {
            case ActionPanelState.Hidden:
                // Everything hidden
                break;
                
            case ActionPanelState.CategorySelect:
                // Show main panel with category icons
                originZoom = mainCamera.orthographicSize; // band-aid selection flow solution
                actionPanel.gameObject.SetActive(true);
                categoryIconsPanel.SetActive(true);
                if (tileHealthText != null) tileHealthText.gameObject.SetActive(true);
                break;
                
            case ActionPanelState.ActionList:
                // Show main panel with action list (horizontal scroll)
                actionPanel.gameObject.SetActive(true);
                actionCardsPanel.gameObject.SetActive(true);
                selectedCardImage.gameObject.SetActive(false);
                actionListPanel.SetActive(true);
                backButton.gameObject.SetActive(true);
                // categoryTitleText.gameObject.SetActive(true);
                // categoryTitleText.text = selectedCategory.GetDisplayName();
                break;
            
            case ActionPanelState.VariantSelect:
                actionPanel.gameObject.SetActive(true);
                actionCardsPanel.gameObject.SetActive(true);
                selectedCardImage.gameObject.SetActive(false);
                actionListPanel.SetActive(true);
                backButton.gameObject.SetActive(true);
                break;
                
            case ActionPanelState.MultiSelect:
                // Hide action panel, show multi-select panel
                actionPanel.gameObject.SetActive(false);
                actionCardsPanel.gameObject.SetActive(true);
                selectedCardImage.gameObject.SetActive(true);
                multiSelectPanel.SetActive(true);
                break;
            
            case ActionPanelState.InspectMode:
                
                categoryIconsPanel.SetActive(false);
                categoryIconsPanel.SetActive(false);
                // Action panel stays hidden; inspect panel is shown by InspectModeManager.
                // Nothing to activate here — SetState already hid everything above.
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
        
        Vector3 tileWorldPos = new Vector3(
            selectedTile.gridPosition.x + (xPosOffset),
            0f,
            selectedTile.gridPosition.y + (zPosOffset)
        );
        
        float distance = mainCamera.transform.position.y; // Distance from camera to ground (preserve current distance)
        Vector3 offset = -mainCamera.transform.forward * distance; // Move backwards along camera's forward direction
        Vector3 target = tileWorldPos + offset;
        
        if (originZoom != null)
            originZoom = mainCamera.orthographicSize;
        
        if (target != null)
        {
            EventCameraHandler.Instance.PanTo(target);
            EventCameraHandler.Instance.ZoomTo(3f);
        }
    }
    
    /// <summary>
    /// Spawns action cards into the horizontal scroll view.
    /// </summary>
    void ShowActionList(ActionCategory category)
    {
        ClearActionCards();
        
        List<PlayerAction> actions = GetActionsInCategory(category);
        
        if (actions.Count == 0)
        {
            Debug.LogWarning($"No actions available in category: {category}");
            return;
        }
        
        var renderedGroups = new HashSet<string>();
        
        foreach (PlayerAction action in actions)
        {
            if (action.VariantGroupName == null)
            {
                // Standalone action — existing behavior, untouched
                CreateActionCard(action);
            }
            else if (!renderedGroups.Contains(action.VariantGroupName))
            {
                // First variant seen for this group — render one group button
                renderedGroups.Add(action.VariantGroupName);
                List<PlayerAction> variants = actions
                    .Where(a => a.VariantGroupName == action.VariantGroupName)
                    .ToList();
                CreateGroupCard(action.VariantGroupName, variants);
            }
            // Subsequent variants in the same group — skip; already covered by group button
        }
        
        CreateLockCard(actionListContent);
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
        
        // Set action sprite. Data-driven actions carry their own icon (ActionSO.icon);
        // legacy code-defined actions fall back to the name-keyed ActionIconConfig.
        if (iconImage != null)
        {
            Sprite actionSprite = action.Icon != null
                ? action.Icon
                : actionIconConfig != null ? actionIconConfig.GetSpriteForAction(action.ActionName) : null;

            if (actionSprite != null)
            {
                iconImage.sprite = actionSprite;
                iconImage.enabled = true;
            }
            else
            {
                Debug.LogWarning($"No sprite found for action '{action.ActionName}' (no ActionSO.icon and no ActionIconConfig entry)!");
                iconImage.enabled = false; // Hide if no sprite
            }
        }
        else
        {
            Debug.LogWarning("ActionCardPrefab is missing child named 'ActionIcon' with Image component!");
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
    
    void CreateGroupCard(string groupName, List<PlayerAction> variants)
    {
        if (actionCardPrefab == null || actionListContent == null)
        {
            Debug.LogError("ActionCardPrefab or ActionListContent is null! Check Inspector assignments.");
            return;
        }

        GameObject card = Instantiate(actionCardPrefab, actionListContent);
        spawnedActionCards.Add(card);

        Button cardButton = card.GetComponent<Button>();
        Image iconImage   = card.transform.Find("ActionIcon")?.GetComponent<Image>();

        if (iconImage != null && actionIconConfig != null)
        {
            // Look up a sprite by the group name — add "Cover Cropping" as a key in ActionIconConfig
            Sprite groupSprite = actionIconConfig.GetSpriteForAction(groupName);
            if (groupSprite != null)
            {
                iconImage.sprite  = groupSprite;
                iconImage.enabled = true;
            }
            else
            {
                iconImage.enabled = false;
            }
        }

        if (cardButton != null)
            cardButton.onClick.AddListener(() => ShowVariantList(variants));

        card.name = $"Card_Group_{groupName}";
    }

    void CreateLockCard(Transform parent)
    {
        if (actionCardPrefab == null || parent == null) return;

        GameObject card = Instantiate(actionCardPrefab, parent);
        spawnedActionCards.Add(card);

        // Disable icon — no sprite assigned yet
        Image iconImage = card.transform.Find("ActionIcon")?.GetComponent<Image>();
        if (iconImage != null)
        {
            // TODO: assign lock sprite
            iconImage.enabled = false;
        }

        Button cardButton = card.GetComponent<Button>();
        if (cardButton != null)
            cardButton.onClick.AddListener(ShowLockModal);

        card.name = "Card_Locked";
    }

    private void ShowLockModal() { if (lockModal != null) lockModal.SetActive(true); }
    private void HideLockModal() { if (lockModal != null) lockModal.SetActive(false); }

    void DisplaySelectedCard(PlayerAction action)
    {
        Sprite actionSprite = actionIconConfig.GetSpriteForAction(action.ActionName);

        selectedCardImage.sprite = actionSprite;
    }

    void DisplayCardDescription(PlayerAction action)
    {
        selectedCardDesc.text = action.Description;
    }

    void ShowVariantList(List<PlayerAction> variants)
    {
        ClearActionCards();
        foreach (PlayerAction variant in variants)
            CreateActionCard(variant);   // reuses existing card — variant click flows straight into FloodFill
        SetState(ActionPanelState.VariantSelect);
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
        DisplaySelectedCard(currentAction);
        DisplayCardDescription(currentAction);
        
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

        // FloodFill size is driven by drag now; the slider stays hidden.
        if (floodFillSliderContainer != null)
            floodFillSliderContainer.SetActive(false);

        UpdateTileCounter();
    }
    
    void UpdateTileCounter()
    {
        if (tileSelector == null) return;
        int selected = tileSelector.SelectedTileCount;

        if (confirmButton != null)
            confirmButton.interactable = selected > 0;

        // Hide warning icon if 0 selected
        if (selected == 0)
        {
            SetWarningIconActive(false);
            return;
        }

        // Warning Icon Logic (unchanged)
        if (currentAction != null)
        {
            int people = ResourceManager.Instance.AvailablePeople;
            int personsPerTile = people / selected;
            float range = Mathf.Max(1f, people - currentAction.MinPeoplePerTile);
            float t = Mathf.Clamp01((personsPerTile - currentAction.MinPeoplePerTile) / range);
            SetWarningIconActive(t < 0.33f);
        }

        // UPDATE THE NEW ESTIMATION UI HERE
        UpdateEstimationUI(selected);
    }
    
    private void UpdateEstimationUI(int selectedCount)
    {
        if (currentAction == null || ResourceManager.Instance == null) return;

        int totalWorkers = ResourceManager.Instance.AvailablePeople;
        int minRequired = selectedCount * currentAction.MinPeoplePerTile;
    
        // Calculate exertion for the UI
        float exertion = Mathf.Clamp01((float)minRequired / totalWorkers);

        // 1. Tile Count
        if (tileCountText != null)
            tileCountText.text = $"{selectedCount} Tiles";

        // 2. Day Count
        int baseDays = currentAction.CalculateDays(totalWorkers, selectedCount);
        // (Apply weather multiplier if applicable)
        if (daysEstimateText != null)
            daysEstimateText.text = $"{baseDays} Days";

        // 3. Fatigue Estimation
        if (fatigueEstimateText != null)
        {
            // Expected fatigue = Total Workers * Exertion * AvgSeverity (0.25)
            float expectedFatigued = totalWorkers * exertion * 0.25f;
            int displayCount = Mathf.Max(1, Mathf.RoundToInt(expectedFatigued));

            string riskLevel = exertion < 0.3f ? "Low" : (exertion < 0.7f ? "Moderate" : "High");
        
            fatigueEstimateText.text = $"~{displayCount}";
            // fatigueEstimateText.text = $"Fatigue Risk: {riskLevel} (~{displayCount} workers)";
        
            // Optional: Color code the risk
            // fatigueEstimateText.color = exertion < 0.3f ? Color.green : (exertion < 0.7f ? Color.yellow : Color.red);
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
        EventCameraHandler.Instance.ZoomTo(10f);
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
            ClearActionCards();
            SetState(ActionPanelState.CategorySelect);
        }
        else if (currentState == ActionPanelState.VariantSelect)
        {
            // Back from variant sub-panel → rebuild the grouped action list
            ShowActionList(selectedCategory);
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

            case ActionPanelState.VariantSelect:
                // Escape from variant sub-panel → back to grouped action list
                ShowActionList(selectedCategory);
                break;
                
            case ActionPanelState.MultiSelect:
                if (tileSelector != null)
                    tileSelector.CancelSelection();
                ShowActionList(selectedCategory);
                break;
            
            case ActionPanelState.InspectMode:
                // Delegate back to InspectModeManager so it can clean up its own state
                InspectModeManager.Instance?.ExitInspectMode();
                break;

        }
    }
    
    // ═══════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════
    
    
    void PositionPanel(Vector3 worldPosition)
    {
        // Convert world position to screen space
        Vector3 screenPos = mainCamera.WorldToScreenPoint(worldPosition);

        // Apply your offset
        screenPos.x += screenOffset.x;
        screenPos.y += screenOffset.y;

        // Get panel size
        Vector2 panelSize = actionPanel.GetComponent<RectTransform>().rect.size;

        // Clamp X and Y so panel stays fully inside the screen
        screenPos.x = Mathf.Clamp(screenPos.x, panelSize.x / 2f, Screen.width - panelSize.x / 2f);
        screenPos.y = Mathf.Clamp(screenPos.y, panelSize.y / 5f, Screen.height - panelSize.y / 5f);

        // Assign the position
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