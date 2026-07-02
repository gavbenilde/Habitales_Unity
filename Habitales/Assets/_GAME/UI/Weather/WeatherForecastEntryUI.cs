using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Habitales.UI
{
    /// <summary>
    /// Passive view for a single day in the weather app's forecast strip. Mirrors
    /// <see cref="TimeRemainingUI"/>: no singleton reads, no game-state access — the
    /// controller (<see cref="WeatherAppUI"/>) owns the data push via <see cref="Render"/>
    /// (Law 1 / Law 2). Pooled by the controller; never destroyed/rebuilt per day.
    /// </summary>
    public class WeatherForecastEntryUI : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Icon for the day's forecast weather state (sunny/cloudy/rainy/stormy).")]
        [SerializeField] private Image weatherIcon;

        [Tooltip("\"Today\" / \"Tomorrow\" / short day label.")]
        [SerializeField] private TMP_Text dayLabel;

        [Tooltip("Short calendar date, e.g. \"Jun 14\".")]
        [SerializeField] private TMP_Text dateLabel;

        [Tooltip("Background box image — swapped between selected/unselected sprites for the today-highlight.")]
        [SerializeField] private Image background;

        [Tooltip("Background sprite used when this entry represents today (WeatherBox_individual art).")]
        [SerializeField] private Sprite selectedBackground;

        [Tooltip("Background sprite used for all other days (WeatherBox_individualNotSelected / WeatherBox_notSelected art).")]
        [SerializeField] private Sprite unselectedBackground;

        // ─── One-time warning guards (avoid log spam across a pooled 21-entry strip) ──
        private bool _warnedIcon;
        private bool _warnedDay;
        private bool _warnedDate;
        private bool _warnedBackground;

        /// <summary>
        /// Pushes one day's data into this entry. Passive: sets text/sprites only,
        /// no reads of any manager. Null-guards each wire with a one-time warning
        /// so a missing Inspector ref degrades gracefully instead of throwing (Law 3).
        /// </summary>
        public void Render(string dayText, string dateText, Sprite icon, bool isToday)
        {
            if (weatherIcon != null)
            {
                weatherIcon.sprite = icon;
                weatherIcon.enabled = icon != null;
            }
            else if (!_warnedIcon)
            {
                Debug.LogWarning($"{name}: WeatherForecastEntryUI — weatherIcon is not wired. Drag the icon Image into the Inspector.", this);
                _warnedIcon = true;
            }

            if (dayLabel != null)
            {
                dayLabel.text = dayText;
            }
            else if (!_warnedDay)
            {
                Debug.LogWarning($"{name}: WeatherForecastEntryUI — dayLabel is not wired. Drag the day TMP_Text into the Inspector.", this);
                _warnedDay = true;
            }

            if (dateLabel != null)
            {
                dateLabel.text = dateText;
            }
            else if (!_warnedDate)
            {
                Debug.LogWarning($"{name}: WeatherForecastEntryUI — dateLabel is not wired. Drag the date TMP_Text into the Inspector.", this);
                _warnedDate = true;
            }

            if (background != null)
            {
                Sprite target = isToday ? selectedBackground : unselectedBackground;
                if (target != null)
                    background.sprite = target;
            }
            else if (!_warnedBackground)
            {
                Debug.LogWarning($"{name}: WeatherForecastEntryUI — background is not wired. Drag the background Image into the Inspector.", this);
                _warnedBackground = true;
            }
        }
    }
}
