namespace SaigonBite.Core.Pricing;

public enum VoucherType { Percent, Fixed }

/// <summary>Voucher data needed by the pricing rules (BR9).</summary>
public sealed record VoucherInfo(
    string Code,
    VoucherType Type,
    decimal Value,            // percent (e.g. 10) or fixed amount in VND
    decimal? MaxDiscount,     // cap for PERCENT vouchers; null = no cap
    decimal MinSubtotal,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    bool IsActive);

/// <summary>
/// Everything the price depends on. The service never reads the clock, the database or the
/// web request: callers pass these values in (this is also the BR17 snapshot).
/// </summary>
/// <param name="Subtotal">S: sum of price x quantity, before any discount.</param>
/// <param name="HubLoad">Load as a fraction: 0.29 = 29 %. Use HubLoad.Compute.</param>
/// <param name="DistanceKm">Distance hub to address in km (decimal, not double).</param>
/// <param name="PlacedAt">Placement time in Vietnam local time (UTC+7).</param>
/// <param name="HeavyRain">Admin heavy-rain flag at placement.</param>
public sealed record PricingInput(
    decimal Subtotal,
    decimal HubLoad,
    decimal DistanceKm,
    DateTime PlacedAt,
    bool HeavyRain,
    VoucherInfo? Voucher = null);

public sealed record PriceBreakdown(
    decimal Subtotal,         // S
    decimal QuietDiscount,    // Q
    decimal VoucherDiscount,  // V
    decimal FoodTotal,        // F = S - Q - V
    decimal DeliveryFee,      // D
    decimal RushSurcharge,    // R
    decimal RainSurcharge,    // R2
    decimal RawTotal,
    decimal Total,            // rounded once, nearest 1,000
    string? VoucherNote);     // why a voucher was ignored (A-11), otherwise null

public enum RejectReason { None, BelowMinimum, OutOfCoverage, HubBusy }

public sealed record PricingResult(bool Accepted, RejectReason Reason, PriceBreakdown? Breakdown)
{
    public static PricingResult Rejected(RejectReason reason) => new(false, reason, null);
    public static PricingResult Ok(PriceBreakdown breakdown) => new(true, RejectReason.None, breakdown);
}
