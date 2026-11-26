namespace SaigonBite.Web.Services;

/// <summary>
/// N-06: dev-only fixed clock for repeatable spec demonstrations. Enabled by setting
/// DemoClock:LocalTime (Vietnam local time, e.g. "15:00"); production keeps SystemClock.
/// </summary>
public sealed class DemoClock : IClock
{
    public DemoClock(TimeOnly localTime)
    {
        var local = new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day,
            localTime.Hour, localTime.Minute, 0, DateTimeKind.Unspecified);
        UtcNow = new DateTimeOffset(local, TimeSpan.FromHours(7));
    }

    public DateTimeOffset UtcNow { get; }
}
