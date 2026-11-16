using System.Globalization;

public static class Messages
{
    // Built by hand instead of ru-RU: same output on Windows, CI and chiseled containers without ICU (D-04)
    static readonly NumberFormatInfo Nfi = CreateNfi();
    static NumberFormatInfo CreateNfi()
    {
        var n = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        n.NumberDecimalSeparator = ",";
        n.NumberGroupSeparator = " ";
        return n;
    }

    public static string Money(decimal v) => v.ToString("#,0.00", Nfi);

    // Telegram HTML needs only these three escaped; WebUtility.HtmlEncode would turn "ó" into "&#243;"
    public static string Esc(string? s) => (s ?? "?").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    public static string Confirmation(Transaction tx, decimal spent)
    {
        if (tx.Amount is null)   // D-03
            return $"⚠️ Не разобрал сумму: «{Esc(tx.AmountRaw ?? "")}» · {Esc(tx.Merchant)}";
        var limit = Week.Limit.ToString("0", Nfi);
        // "−" is U+2212
        return $"− {Money(tx.Amount.Value)} zł · {Esc(tx.Merchant)}\n🟢 Осталось {Money(Week.Limit - spent)} из {limit} zł";
    }
}
