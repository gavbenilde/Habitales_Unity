using System.Collections.Generic;
using UnityEngine;
using Habitales.Dialogue;
using Habitales.Meta;

namespace Habitales.UI
{
    /// <summary>
    /// The periodic check-in scheduler — the slim standalone gate behind
    /// <see cref="CheckInPanelUI"/> (2026-07-15 rework; formerly AziCheckInNotifier, and
    /// before that DaysLeftPopupNotifier). Purely a meaning-events subscriber (Law 2) —
    /// it reacts to <see cref="RunManager.OnDayResolved"/> and reads public singleton
    /// state; it does not own or mutate any simulation data, and it touches the popup
    /// system zero times: a check-in either shows the CheckInPanel or loud-fails and
    /// skips (one presentation path — the side-bubble fallback was a test feature,
    /// deleted). The sim NEVER pauses for a check-in that can't show; the pause lives
    /// inside CheckInPanelUI.Show().
    ///
    /// ── Cadence ──
    /// Fires every <see cref="checkInIntervalDays"/> resolved days (default 60), skipping
    /// day &lt;= 0, daysLeft &lt;= 0, and an already-ended run. Interior season boundaries
    /// DO get a check-in; only the run's true final day is skipped — the End Conversation
    /// owns it (1 season = 2 check-ins; 3 seasons = 3+3+2; 6 = 3+3+3+3+3+2).
    ///
    /// ── Significant tile deltas ──
    /// At every check-in each tile's health is compared against the snapshot taken at the
    /// PREVIOUS check-in (baseline seeded on the first resolved day). A tile whose health
    /// moved by at least <see cref="significantDeltaThreshold"/> counts as significantly
    /// improved/decayed; tiles that didn't exist at the previous snapshot (region unlocks)
    /// are skipped for one window. The counts drive which <see cref="CheckInConversationSO"/>
    /// variant plays AND appear in the panel's stats column.
    ///
    /// ── Projection slope ──
    /// <see cref="ComputeBaselineSlope"/> feeds the panel graph's dashed projection. It is
    /// baseline-relative, NOT world-average-relative (zone unlocks dump low-health tiles
    /// into the world average, which would make expansion read as failure):
    /// slope = Σ(tile health now − tile baseline) ÷ baselined-tile-count ÷ window days.
    /// Tiles without a baseline (unlocked mid-window) are excluded — unlock-immune.
    ///
    /// ── Failure policy ──
    /// Any missing piece → Debug.LogError once per run (Law 3, with this GameObject
    /// highlighted), check-in skipped. Everything derives from the interval: the graph
    /// window, projection span, and delta window all read <see cref="checkInIntervalDays"/>.
    ///
    /// ── Restart ──
    /// RunRestart is a full scene reload (RunRestart.cs) — this component re-Awakes clean,
    /// so the baseline dictionary and the error-once flag need no explicit reset hook.
    ///
    /// WIRING (human):
    ///   1. This component lives on the scene object that carried AziCheckInNotifier
    ///      (the git mv kept the meta GUID — the scene reference survives the rename).
    ///   2. Author a CheckInConversationSO (see its AUTHORING notes) and assign
    ///      <c>checkInDialogue</c>.
    ///   3. Build + wire the CheckInPanel prefab (see CheckInPanelUI's WIRING notes).
    ///   4. Tune <c>checkInIntervalDays</c> and <c>significantDeltaThreshold</c>.
    /// </summary>
    public class CheckInScheduler : MonoBehaviour
    {
        [Header("Cadence")]
        [Tooltip("A check-in fires every Nth resolved day (default 60). Never fires once days-left reaches 0; the End Conversation owns the final day. Also the graph window, projection span, and delta window — everything derives from this one field.")]
        [SerializeField] private int checkInIntervalDays = 60;

        [Header("Significant tile deltas")]
        [Tooltip("Health-points a tile must gain/lose since the previous check-in to count as significantly improved/decayed.")]
        [SerializeField] private float significantDeltaThreshold = 10f;

