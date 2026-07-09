namespace Habitales.Meta
{
    /// <summary>
    /// HQ's verdict on the field season, derived from the same weighted-XP-per-tile ratio
    /// RunEndCoordinator already computes (arch ENDGAME_BUILD_PLAN §3). Ordered worst→best so
    /// comparisons ("did the grade improve?") read naturally as int comparisons.
    /// </summary>
    public enum SeasonGrade
    {
        /// <summary>≥90% critical tiles — the run ended in Ecosystem Collapse. Forced, never scored.</summary>
        Collapse = 0,

        /// <summary>The region is losing ground. Most tiles degraded or critical.</summary>
        Concerning = 1,

        /// <summary>Holding the line. A mix of thriving and degraded, nothing collapsing.</summary>
        Adequate = 2,

        /// <summary>Clearly working. Thriving tiles dominate the region.</summary>
        Commendable = 3,

        /// <summary>Near-total thriving coverage. The best a season can look.</summary>
        Exemplary = 4
    }
}
