using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Upper-right "Unlock Next Zone" button. Hidden until world health crosses
/// the Zone Unlock Threshold, at which point RunManager fires OnRegionUnlockReady.
/// Pressing it asks RunManager to generate the next zone, then hides again.
/// </summary>
[RequireComponent(typeof(Button))]
public class UnlockNextZoneButtonUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Root object toggled on/off. If null, this GameObject is used.")]
    [SerializeField] private GameObject buttonRoot;
    [SerializeField] private Button button;

    private RunManager runManager;

    void Awake()
    {
        if (buttonRoot == null) buttonRoot = gameObject;
        if (button == null) button = GetComponent<Button>();
    }

    void Start()
    {
        runManager = RunManager.Instance;

        if (runManager != null)
        {
            runManager.OnRegionUnlockReady += HandleUnlockReady;
            runManager.OnRegionUnlocked    += HandleUnlocked;
        }
        else
        {
            Debug.LogWarning("UnlockNextZoneButtonUI: RunManager.Instance is null at Start — button will stay hidden.");
        }

        if (button != null)
            button.onClick.AddListener(HandleClicked);

        SetVisible(false); // start hidden
    }

    void OnDestroy()
    {
        if (runManager != null)
        {
            runManager.OnRegionUnlockReady -= HandleUnlockReady;
            runManager.OnRegionUnlocked    -= HandleUnlocked;
        }

        if (button != null)
            button.onClick.RemoveListener(HandleClicked);
    }

    private void HandleUnlockReady() => SetVisible(true);
    private void HandleUnlocked()    => SetVisible(false);

    private void HandleClicked()
    {
        if (runManager != null)
            runManager.UnlockNextRegion();
    }

    private void SetVisible(bool visible)
    {
        if (buttonRoot != null)
            buttonRoot.SetActive(visible);
    }
}
