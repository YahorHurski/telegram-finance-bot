using System.Globalization;
using System.Text.RegularExpressions;
using NodaTime;
using NodaTime.Text;

public static class Parsing
{
    // "45,99 zł"→45.99, "1 234,56 zł"→1234.56, "PLN 23.50"→23.50, "1.234,56"→1234.56, "1,234.56"→1234.56;
    // "-23,50 zł", "0,00 zł", "abc", "", "€5,00" → null
    public static decimal? Amount(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // shortcut: temporary parser until the SPIKE-01 corpus; full version (ING-04, ING-09) in Phase 3
        raw = raw.Replace('−', '-').Replace('–', '-');   // typographic minus/dash: a refund, not a purchase
        // Allow-list, not block-list: anything besides "zł"/"PLN" is a foreign currency (Kč, CHF, €...)
        var unit = new string(raw.Where(c => !char.IsDigit(c) && !char.IsWhiteSpace(c) && c is not (',' or '.' or '-')).ToArray());
        if (unit.Length > 0 && !unit.Equals("zł", StringComparison.OrdinalIgnoreCase) && !unit.Equals("PLN", StringComparison.OrdinalIgnoreCase))
            return null;
        var s = new string(raw.Where(c => char.IsDigit(c) || c is ',' or '.' or '-').ToArray());
        int sep = s.LastIndexOfAny([',', '.']);
        // last separator followed by 1-2 digits is the decimal point, everything else is grouping
        s = sep >= 0 && s.Length - sep - 1 is 1 or 2
            ? s[..sep].Replace(",", "").Replace(".", "") + "." + s[(sep + 1)..]
            : s.Replace(",", "").Replace(".", "");
        return decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                                CultureInfo.InvariantCulture, out var v) && v > 0 && v < 1_000_000 ? v : null;
    }

    // Custom "Format Date" output, plus the iOS ru-locale date+time of the raw "Date" variable
    // ("Короткий" date with "Средний"/"Короткий" time: "15.09.2026, 09:41:00" / "15.09.2026, 09:41").
    static readonly LocalDateTimePattern[] TimePatterns =
    [
        LocalDateTimePattern.CreateWithInvariantCulture("dd'.'MM'.'uuuu HH':'mm':'ss"),
        LocalDateTimePattern.CreateWithInvariantCulture("dd'.'MM'.'uuuu', 'HH':'mm':'ss"),
        LocalDateTimePattern.CreateWithInvariantCulture("dd'.'MM'.'uuuu', 'HH':'mm"),
    ];

    // iOS ru-locale "Средний" date: "17 нояб. 2026 г., 12:03" (iOS puts NBSP/narrow NBSP between parts)
    static readonly Regex RuMediumDate = new(
        @"^(\d{1,2}) (\p{L}{3})\p{L}*\.? (\d{4}) г\.?, (\d{1,2}):(\d{2})(?::(\d{2}))?$", RegexOptions.CultureInvariant);
    static readonly string[] RuMonths = ["янв", "фев", "мар", "апр", "ма", "июн", "июл", "авг", "сен", "окт", "ноя", "дек"];

    // Phone local time, read as Europe/Warsaw (D-08). DST gap/overlap resolved leniently.
    public static Instant? Time(string? raw)
    {
        if (raw is null) return null;
        var s = raw.Replace(' ', ' ').Replace(' ', ' ').Trim();
        foreach (var p in TimePatterns)
        {
            var r = p.Parse(s);
            if (r.Success) return Week.Warsaw.AtLeniently(r.Value).ToInstant();
        }
        var m = RuMediumDate.Match(s);
        if (!m.Success) return null;
        int month = Array.FindIndex(RuMonths, x => m.Groups[2].Value.ToLowerInvariant().StartsWith(x)) + 1;
        if (month == 0) return null;
        try
        {
            var local = new LocalDateTime(int.Parse(m.Groups[3].Value), month, int.Parse(m.Groups[1].Value),
                int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value),
                m.Groups[6].Success ? int.Parse(m.Groups[6].Value) : 0);
            return Week.Warsaw.AtLeniently(local).ToInstant();
        }
        catch (ArgumentOutOfRangeException) { return null; }   // "31 февр." and the like
    }
}
