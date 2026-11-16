using NodaTime;
using NodaTime.Text;

// Mon-Sun week in Europe/Warsaw; inputs are fixed local times, never the system clock
public class WeekTests
{
    [Theory]
    [InlineData(2026, 11, 15, 23, 59, 59, "2026-11-08T23:00:00Z", "2026-11-15T23:00:00Z", 168)]  // Sunday, last second
    [InlineData(2026, 11, 16, 0, 0, 0, "2026-11-15T23:00:00Z", "2026-11-22T23:00:00Z", 168)]     // Monday, first second
    [InlineData(2026, 3, 25, 12, 0, 0, "2026-03-22T23:00:00Z", "2026-03-29T22:00:00Z", 167)]     // switch to summer time
    [InlineData(2026, 10, 21, 12, 0, 0, "2026-10-18T22:00:00Z", "2026-10-25T23:00:00Z", 169)]    // switch to winter time
    public void Containing_ReturnsMondayToMondayInWarsaw(
        int y, int mo, int d, int h, int mi, int s, string start, string end, int hours)
    {
        var t = new LocalDateTime(y, mo, d, h, mi, s).InZoneLeniently(Week.Warsaw).ToInstant();

        var (actualStart, actualEnd) = Week.Containing(t);

        Assert.Equal(InstantPattern.ExtendedIso.Parse(start).Value, actualStart);
        Assert.Equal(InstantPattern.ExtendedIso.Parse(end).Value, actualEnd);
        Assert.Equal(hours, (actualEnd - actualStart).TotalHours);
    }
}
