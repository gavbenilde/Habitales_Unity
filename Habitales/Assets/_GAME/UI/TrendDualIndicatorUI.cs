using UnityEngine;
using UnityEngine.UI;

namespace Habitales.UI
{
    /// <summary>
    /// Passive dual trend indicator (added 2026-07-22) — two persistent Images flanking a health
    /// bar: a DECAYING glyph on the left and a THRIVING glyph on the right. The owner pushes a
    /// signed per-day trend via <see cref="SetTrend"/>; this view shows exactly one side (tiered
    /// by magnitude) and hides the other, or hides both when the change is below tier 1 (flat →
    /// no noise). Same tier-bucketing as <see cref="TrendIndicatorUI"/> (thresholds 0.5 / 1.5 /
    /// 2.5 health-points per day by default) — kept a standalone widget rather than a subclass so
    /// each side keeps its own three sprite slots.
    ///
    /// Law 1: passive view only — it never reads game state, it is fed. Loud-warns on missing
    /// Image refs like its sibling widgets.
    ///
    /// WIRING (human):
    ///   1. Place this on a persistent GameObject beside the health bar (it stays active; only
    ///      the two child Images toggle).
    ///   2. Set <c>_decayingIcon</c> → the left Image, <c>_thrivingIcon</c> → the right Image.
    ///   3. Assign the three decaying sprites + three thriving sprites (tier 1 → 3).
    ///   4. (Optional) Tune the tier thresholds to match TrendIndicatorUI.
    /// </summary>
    public class TrendDualIndicatorUI : MonoBehaviour
    {
        [Header("Decaying (left) — shown when trend < 0")]
        [SerializeField] private Image _decayingIcon;
        [SerializeField] private Sprite _decaying1;
        [SerializeField] private Sprite _decaying2;
        [SerializeField] private Sprite _decaying3;

        [Header("Thriving (right) — shown when trend > 0")]
        [SerializeField] private Image _thrivingIcon;
        [SerializeField] private Sprite _thriving1;
        [SerializeField] private Sprite _thriving2;
        [SerializeField] private Sprite _thriving3;

        [Header("Tier thresholds (|Δ| per day, health-points)")]
        [SerializeField] private float _tier1Threshold = 0.5f;  // below this → flat → both hidden
        [SerializeField] private float _tier2Threshold = 1.5f;
        [SerializeField] private float _tier3Threshold = 2.5f;

        private void Awake()
        {
            if (_decayingIcon == null || _thrivingIcon == null)
                Debug.LogError($"{name}: TrendDualIndicatorUI is missing a decaying/thriving Image ref — " +
                               "wire both in the Inspector.", this);
        }

        /// <summary>
        /// Pushes the current per-day trend. trend &lt; 0 → show the decaying glyph (tiered), hide
        /// thriving; trend &gt; 0 → show the thriving glyph (tiered), hide decaying; |Δ| below
        /// tier 1 → hide both. Safe to call while the icons are inactive.
        /// </summary>
        public void SetTrend(float trend)
        {
            float mag = Mathf.Abs(trend);

            if (mag < _tier1Threshold)
            {
                SetSide(_decayingIcon, null, false);
                SetSide(_thrivingIcon, null, false);
                return;
            }

            int tier = mag >= _tier3Threshold ? 3 : mag >= _tier2Threshold ? 2 : 1;

            if (trend < 0f)
            {
                SetSide(_decayingIcon, DecayingSpriteFor(tier), true);
                SetSide(_thrivingIcon, null, false);
            }
            else
            {
                SetSide(_thrivingIcon, ThrivingSpriteFor(tier), true);
                SetSide(_decayingIcon, null, false);
            }
        }

        private Sprite DecayingSpriteFor(int tier) => tier == 3 ? _decaying3 : tier == 2 ? _decaying2 : _decaying1;
        private Sprite ThrivingSpriteFor(int tier) => tier == 3 ? _thriving3 : tier == 2 ? _thriving2 : _thriving1;

        private void SetSide(Image icon, Sprite sprite, bool visible)
        {
            if (icon == null) return;

            if (visible)
            {
                if (sprite != null) icon.sprite = sprite;
                else Debug.LogWarning($"{name}: TrendDualIndicatorUI has no sprite for the requested tier — " +
                                      "assign all three tier slots on both sides.", this);
            }

            if (icon.gameObject.activeSelf != visible)
                icon.gameObject.SetActive(visible);
        }
    }
}
