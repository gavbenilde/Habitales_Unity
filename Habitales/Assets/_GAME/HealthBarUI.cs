using UnityEngine;
using UnityEngine.UI;

public class HealthBarUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Slider healthSlider; // Drag the Slider here
    [SerializeField] private Image fillImage;     // Drag the 'Fill' object here

    [Header("Color Gradient Settings")]
    [SerializeField] private Color purple = new Color(0.6f, 0.2f, 0.8f);
    [SerializeField] private Color blue   = Color.blue;
    [SerializeField] private Color pink   = new Color(1f, 0.4f, 0.7f);
    [SerializeField] private Color green  = Color.green;

    void Start()
    {
        // Ensure the slider range matches 0-100 health
        healthSlider.minValue = 0;
        healthSlider.maxValue = 100;
    }

    void Update()
    {
        // 1. Check if GameManager exists
        if (GameManager.Instance == null) return;

        // 2. Use the Instance to get the ZoneManager safely
        // Based on your GameManager.cs, we need to find the ZoneManager component
        ZoneManager zm = FindObjectOfType<ZoneManager>(); 
    
        if (zm != null)
        {
            float currentHealth = zm.GetTotalAverageHealth();

            // Update Slider
            healthSlider.value = currentHealth;

            // Update Color
            fillImage.color = GetStepGradient(currentHealth / 100f);
        }
    }

    private Color GetStepGradient(float t)
    {
        // t is normalized 0 to 1
        if (t < 0.33f) 
            return Color.Lerp(purple, blue, t / 0.33f);
        if (t < 0.66f) 
            return Color.Lerp(blue, pink, (t - 0.33f) / 0.33f);
        
        return Color.Lerp(pink, green, (t - 0.66f) / 0.34f);
    }
}