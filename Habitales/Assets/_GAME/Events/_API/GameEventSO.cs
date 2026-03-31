using UnityEngine;

public enum EventTriggerType
{
    OnDay,              // fires on a specific day number
    OnZoneUnlock,       // fires when any new zone generates
    OnHealthThreshold,  // fires when world health drops below a value
    Random,             // rolls each time AdvanceTime is called
    Manual              // only fired explicitly by code (e.g. fire breaks out)
}

[CreateAssetMenu(fileName = "NewEvent", menuName = "Habitales/Game Event")]
public class GameEventSO : ScriptableObject
{
    [Header("Identity")]
    public string eventID;              // unique key, e.g. "first_fire_warning"
    public EventTriggerType triggerType;
    public bool fireOnce = true;        // if true, can only trigger once per run

    [Header("Presentation")]
    public string headline;
    [TextArea(2, 5)]
    public string bodyText;
    public string speakerName;          // "Azi", "Bob", "" = headline/news format
    public Sprite speakerPortrait;      // null = no portrait shown
    public bool canInterruptAction = true; // shows Continue/Abort buttons
    public bool focusCameraOnTarget = false;

    [Header("Trigger Conditions")]
    [Range(0f, 1f)]
    public float triggerChance = 1f;    // used by Random type
    public int triggerOnDay = -1;       // used by OnDay type; -1 = ignore
    [Range(0f, 100f)]
    public float triggerBelowWorldHealth = -1f; // used by OnHealthThreshold; -1 = ignore
    
    [Header("Dialogue Bridge")]
    public Habitales.Dialogue.DialogueThreadSO linkedThread;
}