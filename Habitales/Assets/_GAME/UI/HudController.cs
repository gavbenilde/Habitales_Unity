using UnityEngine;

namespace Habitales.UI
{
    /// <summary>
    /// HUD subsystem controller (U3). Owns all persistent in-game HUD views:
    /// HealthBarUI, ObjectiveBannerUI, UnlockNextZoneButtonUI, ResourceDisplay,
    /// and RegionHealthUI. Satisfies Law 2 by subscribing to <c>RunManager.OnDayResolved</c>
    /// and pushing fresh values to passive views — no per-frame polling.
    ///
    /// <para><b>Why OnDayResolved is sufficient for the health bar:</b>
    /// RegionManager has no health-changed event; health only meaningfully changes when
    /// the sim resolves a day (cascade → EvaluateThresholds → visuals refresh all
    /// settle before OnDayResolved fires). Polling every frame between day advances
    /// would see the same value over and over — it is waste. OnDayResolved is the
    /// correct minimum-refresh boundary per the architecture (arch §2.1 / Law 2).</para>
    /// </summary>
    public class HudController : MonoBehaviour, IUISubsystem
    {
        // ─── IUISubsystem ─────────────────────────────────────────────────────

        public string SubsystemId => "hud";
        public bool   IsVisible   { get; private set; } = true;

        /// <summary>
        /// Shows or hides the entire HUD root. Passive — no game-state side-effects (Law 1).
        /// </summary>
        public void SetVisible(bool visible)
        {
            IsVisible = visible;
            if (_hudRoot != null)
                _hudRoot.SetActive(visible);
        }

        // ─── HUD root ─────────────────────────────────────────────────────────

        [Header("HUD Root")]
        [Tooltip("The root GameObject that contains all HUD elements. " +
                 "SetVisible toggles this on/off. If null, uses this GameObject.")]
        [SerializeField] private GameObject _hudRoot;

        // ─── HUD Views (passive — controller pushes, they render) ─────────────

        [Header("HUD Views")]
        [SerializeField] private HealthBarUI             _healthBar;
        [SerializeField] private TimeRemainingUI         _timeRemaining;
        [SerializeField] private ObjectiveBannerUI       _objectiveBanner;
        [SerializeField] private UnlockNextZoneButtonUI  _unlockNextZoneButton;
        [SerializeField] private ResourceDisplay         _resourceDisplay;
        [SerializeField] private RegionHealthUI          _regionHealthUI;
        [Tooltip("Optional — the tiered trend arrow beside the world health bar.")]
        [SerializeField] private TrendIndicatorUI        _worldTrend;

        // ─── Final Stretch (ENDGAME §5 item 2) ─────────────────────────────────

        [Header("Final Stretch (ENDGAME §5 item 2)")]
        [Tooltip("When days remaining <= this, the day counter tints amber and pulses once per " +
                 "day-tick. Denominator is ResourceManager.RunLengthDays.")]
        [SerializeField] private int _finalStretchDays = 10;
        [Tooltip("When days remaining <= this, the day counter tints red instead of amber.")]
        [SerializeField] private int _criticalStretchDays = 3;

        // ─── Progressive health-bar mapping ────────────────────────────────────
        // The bar is normalized: 0 = _barFloor, 1 = RunManager.ZoneUnlockThreshold.
        // The floor resets to the current world health whenever a region generates
        // (initial spawn AND each unlock), and ratchets DOWN to any new low-point
        // so the bar never reads negative — a deep collapse just means slower fill.
        // Purely presentational; RunManager's threshold mechanics are untouched.
        private float _barFloor = float.NaN; // NaN until the first region generates

        // ─── Lifecycle ────────────────────────────────────────────────────────

        void Awake()
        {
            if (_hudRoot == null)
                _hudRoot = gameObject;

            ValidateRefs();
        }

        void OnEnable()
        {
            // Subscribe to the universal heartbeat — data has settled, visuals refreshed (arch §2.1).
            if (RunManager.Instance != null)
            {
                RunManager.Instance.OnDayResolved       += HandleDayResolved;
                RunManager.Instance.OnRegionUnlockReady += HandleRegionUnlockReady;
                RunManager.Instance.OnRegionUnlocked    += HandleRegionUnlocked;
            }

            // Region generation is the load-time fix AND the bar-floor reset point:
            // OnEnable runs before RunManager.Start() spawns Zone 1, so the initial
            // push below sees zero tiles. When the zone lands (and on every unlock),
            // this event re-pushes with real data.
            if (RegionManager.Instance != null)
                RegionManager.Instance.OnRegionGenerated += HandleRegionGenerated;

            // Initial push so the views show correct values on scene load.
            PushHealthToBar();
            PushTimeToHud();
            PushWorldTrend();
        }

