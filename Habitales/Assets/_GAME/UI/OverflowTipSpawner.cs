using UnityEngine;
using Habitales.UI;   // IUISubsystem

public class OverflowTipSpawner : MonoBehaviour, IUISubsystem
{
    public static OverflowTipSpawner Instance { get; private set; }

    [Header("References")]
    [SerializeField] private GameObject tipPrefab;
    [SerializeField] private Canvas tipCanvas;

    // Track the single live tip
    private GameObject activeTip;

    // Suppresses SpawnAtCursor while the hub has hidden UI (screenshot hide-all).
    // Defaults visible so behaviour is unchanged until a human wires this into
    // UIManager.subsystems.
    private bool _visible = true;

    // ── IUISubsystem ─────────────────────────────────────────────────────────
    //
    // "Fold OverflowTip under the hub" (U5 follow-up). SetVisible(false) kills
    // any currently-drifting tip immediately (same as a new spawn would) so a
    // stray tip can't survive into a hide-all screenshot, and suppresses further
    // spawning until SetVisible(true) restores it. tipCanvas itself is left
    // alone (it may host other content); this only gates OverflowTipSpawner's
    // own tip instances.

    public string SubsystemId => "overflowTips";
    public bool   IsVisible   => _visible;
    public void   SetVisible(bool visible)
    {
        _visible = visible;

        if (!visible && activeTip != null)
        {
            Destroy(activeTip);
            activeTip = null;
        }
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>
    /// Spawns a tip at the cursor. Destroys any existing tip immediately first.
    /// No-ops while the hub has hidden UI (SetVisible(false)) so tips can't spawn
    /// into a screenshot.
    /// </summary>
    public void SpawnAtCursor(string message)
    {
        if (!_visible) return;

        if (tipPrefab == null || tipCanvas == null)
        {
            Debug.LogWarning("OverflowTipSpawner: tipPrefab or tipCanvas not assigned!");
            return;
        }

        // Kill the previous tip instantly — no lingering clutter
        if (activeTip != null)
            Destroy(activeTip);

        activeTip = Instantiate(tipPrefab, tipCanvas.transform);
        activeTip.transform.position = Input.mousePosition + new Vector3(0f, 20f, 0f);

        OverflowTip overflowTip = activeTip.GetComponent<OverflowTip>();
        if (overflowTip != null)
            overflowTip.Initialize(message);
        else
            Debug.LogWarning("OverflowTipSpawner: tipPrefab is missing OverflowTip component!");
    }
}