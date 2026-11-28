using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Core.Payments;
using SaigonBite.Web.Data;

namespace SaigonBite.Web.Services;

public sealed record StaffOrderLineDto(string ItemName, string Category, int Quantity);

public sealed record StaffOrderDto(
    int Id, OrderStatus Status, int HubId, string HubName, DateTime PlacedAt, decimal Total,
    string DeliveryAddress, string HotelName, string RoomNumber,
    PaymentMethod? Method, PaymentStatus? PayStatus, bool CashConfirmed,
    int? ShipperId, string? ShipperName,
    IReadOnlyList<StaffOrderLineDto> Lines)
{
    public bool IsCod => Method == PaymentMethod.Cod;
}

public sealed record ShipperOptionDto(int Id, string Identifier, int OutForDelivery);

public sealed record HubOptionDto(int Id, string Name);

/// <summary>
/// Read-only lists for the kitchen, shipper and admin order screens. No rule lives here:
/// every change still goes through OrderStatusService / ShipperAssignmentService.
/// </summary>
public sealed class StaffOrderReadService
{
    private readonly SaigonBiteDbContext _db;

    public StaffOrderReadService(SaigonBiteDbContext db) => _db = db;

    public async Task<int?> GetHubIdAsync(int userId) =>
        await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive)
            .Select(u => u.HubId)
            .SingleOrDefaultAsync();

    public async Task<string?> GetHubNameAsync(int hubId) =>
        await _db.Hubs.IgnoreQueryFilters().AsNoTracking()
            .Where(h => h.Id == hubId)
            .Select(h => h.Name)
            .SingleOrDefaultAsync();

    /// <summary>Orders of one hub that the kitchen still has to act on or hand over, oldest first.</summary>
    public async Task<IReadOnlyList<StaffOrderDto>> GetKitchenOrdersAsync(int hubId)
    {
        var statuses = new[] { OrderStatus.Placed, OrderStatus.Confirmed, OrderStatus.Preparing, OrderStatus.Ready };
        var rows = await Base()
            .Where(o => o.HubId == hubId && statuses.Contains(o.Status))
            .OrderBy(o => o.PlacedAt)
            .ToListAsync();
        return rows.Select(Map).ToList();
    }

    /// <summary>Orders currently assigned to one shipper, newest first.</summary>
    public async Task<IReadOnlyList<StaffOrderDto>> GetShipperOrdersAsync(int shipperId)
    {
        var rows = await Base()
            .Where(o => o.AssignedShipperId == shipperId)
            .OrderByDescending(o => o.PlacedAt)
            .Take(50)
            .ToListAsync();
        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<StaffOrderDto>> GetAdminOrdersAsync(OrderStatus? status, int? hubId)
    {
        var query = Base();
        if (status is { } s) query = query.Where(o => o.Status == s);
        if (hubId is { } h) query = query.Where(o => o.HubId == h);
        var rows = await query.OrderByDescending(o => o.PlacedAt).Take(200).ToListAsync();
        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<ShipperOptionDto>> GetShippersAsync()
    {
        var shippers = await _db.Users.AsNoTracking()
            .Where(u => u.Role == Role.Shipper && u.IsActive)
            .OrderBy(u => u.Identifier)
            .Select(u => new { u.Id, u.Identifier })
            .ToListAsync();
        var outForDelivery = await _db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.OutForDelivery && o.AssignedShipperId != null)
            .Select(o => o.AssignedShipperId)
            .ToListAsync();
        return shippers
            .Select(s => new ShipperOptionDto(s.Id, s.Identifier, outForDelivery.Count(x => x == s.Id)))
            .ToList();
    }

    public async Task<IReadOnlyList<HubOptionDto>> GetHubsAsync() =>
        await _db.Hubs.AsNoTracking()
            .OrderBy(h => h.Name)
            .Select(h => new HubOptionDto(h.Id, h.Name))
            .ToListAsync();

    // A-09: soft-deleted menu items, hubs and users must not hide old orders.
    private IQueryable<Order> Base() =>
        _db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Include(o => o.Hub)
            .Include(o => o.Payments)
            .Include(o => o.AssignedShipper)
            .Include(o => o.Lines).ThenInclude(l => l.MenuItem)
            .AsSplitQuery();

    private static StaffOrderDto Map(Order o)
    {
        var payment = o.Payments.OrderBy(p => p.Id).FirstOrDefault();
        return new StaffOrderDto(
            o.Id, o.Status, o.HubId, o.Hub.Name, o.PlacedAt, o.Total,
            o.DeliveryAddress, o.HotelName, o.RoomNumber,
            payment?.Method, payment?.Status, payment?.CashConfirmed ?? o.CashConfirmed,
            o.AssignedShipperId, o.AssignedShipper?.Identifier,
            o.Lines.Select(l => new StaffOrderLineDto(l.MenuItem.Name, l.MenuItem.Category, l.Quantity)).ToList());
    }
}
