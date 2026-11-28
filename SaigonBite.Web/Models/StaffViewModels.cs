using SaigonBite.Core;
using SaigonBite.Web.Services;

namespace SaigonBite.Web.Models;

public sealed class KitchenViewModel
{
    public bool HasHub { get; set; }
    public string HubName { get; set; } = string.Empty;
    public IReadOnlyList<StaffOrderDto> Orders { get; set; } = Array.Empty<StaffOrderDto>();
}

public sealed class ShipperViewModel
{
    public IReadOnlyList<StaffOrderDto> Orders { get; set; } = Array.Empty<StaffOrderDto>();
    public int OutForDeliveryLimit { get; set; }
}

public sealed class AdminOrdersViewModel
{
    public IReadOnlyList<StaffOrderDto> Orders { get; set; } = Array.Empty<StaffOrderDto>();
    public IReadOnlyList<ShipperOptionDto> Shippers { get; set; } = Array.Empty<ShipperOptionDto>();
    public IReadOnlyList<HubOptionDto> Hubs { get; set; } = Array.Empty<HubOptionDto>();
    public OrderStatus? Status { get; set; }
    public int? HubId { get; set; }
}

public sealed class AdminSettingsViewModel
{
    public bool HeavyRain { get; set; }
}
