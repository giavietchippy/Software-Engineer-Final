using SaigonBite.Core.Pricing;

namespace SaigonBite.Web.Services;

/// <summary>Serializable snapshot used between redirect and return for live gateways.</summary>
public sealed class PendingCheckoutPayload
{
    public PlaceOrderInput Input { get; set; } = null!;
    public int CustomerId { get; set; }
    public int HubId { get; set; }
    public DateTime PlacedAt { get; set; }
    public decimal SnapshotHubLoad { get; set; }
    public bool SnapshotHeavyRain { get; set; }
    public List<PendingCheckoutLine> Lines { get; set; } = new();
    public PriceBreakdown Price { get; set; } = null!;
    public int? VoucherId { get; set; }
    public string? VoucherNote { get; set; }
}

public sealed class PendingCheckoutLine
{
    public int MenuItemId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}