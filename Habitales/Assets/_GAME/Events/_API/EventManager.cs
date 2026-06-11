using System.Collections.Generic;
using UnityEngine;
using UTILITIES.Camera;
using Habitales.Dialogue; 

[DefaultExecutionOrder(-100)] // manager — initializes after core services (arch §4 init order)
public class EventManager : MonoBehaviour
{
    public static EventManager Instance { get; private set; }

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;

    [Header("Data")]
    [SerializeField] private GameEventRegistry eventRegistry;
    private List<GameEventSO> allEvents;
    
    private HashSet<string> firedEventIDs = new HashSet<string>();
    private Queue<GameEventSO> eventQueue = new Queue<GameEventSO>();
    private bool _pannedThisBatch = false;
    public bool IsShowingEvent { get; private set; } = false;

    // ───────────────────────────────────────────
    // LIFECYCLE
    // ───────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (eventRegistry == null)
        {
            Debug.LogError("EventManager: No GameEventRegistry assigned in Inspector!");
            return;
        }

        allEvents = eventRegistry.events;

        if (showDebugInfo)
            Debug.Log($"EventManager: Registry loaded {allEvents.Count} events.");
    }

    void Start()
    {
        ResourceManager rm = ResourceManager.Instance;
        if (rm != null)
        {
            rm.OnTimeAdvanced    += HandleTimeAdvanced;
            rm.OnGameOver        += HandleGameOver;
        }
        else Debug.LogError("EventManager: ResourceManager not found!");

        RegionManager zm = RegionManager.Instance;
        if (zm != null)
            zm.OnRegionGenerated += HandleRegionGenerated;
        else Debug.LogWarning("EventManager: RegionManager not found — OnRegionUnlock events won't fire.");
        
        RefreshGlobalContext(0);
        ResourceManager.Instance.OnTimeAdvanced += RefreshGlobalContext;
    }

    void OnDestroy()
    {
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.OnTimeAdvanced -= HandleTimeAdvanced;
            ResourceManager.Instance.OnTimeAdvanced -= RefreshGlobalContext;
            ResourceManager.Instance.OnGameOver     -= HandleGameOver;
        }
    }

    // ───────────────────────────────────────────
    // TRIGGER HANDLERS
    // ───────────────────────────────────────────

    void HandleTimeAdvanced(int days)
    {
        if (IsShowingEvent) return; // queue already mid-playback, don't collect new events
        int today = ResourceManager.Instance.TotalDays;
        float worldHealth = RegionManager.Instance?.GetTotalAverageHealth() ?? 100f;
        foreach (GameEventSO ev in allEvents)
        {
            if (ShouldSkip(ev)) continue;
            bool fires = ev.triggerType switch
            {
                EventTriggerType.OnDay             => ev.triggerOnDay > 0 && today == ev.triggerOnDay,
                EventTriggerType.OnHealthThreshold => ev.triggerBelowWorldHealth > 0 && worldHealth < ev.triggerBelowWorldHealth,
                EventTriggerType.Random            => Random.value < ev.triggerChance * (days / 365f),
                _                                  => false
            };
            if (fires) eventQueue.Enqueue(ev); // collect ALL qualifying events this tick
        }
        ShowNextInQueue();
    }

    void HandleRegionGenerated(RegionGenerationResult result)
    {
        if (IsShowingEvent) return;
        foreach (GameEventSO ev in allEvents)
        {
            if (ShouldSkip(ev)) continue;
            if (ev.triggerType == EventTriggerType.OnZoneUnlock)
                eventQueue.Enqueue(ev);
        }
        ShowNextInQueue();
    }


    void HandleGameOver()
    {
        eventQueue.Clear();
        _pannedThisBatch = false;
        if (IsShowingEvent) EventPopupUI.Instance?.Hide();
        IsShowingEvent = false;
    }


    // ───────────────────────────────────────────
    // FIRING
    // ───────────────────────────────────────────

    /// <summary>
    /// Fires an event by ID string. Use this for Manual trigger types
    /// called directly from other systems (e.g. FireEntity, VillageEntity).
    /// </summary>
    public void FireEventByID(string eventID)
    {
        GameEventSO ev = allEvents.Find(e => e.eventID == eventID);
        if (ev == null) { Debug.LogWarning($"EventManager: No event found with ID '{eventID}'"); return; }
        if (ShouldSkip(ev)) return;
        eventQueue.Enqueue(ev);
        if (!IsShowingEvent) ShowNextInQueue(); // if queue was idle, kick it off
        // if already showing, the event will appear after the current one dismisses
    }

    void ShowNextInQueue()
    {
        if (eventQueue.Count == 0)
        {
            IsShowingEvent = false;
            if (showDebugInfo) Debug.Log("EventManager: Queue empty, game resumed.");

            if (_pannedThisBatch && EventCameraHandler.Instance != null)
            {
                _pannedThisBatch = false;
                EventCameraHandler.Instance.ReturnToOrigin(
                    onComplete: () => RunManager.Instance?.ResumeFromEvent()
                );
            }
            else
            {
                RunManager.Instance?.ResumeFromEvent();
            }
            return;
        }
        FireEvent(eventQueue.Dequeue());
    }

    void FireEvent(GameEventSO ev)
    {
        if (EventPopupUI.Instance == null)
        {
            Debug.LogError("EventManager: EventPopupUI.Instance is null!");
            return;
        }
        if (showDebugInfo) Debug.Log($"EventManager: Firing '{ev.eventID}' ({eventQueue.Count} remaining in queue)");
        if (ev.fireOnce) firedEventIDs.Add(ev.eventID);

        string resolvedHeadline = EventContext.Resolve(ev.headline);
        string resolvedBody     = EventContext.Resolve(ev.bodyText);

        Vector3? focusTarget = EventContext.GetFocusTarget();
        EventContext.ClearOverrides(); // clears token overrides AND focus target

        if (!IsShowingEvent) RunManager.Instance?.PauseForEvent();
        IsShowingEvent = true;

        void ShowPopup()
        {
            EventPopupUI.Instance.Show(ev, resolvedHeadline, resolvedBody,
                onContinueCallback: ResumeAfterEvent,
                onAbortCallback:    null
            );
        }

        if (ev.focusCameraOnTarget && focusTarget.HasValue && EventCameraHandler.Instance != null)
        {
            _pannedThisBatch = true;
            EventCameraHandler.Instance.PanTo(focusTarget.Value, onComplete: ShowPopup);
        }
        else
        {
            ShowPopup();
        }
        
        if (ev.linkedThread != null)
            DialogueManager.Instance.AppendThread(ev.linkedThread);
        
        // Habitales.Dialogue.DialogueManager.Instance.AppendThread(ev.linkedThread);
    }

    void ResumeAfterEvent()
    {
        if (showDebugInfo) Debug.Log("EventManager: Event dismissed, checking queue...");
        ShowNextInQueue(); // pops next or resumes game if empty
    }
    
    void RefreshGlobalContext(int _ = 0)
    {
        ResourceManager rm = ResourceManager.Instance;
        if (rm == null) return;

        int year = rm.TotalDays / rm.DaysPerYear + 1;
        int day  = rm.TotalDays % rm.DaysPerYear + 1;

        EventContext.Set("current_year",        year.ToString());
        EventContext.Set("current_day",         day.ToString());
        EventContext.Set("available_workers",   rm.AvailablePeople.ToString());
        EventContext.Set("total_workers",       rm.TotalPeople.ToString());
        EventContext.Set("recovering_workers",  rm.RecoveringPeopleCount.ToString());

        RegionManager zm = RegionManager.Instance;
        if (zm != null)
        {
            EventContext.Set("zone_count",      (zm.NextRegionID - 1).ToString());
            EventContext.Set("world_health",    zm.GetTotalAverageHealth().ToString("F1"));
        }
    } 

    // ───────────────────────────────────────────
    // HELPERS
    // ───────────────────────────────────────────

    bool ShouldSkip(GameEventSO ev)
    {
        if (ev == null)
        {
            Debug.LogWarning("EventManager: Null entry found in GameEventRegistry. Remove the empty slot.");
            return true;
        }
        if (string.IsNullOrEmpty(ev.eventID))
        {
            Debug.LogWarning("EventManager: Skipping event with empty eventID.");
            return true;
        }
        return ev.fireOnce && firedEventIDs.Contains(ev.eventID);
    }

    /// <summary>
    /// Wipes fired history â€” call this on a new run.
    /// </summary>
    public void ResetForNewRun()
    {
        firedEventIDs.Clear();
        eventQueue.Clear();
        _pannedThisBatch = false;
    }
}