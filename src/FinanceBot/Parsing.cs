using System.Globalization;
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
        if (raw.IndexOfAny(['€', '$', '£']) >= 0 || raw.Contains("EUR") || raw.Contains("USD") || raw.Contains("GBP"))
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

    // Phone local time, read as Europe/Warsaw (D-08). DST gap/overlap resolved leniently.
    public static Instant? Time(string? raw)
    {
        if (raw is null) return null;
        foreach (var p in TimePatterns)
        {
            var r = p.Parse(raw.Trim());
            if (r.Success) return Week.Warsaw.AtLeniently(r.Value).ToInstant();
        }
        return null;
    }
}
