using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Web.Data;

namespace SaigonBite.Web.Services;

/// <summary>
/// A-09: order detail and history must survive soft-deletes, so these reads ignore global filters.
/// </summary>
public sealed class OrderReadService
{
    private readonly SaigonBiteDbContext _db;
    public OrderReadService(SaigonBiteDbContext db) => _db = db;

    public async Task<OrderDetailDto> GetDetailAsync(int orderId)
    {
        var order = await _db.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(o => o.Lines).ThenInclude(l => l.MenuItem)
            .SingleAsync(o => o.Id == orderId);

        return new OrderDetailDto(
            order.Id, order.CustomerId, order.HubId, order.Status, order.Total, order.PlacedAt,
            order.DeliveryAddress, order.HotelName, order.RoomNumber,
            order.SnapshotDistanceKm, order.SnapshotHubLoad, order.SnapshotHeavyRain,
            order.VoucherId, order.VoucherNote,
            order.Lines.Select(l => new OrderLineDto(
                l.MenuItemId, l.MenuItem.Name, l.MenuItem.Category, l.Quantity, l.UnitPrice)).ToList());
    }

    /// <summary>FR-C5: only the signed-in customer's orders, newest first.</summary>
    public async Task<IReadOnlyList<OrderListItemDto>> GetCustomerOrdersAsync(int customerId)
    {
        return await _db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.PlacedAt)
            .Select(o => new OrderListItemDto(
                o.Id, o.Status, o.PlacedAt, o.Total, o.Lines.Count, o.DeliveryAddress, o.Hub.Name))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<OrderHistoryDto>> GetHistoryAsync(int orderId)
    {
        var rows = await _db.OrderStatusHistories
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(h => h.Actor)
            .Where(h => h.OrderId == orderId)
            .OrderBy(h => h.ChangedAt)
            .ToListAsync();

        return rows.Select(h => new OrderHistoryDto(
            h.FromStatus, h.ToStatus, h.ChangedAt, h.Actor.Identifier, h.Reason)).ToList();
    }
}

public sealed record OrderDetailDto(
    int Id,
    int CustomerId,
    int HubId,
    OrderStatus Status,
    decimal Total,
    DateTime PlacedAt,
    string DeliveryAddress,
    string HotelName,
    string RoomNumber,
    decimal DistanceKm,
    decimal HubLoad,
    bool HeavyRain,
    int? VoucherId,
    string? VoucherNote,
    IReadOnlyList<OrderLineDto> Lines);

public sealed record OrderLineDto(
    int MenuItemId, string ItemName, string Category, int Quantity, decimal UnitPrice);

public sealed record OrderListItemDto(
    int Id, OrderStatus Status, DateTime PlacedAt, decimal Total, int LineCount,
    string DeliveryAddress, string HubName);

public sealed record OrderHistoryDto(
    OrderStatus? FromStatus,
    OrderStatus ToStatus,
    DateTime ChangedAt,
    string ActorIdentifier,
    string? Reason);
