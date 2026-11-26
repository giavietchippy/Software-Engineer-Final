namespace SaigonBite.Web.Services;

/// <summary>A-06: simulated hub-to-district distance. Replace this one class for a real map API.</summary>
public interface IDistanceService
{
    decimal GetDistanceKm(int hubId, string district);
}

public sealed class DistanceService : IDistanceService
{
    private const decimal OutOfCoverageFallback = 9.0m; // > BR2's 8 km limit

    // Deterministic demo lookup. District names are case-insensitive.
    private static readonly Dictionary<int, Dictionary<string, decimal>> Lookup = new()
    {
        [1] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["District 1"] = 3.2m, ["District 2"] = 5.0m, ["Binh Thanh"] = 1.5m,
            ["Thu Duc"] = 7.0m, ["District 7"] = 10.5m
        },
        [2] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Binh Thanh"] = 1.0m, ["District 1"] = 2.5m, ["Thu Duc"] = 4.0m,
            ["District 7"] = 9.2m
        },
        [3] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Thu Duc"] = 1.2m, ["District 1"] = 6.8m, ["District 7"] = 8.5m
        }
    };

    public decimal GetDistanceKm(int hubId, string district) =>
        Lookup.TryGetValue(hubId, out var byDistrict)
        && byDistrict.TryGetValue(district ?? string.Empty, out var km)
            ? km
            : OutOfCoverageFallback;
}
