using System.Collections.Generic;

namespace Habitales.Meta
{
    /// <summary>
    /// Immutable mid-run snapshot passed from RunManager to CheckInPanelUI (originally built
    /// for the archived SeasonReportLiteUI, arch ENDGAME_BUILD_PLAN §4). EndGameData's little
    /// sibling: trajectory instead of totals. Every field is a subset read of state RunManager
    /// already accumulates for BuildEndGameData — this is just the mid-run cut of it.
    /// Constructed once by RunManager.BuildSeasonReportData(); the UI only reads.
    /// </summary>
    public sealed class SeasonReportData
    {
        // Header
        /// <summary>Days elapsed so far this run (ResourceManager.TotalDays).</summary>
        public readonly int currentDay;
        /// <summary>Total length of the field season (ResourceManager.RunLengthDays) — the denominator for "Day X of Y".</summary>
        public readonly int runLengthDays;

        /// <summary>World-average health, one entry per day elapsed so far — defensive copy, safe for the sparkline to hold onto.</summary>
        public readonly IReadOnlyList<float> healthHistory;
        /// <summary>Smoothed per-day change in world-average health over the last RunManager.TrendWindowDays days (RunManager.WorldHealthTrend). Drives the trend arrow.</summary>
        public readonly float worldHealthTrend;

        // Tile breakdown — same tier classifier as the end screen (ComputeTileCounts / TierOf)
        public readonly int thrivingCount;
        public readonly int degradedCount;
        public readonly int criticalCount;

        /// <summary>"On track for: …" — current tile mix run through the same grade math as the final verdict (RunEndCoordinator.ComputeGrade), never forced to Collapse mid-run.</summary>
        public readonly SeasonGrade projectedGrade;

        /// <summary>Most-used action so far this run, or null/empty if no actions have been used yet.</summary>
        public readonly string topActionName;

        public SeasonReportData(
            int currentDay,
            int runLengthDays,
            IReadOnlyList<float> healthHistory,
            float worldHealthTrend,
            int thrivingCount,
            int degradedCount,
            int criticalCount,
            SeasonGrade projectedGrade,
            string topActionName)
        {
            this.currentDay       = currentDay;
            this.runLengthDays    = runLengthDays;
            this.healthHistory    = healthHistory;
            this.worldHealthTrend = worldHealthTrend;
            this.thrivingCount    = thrivingCount;
            this.degradedCount    = degradedCount;
            this.criticalCount    = criticalCount;
            this.projectedGrade   = projectedGrade;
            this.topActionName    = topActionName;
        }
    }
}
