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
            order.Id, order.Status, order.Total, order.PlacedAt,
            order.DeliveryAddress, order.HotelName, order.RoomNumber,
            order.SnapshotDistanceKm, order.SnapshotHubLoad, order.SnapshotHeavyRain,
            order.VoucherId, order.VoucherNote,
            order.Lines.Select(l => new OrderLineDto(
                l.MenuItem.Name, l.MenuItem.Category, l.Quantity, l.UnitPrice)).ToList());
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

public sealed record OrderLineDto(string ItemName, string Category, int Quantity, decimal UnitPrice);

public sealed record OrderHistoryDto(
    OrderStatus FromStatus,
    OrderStatus ToStatus,
    DateTime ChangedAt,
    string ActorIdentifier,
    string? Reason);
