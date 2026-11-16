// D-01..D-04 texts. Invisible and look-alike characters are written as escapes:
// U+2212 minus, U+00B7 middle dot, U+00A0 NBSP, U+00AB/U+00BB quotes, U+26A0 U+FE0F warning sign, U+2026 ellipsis
public class MessagesTests
{
    static Transaction Tx(decimal? amount, string? merchant, string? raw = null) =>
        new() { Amount = amount, Merchant = merchant, AmountRaw = raw };

    static string Line(string text, int i) => text.Split('\n')[i];

    [Fact]
    public void Green_ShowsAmountMerchantAndRemainder() =>
        Assert.Equal("\u2212 45,99 zł \u00B7 Żabka\n🟢 Осталось 154,01 из 200 zł",
                     Messages.Confirmation(Tx(45.99m, "Żabka"), 45.99m));

    [Fact]
    public void AtLimit_StillGreenWithZero() =>
        Assert.Equal("🟢 Осталось 0,00 из 200 zł", Line(Messages.Confirmation(Tx(10m, "Lidl"), 200m), 1));

    [Fact]
    public void Overspend_RedBoldWithOverrun()
    {
        var text = Messages.Confirmation(Tx(50m, "Lidl"), 237.50m);

        Assert.Equal("🔴 <b>Потрачено 237,50/200 \u00B7 Перерасход 37,50 zł</b>", Line(text, 1));
        Assert.DoesNotContain("Осталось", text);
    }

    [Fact]
    public void Grouping_UsesNbspAndDecimalComma() =>
        Assert.StartsWith("\u2212 1\u00A0234,50 zł", Messages.Confirmation(Tx(1234.5m, "Lidl"), 1234.5m));

    [Theory]
    [InlineData("A&B <x>", "A&amp;B &lt;x&gt;")]
    [InlineData("Żabka Mokotów", "Żabka Mokotów")]
    public void Escape_OnlyAmpersandAndAngleBrackets(string merchant, string expected) =>
        Assert.EndsWith(" \u00B7 " + expected, Line(Messages.Confirmation(Tx(5m, merchant), 5m), 0));

    [Fact]
    public void NullMerchant_ShownAsQuestionMark() =>
        Assert.EndsWith(" \u00B7 ?", Line(Messages.Confirmation(Tx(5m, null), 5m), 0));

    [Fact]
    public void Unparsed_WarnsWithoutRemainder()
    {
        var text = Messages.Confirmation(Tx(null, "Lidl", "abc"), 0m);

        Assert.Equal("\u26A0\uFE0F Не разобрал сумму: \u00ABabc\u00BB \u00B7 Lidl", text);
        Assert.DoesNotContain("Осталось", text);
    }

    [Fact]
    public void UnparsedLong_TruncatedTo100CharsWithEllipsis() =>
        Assert.Equal("\u26A0\uFE0F Не разобрал сумму: \u00AB" + new string('x', 100) + "\u2026\u00BB \u00B7 Lidl",
                     Messages.Confirmation(Tx(null, "Lidl", new string('x', 500)), 0m));

    [Fact]
    public void UnparsedEscape_RawAmountIsEscaped()
    {
        var text = Messages.Confirmation(Tx(null, "Lidl", "<b>5</b>"), 0m);

        Assert.Contains("&lt;b&gt;5&lt;/b&gt;", text);
        Assert.DoesNotContain("<b>", text);
    }
}
