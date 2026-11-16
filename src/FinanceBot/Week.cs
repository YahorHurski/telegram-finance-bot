using NodaTime;

public static class Week
{
    public static readonly DateTimeZone Warsaw = DateTimeZoneProviders.Tzdb["Europe/Warsaw"];
    public const decimal Limit = 200m;   // shortcut: constant until Phase 5 (LIM-01)

    // [Monday 00:00, next Monday 00:00) in Warsaw; 167 or 169 hours on DST weeks
    public static (Instant Start, Instant End) Containing(Instant t)
    {
        var monday = t.InZone(Warsaw).Date.With(DateAdjusters.PreviousOrSame(IsoDayOfWeek.Monday));
        return (Warsaw.AtStartOfDay(monday).ToInstant(), Warsaw.AtStartOfDay(monday.PlusWeeks(1)).ToInstant());
    }
}
