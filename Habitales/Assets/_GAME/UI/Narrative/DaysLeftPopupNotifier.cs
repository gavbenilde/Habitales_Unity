using UnityEngine;

namespace Habitales.UI
{
    /// <summary>
    /// Passive periodic check-in: every <see cref="intervalDays"/> resolved days, Azi speaks a
    /// non-intrusive side-bubble line reporting how fast the world's health is trending and how
    /// many days remain in the run. Purely a meaning-events subscriber (Law 2 — subscribes to
    /// meaning-events, never polls) — it reacts to <see cref="RunManager.OnDayResolved"/> and
    /// reads public singleton state; it does not own or mutate any simulation data.
    ///
    /// The six lines mirror <see cref="TrendIndicatorUI"/>'s tier bucketing so Azi's tone always
    /// agrees with the on-screen trend arrow: rising/falling × tier 1/2/3, thresholds 0.5 / 1.5 /
    /// 2.5 health-points per day. Tier 1 also catches "flat" (|Δ| below 0.5) — TrendIndicatorUI
    /// hides its arrow entirely below tier 1, but Azi still has to say *something*, so flat
    /// deliberately folds into tier 1 of whichever sign the delta carries (delta >= 0 reads as
    /// "rising"). That keeps exactly six cases instead of needing a seventh "flat" line.
    ///
    /// WIRING (human):
    ///   1. Add this component to a persistent scene GameObject (e.g. alongside NarrativePopupManager
    ///      or another always-on manager object) — it must outlive individual days to catch every tick.
    ///   2. Assign <c>aziPortrait</c> to Azi's portrait sprite.
    ///   3. Tune <c>intervalDays</c> (default 15) and the six line fields to taste. Lines may use
    ///      the token "{days}", replaced at show time with the run's remaining day count.
    /// </summary>
    public class DaysLeftPopupNotifier : MonoBehaviour
    {
        [Header("Speaker")]
        [SerializeField] private Sprite aziPortrait;

        [Header("Cadence")]
        [SerializeField] private int intervalDays = 15;   // Azi speaks every Nth resolved day

        [Header("Rising lines (tier 1 → 3, includes flat)")]
        [TextArea]
        [SerializeField]
        private string risingTier1 =
            "The world is holding steady, gently improving. {days} days left in our journey together.";
        [TextArea]
        [SerializeField]
        private string risingTier2 =
            "Good news — the world is healing at a real pace now. {days} days remain. Keep this up.";
        [TextArea]
        [SerializeField]
        private string risingTier3 =
            "The world is healing fast! Whatever you're doing, it's working — and we still have {days} days to build on it.";

        [Header("Falling lines (tier 1 → 3, includes flat)")]
        [TextArea]
        [SerializeField]
        private string fallingTier1 =
            "Things are drifting downward, slowly. Nothing urgent yet, but keep an eye on it — {days} days left.";
        [TextArea]
        [SerializeField]
        private string fallingTier2 =
            "The world's health is slipping at a worrying pace. We have {days} days to change course.";
        [TextArea]
        [SerializeField]
        private string fallingTier3 =
            "The world is failing fast — and only {days} days remain. We must turn this around now.";

        // ── Tier thresholds — mirror TrendIndicatorUI's defaults exactly ──
        private const float Tier1Threshold = 0.5f;
        private const float Tier2Threshold = 1.5f;
        private const float Tier3Threshold = 2.5f;

        private bool _warnedMissingPopupManager;   // warn-once guard, don't spam every interval

        void OnEnable()
        {
            if (RunManager.Instance == null)
            {
                Debug.LogError($"{name}: RunManager.Instance is null — DaysLeftPopupNotifier cannot subscribe to OnDayResolved.", this);
                return;
            }
            RunManager.Instance.OnDayResolved += HandleDayResolved;
        }

        void OnDisable()
        {
            if (RunManager.Instance != null)
                RunManager.Instance.OnDayResolved -= HandleDayResolved;
        }

        private void HandleDayResolved(int day)
        {
            if (day <= 0 || day % intervalDays != 0) return;

            float delta = RunManager.Instance.WorldHealthDelta;
            float mag   = Mathf.Abs(delta);

            // NOTE: flat (|Δ| < tier1) deliberately folds into tier 1 of its sign rather than
            // getting a seventh case — TrendIndicatorUI hides its arrow when flat, but Azi still
            // needs a line to say, so "rising" (delta >= 0) inherits the flat case here.
            bool rising = delta >= 0f;
            int  tier   = mag >= Tier3Threshold ? 3 : mag >= Tier2Threshold ? 2 : 1;

            string line = LineFor(rising, tier);

            int daysLeft = 0;
            if (ResourceManager.Instance != null)
                daysLeft = Mathf.Max(0, ResourceManager.Instance.RunLengthDays - ResourceManager.Instance.TotalDays);

            line = line.Replace("{days}", daysLeft.ToString());

            var npm = NarrativePopupManager.Instance;
            if (npm == null)
            {
                if (!_warnedMissingPopupManager)
                {
                    Debug.LogWarning($"{name}: NarrativePopupManager.Instance is null — skipping Azi's days-left check-in.", this);
                    _warnedMissingPopupManager = true;
                }
                return;
            }

            npm.Say(line, aziPortrait, "Azi", PopupStyle.Character);
        }

        private string LineFor(bool rising, int tier)
        {
            if (rising) return tier == 3 ? risingTier3  : tier == 2 ? risingTier2  : risingTier1;
            return             tier == 3 ? fallingTier3 : tier == 2 ? fallingTier2 : fallingTier1;
        }
    }
}
