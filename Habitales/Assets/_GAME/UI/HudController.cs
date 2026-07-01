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
                RunManager.Instance.OnDayResolved += HandleDayResolved;

            // Initial push so the views show correct values on scene load.
            PushHealthToBar();
            PushTimeToHud();
            PushWorldTrend();
        }

        void OnDisable()
        {
            if (RunManager.Instance != null)
                RunManager.Instance.OnDayResolved -= HandleDayResolved;
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

        // ─── Push helpers ─────────────────────────────────────────────────────

        private void PushHealthToBar()
        {
            if (_healthBar == null) return;
            if (RegionManager.Instance == null) return;

            float health = RegionManager.Instance.GetTotalAverageHealth();
            _healthBar.Render(health);
        }

        private void PushTimeToHud()
        {
            if (_timeRemaining == null) return;
            if (ResourceManager.Instance == null) return;

            _timeRemaining.Render(
                ResourceManager.Instance.TotalDays,
                ResourceManager.Instance.RunLengthDays);
        }

        private void PushWorldTrend()
        {
            if (_worldTrend == null) return;
            if (RunManager.Instance == null) return;

            _worldTrend.SetDelta(RunManager.Instance.WorldHealthDelta);
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
