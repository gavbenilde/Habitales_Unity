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
    /// <para><b>Display conventions:</b> 1 year = <see cref="GameCalendar.DaysPerYear"/> (360)
    /// days; 1 season = half a year = 180 days (matches the sim's Dry/Wet season length).
    /// The division math lives here so the rest of the codebase never has to.</para>
    ///
    /// <para><b>Day counter (ENDGAME §2 item 1):</b> <see cref="dayCounterText"/> is the run's
    /// stated contract — it must always read "Day {TotalDays} of {RunLengthDays}", never a bare
    /// day number (ENDGAME_BUILD_PLAN §0 decision 5: this is a countdown, not the calendar date —
    /// the calendar date lives only in the Weather app). Optional: a HUD without this label wired
    /// still functions, it just doesn't show the contract — warn, don't brick (Law 3).</para>
    ///
    /// <para><b>Final-stretch state (ENDGAME §5 item 2):</b> <see cref="RenderFinalStretch"/> tints
    /// the day counter toward <see cref="finalStretchColor"/> (then <see cref="criticalStretchColor"/>
    /// in the last <see cref="criticalStretchDays"/> days) and gives it a single LeanTween scale
    /// pulse per day-tick. Entirely optional-with-warning — refs may be unwired without bricking
    /// the HUD (Law 3).</para>
    /// </summary>
    public class TimeRemainingUI : MonoBehaviour
    {
        // Display-only divisors, derived from the sim calendar so they can't drift from it.
        private const float DaysPerYear   = GameCalendar.DaysPerYear;
        private const float DaysPerSeason = GameCalendar.DaysPerSeason;

        [Header("Reference (TextMeshPro)")]
        [Tooltip("Single line, e.g. \"1 year and 39 days left\".")]
        [SerializeField] private TMP_Text timeText;

        [Header("Display")]
        [Tooltip("Use Seasons instead — render the remaining time in seasons (180 days) rather than years.")]
        [SerializeField] private bool useSeasons;

        [Header("Day Counter (ENDGAME §0 decision 5 — the contract, always visible)")]
        [Tooltip("Optional — \"Day {TotalDays} of {RunLengthDays}\". If unwired, only the " +
                 "\"N years/days left\" line renders; a warning is logged once at Awake.")]
        [SerializeField] private TMP_Text dayCounterText;

        [Header("Final Stretch — Tint")]
        [Tooltip("Normal (non-final-stretch) color the day counter restores to. Set this to the " +
                 "label's authored color so a run restart can reliably restore it.")]
        [SerializeField] private Color normalColor = Color.white;
        [Tooltip("Color the day counter tints toward once days-remaining <= finalStretchDays.")]
        [SerializeField] private Color finalStretchColor = new Color(1f, 0.65f, 0f); // amber
        [Tooltip("Color the day counter tints toward once days-remaining <= criticalStretchDays.")]
        [SerializeField] private Color criticalStretchColor = new Color(0.9f, 0.15f, 0.15f); // red

        [Header("Final Stretch — Pulse")]
        [Tooltip("Peak scale of the once-per-day-tick pulse (1.0 = no pulse).")]
        [SerializeField] private float pulseScale = 1.06f;
        [Tooltip("Total duration of the scale-up + scale-down pulse, in seconds.")]
        [SerializeField] private float pulseDuration = 0.8f;

        private Vector3 _dayCounterBaseScale = Vector3.one;
        private Vector3 timeTextOriginalScale;

        void Awake()
        {
            ValidateRefs();

            if (dayCounterText != null)
                _dayCounterBaseScale = dayCounterText.rectTransform.localScale;
        }

        private void Start()
        {
            if (timeText != null)
            {
                timeTextOriginalScale = timeText.transform.localScale;
            }
        }
        
        /// <summary>
        /// Called by HudController to push the current run-clock values.
        /// Passive: no game-state reads or writes. Both args come from ResourceManager
        /// (TotalDays + RunLengthDays) — this view does the unit arithmetic itself.
        /// </summary>
        public void Render(int totalDays, int runLengthDays)
        {
            if (dayCounterText != null)
                dayCounterText.text = $"Day {totalDays} of {runLengthDays}";

            if (timeText == null) return;
            
            LeanTween.cancel(timeText.gameObject);

            LeanTween.scale(timeText.gameObject, timeTextOriginalScale * 0.85f, 0.1f)
                .setEaseOutQuad()
                .setOnComplete(() =>
                {
                    LeanTween.scale(timeText.gameObject, timeTextOriginalScale, 0.15f)
                        .setEaseOutBack();
                });

            int daysLeft = Mathf.Max(0, runLengthDays - totalDays);

            if (useSeasons)
            {
                // int seasons = Mathf.FloorToInt(daysLeft / DaysPerSeason);
                // int days    = daysLeft - Mathf.RoundToInt(seasons * DaysPerSeason);
                // timeText.text = $"{Unit(seasons, "season")} and {Unit(days, "day")} left";
                
                timeText.text = $"{daysLeft} {(daysLeft == 1 ? "day" : "days")} left";
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

        /// <summary>
        /// Called by HudController once per resolved day to drive the final-stretch tint + pulse
        /// on the day counter. Passive — no game-state reads. <paramref name="daysRemaining"/> and
        /// the two thresholds are all HudController already has via ResourceManager (Law 1).
        /// Pulses once per call (day-tick) — never loops. Cancels any in-flight tween first so
        /// rapid day-advances (multi-day actions) don't stack pulses.
        /// </summary>
        public void RenderFinalStretch(int daysRemaining, int finalStretchDays, int criticalStretchDays)
        {
            if (dayCounterText == null) return;

            bool inFinalStretch = daysRemaining <= finalStretchDays;
            bool inCritical     = daysRemaining <= criticalStretchDays;

            Color target = inCritical ? criticalStretchColor
                          : inFinalStretch ? finalStretchColor
                          : normalColor;
            dayCounterText.color = target;

            if (inFinalStretch)
                PlayPulse();
        }

        /// <summary>
        /// Restores the day counter to its normal (non-final-stretch) color and cancels any
        /// in-flight pulse. Call this on run restart, if/when RunManager gains a restart hook —
        /// no such hook exists yet (see ENDGAME WO-END-3 report), so nothing calls this today.
        /// </summary>
        public void ResetFinalStretchVisuals()
        {
            if (dayCounterText == null) return;

            LeanTween.cancel(dayCounterText.gameObject);
            dayCounterText.color = normalColor;
            dayCounterText.rectTransform.localScale = _dayCounterBaseScale;
        }

        private void PlayPulse()
        {
            var target = dayCounterText.rectTransform;

            // Cancel any in-flight tween first so a multi-day action's rapid OnDayResolved
            // calls don't stack pulses (project LeanTween idiom — MessagingAppIconUI.PlayShake).
            LeanTween.cancel(target.gameObject);
            target.localScale = _dayCounterBaseScale;

            float half = pulseDuration * 0.5f;
            LeanTween.scale(target.gameObject, _dayCounterBaseScale * pulseScale, half)
                .setEase(LeanTweenType.easeOutQuad)
                .setOnComplete(() =>
                    LeanTween.scale(target.gameObject, _dayCounterBaseScale, half)
                        .setEase(LeanTweenType.easeInOutQuad));
        }

        // ─── Validation (Law 3 — loud-fail on unwired Inspector refs) ─────────────
        private void ValidateRefs()
        {
            if (timeText == null)
            {
                Debug.LogError($"{name}: timeText is not wired — drag the time TMP_Text into the Inspector.", this);
                enabled = false;
            }
            // dayCounterText and the final-stretch visuals are optional-with-warning: the HUD is
            // live in-scene and must not brick if these are unwired (Law 3 — artist-facing, not fatal).
            if (dayCounterText == null)
                Debug.LogWarning($"{name}: dayCounterText is not wired — the \"Day X of Y\" contract label will not render.", this);
        }
    }
}
