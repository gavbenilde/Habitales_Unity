using UnityEngine;

/// <summary>
/// Two-season, real-ish calendar. Global static utility — pure functions over a
/// 0-based day-of-year index (0 = Jan 1). No state, no singleton: anyone can call these
/// (Law 1 — this is data math, not another system's owned state).
///
/// Dry season: Dec 1 – May 31. Wet season: Jun 1 – Nov 30. Stylized 12×30 calendar —
/// every month is exactly 30 days, so a year is <see cref="DaysPerYear"/> = 360 and each
/// season is exactly 180 days. ResourceManager derives its year math from this constant.
/// </summary>
public static class GameCalendar
{
    public const int DaysPerYear = 360;

    /// <summary>Stylized month lengths, Jan..Dec — a uniform 30 keeps all date/season math trivial.</summary>
    private static readonly int[] MonthLengths =
    {
        30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30
    };

    private static readonly string[] MonthNames =
    {
        "Jan", "Feb", "Mar", "Apr", "May", "Jun",
        "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
    };

    // First day-of-year (0-based) of each month, e.g. MonthStart[0] = 0 (Jan 1),
    // MonthStart[5] = 151 (Jun 1). Built once from MonthLengths.
    private static readonly int[] MonthStart = BuildMonthStarts();

    private static int[] BuildMonthStarts()
    {
        var starts = new int[12];
        int acc = 0;
        for (int i = 0; i < 12; i++)
        {
            starts[i] = acc;
            acc += MonthLengths[i];
        }
        return starts;
    }

    // Dry season starts Dec 1 (month index 11); Wet season starts Jun 1 (month index 5).
    private const int DrySeasonStartMonth = 11;
    private const int WetSeasonStartMonth = 5;

    /// <summary>Normalizes any int (negative or overflowing) into a valid [0, DaysPerYear) day-of-year.</summary>
    private static int Normalize(int dayOfYear)
    {
        int d = dayOfYear % DaysPerYear;
        if (d < 0) d += DaysPerYear;
        return d;
    }

    /// <summary>0-based month index (0..11) for a given 0-based day-of-year.</summary>
    public static int GetMonth(int dayOfYear)
    {
        int d = Normalize(dayOfYear);
        for (int m = 11; m >= 0; m--)
        {
            if (d >= MonthStart[m]) return m;
        }
        return 0; // unreachable — MonthStart[0] == 0
    }

    /// <summary>1-based day-of-month (1..31) for a given 0-based day-of-year.</summary>
    public static int GetDayOfMonth(int dayOfYear)
    {
        int d = Normalize(dayOfYear);
        int month = GetMonth(d);
        return d - MonthStart[month] + 1;
    }

    /// <summary>Dry (Dec–May) or Wet (Jun–Nov) season for a given 0-based day-of-year.</summary>
    public static Season GetSeason(int dayOfYear)
    {
        int month = GetMonth(dayOfYear);
        // Wet season is the contiguous block [Jun..Nov] = months [5..10]; everything else is Dry.
        return (month >= WetSeasonStartMonth && month < DrySeasonStartMonth) ? Season.Wet : Season.Dry;
    }

    /// <summary>"Mon D" short date string, e.g. "Jun 14".</summary>
    public static string GetShortDate(int dayOfYear)
    {
        int month = GetMonth(dayOfYear);
        int day = GetDayOfMonth(dayOfYear);
        return $"{MonthNames[month]} {day}";
    }

    /// <summary>True on the first calendar day of either season (Dec 1 or Jun 1).</summary>
    public static bool IsSeasonStart(int dayOfYear)
    {
        int d = Normalize(dayOfYear);
        return d == MonthStart[DrySeasonStartMonth] || d == MonthStart[WetSeasonStartMonth];
    }

    /// <summary>How many days into the current season this day is (0 on the season's first day).</summary>
    public static int DaysIntoSeason(int dayOfYear)
    {
        int d = Normalize(dayOfYear);
        Season season = GetSeason(d);
        int seasonStartDay = season == Season.Dry ? MonthStart[DrySeasonStartMonth] : MonthStart[WetSeasonStartMonth];

        int diff = d - seasonStartDay;
        if (diff < 0) diff += DaysPerYear; // Dry season wraps across the year boundary (Dec → May)
        return diff;
    }

    /// <summary>How many days remain until the current season ends (0 on the season's last day).</summary>
    public static int DaysUntilSeasonEnd(int dayOfYear)
    {
        Season season = GetSeason(dayOfYear);
        int seasonLength = season == Season.Dry
            ? DaysPerYear - MonthStart[DrySeasonStartMonth] + MonthStart[WetSeasonStartMonth]  // Dec 1 → May 31 (wraps)
            : MonthStart[DrySeasonStartMonth] - MonthStart[WetSeasonStartMonth];                // Jun 1 → Nov 30

        return seasonLength - 1 - DaysIntoSeason(dayOfYear);
    }
}

/// <summary>The two-season model driving weather weighting and calendar UI.</summary>
public enum Season { Dry, Wet }
