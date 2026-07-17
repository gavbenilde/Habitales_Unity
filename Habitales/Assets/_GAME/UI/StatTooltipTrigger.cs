using UnityEngine;

namespace Habitales.UI
{
    /// <summary>
    /// Tooltip trigger for the inspect panel's six substat icons: shows the stat's
    /// display name plus its live value, e.g. "Nutrient Balance - 50%".
    /// InspectPanelUI pushes the value via SetValue on every populate; SetUnknown
    /// is the locked/empty fallback ("Nutrient Balance - ?").
    ///
    /// WIRING (human steps):
    ///   1. On each of the 6 substat icon GameObjects, replace HoverTooltipTrigger
    ///      with this component (Add Component → Habitales → UI → Stat Tooltip Trigger).
    ///   2. Set "Stat Name" (e.g. "Nutrient Balance"). Leave Tooltip Text empty —
    ///      it is overwritten at runtime.
    ///   3. Assign the trigger into InspectPanelUI's "Substat Tooltips" array,
    ///      same order as the indicator array (or leave the slot empty if the
    ///      trigger sits on the indicator Image itself — Awake picks it up).
    /// </summary>
    [AddComponentMenu("Habitales/UI/Stat Tooltip Trigger")]
    public class StatTooltipTrigger : HoverTooltipTrigger
    {
        [Header("Stat")]
        [Tooltip("Display name shown before the value, e.g. \"Nutrient Balance\".")]
        [SerializeField] private string _statName;

        /// <summary>Composes "Stat Name - 50%" from the pushed 0–100 value.</summary>
        public void SetValue(float value) => SetText($"{_statName} - {value:F0}%");

        /// <summary>Locked/empty fallback: "Stat Name - ?".</summary>
        public void SetUnknown() => SetText($"{_statName} - ?");
    }
}
