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

    [Header("Positioning")]
    [SerializeField] private Vector2 screenOffset = new Vector2(150f, 0f);
    [SerializeField] private Camera mainCamera;

    [Header("Action System")]
    [SerializeField] private ActionManager actionManager;

    private Tile currentTile;
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

        // Hide panel initially
        if (actionPanel != null)
        {
            actionPanel.gameObject.SetActive(false);
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
        // ← CHANGED: Get actions from ActionManager instead of hardcoded list
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

        // Execute action (wrap single tile in list)
        actionManager.ExecuteAction(action, new List<Tile> { currentTile });

        // Hide menu after action
        HideActions();
    }
}
