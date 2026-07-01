using TMPro;
using UnityEngine;

namespace Habitales.UI
{
    /// <summary>
    /// Passive tiered trend arrow. The owner pushes a signed per-day delta via
    /// <see cref="SetDelta"/>; this view buckets the magnitude into three tiers and renders
    /// a text glyph (<c>&gt;</c> / <c>&gt;&gt;</c> / <c>&gt;&gt;&gt;</c>, flipped to
    /// <c>&lt;</c> when falling), tinted green (rising) or red (falling), and hides itself
    /// when the change is below tier 1 (flat → no noise).
    ///
    /// Arrows are pure TMP text — no art dependency. Tier thresholds are tunable in the
    /// Inspector (defaults 0.5 / 1.5 / 2.5 health-points per day).
    ///
    /// WIRING (human):
    ///   1. Place this on a persistent GameObject (it stays active; only <c>_root</c> toggles).
    ///   2. Set <c>_label</c> → the TMP_Text that shows the arrow glyph.
    ///   3. (Optional) Set <c>_root</c> → the visuals child to hide when flat (defaults to this GameObject).
    ///   4. (Optional) Set <c>_tooltip</c> → a HoverTooltipTrigger on the arrow; this view feeds it
    ///      the live trend string ("Improving fast (+3.2/day)") via SetText.
    /// </summary>
    public class TrendIndicatorUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TMP_Text            _label;    // the arrow glyph (critical)
        [SerializeField] private GameObject          _root;     // toggled visuals (defaults to this GO)
        [SerializeField] private HoverTooltipTrigger _tooltip;  // optional — fed the live trend string

        [Header("Tier thresholds (|Δ| per day, health-points)")]
        [SerializeField] private float _tier1Threshold = 0.5f;  // below this → flat → hidden
        [SerializeField] private float _tier2Threshold = 1.5f;
        [SerializeField] private float _tier3Threshold = 2.5f;

        [Header("Colors")]
        [SerializeField] private Color _risingColor  = new Color(0.26f, 0.72f, 0.20f); // green
        [SerializeField] private Color _fallingColor = new Color(0.85f, 0.18f, 0.12f); // red

        private void Awake()
        {
            if (_label == null)
            {
                Debug.LogError($"{name}: TrendIndicatorUI._label (TMP_Text) missing — wire it in the Inspector.", this);
                enabled = false;
            }
            if (_root == null) _root = gameObject;
        }

        /// <summary>
        /// Pushes the current per-day delta. Positive = rising (green), negative = falling (red),
        /// |Δ| &lt; tier 1 = flat (hidden). Safe to call while the visuals root is inactive.
        /// </summary>
        public void SetDelta(float delta)
        {
            float mag = Mathf.Abs(delta);

            if (mag < _tier1Threshold)
            {
                SetVisible(false);
                return;
            }

            bool rising = delta > 0f;
            int  tier   = mag >= _tier3Threshold ? 3 : mag >= _tier2Threshold ? 2 : 1;

            if (_label != null)
            {
                _label.text  = Glyph(rising, tier);
                _label.color = rising ? _risingColor : _fallingColor;
            }

            if (_tooltip != null)
            {
                string dir  = rising ? "Improving" : "Declining";
                string rate = tier == 3 ? " fast" : tier == 1 ? " slowly" : "";
                _tooltip.SetText($"{dir}{rate} ({(rising ? "+" : "")}{delta:F1}/day)");
            }

            SetVisible(true);
        }

        private static string Glyph(bool rising, int tier)
        {
            if (rising) return tier == 3 ? ">>>" : tier == 2 ? ">>" : ">";
            return            tier == 3 ? "<<<" : tier == 2 ? "<<" : "<";
        }

        private void SetVisible(bool visible)
        {
            if (_root != null && _root.activeSelf != visible)
                _root.SetActive(visible);
        }
    }
}
