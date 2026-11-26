using SaigonBite.Core;
using SaigonBite.Core.Payments;
using SaigonBite.Core.Pricing;
using SaigonBite.Web.Services;

namespace SaigonBite.Web.Models;

// FR-C1..C5: plain view models. No rule evaluation here; services own every decision.
public sealed class CartLine
{
    public int MenuItemId { get; set; }
    public int Quantity { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
}

public sealed class CartInput
{
    public int MenuItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class HubsViewModel
{
    public IReadOnlyList<HubCard> Hubs { get; set; } = Array.Empty<HubCard>();
}

public sealed class MenuViewModel
{
    public int HubId { get; set; }
    public string HubName { get; set; } = string.Empty;
    public IReadOnlyList<MenuBrand> Brands { get; set; } = Array.Empty<MenuBrand>();
    public string? Error { get; set; }
}

public sealed class CheckoutViewModel
{
    public int HubId { get; set; }
    public List<CartLine> Lines { get; set; } = new();
    public IReadOnlyList<DistrictOption> Districts { get; set; } = Array.Empty<DistrictOption>();
    public string District { get; set; } = "District 1";
    public string DeliveryAddress { get; set; } = string.Empty;
    public string HotelName { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public string? VoucherCode { get; set; }
    public PriceBreakdown? Breakdown { get; set; }
    public RejectReason? RejectReason { get; set; }
    public IReadOnlyList<string> LineErrors { get; set; } = Array.Empty<string>();
    public IReadOnlyList<PaymentMethod> PaymentMethods { get; set; } = Array.Empty<PaymentMethod>();
    public bool HasPaymentMethods => PaymentMethods.Count > 0;
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cod;
    public bool IsTourist { get; set; }
    public string? Error { get; set; }
    public string? Notice { get; set; }
}

public sealed class TrackViewModel
{
    public OrderDetailDto Detail { get; set; } = null!;
    public IReadOnlyList<OrderHistoryDto> History { get; set; } = Array.Empty<OrderHistoryDto>();
    public CustomerOrderActions Actions { get; set; } = new(false, false);
    public string? Error { get; set; }
    public string? Notice { get; set; }
}

public sealed class EditItemsViewModel
{
    public int OrderId { get; set; }
    public List<CartLine> Lines { get; set; } = new();
    public string? Error { get; set; }
    public string? Notice { get; set; }
}

public sealed class MyOrdersViewModel
{
    public IReadOnlyList<OrderListItemDto> Orders { get; set; } = Array.Empty<OrderListItemDto>();
}
