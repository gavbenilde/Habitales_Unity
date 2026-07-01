using UnityEngine;

/// <summary>
/// Transitional forwarding shim (WO-4, 2026-06-30).
/// Real logic lives in <see cref="Habitales.Triggers.TriggerManager"/>.
/// Scheduled for deletion in M3 once callers are repointed directly.
///
/// Callers that still go through this shim (do NOT edit them — the shim covers them):
///   • OnboardingDirector.cs:686  — FireEventByID
///   • OnboardingBootstrap.cs:63,69 — FireEventByID
///   • IEntityEventSink (EventManagerEntitySink):69 — FireEventByID(e.cause)
///   • TileSelector.cs:371/382/732 — IsShowingEvent
///   • GameBootstrap.cs:50        — EventManager.Instance != null
/// </summary>
[DefaultExecutionOrder(-100)] // manager — initializes after core services (arch §4 init order)
public class EventManager : MonoBehaviour
{
    public static EventManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── Forwarded API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Forwards to <see cref="Habitales.Triggers.TriggerManager.Fire"/>.
    /// Logs a loud error if TriggerManager is not yet in the scene.
    /// </summary>
    public void FireEventByID(string eventID)
    {
        if (Habitales.Triggers.TriggerManager.Instance == null)
        {
            Debug.LogError(
                "EventManager (shim): TriggerManager.Instance is null — " +
                "is a TriggerManager wired in the scene?", this);
            return;
        }
        Habitales.Triggers.TriggerManager.Instance.Fire(eventID);
    }

    /// <summary>
    /// Reflects <see cref="Habitales.Triggers.TriggerManager.IsBusy"/>.
    /// Returns false when TriggerManager is absent (safe default — no popup on screen).
    /// </summary>
    public bool IsShowingEvent =>
        Habitales.Triggers.TriggerManager.Instance != null &&
        Habitales.Triggers.TriggerManager.Instance.IsBusy;

    /// <summary>
    /// Forwards to <see cref="Habitales.Triggers.TriggerManager.ResetForNewRun"/>.
    /// </summary>
    public void ResetForNewRun() =>
        Habitales.Triggers.TriggerManager.Instance?.ResetForNewRun();
}
