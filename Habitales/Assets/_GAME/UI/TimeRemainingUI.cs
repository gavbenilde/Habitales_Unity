using UnityEngine;
using TMPro;

namespace Habitales.UI
{
    /// <summary>
    /// Passive HUD view — renders how much of the run is left in a single line.
    /// Default reads "N years and N days left"; tick <see cref="useSeasons"/> to render
    /// "N seasons and N days left" instead. Mirrors <see cref="HealthBarUI"/>: do NOT read
    /// ResourceManager or any singleton from inside this class. The controller (HudController)
    /// owns the data push via <see cref="Render"/> (Law 1 / Law 2). No Update() polling — run
    /// length only changes meaning when a day resolves, so HudController.HandleDayResolved →
    /// Render() is the correct and sufficient refresh path.
    ///
    /// <para><b>Display conventions (not sim concepts):</b> 1 year = 365 days; 1 "season" =
    /// 6 months = 182.5 days. The math lives here so the rest of the codebase never has to
    /// know about either.</para>
    /// </summary>
    public class TimeRemainingUI : MonoBehaviour
    {
        // Display-only divisors. Not sim concepts.
        private const float DaysPerYear   = 365f;
        private const float DaysPerSeason = 182.5f;

        [Header("Reference (TextMeshPro)")]
        [Tooltip("Single line, e.g. \"1 year and 39 days left\".")]
        [SerializeField] private TMP_Text timeText;

        [Header("Display")]
        [Tooltip("Use Seasons instead — render the remaining time in seasons (182.5 days) rather than years.")]
        [SerializeField] private bool useSeasons;

        void Awake()
        {
            ValidateRefs();
        }

        /// <summary>
        /// Called by HudController to push the current run-clock values.
        /// Passive: no game-state reads or writes. Both args come from ResourceManager
        /// (TotalDays + RunLengthDays) — this view does the unit arithmetic itself.
        /// </summary>
        public void Render(int totalDays, int runLengthDays)
        {
            if (timeText == null) return;

            int daysLeft = Mathf.Max(0, runLengthDays - totalDays);

            if (useSeasons)
            {
                int seasons = Mathf.FloorToInt(daysLeft / DaysPerSeason);
                int days    = daysLeft - Mathf.RoundToInt(seasons * DaysPerSeason);
                timeText.text = $"{Unit(seasons, "season")} and {Unit(days, "day")} left";
            }
            else
            {
                int years = Mathf.FloorToInt(daysLeft / DaysPerYear);
                int days  = daysLeft - Mathf.RoundToInt(years * DaysPerYear);
                timeText.text = $"{Unit(years, "year")} and {Unit(days, "day")} left";
            }
        }

        // "1 year" / "2 years"
        private static string Unit(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

        // ─── Validation (Law 3 — loud-fail on unwired Inspector refs) ─────────────
        private void ValidateRefs()
        {
            if (timeText == null)
            {
                Debug.LogError($"{name}: timeText is not wired — drag the time TMP_Text into the Inspector.", this);
                enabled = false;
            }
        }
    }
}
