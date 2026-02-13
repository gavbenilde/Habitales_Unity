using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ActionUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Canvas actionCanvas;
    [SerializeField] private RectTransform actionPanel;
    [SerializeField] private Button actionButtonPrefab;
    
    [Header("Multi-Select UI")] // NEW! ⭐
    [SerializeField] private GameObject multiSelectPanel;
    [SerializeField] private TextMeshProUGUI tileCounterText;
    [SerializeField] private TextMeshProUGUI actionNameText; // Optional: show action name
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;
    
    [Header("Positioning")]
    [SerializeField] private Vector2 screenOffset = new Vector2(150f, 0f);
    [SerializeField] private Camera mainCamera;
    
    [Header("Action System")]
    [SerializeField] private ActionManager actionManager;
    [SerializeField] private TileSelector tileSelector; // NEW! ⭐
    
    private Tile currentTile;
    private PlayerAction currentAction; // NEW: Store current action ⭐
    private List<Button> spawnedButtons = new List<Button>();
    
    void Awake()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
        
        if (actionManager == null)
        {
            actionManager = FindObjectOfType<ActionManager>();
            if (actionManager == null)
            {
                Debug.LogError("ActionUI requires ActionManager in scene!");
            }
        }
        
        // NEW: Find TileSelector ⭐
        if (tileSelector == null)
        {
            tileSelector = FindObjectOfType<TileSelector>();
            if (tileSelector == null)
            {
                Debug.LogError("ActionUI requires TileSelector in scene!");
            }
        }
        
        // Hide panels initially
        if (actionPanel != null)
        {
            actionPanel.gameObject.SetActive(false);
        }
        
        if (multiSelectPanel != null)
        {
            multiSelectPanel.SetActive(false);
        }
        
        // NEW: Wire up multi-select UI buttons ⭐
        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(OnConfirmClicked);
        }
        
        if (cancelButton != null)
        {
            cancelButton.onClick.AddListener(OnCancelClicked);
        }
    }
    
    void OnEnable()
    {
        // NEW: Subscribe to TileSelector events ⭐
        if (tileSelector != null)
        {
            tileSelector.OnMultiSelectionConfirmed += HandleMultiSelectionConfirmed;
        }
    }
    
    void OnDisable()
    {
        // NEW: Unsubscribe from TileSelector events ⭐
        if (tileSelector != null)
        {
            tileSelector.OnMultiSelectionConfirmed -= HandleMultiSelectionConfirmed;
        }
    }
    
    void Update()
    {
        // NEW: Update tile counter every frame while in multi-select mode ⭐
        if (tileSelector != null && tileSelector.IsMultiSelectMode && multiSelectPanel != null && multiSelectPanel.activeSelf)
        {
            UpdateTileCounter();
        }
    }
    
    public void ShowActionsForTile(Tile tile, Vector3 worldPosition)
    {
        currentTile = tile;
        
        // Clear existing buttons
        ClearButtons();
        
        // Position panel near tile
        PositionPanel(worldPosition);
        
        // Create action buttons
        CreateActionButtons();
        
        // Show panel
        actionPanel.gameObject.SetActive(true);
    }
    
    public void HideActions()
    {
        currentTile = null;
        ClearButtons();
        actionPanel.gameObject.SetActive(false);
    }
    
    void PositionPanel(Vector3 worldPosition)
    {
        // Convert world position to screen space
        Vector3 screenPos = mainCamera.WorldToScreenPoint(worldPosition);
        
        // Apply offset
        screenPos.x += screenOffset.x;
        screenPos.y += screenOffset.y;
        
        // Set panel position
        actionPanel.position = screenPos;
    }
    
    void CreateActionButtons()
    {
        List<PlayerAction> availableActions = actionManager.GetAvailableActions();
        if (availableActions == null || availableActions.Count == 0)
        {
            Debug.LogWarning("No actions available!");
            return;
        }
        
        foreach (PlayerAction action in availableActions)
        {
            // Instantiate button
            Button btn = Instantiate(actionButtonPrefab, actionPanel);
            
            // Set button text
            TextMeshProUGUI btnText = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (btnText != null)
            {
                btnText.text = action.ActionName;
            }
            
            PlayerAction capturedAction = action; // Capture for closure
            btn.onClick.AddListener(() => OnActionButtonClicked(capturedAction));
            
            spawnedButtons.Add(btn);
        }
    }
    
    void ClearButtons()
    {
        foreach (Button btn in spawnedButtons)
        {
            if (btn != null)
            {
                Destroy(btn.gameObject);
            }
        }
        spawnedButtons.Clear();
    }
    
    void OnActionButtonClicked(PlayerAction action)
    {
        if (currentTile == null)
        {
            Debug.LogWarning("No tile selected!");
            return;
        }
        
        if (actionManager == null)
        {
            Debug.LogError("ActionManager is missing - cannot execute action!");
            return;
        }
        
        // Store current action
        currentAction = action;
        
        // Enter multi-select mode
        if (tileSelector != null)
        {
            tileSelector.EnterMultiSelectMode(action, currentTile);
            
            // Hide action menu
            HideActions();
            
            // Show multi-select UI
            ShowMultiSelectUI();
            
            Debug.Log($"Entered multi-select mode for {action.ActionName}");
        }
        else
        {
            Debug.LogError("TileSelector not found!");
        }
    }
    
    // NEW: Show multi-select UI panel ⭐
    void ShowMultiSelectUI()
    {
        if (multiSelectPanel == null) return;
        
        multiSelectPanel.SetActive(true);
        
        // Update action name (optional)
        if (actionNameText != null && currentAction != null)
        {
            actionNameText.text = currentAction.ActionName;
        }
        
        UpdateTileCounter();
    }
    
    // NEW: Hide multi-select UI panel ⭐
    void HideMultiSelectUI()
    {
        if (multiSelectPanel == null) return;
        
        multiSelectPanel.SetActive(false);
        currentAction = null;
    }
    
    // NEW: Update tile counter display ⭐
    void UpdateTileCounter()
    {
        if (tileCounterText == null || tileSelector == null) return;
        
        int selected = tileSelector.SelectedTileCount;
        int max = tileSelector.MaxSelectableTiles;
        
        tileCounterText.text = $"{selected} / {max} tiles selected";
        
        // Optional: Change color based on selection
        if (selected == 0)
        {
            tileCounterText.color = Color.red;
        }
        else if (selected >= max)
        {
            tileCounterText.color = Color.green;
        }
        else
        {
            tileCounterText.color = Color.white;
        }
    }
    
    // NEW: Handle Confirm button click ⭐
    void OnConfirmClicked()
    {
        if (tileSelector == null) return;
        
        // Trigger confirmation (will call HandleMultiSelectionConfirmed via event)
        tileSelector.ConfirmSelection();
        
        // Hide multi-select UI
        HideMultiSelectUI();
    }
    
    // NEW: Handle Cancel button click ⭐
    void OnCancelClicked()
    {
        if (tileSelector == null) return;
        
        // Cancel selection
        tileSelector.CancelSelection();
        
        // Hide multi-select UI
        HideMultiSelectUI();
        
        Debug.Log("Multi-select cancelled");
    }
    
    // NEW: Handle confirmed multi-selection ⭐
    void HandleMultiSelectionConfirmed(List<Tile> tiles)
    {
        if (currentAction == null || actionManager == null)
        {
            Debug.LogError("Cannot execute action - missing action or manager!");
            return;
        }
        
        Debug.Log($"Executing {currentAction.ActionName} on {tiles.Count} tiles");
        
        // Execute action on all selected tiles
        actionManager.ExecuteAction(currentAction, tiles);
        
        // Hide multi-select UI
        HideMultiSelectUI();
    }
}
