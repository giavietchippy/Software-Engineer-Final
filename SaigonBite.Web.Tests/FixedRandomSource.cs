using SaigonBite.Web.Services;

namespace SaigonBite.Web.Tests;

/// <summary>Test double for IRandomSource: always returns the same fraction in [0, 1).</summary>
internal sealed class FixedRandomSource : IRandomSource
{
    private readonly double _value;

    public FixedRandomSource(double value) => _value = value;

    public double NextDouble() => _value;

    /// <summary>A source whose DistanceService result is exactly <paramref name="km"/> (0.5 to 10.0).</summary>
    public static FixedRandomSource ForKm(decimal km) =>
        new((double)((km - DistanceService.MinKm) / (DistanceService.MaxKm - DistanceService.MinKm)));

    public static DistanceService DistanceOf(decimal km) => new(ForKm(km));
}
