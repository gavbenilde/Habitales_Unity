using UnityEngine;
using UnityEngine.UI;
using System;

/// <summary>
/// Scene-wiring shell for Inspect Mode.
/// Owns the inspector references (panel, selector, button) and the public API
/// surface. All state lives in ActionUI — this class just delegates.
/// </summary>
public class InspectModeManager : MonoBehaviour
{
    public static InspectModeManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private InspectPanelUI inspectPanel;
    [SerializeField] private TileSelector   tileSelector;
    [SerializeField] private ActionUI       actionUI;

    [Header("Test Button (optional)")]
    [SerializeField] private Button inspectModeButton;

    // Convenience — lets external callers check inspect state without
    // reaching into ActionUI directly.
    public bool IsInspectMode =>
        actionUI != null && actionUI.CurrentState == ActionPanelState.InspectMode;

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (actionUI == null)
            actionUI = FindObjectOfType<ActionUI>();
    }

    void OnEnable()
    {
        if (inspectModeButton != null)
            inspectModeButton.onClick.AddListener(EnterInspectMode);

        if (tileSelector != null)
        {
            tileSelector.OnTileSelected   += HandleTileSelected;
            tileSelector.OnTileDeselected += HandleTileDeselected;
        }
    }

    void OnDisable()
    {
        if (inspectModeButton != null)
            inspectModeButton.onClick.RemoveListener(EnterInspectMode);

        if (tileSelector != null)
        {
            tileSelector.OnTileSelected   -= HandleTileSelected;
            tileSelector.OnTileDeselected -= HandleTileDeselected;
        }
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public void EnterInspectMode()
    {
        if (actionUI == null) return;
        actionUI.EnterInspectMode();
        inspectPanel?.Show();
    }

    public void ExitInspectMode()
    {
        if (actionUI == null) return;
        actionUI.ExitInspectMode();
        tileSelector?.ClearSelection();
        inspectPanel?.Hide();
    }

    // ── TileSelector listeners ───────────────────────────────────────────────

    private void HandleTileSelected(Tile tile, Vector3 worldPos)
    {
        if (!IsInspectMode) return;
        inspectPanel?.Populate(tile);
    }

    private void HandleTileDeselected()
    {
        if (!IsInspectMode) return;
        inspectPanel?.ShowEmpty();
    }
}
