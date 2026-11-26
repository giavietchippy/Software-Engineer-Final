using Microsoft.Extensions.DependencyInjection;
using SaigonBite.Web.Services;

namespace SaigonBite.Web.Tests;

/// <summary>
/// N-06 review: the fixed clock is opt-in and never active in Production.
/// </summary>
public sealed class FixedClockTests
{
    [Fact]
    public void Without_environment_variable_the_real_clock_is_used()
    {
        using var harness = new WebTestHarness(); // clears SAIGONBITE_FIXED_CLOCK for this host
        var before = DateTimeOffset.UtcNow.AddSeconds(-2);

        var clock = harness.Factory.Services.GetRequiredService<IClock>();

        var after = DateTimeOffset.UtcNow.AddSeconds(2);
        Assert.IsType<SystemClock>(clock);
        Assert.InRange(clock.UtcNow, before, after);
    }

    [Fact]
    public void Development_uses_the_fixed_clock_when_the_variable_is_set()
    {
        using var harness = new WebTestHarness(new DateTimeOffset(2026, 11, 26, 15, 0, 0, TimeSpan.FromHours(7)));

        var clock = harness.Factory.Services.GetRequiredService<IClock>();

        var demo = Assert.IsType<DemoClock>(clock);
        Assert.Equal(new DateTimeOffset(2026, 11, 26, 8, 0, 0, TimeSpan.Zero), demo.UtcNow);
    }

    [Fact]
    public void Production_ignores_the_fixed_clock_variable()
    {
        using var harness = new WebTestHarness(
            new DateTimeOffset(2026, 11, 26, 15, 0, 0, TimeSpan.FromHours(7)),
            environment: "Production");

        Assert.IsType<SystemClock>(harness.Factory.Services.GetRequiredService<IClock>());
    }
}
