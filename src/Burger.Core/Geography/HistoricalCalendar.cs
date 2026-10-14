using System.Globalization;

namespace Burger.Core.Geography;

/// <summary>
/// The one date representation geography history uses: <see cref="GameDate.TotalDays"/>, whole days since 1.1.1 (astronomical year 1).
/// Years follow astronomical numbering, so year 0 is 1 BCE and -499 is 500 BCE; there is no gap at the BCE/CE boundary and
/// plain integer comparison orders every date. Conventional text may carry a BCE/CE suffix (<c>500BCE.3.1</c>, <c>1CE</c>), which
/// has no year zero: <c>0BCE</c> and <c>0CE</c> are rejected. Plain numbers without a suffix are astronomical (the engine's existing convention).
/// </summary>
public static class HistoricalCalendar
{
    /// <summary>End of an open-ended interval. Later than any date a campaign can reach.</summary>
    public const int Forever = int.MaxValue;
    /// <summary>Before every date.</summary>
    public const int Beginning = int.MinValue;

    public static int FromAstronomical(int year, int month = 1, int day = 1) => new GameDate(year, month, day).TotalDays;

    /// <summary>Converts a conventional year (<c>bce</c> = before common era) to the astronomical year. Throws on year 0.</summary>
    public static int AstronomicalYear(int conventionalYear, bool bce)
    {
        if (conventionalYear <= 0) throw new ArgumentOutOfRangeException(nameof(conventionalYear), "Conventional years start at 1 (there is no year 0)");
        return bce ? 1 - conventionalYear : conventionalYear;
    }

    public static int FromConventional(int year, bool bce, int month = 1, int day = 1) => FromAstronomical(AstronomicalYear(year, bce), month, day);

    /// <summary>Parses <c>Y</c>, <c>Y.M</c>, <c>Y.M.D</c>, each year optionally <c>500BCE</c>/<c>500 BCE</c>/<c>2020CE</c>/<c>2020AD</c>; <c>forever</c>/<c>never</c> = <see cref="Forever"/>.</summary>
    public static bool TryParse(string? text, out int days, out string? error)
    {
        days = 0; error = null;
        if (string.IsNullOrWhiteSpace(text)) { error = "empty date"; return false; }
        string s = text.Trim();
        if (s.Equals("forever", StringComparison.OrdinalIgnoreCase) || s.Equals("never", StringComparison.OrdinalIgnoreCase)) { days = Forever; return true; }
        var parts = s.Split('.');
        if (parts.Length > 3) { error = $"'{text}' has too many parts (year.month.day)"; return false; }
        string y = parts[0].Trim();
        bool? bce = null;
        foreach (var (suffix, isBce) in new[] { ("BCE", true), ("BC", true), ("CE", false), ("AD", false) })
            if (y.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { bce = isBce; y = y[..^suffix.Length].Trim(); break; }
        if (!int.TryParse(y, NumberStyles.Integer, CultureInfo.InvariantCulture, out int yr)) { error = $"'{text}': year is not a number"; return false; }
        if (bce != null)
        {
            if (yr <= 0) { error = $"'{text}': BCE/CE years start at 1 (there is no year 0; write -{Math.Max(0, -yr)} or 1BCE style)"; return false; }
            yr = AstronomicalYear(yr, bce.Value);
        }
        int mo = 1, d = 1;
        if (parts.Length > 1 && !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out mo)) { error = $"'{text}': month is not a number"; return false; }
        if (parts.Length > 2 && !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out d)) { error = $"'{text}': day is not a number"; return false; }
        if (mo < 1 || mo > 12) { error = $"'{text}': month must be 1-12"; return false; }
        if (d < 1 || d > GameDate.DaysInMonth[mo - 1]) { error = $"'{text}': day out of range for month {mo}"; return false; }
        days = FromAstronomical(yr, mo, d);
        return true;
    }

    /// <summary>Human text for a day number: <c>1 March 500 BCE</c>, <c>forever</c> for the open end.</summary>
    public static string Format(int days)
    {
        if (days == Forever) return "forever";
        if (days == Beginning) return "the beginning";
        return new GameDate(days).ToDisplay();
    }

    /// <summary>Script round-trip text (astronomical, unambiguous): <c>-499.3.1</c>.</summary>
    public static string ToScript(int days) => days == Forever ? "forever" : new GameDate(days).ToScript();
}
