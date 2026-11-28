namespace SaigonBite.Core.Pricing;

/// <summary>
/// All pricing rules live here (BR1 to BR11). No UI, database or HTTP code.
/// Calculation order is BR10: S, Q, V, F, D (+waiver), R, R2, raw total, round once.
/// Used for both the checkout preview and the final price at placement (BR17).
/// </summary>
public sealed class PricingService
{
    public const decimal MinimumSubtotal = 50_000m;        // BR1
    public const decimal MaxDistanceKm = 10.0m;            // BR2: delivery radius is 10 km
    public const decimal FreeDeliveryThreshold = 300_000m; // BR6
    public const decimal QuietKitchenBelow = 0.30m;        // BR7: below 30 %
    public const decimal QuietKitchenRate = 0.10m;         // BR7
    public const decimal HubProtectionAtOrAbove = 0.90m;   // BR8: 90 % or above
    public const decimal RushRate = 0.20m;                 // BR4
    public const decimal HeavyRainFee = 10_000m;           // BR5

    public PricingResult Calculate(PricingInput input)
    {
        if (input.Subtotal < MinimumSubtotal)           // BR1
            return PricingResult.Rejected(RejectReason.BelowMinimum);
        if (input.DistanceKm > MaxDistanceKm)           // BR2
            return PricingResult.Rejected(RejectReason.OutOfCoverage);
        if (input.HubLoad >= HubProtectionAtOrAbove)    // BR8 (the new order is not counted)
            return PricingResult.Rejected(RejectReason.HubBusy);

        var s = input.Subtotal;                                                   // (1)
        var q = input.HubLoad < QuietKitchenBelow ? s * QuietKitchenRate : 0m;    // (2) BR7
        var v = CalculateVoucher(input.Voucher, s, q,
                    DateOnly.FromDateTime(input.PlacedAt), out var note);         // (3) BR9
        var f = s - q - v;                                                        // (4)

        var d = DeliveryFee(input.DistanceKm);                                    // (5) BR3
        var r = 0m;
        if (s >= FreeDeliveryThreshold)                                           // BR6
            d = 0m;                                                               // D and R waived
        else if (IsRushHour(input.PlacedAt))                                      // (6) BR4, only if D not waived
            r = d * RushRate;

        var r2 = input.HeavyRain ? HeavyRainFee : 0m;                             // (7) BR5 (still applies when D is waived)
        var raw = f + d + r + r2;                                                 // (8)
        var total = RoundToThousand(raw);                                         // (9) BR11, once, at the end

        return PricingResult.Ok(new PriceBreakdown(s, q, v, f, d, r, r2, raw, total, note));
    }

    /// <summary>
    /// BR3 (coverage up to MaxDistanceKm = 10 km):
    /// 0-2 km: 15,000; 2-5 km: 15,000 + 5,000 per started km after 2 km (max 30,000);
    /// 5-10 km: 30,000 + 7,000 per started km after 5 km. The 8-10 km band keeps the same
    /// 7,000 per started km, so 8.0 km = 51,000, 9.0 km = 58,000, 10.0 km = 65,000.
    /// Distances up to 8 km are priced exactly as before.
    /// </summary>
    public static decimal DeliveryFee(decimal km)
    {
        if (km <= 2.0m) return 15_000m;
        if (km <= 5.0m) return 15_000m + 5_000m * Math.Ceiling(km - 2.0m);
        return 30_000m + 7_000m * Math.Ceiling(km - 5.0m); // also covers 8-10 km
    }

    /// <summary>BR4: [11:00, 13:00) and [17:30, 19:30). Start inclusive, end exclusive.</summary>
    public static bool IsRushHour(DateTime placedAt)
    {
        var t = TimeOnly.FromDateTime(placedAt);
        return InWindow(t, new TimeOnly(11, 0), new TimeOnly(13, 0))
            || InWindow(t, new TimeOnly(17, 30), new TimeOnly(19, 30));
    }

    /// <summary>BR11: nearest 1,000, half rounds up.</summary>
    public static decimal RoundToThousand(decimal raw) =>
        Math.Round(raw / 1000m, 0, MidpointRounding.AwayFromZero) * 1000m;

    private static bool InWindow(TimeOnly t, TimeOnly start, TimeOnly end) => t >= start && t < end;

    /// <summary>BR9. An unusable voucher gives V = 0 and a note; the order continues (A-11).</summary>
    private static decimal CalculateVoucher(VoucherInfo? voucher, decimal s, decimal q, DateOnly today, out string? note)
    {
        note = null;
        if (voucher is null) return 0m;
        if (!voucher.IsActive) { note = "Voucher is not active."; return 0m; }
        if (today < voucher.ValidFrom || today > voucher.ValidTo) { note = "Voucher is outside its validity dates."; return 0m; }
        if (s < voucher.MinSubtotal) { note = "Subtotal is below the voucher's minimum."; return 0m; } // checked against S

        var basis = s - q;
        if (voucher.Type == VoucherType.Percent)
        {
            var amount = basis * voucher.Value / 100m;                 // p % of (S - Q)
            return voucher.MaxDiscount is { } cap ? Math.Min(amount, cap) : amount;
        }
        return Math.Min(voucher.Value, basis);                          // FIXED: never more than (S - Q)
    }
}
