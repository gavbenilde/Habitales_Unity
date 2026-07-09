namespace Habitales.Core
{
    /// <summary>
    /// Cross-scene run setup chosen in the main menu — currently just the season count.
    /// Static by design: the main menu and the run live in different scenes, and this is
    /// the one hand-off between them (SeasonSelectPanelUI writes, ResourceManager.Awake
    /// reads). 0 / unset means "no selection was made" (direct scene load in the editor,
    /// tests) and ResourceManager keeps its serialized default instead.
    ///
    /// STATIC-STATE AUDIT (see RunRestart): LEAVE ALONE on restart — a restarted run is
    /// the same run setup, so the picked season count must survive the scene reload.
    /// It is naturally overwritten the next time the player confirms in the main menu.
    /// </summary>
    public static class RunConfig
    {
        public const int MinSeasons = 1;
        public const int MaxSeasons = 10;

        /// <summary>Seasons picked in the main menu; 0 = no selection made this session.</summary>
        public static int SelectedSeasons = 0;

        public static bool HasSelection => SelectedSeasons >= MinSeasons;

        /// <summary>Run length in days for a season count (seasons × 180).</summary>
        public static int RunLengthDaysFor(int seasons) => seasons * GameCalendar.DaysPerSeason;

        /// <summary>Run length in days for the current selection. Only meaningful when
        /// <see cref="HasSelection"/> is true.</summary>
        public static int RunLengthDaysForSelection() => RunLengthDaysFor(SelectedSeasons);
    }
}
