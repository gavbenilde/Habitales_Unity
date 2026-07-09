using System.Collections.Generic;
using UnityEngine;
using Habitales.Dialogue;
using Habitales.Meta;

namespace Habitales.UI
{
    /// <summary>
    /// Azi's periodic check-in — the scheduler behind <see cref="CheckInPanelUI"/>.
    /// Formerly DaysLeftPopupNotifier (side-bubble + Season Report Lite message); reworked
    /// 2026-07-07 into the inescapable two-column check-in panel. Purely a meaning-events
    /// subscriber (Law 2) — it reacts to <see cref="RunManager.OnDayResolved"/> and reads
    /// public singleton state; it does not own or mutate any simulation data.
    ///
    /// ── Cadence ──
    /// Fires every <see cref="checkInIntervalDays"/> resolved days (default 60 — two
    /// check-ins across a 180-day season), and never at the very end: when days-left hits 0
    /// the run-end report IS the final check-in, so this stays silent. The old final-stretch
    /// escalation and daily-urgent one-liners are deliberately removed — check-ins are
    /// sparse and intentional now.
    ///
    /// ── Significant tile deltas ──
    /// At every check-in each tile's health is compared against the snapshot taken at the
    /// PREVIOUS check-in (baseline seeded on the first resolved day). A tile whose health
    /// moved by at least <see cref="significantDeltaThreshold"/> counts as significantly
    /// improved/decayed; tiles that didn't exist at the previous snapshot (region unlocks)
    /// are skipped for one window. The counts drive which <see cref="CheckInConversationSO"/>
    /// variant plays AND appear in the panel's stats column.
    ///
    /// ── Delivery ──
    /// PRIMARY: pick a conversation via <see cref="checkInDialogue"/>.Select(), build
    /// <see cref="SeasonReportData"/> fresh, and open <see cref="CheckInPanelUI"/> — which
    /// pauses the sim until the player reads the whole conversation and presses [Continue].
    /// FALLBACK (loud-warn once): if the dialogue asset, panel, or DialogueManager is
    /// unwired, degrade to the original trend-bucketed side-bubble via
    /// <see cref="NarrativePopupManager.Say"/> — the check-in never goes silent (Law 3).
    ///
    /// The six fallback lines mirror <see cref="TrendIndicatorUI"/>'s tier bucketing
    /// (rising/falling × tier 1/2/3, thresholds 0.5 / 1.5 / 2.5 health-points per day; flat
    /// folds into tier 1 of the delta's sign) so Azi's tone always agrees with the HUD arrow.
    ///
    /// WIRING (human):
    ///   1. This component already lives on the scene object that carried
    ///      DaysLeftPopupNotifier (the rename kept the meta GUID). Re-check aziPortrait
    ///      survived; checkInIntervalDays is a NEW field (deliberately renamed so the old
    ///      scene value of 7 doesn't stick) — it starts at the 60-day default.
    ///   2. Author a CheckInConversationSO (see its AUTHORING notes) and assign
    ///      <c>checkInDialogue</c>.
    ///   3. Build + wire the CheckInPanel prefab (see CheckInPanelUI's WIRING notes).
    ///   4. Tune <c>significantDeltaThreshold</c> — health-points a tile must move over one
    ///      check-in window to count as significant.
    /// </summary>
    public class AziCheckInNotifier : MonoBehaviour
    {
        [Header("Speaker")]
        [SerializeField] private Sprite aziPortrait;

        [Header("Cadence")]
        [Tooltip("A check-in fires every Nth resolved day (default 60 — two per 180-day season). Never fires once days-left reaches 0; the run-end report is the final check-in.")]
        [SerializeField] private int checkInIntervalDays = 60;

        [Header("Significant tile deltas")]
        [Tooltip("Health-points a tile must gain/lose since the previous check-in to count as significantly improved/decayed.")]
        [SerializeField] private float significantDeltaThreshold = 10f;

        [Header("Check-in dialogue (PRIMARY delivery — leave null to keep side-bubble-only fallback)")]
        [Tooltip("Variant list played inside CheckInPanelUI. Bodies may use {days}, {improved}, {decayed}.")]
        [SerializeField] private CheckInConversationSO checkInDialogue;

        [Header("Fallback rising lines (tier 1 → 3, includes flat)")]
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

        [Header("Fallback falling lines (tier 1 → 3, includes flat)")]
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

        private bool _warnedMissingPopupManager; // warn-once guard, don't spam every interval
        private bool _warnedPanelPathUnwired;    // warn-once guard for the fallback path