        void OnDisable()
        {
            if (RunManager.Instance != null)
            {
                RunManager.Instance.OnDayResolved       -= HandleDayResolved;
                RunManager.Instance.OnRegionUnlockReady -= HandleRegionUnlockReady;
                RunManager.Instance.OnRegionUnlocked    -= HandleRegionUnlocked;
            }

            if (RegionManager.Instance != null)
                RegionManager.Instance.OnRegionGenerated -= HandleRegionGenerated;
        }

        // ─── Event handler ────────────────────────────────────────────────────

        /// <summary>
        /// Called once per resolved day (after cascade, threshold checks, and visual
        /// refresh have all completed). Pushes the freshly-settled health value to the
        /// health bar view (Law 2 — push on meaning, not per frame).
        /// </summary>
        private void HandleDayResolved(int day)
        {
            PushHealthToBar();
            PushTimeToHud();
            PushWorldTrend();
        }

        /// <summary>
        /// A region just generated — initial Zone 1 spawn or a player unlock. Either way
        /// the world health that INCLUDES the new tiles becomes the bar's new 0%, so an
        /// unlock visibly drops the bar to empty and the climb toward the threshold restarts.
        /// </summary>
        private void HandleRegionGenerated(RegionGenerationResult result)
        {
            if (RegionManager.Instance != null)
                _barFloor = RegionManager.Instance.GetTotalAverageHealth();

            PushHealthToBar();
        }

        // Re-push on the unlock lifecycle so the bar latches full the moment the
        // threshold is earned and empties the moment the button is pressed —
        // without waiting for the next day to resolve.
        private void HandleRegionUnlockReady() => PushHealthToBar();
        private void HandleRegionUnlocked()    => PushHealthToBar();

        // ─── Push helpers ─────────────────────────────────────────────────────

        private void PushHealthToBar()
        {
            if (_healthBar == null) return;
            if (RegionManager.Instance == null) return;

            float health = RegionManager.Instance.GetTotalAverageHealth();
            _healthBar.Render(ComputeBarFraction(health), IsUnlockPending());
        }

        /// <summary>
        /// Maps raw world health onto the progressive bar: 0 at the current floor,
        /// 1 at the unlock threshold. New low-points ratchet the floor down; an
        /// earned-but-unclaimed unlock latches the bar at 1 regardless of decay.
        /// </summary>
        private float ComputeBarFraction(float health)
        {
            if (float.IsNaN(_barFloor))
                _barFloor = health; // no region has generated yet — anchor to whatever we see first

            if (health < _barFloor)
                _barFloor = health; // new low-point becomes the bar's 0%

            if (IsUnlockPending())
                return 1f; // earned: stay full until the player presses the button

            float threshold = RunManager.Instance != null
                ? RunManager.Instance.ZoneUnlockThreshold
                : 80f;

            float span = threshold - _barFloor;
            if (span <= 0.001f)
                return 1f; // floor at/above threshold — already there

            return Mathf.Clamp01((health - _barFloor) / span);
        }

        private bool IsUnlockPending()
        {
            return RunManager.Instance != null && RunManager.Instance.RegionUnlockPending;
        }

        private void PushTimeToHud()
        {
            if (_timeRemaining == null) return;
            if (ResourceManager.Instance == null) return;

            _timeRemaining.Render(
                ResourceManager.Instance.TotalDays,
                ResourceManager.Instance.RunLengthDays);

            _timeRemaining.RenderFinalStretch(
                ResourceManager.Instance.DaysRemaining,
                _finalStretchDays,
                _criticalStretchDays);
        }

        private void PushWorldTrend()
        {
            if (_worldTrend == null) return;
            if (RunManager.Instance == null) return;

            _worldTrend.SetTrend(RunManager.Instance.WorldHealthTrend);
        }

        // ─── Validation (Law 3) ───────────────────────────────────────────────

        private void ValidateRefs()
        {
            if (_healthBar == null)
            {
                Debug.LogError($"{name}: _healthBar is not wired — drag the HealthBarUI component into the Inspector.", this);
                enabled = false;
            }
            if (_timeRemaining == null)
            {
                Debug.LogError($"{name}: _timeRemaining is not wired — drag the TimeRemainingUI component into the Inspector.", this);
                enabled = false;
            }
            if (_objectiveBanner == null)
                Debug.LogError($"{name}: _objectiveBanner is not wired — drag the ObjectiveBannerUI component into the Inspector.", this);
            if (_unlockNextZoneButton == null)
                Debug.LogError($"{name}: _unlockNextZoneButton is not wired — drag the UnlockNextZoneButtonUI component into the Inspector.", this);
            if (_resourceDisplay == null)
                Debug.LogError($"{name}: _resourceDisplay is not wired — drag the ResourceDisplay component into the Inspector.", this);
            // RegionHealthUI is show/hide driven (called externally from RegionOutlineRenderer),
            // so it is non-critical from HudController's standpoint — warn only.
            if (_regionHealthUI == null)
                Debug.LogWarning($"{name}: _regionHealthUI is not wired — RegionHealthUI will not be managed by HudController.", this);
        }
    }
}
