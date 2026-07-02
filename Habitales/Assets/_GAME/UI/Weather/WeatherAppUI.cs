using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace Habitales.UI
{
    /// <summary>
    /// In-game tablet "weather app" — renders WeatherManager's 21-day forecast strip
    /// verbatim (index 0 = today). The forecast's deliberate unreliability (the sim's
    /// "lying" logic) lives entirely in WeatherManager; this view does no accuracy math,
    /// it just displays <c>Forecast[k]</c> for each offset.
    ///
    /// Mirrors <see cref="HudController"/>'s push model: subscribes to a meaning-event
    /// (<c>OnForecastUpdated</c>) and refreshes passive views — no per-frame polling
    /// (Law 2). Reads WeatherManager/ResourceManager only via their public getters,
    /// never writes sim state (Law 1).
    ///
    /// <para><b>REQUIRED SCENE WIRING (human, in the Unity editor):</b></para>
    /// <para>
    /// 1. Under the tablet canvas, create a panel GameObject "WeatherApp" (this component
    ///    lives on it) sized to the tablet screen, alongside the other tablet apps
    ///    (Chat, etc.). Start it inactive/hidden per however the tablet app-switcher
    ///    shows/hides apps elsewhere in the project.
    /// </para>
    /// <para>
    /// 2. Header row (top of the panel): use
    ///    <c>_ART/_UI/Tablet/Weather/apps_weatherInterface_WeatherHeader.png</c> as the
    ///    header background Image. Add three TMP_Text children on top of it and wire
    ///    them to <see cref="headerCurrentWeather"/> (e.g. "Rainy"),
    ///    <see cref="headerDate"/> (e.g. "Jun 14, Year 1"), and <see cref="headerSeason"/>
    ///    (e.g. "Wet Season").
    /// </para>
    /// <para>
    /// 3. Below the header, add a scrollable strip container — a child GameObject with
    ///    a <c>HorizontalLayoutGroup</c> (or <c>GridLayoutGroup</c> if a grid layout is
    ///    preferred) + <c>ContentSizeFitter</c>, optionally inside a <c>ScrollRect</c> so
    ///    all 21 days can be scrolled through. Wire this Transform to
    ///    <see cref="entryContainer"/>.
    /// </para>
    /// <para>
    /// 4. Build the entry prefab: a GameObject with <see cref="WeatherForecastEntryUI"/>
    ///    attached, containing:
    ///       - a background <c>Image</c> — assign
    ///         <c>apps_weatherInterface_WeatherBox_individual.png</c> to its
    ///         <c>selectedBackground</c> field (today's highlighted box) and
    ///         <c>apps_weatherInterface_WeatherBox_individualNotSelected.png</c> (or
    ///         <c>apps_weatherInterface_WeatherBox_notSelected.png</c> for a grouped/row
    ///         variant) to <c>unselectedBackground</c>; assign the same Image to
    ///         <c>background</c>.
    ///       - a child <c>Image</c> for the per-day weather icon → wire to
    ///         <c>weatherIcon</c>.
    ///       - two child <c>TMP_Text</c> labels → wire to <c>dayLabel</c> (e.g. "Today")
    ///         and <c>dateLabel</c> (e.g. "Jun 14").
    ///    Save it as a prefab and wire it to <see cref="entryPrefab"/> on this component.
    ///    Do NOT hand-place 21 instances in the scene — WeatherAppUI pools them at
    ///    runtime from this single prefab.
    /// </para>
    /// <para>
    /// 5. Assign the four per-state icon sprites (<see cref="sunnyIcon"/>,
    ///    <see cref="cloudyIcon"/>, <see cref="rainyIcon"/>, <see cref="stormyIcon"/>) —
    ///    use <c>apps_weatherIcon.png</c> as a placeholder/base if per-state art isn't
    ///    split out yet; swap in dedicated per-state icons when available.
    /// </para>
    /// </summary>
    public class WeatherAppUI : MonoBehaviour
    {
        // ─── Inspector ──────────────────────────────────────────────────────────

        [Header("Forecast Strip")]
        [Tooltip("Prefab for a single forecast day entry (WeatherForecastEntryUI). Pooled — instantiated once, never destroyed/rebuilt.")]
        [SerializeField] private WeatherForecastEntryUI entryPrefab;

        [Tooltip("Parent Transform for pooled forecast entries (HorizontalLayoutGroup or GridLayoutGroup).")]
        [SerializeField] private Transform entryContainer;

        [Header("Header")]
        [Tooltip("Current weather label, e.g. \"Rainy\".")]
        [SerializeField] private TMP_Text headerCurrentWeather;

        [Tooltip("Current calendar date label, e.g. \"Jun 14, Year 1\" (ResourceManager.GetCalendarDisplay).")]
        [SerializeField] private TMP_Text headerDate;

        [Tooltip("Current season label, e.g. \"Wet Season\".")]
        [SerializeField] private TMP_Text headerSeason;

        [Header("Weather Icons")]
        [Tooltip("Icon used for WeatherState.Sunny.")]
        [SerializeField] private Sprite sunnyIcon;

        [Tooltip("Icon used for WeatherState.Cloudy.")]
        [SerializeField] private Sprite cloudyIcon;

        [Tooltip("Icon used for WeatherState.Rainy.")]
        [SerializeField] private Sprite rainyIcon;

        [Tooltip("Icon used for WeatherState.Stormy.")]
        [SerializeField] private Sprite stormyIcon;

        // ─── Runtime state ──────────────────────────────────────────────────────

        private readonly List<WeatherForecastEntryUI> _pooledEntries = new List<WeatherForecastEntryUI>();
        private bool _warnedNoWeatherManager;
        private bool _warnedNoResourceManager;
        private bool _warnedMissingIcon;

        // ─── Lifecycle ────────────────────────────────────────────────────────

        private void OnEnable()
        {
            if (WeatherManager.Instance != null)
            {
                WeatherManager.Instance.OnForecastUpdated -= Refresh;
                WeatherManager.Instance.OnForecastUpdated += Refresh;
            }
            else if (!_warnedNoWeatherManager)
            {
                Debug.LogWarning("WeatherAppUI: WeatherManager not present — app will stay empty.", this);
                _warnedNoWeatherManager = true;
            }

            Refresh();
        }

        private void OnDisable()
        {
            if (WeatherManager.Instance != null)
                WeatherManager.Instance.OnForecastUpdated -= Refresh;
        }

        // ─── Refresh ────────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the forecast strip and header from the current WeatherManager /
        /// ResourceManager state. Entries are pooled (instantiated once on first use,
        /// reused thereafter) — this never destroys/recreates GameObjects per call.
        /// </summary>
        private void Refresh()
        {
            WeatherManager weather = WeatherManager.Instance;
            if (weather == null)
            {
                if (!_warnedNoWeatherManager)
                {
                    Debug.LogWarning("WeatherAppUI: WeatherManager not present — app will stay empty.", this);
                    _warnedNoWeatherManager = true;
                }
                return;
            }

            ResourceManager resources = ResourceManager.Instance;
            if (resources == null)
            {
                if (!_warnedNoResourceManager)
                {
                    Debug.LogWarning("WeatherAppUI: ResourceManager not present — app will stay empty.", this);
                    _warnedNoResourceManager = true;
                }
                return;
            }

            RefreshHeader(weather, resources);
            RefreshForecastStrip(weather, resources);
        }

        private void RefreshHeader(WeatherManager weather, ResourceManager resources)
        {
            if (headerCurrentWeather != null)
                headerCurrentWeather.text = weather.CurrentWeather.ToString();

            if (headerDate != null)
                headerDate.text = resources.GetCalendarDisplay();

            if (headerSeason != null)
                headerSeason.text = resources.CurrentSeason == Season.Wet ? "Wet Season" : "Dry Season";
        }

        private void RefreshForecastStrip(WeatherManager weather, ResourceManager resources)
        {
            if (entryPrefab == null || entryContainer == null)
            {
                Debug.LogWarning("WeatherAppUI: entryPrefab or entryContainer is not wired — forecast strip will stay empty.", this);
                return;
            }

            EnsurePool();

            IReadOnlyList<WeatherState> forecast = weather.Forecast;
            if (forecast == null)
            {
                Debug.LogWarning("WeatherAppUI: WeatherManager.Forecast is null — forecast strip will stay empty.", this);
                return;
            }

            int count = Mathf.Min(_pooledEntries.Count, forecast.Count);
            for (int k = 0; k < count; k++)
            {
                bool isToday = k == 0;
                string dayText = isToday ? "Today" : (k == 1 ? "Tomorrow" : $"+{k} days");

                int absoluteDay = resources.TotalDays + k;
                int dayOfYear = resources.DayOfYearFor(absoluteDay);
                string dateText = GameCalendar.GetShortDate(dayOfYear);

                Sprite icon = GetIcon(forecast[k]);

                _pooledEntries[k].Render(dayText, dateText, icon, isToday);
            }
        }

        /// <summary>
        /// Instantiates <see cref="WeatherManager.ForecastLength"/> pooled entries into
        /// <see cref="entryContainer"/> on first use. Never called again after the pool
        /// is full-sized — subsequent refreshes reuse the same entries.
        /// </summary>
        private void EnsurePool()
        {
            if (_pooledEntries.Count >= WeatherManager.ForecastLength) return;

            for (int i = _pooledEntries.Count; i < WeatherManager.ForecastLength; i++)
            {
                WeatherForecastEntryUI entry = Instantiate(entryPrefab, entryContainer);
                _pooledEntries.Add(entry);
            }
        }

        // ─── Icon lookup ────────────────────────────────────────────────────────

        /// <summary>
        /// Maps a forecast day's WeatherState to its Inspector-assigned icon Sprite.
        /// Warns once (not per-entry, per-missing-state) if an icon slot was never wired.
        /// </summary>
        private Sprite GetIcon(WeatherState state)
        {
            Sprite icon = state switch
            {
                WeatherState.Sunny  => sunnyIcon,
                WeatherState.Cloudy => cloudyIcon,
                WeatherState.Rainy  => rainyIcon,
                WeatherState.Stormy => stormyIcon,
                _                   => null
            };

            if (icon == null && !_warnedMissingIcon)
            {
                Debug.LogWarning($"WeatherAppUI: no icon Sprite wired for WeatherState.{state} — drag the matching icon into the Inspector.", this);
                _warnedMissingIcon = true;
            }

            return icon;
        }
    }
}
