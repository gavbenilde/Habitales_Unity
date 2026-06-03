using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Single zone status pill spawned dynamically by EndGameScreenUI.
/// Assign label and background Image in the prefab.
/// </summary>
public class ZonePillUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private Image           background;

    // Colours matching the game's health thresholds
    private static readonly Color ColourThriving = new Color(0.26f, 0.48f, 0.13f, 1f);
    private static readonly Color ColourDegraded  = new Color(0.85f, 0.44f, 0.10f, 1f);
    private static readonly Color ColourCritical  = new Color(0.63f, 0.17f, 0.17f, 1f);

    public void Setup(int regionID, float health)
    {
        string status;
        Color  bg;

        if      (health > 67f) { status = "Thriving"; bg = ColourThriving; }
        else if (health > 33f) { status = "Degraded";  bg = ColourDegraded;  }
        else                   { status = "Critical";  bg = ColourCritical;  }

        label.text       = $"Zone {regionID} — {status}";
        background.color = bg;
    }
}