        // Per-tile health at the previous check-in (seeded on the first resolved day).
        // Keyed by Tile reference — tiles are only ever added (region unlocks), never destroyed
        // mid-run; a new tile simply has no baseline until the next snapshot.
        private readonly Dictionary<Tile, float> _baselineHealth = new Dictionary<Tile, float>();

        void OnEnable()
        {
            if (RunManager.Instance == null)
            {
                Debug.LogError($"{name}: RunManager.Instance is null — AziCheckInNotifier cannot subscribe to OnDayResolved.", this);
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
            if (day <= 0) return;

            // Seed the very first baseline so the first check-in measures a real window.
            if (_baselineHealth.Count == 0)
                SnapshotBaseline();

            int daysLeft = 0;
            if (ResourceManager.Instance != null)
                daysLeft = Mathf.Max(0, ResourceManager.Instance.RunLengthDays - ResourceManager.Instance.TotalDays);

            // The run-end report owns the finale — no check-in on top of it.
            if (daysLeft <= 0) return;

            if (checkInIntervalDays <= 0 || day % checkInIntervalDays != 0) return;

            FireCheckIn(daysLeft);
        }

        // ── Check-in ──────────────────────────────────────────────────────────

        private void FireCheckIn(int daysLeft)
        {
            ComputeSignificantDeltas(out int improved, out int decayed);
            SnapshotBaseline(); // next window measures from THIS check-in

            SeasonReportData data       = RunManager.Instance != null ? RunManager.Instance.BuildSeasonReportData() : null;
            ConversationSO conversation = checkInDialogue != null ? checkInDialogue.Select(improved, decayed, daysLeft) : null;

            bool panelReady = data != null
                              && conversation != null
                              && CheckInPanelUI.Instance != null
                              && DialogueManager.Instance != null;

            if (panelReady)
            {
                CheckInPanelUI.Instance.Show(data, improved, decayed, conversation);
                return;
            }

            if (!_warnedPanelPathUnwired)
            {
                _warnedPanelPathUnwired = true;
                string reason =
                    checkInDialogue == null            ? "checkInDialogue is not wired" :
                    conversation == null               ? "checkInDialogue has no usable variant" :
                    data == null                       ? "BuildSeasonReportData returned null (runEndCoordinator likely unwired)" :
                    CheckInPanelUI.Instance == null    ? "no CheckInPanelUI is wired in-scene" :
                                                         "DialogueManager.Instance is null";
                Debug.LogWarning($"{name}: {reason} — falling back to the side-bubble check-in (the check-in panel will not open).", this);
            }
            SayFallback(BuildTrendLine(daysLeft));
        }

        /// <summary>
        /// Counts tiles whose health moved by at least <see cref="significantDeltaThreshold"/>
        /// since the previous baseline. Tiles with no baseline (spawned this window) are skipped.
        /// </summary>
        private void ComputeSignificantDeltas(out int improved, out int decayed)
        {
            improved = decayed = 0;

            var tileManager = RunManager.Instance != null ? RunManager.Instance.TileManager : null;
            if (tileManager == null)
            {
                Debug.LogWarning($"{name}: TileManager unavailable — significant tile deltas report 0/0 this check-in.", this);
                return;
            }

            foreach (Tile tile in tileManager.GetAllTiles())
            {
                if (tile == null || !_baselineHealth.TryGetValue(tile, out float baseline)) continue;

                float delta = tile.CalculateHealth() - baseline;
                if      (delta >=  significantDeltaThreshold) improved++;
                else if (delta <= -significantDeltaThreshold) decayed++;
            }
        }

        private void SnapshotBaseline()
        {
            var tileManager = RunManager.Instance != null ? RunManager.Instance.TileManager : null;
            if (tileManager == null) return;

            _baselineHealth.Clear();
            foreach (Tile tile in tileManager.GetAllTiles())
            {
                if (tile != null)
                    _baselineHealth[tile] = tile.CalculateHealth();
            }
        }

        // ── Side-bubble fallback ──────────────────────────────────────────────

        private string BuildTrendLine(int daysLeft)
        {
            float trend = RunManager.Instance != null ? RunManager.Instance.WorldHealthTrend : 0f;
            float mag   = Mathf.Abs(trend);
            bool rising = trend >= 0f;
            int  tier   = mag >= Tier3Threshold ? 3 : mag >= Tier2Threshold ? 2 : 1;

            return LineFor(rising, tier).Replace("{days}", daysLeft.ToString());
        }

        private void SayFallback(string line)
        {
            var npm = NarrativePopupManager.Instance;
            if (npm == null)
            {
                if (!_warnedMissingPopupManager)
                {
                    Debug.LogWarning($"{name}: NarrativePopupManager.Instance is null — skipping Azi's check-in.", this);
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
