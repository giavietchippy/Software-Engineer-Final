namespace SaigonBite.Web.Services;

/// <summary>Source of randomness, injected so tests can replace it with a fixed value.</summary>
public interface IRandomSource
{
    /// <summary>A value in [0.0, 1.0).</summary>
    double NextDouble();
}

public sealed class SystemRandomSource : IRandomSource
{
    public double NextDouble() => Random.Shared.NextDouble(); // thread-safe
}

/// <summary>A-06: simulated hub-to-address distance. Replace this one class for a real map API.</summary>
public interface IDistanceService
{
    decimal GetDistanceKm(int hubId, string district);
}

/// <summary>
/// Simulated distance: a random value in [MinKm, MaxKm] km rounded to 1 decimal place.
/// Callers must draw it once per checkout and keep it (see CheckoutDistanceService);
/// this class never calls System.Random directly.
/// </summary>
public sealed class DistanceService : IDistanceService
{
    public const decimal MinKm = 0.5m;
    public const decimal MaxKm = 10.0m; // equals PricingService.MaxDistanceKm (BR2)

    private readonly IRandomSource _random;

    public DistanceService(IRandomSource random) => _random = random;

    public decimal GetDistanceKm(int hubId, string district)
    {
        var fraction = Math.Clamp(_random.NextDouble(), 0.0, 1.0);
        var km = MinKm + (decimal)fraction * (MaxKm - MinKm);
        return Math.Clamp(Math.Round(km, 1, MidpointRounding.AwayFromZero), MinKm, MaxKm);
    }
}