        [Header("Check-in dialogue")]
        [Tooltip("Variant list played inside CheckInPanelUI. Bodies may use {days}, {improved}, {decayed}. Unwired → LogError once per run, check-in skipped.")]
        [SerializeField] private CheckInConversationSO checkInDialogue;

        private bool _erroredThisRun; // error-once guard — a broken wire shouldn't spam every interval

        // Per-tile health at the previous check-in (seeded on the first resolved day).
        // Keyed by Tile reference — tiles are only ever added (region unlocks), never destroyed
        // mid-run; a new tile simply has no baseline until the next snapshot.
        private readonly Dictionary<Tile, float> _baselineHealth = new Dictionary<Tile, float>();

        void OnEnable()
        {
            if (RunManager.Instance == null)
            {
                Debug.LogError($"{name}: RunManager.Instance is null — CheckInScheduler cannot subscribe to OnDayResolved.", this);
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

            // The end flow owns the finale — no check-in on top of it. NOTE: within one
            // heartbeat, OnDayResolved fires BEFORE the game-over evaluation, so on a
            // collapse day IsGameOver is still false here — the real decision-12 protection
            // is CheckInPanelUI's OpenSession force-reset (end flow trumps). This guard is
            // purely defensive, for any future early-end path that sets the flag sooner.
            if (daysLeft <= 0) return;
            if (RunManager.Instance != null && RunManager.Instance.IsGameOver) return;

            if (checkInIntervalDays <= 0 || day % checkInIntervalDays != 0) return;

            FireCheckIn();
        }

        // ── Check-in ──────────────────────────────────────────────────────────

        private void FireCheckIn()
        {
            ComputeSignificantDeltas(out int improved, out int decayed);
            ComputeBaselineSlope(out float slopePerDay);
            SnapshotBaseline(); // next window measures from THIS check-in

            SeasonReportData data = RunManager.Instance != null ? RunManager.Instance.BuildSeasonReportData() : null;
            int daysLeft = data != null ? Mathf.Max(0, data.runLengthDays - data.currentDay) : 0;
            ConversationSO conversation = checkInDialogue != null ? checkInDialogue.Select(improved, decayed, daysLeft) : null;

            string missing =
                checkInDialogue == null            ? "checkInDialogue is not wired" :
                conversation == null               ? "checkInDialogue has no usable variant" :
                data == null                       ? "BuildSeasonReportData returned null (runEndCoordinator likely unwired)" :
                CheckInPanelUI.Instance == null    ? "no CheckInPanelUI is in the scene" :
                DialogueManager.Instance == null   ? "DialogueManager.Instance is null" :
                                                     null;
            if (missing != null)
            {
                // One presentation path: no fallback tier. Skip loudly, once per run —
                // and never pause the sim for a check-in that can't show.
                if (!_erroredThisRun)
                {
                    _erroredThisRun = true;
                    Debug.LogError($"{name}: {missing} — the check-in is skipped (no fallback; wire the missing piece).", this);
                }
                return;
            }

            CheckInPanelUI.Instance.Show(data, improved, decayed, slopePerDay, checkInIntervalDays, conversation);
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

        /// <summary>
        /// Per-day health slope for the graph's dashed projection — baseline-relative
        /// (see class doc): the mean raw delta over baselined tiles, spread across the
        /// window. 0 when no baselined tiles exist.
        /// </summary>
        private void ComputeBaselineSlope(out float slopePerDay)
        {
            slopePerDay = 0f;

            var tileManager = RunManager.Instance != null ? RunManager.Instance.TileManager : null;
            if (tileManager == null || checkInIntervalDays <= 0) return;

            float deltaSum = 0f;
            int baselinedCount = 0;
            foreach (Tile tile in tileManager.GetAllTiles())
            {
                if (tile == null || !_baselineHealth.TryGetValue(tile, out float baseline)) continue;
                deltaSum += tile.CalculateHealth() - baseline;
                baselinedCount++;
            }

            if (baselinedCount > 0)
                slopePerDay = deltaSum / baselinedCount / checkInIntervalDays;
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
    }
}
