using System.Globalization;
using SaigonBite.Core.Pricing;

namespace SaigonBite.Tests;

/// <summary>EP and BVA tests for BR1 to BR11. Test IDs match section 3.4.1 / 3.4.2 of the report.</summary>
public class PricingServiceTests
{
    private static readonly PricingService Svc = new();
    private static readonly DateOnly Today = new(2026, 11, 25);

    private static readonly VoucherInfo P10 = new("P10", VoucherType.Percent, 10m, 30_000m, 100_000m,
        new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), true);
    private static readonly VoucherInfo Fixed60 = new("F60", VoucherType.Fixed, 60_000m, null, 0m,
        new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), true);

    /// <summary>Base case: S 100,000, load 50 %, 1.0 km, 15:00, no rain, no voucher. Total 115,000.</summary>
    private static PricingInput Input(decimal s = 100_000m, decimal load = 0.50m, decimal km = 1.0m,
        string time = "15:00", bool rain = false, VoucherInfo? v = null)
    {
        var t = TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture);
        return new PricingInput(s, load, km, Today.ToDateTime(t), rain, v);
    }

    public static TheoryData<string, decimal, decimal, decimal, string, bool, decimal> AcceptedCases => new()
    {
        // id, S, load, km, time, rain, expected total
        { "T-02", 50_000m,  0.50m, 1.0m,  "15:00", false, 65_000m },   // BR1 boundary
        { "T-03", 100_000m, 0.50m, 1.0m,  "15:00", false, 115_000m },  // base case
        { "T-04", 100_000m, 0.50m, 8.0m,  "15:00", false, 151_000m },  // BR2/BR3 boundary 8.0 km
        { "T-06", 100_000m, 0.50m, 2.0m,  "15:00", false, 115_000m },  // BR3
        { "T-07", 100_000m, 0.50m, 2.01m, "15:00", false, 120_000m },
        { "T-08", 100_000m, 0.50m, 5.0m,  "15:00", false, 130_000m },
        { "T-09", 100_000m, 0.50m, 5.01m, "15:00", false, 137_000m },
        { "T-10", 100_000m, 0.50m, 3.2m,  "15:00", false, 125_000m },
        { "T-11", 100_000m, 0.50m, 1.0m,  "10:59", false, 115_000m },  // BR4
        { "T-12", 100_000m, 0.50m, 1.0m,  "11:00", false, 118_000m },
        { "T-13", 100_000m, 0.50m, 1.0m,  "12:59", false, 118_000m },
        { "T-14", 100_000m, 0.50m, 1.0m,  "13:00", false, 115_000m },
        { "T-15", 100_000m, 0.50m, 1.0m,  "17:29", false, 115_000m },
        { "T-16", 100_000m, 0.50m, 1.0m,  "17:30", false, 118_000m },
        { "T-17", 100_000m, 0.50m, 1.0m,  "19:29", false, 118_000m },
        { "T-18", 100_000m, 0.50m, 1.0m,  "19:30", false, 115_000m },
        { "T-19", 100_000m, 0.50m, 1.0m,  "15:00", true,  125_000m },  // BR5
        { "T-20", 299_999m, 0.50m, 1.0m,  "15:00", false, 315_000m },  // BR6 (raw 314,999)
        { "T-21", 300_000m, 0.50m, 1.0m,  "15:00", false, 300_000m },
        { "T-22", 300_000m, 0.50m, 1.0m,  "12:00", true,  310_000m },  // R2 still applies
        { "T-23", 100_000m, 0.29m, 1.0m,  "15:00", false, 105_000m },  // BR7
        { "T-24", 100_000m, 0.30m, 1.0m,  "15:00", false, 115_000m },
        { "T-25", 100_000m, 0.00m, 1.0m,  "15:00", false, 105_000m },
        { "T-26", 100_000m, 0.89m, 1.0m,  "15:00", false, 115_000m },  // BR8: 89 % still accepted
        { "T-39", 200_000m, 0.20m, 3.2m,  "15:00", false, 205_000m },  // worked example, section D
        { "T-41", 99_499m,  0.50m, 1.0m,  "15:00", false, 114_000m },  // BR11 (raw 114,499)
        { "T-42", 99_500m,  0.50m, 1.0m,  "15:00", false, 115_000m },  // BR11 (raw 114,500)
        { "T-55", 1_000_000m, 0.50m, 1.0m, "15:00", false, 1_000_000m },
        { "T-56", 1_000_499m, 0.50m, 1.0m, "15:00", false, 1_000_000m },
        { "T-57", 1_000_500m, 0.50m, 1.0m, "15:00", false, 1_001_000m },
    };

    [Theory]
    [MemberData(nameof(AcceptedCases))]
    public void Accepted_orders_have_the_expected_total(string id, decimal s, decimal load, decimal km, string time, bool rain, decimal expected)
    {
        var result = Svc.Calculate(Input(s, load, km, time, rain));
        Assert.True(result.Accepted, id);
        Assert.Equal(expected, result.Breakdown!.Total);
    }

    public static TheoryData<string, decimal, decimal, decimal, RejectReason> RejectedCases => new()
    {
        { "T-01", 49_999m,  0.50m, 1.0m,  RejectReason.BelowMinimum },   // BR1
        { "T-05", 100_000m, 0.50m, 8.01m, RejectReason.OutOfCoverage },  // BR2
        { "T-27", 100_000m, 0.90m, 1.0m,  RejectReason.HubBusy },        // BR8
        { "T-28", 100_000m, 1.00m, 1.0m,  RejectReason.HubBusy },
    };

    [Theory]
    [MemberData(nameof(RejectedCases))]
    public void Orders_breaking_a_rule_are_rejected(string id, decimal s, decimal load, decimal km, RejectReason expected)
    {
        var result = Svc.Calculate(Input(s, load, km));
        Assert.False(result.Accepted, id);
        Assert.Equal(expected, result.Reason);
        Assert.Null(result.Breakdown);
    }

    // ----- BR9 vouchers (facts, so each test shows its ID in Test Explorer) -----

    [Fact]
    public void T30_voucher_ignored_when_subtotal_below_minimum()
    {
        var b = Svc.Calculate(Input(99_999m, v: P10)).Breakdown!;
        Assert.Equal(0m, b.VoucherDiscount);
        Assert.NotNull(b.VoucherNote);
        Assert.Equal(115_000m, b.Total);   // raw 114,999 rounds up
    }

    [Fact]
    public void T31_percent_voucher_at_minimum_subtotal()
    {
        var b = Svc.Calculate(Input(100_000m, v: P10)).Breakdown!;
        Assert.Equal(10_000m, b.VoucherDiscount);
        Assert.Equal(105_000m, b.Total);
    }

    [Fact]
    public void T32_percent_voucher_exactly_at_cap()
    {
        var b = Svc.Calculate(Input(300_000m, v: P10)).Breakdown!;
        Assert.Equal(30_000m, b.VoucherDiscount);
        Assert.Equal(0m, b.DeliveryFee);
        Assert.Equal(270_000m, b.Total);
    }

    [Fact]
    public void T33_percent_voucher_above_cap_is_limited()
    {
        var b = Svc.Calculate(Input(310_000m, v: P10)).Breakdown!;
        Assert.Equal(30_000m, b.VoucherDiscount);   // 10 % would be 31,000
        Assert.Equal(280_000m, b.Total);
    }

    [Fact]
    public void T34_percent_voucher_is_taken_from_subtotal_after_quiet_kitchen_discount()
    {
        var b = Svc.Calculate(Input(100_000m, load: 0.20m, v: P10)).Breakdown!;
        Assert.Equal(10_000m, b.QuietDiscount);
        Assert.Equal(9_000m, b.VoucherDiscount);    // 10 % of (S - Q) = 10 % of 90,000
        Assert.Equal(96_000m, b.Total);
    }

    [Fact]
    public void T35_fixed_voucher_never_exceeds_subtotal_minus_quiet_discount()
    {
        var b = Svc.Calculate(Input(50_000m, v: Fixed60)).Breakdown!;
        Assert.Equal(50_000m, b.VoucherDiscount);
        Assert.Equal(0m, b.FoodTotal);
        Assert.Equal(15_000m, b.Total);              // only the delivery fee remains
    }

    [Fact]
    public void T36_expired_voucher_is_ignored()
    {
        var expired = P10 with { ValidTo = Today.AddDays(-1) };
        var b = Svc.Calculate(Input(100_000m, v: expired)).Breakdown!;
        Assert.Equal(0m, b.VoucherDiscount);
        Assert.NotNull(b.VoucherNote);
        Assert.Equal(115_000m, b.Total);
    }

    [Fact]
    public void T37_inactive_voucher_is_ignored()
    {
        var inactive = P10 with { IsActive = false };
        var b = Svc.Calculate(Input(100_000m, v: inactive)).Breakdown!;
        Assert.Equal(0m, b.VoucherDiscount);
        Assert.NotNull(b.VoucherNote);
    }

    // ----- BR10 order of calculation -----

    [Fact]
    public void T39_worked_example_breakdown()
    {
        var b = Svc.Calculate(Input(200_000m, load: 0.20m, km: 3.2m)).Breakdown!;
        Assert.Equal(20_000m, b.QuietDiscount);
        Assert.Equal(180_000m, b.FoodTotal);
        Assert.Equal(25_000m, b.DeliveryFee);
        Assert.Equal(0m, b.RushSurcharge);
        Assert.Equal(0m, b.RainSurcharge);
        Assert.Equal(205_000m, b.Total);
    }

    [Fact]
    public void T40_all_factors_together_follow_the_calculation_order()
    {
        var b = Svc.Calculate(Input(100_000m, load: 0.20m, km: 3.2m, time: "12:00", rain: true, v: P10)).Breakdown!;
        Assert.Equal(10_000m, b.QuietDiscount);
        Assert.Equal(9_000m, b.VoucherDiscount);
        Assert.Equal(25_000m, b.DeliveryFee);
        Assert.Equal(5_000m, b.RushSurcharge);       // 20 % of D
        Assert.Equal(10_000m, b.RainSurcharge);
        Assert.Equal(121_000m, b.Total);
    }
}
