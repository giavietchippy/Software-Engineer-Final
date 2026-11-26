using System.Globalization;

namespace SaigonBite.Web.Services;

/// <summary>
/// N-06: opt-in fixed clock for repeatable 15:00 demonstrations. It is enabled only by
/// SAIGONBITE_FIXED_CLOCK (an ISO-8601 instant such as 2026-11-26T15:00:00+07:00) outside
/// Production; every other path uses SystemClock.
/// </summary>
public sealed class DemoClock : IClock
{
    public const string EnvironmentVariableName = "SAIGONBITE_FIXED_CLOCK";

    public DemoClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow.ToUniversalTime();
    }

    public static bool TryCreate(string? value, out DemoClock? clock)
    {
        clock = null;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            return false;

        clock = new DemoClock(parsed);
        return true;
    }

    public DateTimeOffset UtcNow { get; }
}
