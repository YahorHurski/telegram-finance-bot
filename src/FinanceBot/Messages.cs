using System.Globalization;

public static class Messages
{
    // Built by hand instead of the Russian CultureInfo: same output on Windows, CI and chiseled containers without ICU (D-04)
    static readonly NumberFormatInfo Nfi = CreateNfi();
    static NumberFormatInfo CreateNfi()
    {
        var n = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        n.NumberDecimalSeparator = ",";
        n.NumberGroupSeparator = " ";
        return n;
    }

    public static string Money(decimal v) => v.ToString("#,0.00", Nfi);

    // Telegram HTML needs only these three escaped; the framework HTML encoder would turn "ó" into "&#243;"
    public static string Esc(string? s) => (s ?? "?").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    public static string Confirmation(Transaction tx, decimal spent)
    {
        if (tx.Amount is null)   // D-03
        {
            // Truncate before escaping so an entity like "&amp;" is never cut in half; keeps the message far below 4096
            var raw = tx.AmountRaw ?? "";
            if (raw.Length > 100) raw = raw[..100] + "…";
            return $"⚠️ Не разобрал сумму: «{Esc(raw)}» · {Esc(tx.Merchant)}";
        }
        var limit = Week.Limit.ToString("0", Nfi);
        var status = spent > Week.Limit   // D-02; exactly 200 is still green with 0,00
            ? $"🔴 <b>Потрачено {Money(spent)}/{limit} · Перерасход {Money(spent - Week.Limit)} zł</b>"
            : $"🟢 Осталось {Money(Week.Limit - spent)} из {limit} zł";
        // "−" is U+2212
        return $"− {Money(tx.Amount.Value)} zł · {Esc(tx.Merchant)}\n{status}";
    }
}
