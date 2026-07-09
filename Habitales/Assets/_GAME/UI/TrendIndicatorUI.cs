using UnityEngine;
using UnityEngine.UI;

namespace Habitales.UI
{
    /// <summary>
    /// Passive tiered trend arrow. The owner pushes a signed per-day trend via
    /// <see cref="SetTrend"/>; this view buckets the magnitude into three tiers and swaps a
    /// single <see cref="Image"/> to the matching sprite — six interchangeable slots
    /// (rising 1/2/3 + falling 1/2/3). It hides itself when the change is below tier 1
    /// (flat → no noise).
    ///
    /// Tier thresholds are tunable in the Inspector (defaults 0.5 / 1.5 / 2.5 health-points
    /// per day). Placeholder chevron sprites are provided under
    /// _ART/UI/Indicators/Placeholder/ — swap in final art via the same six slots.
    ///
    /// WIRING (human):
    ///   1. Place this on a persistent GameObject (it stays active; only <c>_root</c> toggles).
    ///   2. Set <c>_icon</c> → the Image that shows the arrow sprite.
    ///   3. Assign the six sprite slots (rising/falling × 3 tiers).
    ///   4. (Optional) Set <c>_root</c> → the visuals child to hide when flat (defaults to this GameObject).
    ///   5. (Optional) Set <c>_tooltip</c> → a HoverTooltipTrigger on the arrow; this view feeds it
    ///      the live trend string ("Improving fast (+3.2/day)") via SetText.
    /// </summary>
    public class TrendIndicatorUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Image               _icon;     // the arrow sprite (critical)
        [SerializeField] private GameObject          _root;     // toggled visuals (defaults to this GO)
        [SerializeField] private HoverTooltipTrigger _tooltip;  // optional — fed the live trend string

        [Header("Rising sprites (tier 1 → 3)")]
        [SerializeField] private Sprite _rising1;
        [SerializeField] private Sprite _rising2;
        [SerializeField] private Sprite _rising3;

        [Header("Falling sprites (tier 1 → 3)")]
        [SerializeField] private Sprite _falling1;
        [SerializeField] private Sprite _falling2;
        [SerializeField] private Sprite _falling3;

        [Header("Tier thresholds (|Δ| per day, health-points)")]
        [SerializeField] private float _tier1Threshold = 0.5f;  // below this → flat → hidden
        [SerializeField] private float _tier2Threshold = 1.5f;
        [SerializeField] private float _tier3Threshold = 2.5f;

        private void Awake()
        {
            if (_icon == null)
            {
                Debug.LogError($"{name}: TrendIndicatorUI._icon (Image) missing — wire it in the Inspector.", this);
                enabled = false;
            }
            if (_root == null) _root = gameObject;
        }

        /// <summary>
        /// Pushes the current per-day trend. Positive = rising, negative = falling,
        /// |Δ| &lt; tier 1 = flat (hidden). Safe to call while the visuals root is inactive.
        /// </summary>
        public void SetTrend(float trend)
        {
            float mag = Mathf.Abs(trend);

            if (mag < _tier1Threshold)
            {
                SetVisible(false);
                return;
            }

            bool rising = trend > 0f;
            int  tier   = mag >= _tier3Threshold ? 3 : mag >= _tier2Threshold ? 2 : 1;

            if (_icon != null)
                _icon.sprite = SpriteFor(rising, tier);

            if (_tooltip != null)
            {
                string dir  = rising ? "Improving" : "Declining";
                string rate = tier == 3 ? " fast" : tier == 1 ? " slowly" : "";
                _tooltip.SetText($"{dir}{rate} ({(rising ? "+" : "")}{trend:F1}/day)");
            }

            SetVisible(true);
        }

        private Sprite SpriteFor(bool rising, int tier)
        {
            if (rising) return tier == 3 ? _rising3  : tier == 2 ? _rising2  : _rising1;
            return             tier == 3 ? _falling3 : tier == 2 ? _falling2 : _falling1;
        }

        private void SetVisible(bool visible)
        {
            if (_root != null && _root.activeSelf != visible)
                _root.SetActive(visible);
        }
    }
}
