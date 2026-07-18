using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Habitales.Triggers;

/// <summary>
/// HUD "Pass Day" button — advances the simulation exactly one day with no action.
/// Skipping is free (user decision 2026-07-18). Reuses ResourceManager.AdvanceTimeStepped(1)
/// behind the same gates ActionManager uses (no mid-action skip, no skip while an event popup
/// holds the sim). The heartbeat handles decay/weather/events/game-over — nothing else to call.
///
/// Wiring: attach to the HUD button GameObject (auto-grabs the Button), or assign the ref.
/// </summary>
public class PassDayButton : MonoBehaviour
{
    [SerializeField] private Button button;

    // True while our own one-day advance is mid-cycle (waiting on DayNightCycleHandler).
    private bool _skipRunning;

    void Awake()
    {
        if (button == null) button = GetComponent<Button>();
        if (button == null)
        {
            Debug.LogError($"{name}: PassDayButton needs a Button — attach to the button or wire the ref.", this);
            enabled = false;
        }
    }

    void OnEnable()  { if (button != null) button.onClick.AddListener(HandleClick); }
    void OnDisable() { if (button != null) button.onClick.RemoveListener(HandleClick); }

    void Update()
    {
        button.interactable = CanSkip();
    }

    bool CanSkip()
    {
        if (_skipRunning) return false;
        if (ResourceManager.Instance == null) return false;
        if (ActionManager.Instance != null && ActionManager.Instance.IsActionRunning) return false;
        if (TriggerManager.Instance != null && TriggerManager.Instance.IsBusy) return false;
        // The doc contract ("no skip while an event popup holds the sim") was never actually
        // enforced — a check-in or modal popup left this button live (2026-07-19 fix).
        if (RunManager.Instance != null && RunManager.Instance.IsEventPaused) return false;
        return true;
    }

    void HandleClick()
    {
        if (!CanSkip()) return;
        StartCoroutine(SkipOneDay());
    }

    IEnumerator SkipOneDay()
    {
        _skipRunning = true;
        yield return ResourceManager.Instance.AdvanceTimeStepped(1);
        _skipRunning = false;
    }
}
