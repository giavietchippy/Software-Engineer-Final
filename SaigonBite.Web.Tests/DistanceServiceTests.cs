using SaigonBite.Core.Pricing;
using SaigonBite.Web.Services;

namespace SaigonBite.Web.Tests;

/// <summary>A-06: DistanceService uses the injected random source and stays inside 0.5 to 10.0 km.</summary>
public sealed class DistanceServiceTests
{
    [Theory]
    [InlineData(0.0, 0.5)]
    [InlineData(0.5, 5.3)]        // 0.5 + 0.5 * 9.5 = 5.25, rounded away from zero
    [InlineData(0.999999, 10.0)]
    public void Km_comes_from_the_injected_random_source(double fraction, double expectedKm)
    {
        var service = new DistanceService(new FixedRandomSource(fraction));
        Assert.Equal((decimal)expectedKm, service.GetDistanceKm(1, "District 1"));
    }

    [Fact]
    public void Km_is_always_in_0_5_to_10_with_one_decimal_for_many_random_values()
    {
        var rng = new Random(12345);
        var service = new DistanceService(new SystemRandomSourceForTest(rng));
        for (var i = 0; i < 5_000; i++)
        {
            var km = service.GetDistanceKm(1, "District 1");
            Assert.InRange(km, 0.5m, 10.0m);
            Assert.True(km > 0m);
            Assert.Equal(km, Math.Round(km, 1));
            Assert.True(km <= PricingService.MaxDistanceKm); // every generated km is deliverable
        }
    }

    [Fact]
    public void Out_of_range_random_values_are_clamped()
    {
        Assert.Equal(0.5m, new DistanceService(new FixedRandomSource(-3.0)).GetDistanceKm(1, "x"));
        Assert.Equal(10.0m, new DistanceService(new FixedRandomSource(7.0)).GetDistanceKm(1, "x"));
    }

    [Fact]
    public void Ticket_keeps_the_same_km_until_it_is_replaced_or_tampered_with()
    {
        var clock = new StaticClock(new DateTimeOffset(2026, 11, 26, 8, 0, 0, TimeSpan.Zero));
        var tickets = new CheckoutDistanceService(
            FixedRandomSource.DistanceOf(6.4m),
            new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider(), clock);

        var first = tickets.Resolve(7, 1, "District 1", null);
        Assert.False(first.IsReused);
        Assert.Equal(6.4m, first.Km);

        var again = tickets.Resolve(7, 1, "District 2", first.Ticket); // Preview, then Place
        Assert.True(again.IsReused);
        Assert.Equal(6.4m, again.Km);

        Assert.False(tickets.Resolve(8, 1, "District 1", first.Ticket).IsReused);  // another user
        Assert.False(tickets.Resolve(7, 2, "District 1", first.Ticket).IsReused);  // another hub
        Assert.False(tickets.Resolve(7, 1, "District 1", first.Ticket + "x").IsReused); // tampered
        Assert.False(tickets.Resolve(7, 1, "District 1", "0.5").IsReused);        // raw km is not a ticket
    }

    private sealed class StaticClock : IClock
    {
        public StaticClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class SystemRandomSourceForTest : IRandomSource
    {
        private readonly Random _random;
        public SystemRandomSourceForTest(Random random) => _random = random;
        public double NextDouble() => _random.NextDouble();
    }
}
