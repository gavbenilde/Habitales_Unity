using TMPro;
using UnityEngine;

namespace Habitales.UI
{
    /// <summary>
    /// One label/value row in the check-in panel's stats column. Passive view (Law 1) —
    /// CheckInPanelUI spawns and fills these; adding a new stat to the panel is one
    /// AddRow call, which is what keeps the column scalable.
    /// </summary>
    public class CheckInStatRowUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI labelText;
        [SerializeField] private TextMeshProUGUI valueText;

        public void Set(string label, string value)
        {
            labelText.text = label;
            valueText.text = value;
        }

        public void Set(string label, string value, Color valueColor)
        {
            Set(label, value);
            valueText.color = valueColor;
        }
    }
}
