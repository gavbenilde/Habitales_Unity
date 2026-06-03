using UnityEngine;
using TMPro;

/// <summary>
/// Simple top-of-screen objective banner. Tells the player the goal up front
/// ("Rehabilitate the world!") and — once a zone is ready to unlock — nudges
/// them toward the Unlock button. Reuses RunManager's zone-unlock events.
/// </summary>
public class ObjectiveBannerUI : MonoBehaviour
{
    [Header("Reference")]
    [SerializeField] private TextMeshProUGUI bannerText;

    [Header("Messages")]
    [SerializeField] private string defaultMessage      = "Rehabilitate the world!";
    [SerializeField] private string unlockReadyMessage  = "Zone restored — unlock the next zone!";

    private RunManager runManager;

    void Awake()
    {
        if (bannerText == null) bannerText = GetComponent<TextMeshProUGUI>();
    }

    void Start()
    {
        SetText(defaultMessage);

        runManager = RunManager.Instance;
        if (runManager != null)
        {
            runManager.OnZoneUnlockReady += HandleUnlockReady;
            runManager.OnZoneUnlocked    += HandleUnlocked;
        }
    }

    void OnDestroy()
    {
        if (runManager != null)
        {
            runManager.OnZoneUnlockReady -= HandleUnlockReady;
            runManager.OnZoneUnlocked    -= HandleUnlocked;
        }
    }

    private void HandleUnlockReady() => SetText(unlockReadyMessage);
    private void HandleUnlocked()    => SetText(defaultMessage);

    private void SetText(string msg)
    {
        if (bannerText != null) bannerText.text = msg;
    }
}
