using UnityEngine;
using UnityEngine.UI;
using System;

/// <summary>
/// Scene-wiring shell for Inspect Mode.
/// Owns the inspector references (panel, selector, button) and the public API
/// surface. Inspect-mode state (_inspectMode) is self-contained here.
///
/// DORMANT as of 2026-07-23 — SUPERSEDED by <c>Habitales.UI.SelectedInfoPanelController</c>, which
/// is the de-facto driver of the InspectPanelUI body. Do NOT wire both against the same
/// InspectPanelUI: they would fight over the body's content and visibility. Kept on disk (all its
/// InspectPanelUI calls are null-guarded, so an unwired <see cref="inspectPanel"/> makes it inert)
/// for possible revival of a dedicated "inspect mode" toggle. To keep it dormant, leave
/// <see cref="inspectPanel"/> unwired in-scene (or disable the component).
/// </summary>
public class InspectModeManager : MonoBehaviour
{
    public static InspectModeManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private InspectPanelUI inspectPanel;
    [SerializeField] private TileSelector   tileSelector;

    [Header("Test Button (optional)")]
    [SerializeField] private Button inspectModeButton;

    private bool _inspectMode;

    public bool IsInspectMode => _inspectMode;

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
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
        _inspectMode = true;
        inspectPanel?.Show();
    }

    public void ExitInspectMode()
    {
        _inspectMode = false;
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
