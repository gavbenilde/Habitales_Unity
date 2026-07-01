using UnityEngine;

namespace Habitales.UI
{
    /// <summary>
    /// The small orange "there's a tooltip here" badge. Drop it on a corner Image of any
    /// icon/text the player can learn about. It feeds the tooltip text into a
    /// <see cref="HoverTooltipTrigger"/> on the same GameObject — either pulled from a
    /// <see cref="StatIconLibrary"/> entry (name + description) or a plain override string.
    ///
    /// All positioning/show/hide of the actual tooltip stays in TooltipController (S2);
    /// this only supplies content + the visual affordance.
    ///
    /// WIRING (human):
    ///   1. Add an Image (the orange dot — placeholder sprite provided) to the corner of the icon.
    ///   2. Add Component ▶ TooltipBadge (it auto-adds HoverTooltipTrigger).
    ///   3. Either assign <c>_library</c> + pick a <c>_stat</c> (recommended — single source),
    ///      or untick <c>_useLibraryText</c> and type <c>_overrideText</c>.
    /// </summary>
    [RequireComponent(typeof(HoverTooltipTrigger))]
    public class TooltipBadge : MonoBehaviour
    {
        [Header("Tooltip content source")]
        [Tooltip("When on, pulls 'Name\\nDescription' from the library for the chosen stat.")]
        [SerializeField] private bool            _useLibraryText = true;
        [SerializeField] private StatIconLibrary _library;
        [SerializeField] private IndicatorStatId _stat;

        [Tooltip("Used when 'Use Library Text' is off.")]
        [TextArea]
        [SerializeField] private string _overrideText;

        private void Start()
        {
            var trigger = GetComponent<HoverTooltipTrigger>();
            if (trigger == null) return; // RequireComponent guarantees one, but stay defensive

            trigger.SetText(ResolveText());
        }

        private string ResolveText()
        {
            if (!_useLibraryText)
                return _overrideText;

            if (_library == null)
            {
                Debug.LogError($"{name}: TooltipBadge has 'Use Library Text' on but no _library wired — " +
                               "assign a StatIconLibrary or untick it and use _overrideText.", this);
                return _overrideText;
            }

            string label = _library.GetDisplayName(_stat);
            string body  = _library.GetDescription(_stat);
            return string.IsNullOrEmpty(body) ? label : $"{label}\n{body}";
        }
    }
}
