using UnityEngine;

/// <summary>
/// Hides a set of HUD roots while an action is running (days passing), so the player can't
/// arm a new action mid-resolution and the screen is less cluttered. Restores them when the
/// action completes.
///
/// Drives off <see cref="ActionManager.IsActionRunning"/> (there is no OnActionStarted event,
/// so we watch the bool's rising/falling edge in Update). Wire the HUD roots you want gated:
/// e.g. the ActionBar HUD, the tile Inspect panel, and the zone-selection UI.
/// </summary>
public class GameplayHudGate : MonoBehaviour
{
    [Tooltip("GameObjects to deactivate while an action is running (days passing) and " +
             "reactivate when it completes. e.g. ActionBar HUD, Inspect Tile HUD, Zone Selection UI.")]
    [SerializeField] private GameObject[] hideWhileActionRuns;

    private bool _wasRunning;

    void Start()
    {
        if (ActionManager.Instance == null)
            Debug.LogError($"{name}: GameplayHudGate found no ActionManager — HUD gating disabled. " +
                           "Ensure an ActionManager is in the scene.", this);

        if (hideWhileActionRuns == null || hideWhileActionRuns.Length == 0)
            Debug.LogWarning($"{name}: GameplayHudGate has no HUD roots wired — nothing will be gated. " +
                             "Assign the ActionBar / Inspect / Zone UIs in the Inspector.", this);

        _wasRunning = ActionManager.Instance != null && ActionManager.Instance.IsActionRunning;
    }

    void Update()
    {
        if (ActionManager.Instance == null) return;

        bool running = ActionManager.Instance.IsActionRunning;
        if (running == _wasRunning) return;   // only act on a transition

        _wasRunning = running;
        SetHudActive(!running);               // hide while running, show when at rest
    }

    private void SetHudActive(bool active)
    {
        if (hideWhileActionRuns == null) return;
        foreach (GameObject go in hideWhileActionRuns)
            if (go != null) go.SetActive(active);
    }
}
