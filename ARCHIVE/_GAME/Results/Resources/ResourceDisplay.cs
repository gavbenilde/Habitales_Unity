using UnityEngine;
using TMPro;
using Unity.VisualScripting;

/// <summary>
/// Simple HUD display for Time and People resources.
/// </summary>
public class ResourceDisplay : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI timeText;
    [SerializeField] private TextMeshProUGUI peopleText;
    
    private ResourceManager resourceManager;
    
    void Start()
    {
        resourceManager = ResourceManager.Instance;
        
        if (resourceManager == null)
        {
            Debug.LogError("ResourceDisplay: ResourceManager not found!");
            enabled = false;
            return;
        }
        
        // Subscribe to updates
        resourceManager.OnTimeAdvanced += UpdateDisplay;
        resourceManager.OnPeopleFatigued += (count, day) => UpdateDisplay(0);
        resourceManager.OnPeopleRecovered += (count) => UpdateDisplay(0);
        
        // Initial display
        UpdateDisplay(0);
    }
    
    void UpdateDisplay(int daysAdvanced)
    {
        if (resourceManager == null) return;
        
        // Time display
        if (timeText != null)
        {
            timeText.text = $"{resourceManager.GetFullTimeDisplay()}";
        }
        
        // People display
        if (peopleText != null)
        {
            int available = resourceManager.AvailablePeople;
            int total = resourceManager.TotalPeople;
            int recovering = resourceManager.RecoveringPeopleCount;
            
            string color = available > 15 ? "#61c415" : available > 5 ? "#e7b81d" : "#c41515";
            peopleText.text = $"<color={color}>{available}</color>/{total}";
            
            if (recovering > 0)
            {
                peopleText.text += $" <color=orange>(−{recovering})</color>";
            }
        }
    }
    
    void OnDestroy()
    {
        if (resourceManager != null)
        {
            resourceManager.OnTimeAdvanced -= UpdateDisplay;
        }
    }
}