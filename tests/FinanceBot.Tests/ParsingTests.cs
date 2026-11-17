using System.Globalization;
using NodaTime.Text;

// D-12: pure unit tests, no DB and no host
public class ParsingTests
{
    [Theory]
    [InlineData("45,99 zł", "45.99")]
    [InlineData("1\u00A0234,56 zł", "1234.56")]   // NBSP as group separator
    [InlineData("1\u202F234,56 zł", "1234.56")]   // narrow NBSP as group separator
    [InlineData("PLN 23.50", "23.50")]
    [InlineData("23", "23")]
    [InlineData("0,99 zł", "0.99")]
    [InlineData("1.234,56", "1234.56")]
    [InlineData("1,234.56", "1234.56")]
    [InlineData("12,5", "12.5")]
    public void Amount_ParsesPolishAndCommonFormats(string raw, string expected) =>
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), Parsing.Amount(raw));

    // Never counted as zloty: a wrong amount here would make the remaining limit look better than it is
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("-23,50 zł")]
    [InlineData("0,00 zł")]
    [InlineData("1 000 000,00 zł")]
    [InlineData("€5,00")]
    [InlineData("5,00 €")]
    [InlineData("$5.00")]
    [InlineData("£5.00")]
    [InlineData("5,00 EUR")]
    [InlineData("USD 5.00")]
    [InlineData("5 GBP")]
    public void Amount_ReturnsNullForInvalidOrForeign(string? raw) =>
        Assert.Null(Parsing.Amount(raw));

    [Theory]
    [InlineData("16.11.2026 14:03:05", "2026-11-16T13:03:05Z")]
    [InlineData("29.03.2026 02:30:00", "2026-03-29T01:30:00Z")]   // DST gap: shifted forward by the gap
    [InlineData("25.10.2026 02:30:00", "2026-10-25T00:30:00Z")]   // repeated hour: the earlier one (CEST)
    [InlineData("  16.11.2026 14:03:05 ", "2026-11-16T13:03:05Z")]
    [InlineData("16.11.2026, 14:03:05", "2026-11-16T13:03:05Z")]   // iOS "Date" variable, medium time
    [InlineData("16.11.2026, 14:03", "2026-11-16T13:03:00Z")]      // iOS "Date" variable, short time
    public void Time_ReadsPhoneTimeAsWarsaw(string raw, string expectedUtc) =>
        Assert.Equal(InstantPattern.ExtendedIso.Parse(expectedUtc).Value, Parsing.Time(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("garbage")]
    [InlineData("16.11.2026 14:03")]
    public void Time_ReturnsNullForUnknownFormat(string? raw) =>
        Assert.Null(Parsing.Time(raw));
}
