namespace SaigonBite.Web.Services;

/// <summary>A-12/BR17: services never read the system clock; callers inject time.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>Vietnam local time (UTC+7) on both Windows and Linux.</summary>
public static class VietnamTime
{
    private static readonly string[] ZoneIds = { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" };

    public static DateTime FromUtc(DateTimeOffset utc)
    {
        foreach (var id in ZoneIds)
        {
            try
            {
                return TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.FindSystemTimeZoneById(id)).DateTime;
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        // Last resort on a stripped-down host: fixed UTC+7 with Unspecified kind for SQL datetime2.
        return utc.AddHours(7).DateTime;
    }
}
