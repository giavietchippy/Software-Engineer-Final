using SaigonBite.Core.Pricing;

namespace SaigonBite.Web.Services;

/// <summary>Common no-throw result for service-layer commands that can be rejected by a business rule.</summary>
public sealed record OperationResult(bool Accepted, string? Error)
{
    public static OperationResult Ok() => new(true, null);
    public static OperationResult Fail(string error) => new(false, error);
}

/// <summary>BR17 edit result: the recalculated price is returned only when the edit was saved.</summary>
public sealed record EditResult(bool Accepted, string? Error, PriceBreakdown? Price)
{
    public static EditResult Ok(PriceBreakdown price) => new(true, null, price);
    public static EditResult Fail(string error) => new(false, error, null);
}
