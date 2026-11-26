using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Core.Payments;
using SaigonBite.Web.Data;

namespace SaigonBite.Web.Services;

public sealed record ShipperOrderDto(
    int OrderId, OrderStatus Status, string DeliveryAddress, decimal Total, DateTime AssignedAt);

/// <summary>
/// A-01/A-04/BR14: only an Admin moves assignments; a shipper sees only orders assigned to them.
/// The previous assignment is closed with UnassignedAt instead of being deleted.
/// </summary>
public sealed class ShipperAssignmentService
{
    public const int MaxOutForDelivery = 2; // A-04

    private enum Command { Assign, Reassign, Unassign }

    private readonly SaigonBiteDbContext _db;
    private readonly IClock _clock;

    public ShipperAssignmentService(SaigonBiteDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public Task<OperationResult> AssignAsync(int orderId, int shipperId, int adminUserId) =>
        ChangeAsync(orderId, shipperId, adminUserId, Command.Assign);

    public Task<OperationResult> ReassignAsync(int orderId, int shipperId, int adminUserId) =>
        ChangeAsync(orderId, shipperId, adminUserId, Command.Reassign);

    public Task<OperationResult> UnassignAsync(int orderId, int adminUserId) =>
        ChangeAsync(orderId, 0, adminUserId, Command.Unassign);

    /// <summary>A shipper can only list their own orders; role comes from the database.</summary>
    public async Task<IReadOnlyList<ShipperOrderDto>> ListOwnOrdersAsync(int shipperUserId)
    {
        var shipper = await _db.Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == shipperUserId && u.IsActive && u.Role == Role.Shipper);
        if (shipper is null) return Array.Empty<ShipperOrderDto>();

        return await (from a in _db.ShipperAssignments.AsNoTracking()
                      join o in _db.Orders.AsNoTracking() on a.OrderId equals o.Id
                      where a.ShipperId == shipperUserId && a.UnassignedAt == null && o.AssignedShipperId == shipperUserId
                      orderby a.AssignedAt
                      select new ShipperOrderDto(o.Id, o.Status, o.DeliveryAddress, o.Total, a.AssignedAt))
            .ToListAsync();
    }

    /// <summary>BR14: the assigned shipper confirms COD cash while Out for Delivery; then Delivered is allowed.</summary>
    public async Task<OperationResult> ConfirmCashAsync(int orderId, int shipperUserId)
    {
        var shipper = await _db.Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == shipperUserId && u.IsActive && u.Role == Role.Shipper);
        if (shipper is null) return OperationResult.Fail("Only an active shipper can confirm cash.");

        var order = await _db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId);
        if (order is null) return OperationResult.Fail("Order not found.");
        if (order.AssignedShipperId != shipperUserId)
            return OperationResult.Fail("This order is not assigned to this shipper.");
        if (order.Status != OrderStatus.OutForDelivery)
            return OperationResult.Fail("Cash can only be confirmed while Out for Delivery (BR14).");

        var payment = await _db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == orderId);
        if (payment is null || payment.Method != PaymentMethod.Cod)
            return OperationResult.Fail("Only a COD order has cash to confirm (BR14).");
        if (payment.CashConfirmed && payment.Status == PaymentStatus.Paid)
            return OperationResult.Ok(); // idempotent, no write

        var now = VietnamTime.FromUtc(_clock.UtcNow);
        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var trackedPayment = await _db.Payments.SingleAsync(p => p.Id == payment.Id);
            trackedPayment.CashConfirmed = true;
            trackedPayment.Status = PaymentStatus.Paid;

            var trackedOrder = await _db.Orders.SingleAsync(o => o.Id == orderId);
            trackedOrder.CashConfirmed = true; // mirror used by OrderStateMachine's context

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            return OperationResult.Ok();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task<OperationResult> ChangeAsync(int orderId, int shipperId, int adminUserId, Command command)
    {
        var admin = await _db.Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == adminUserId && u.IsActive && u.Role == Role.Admin);
        if (admin is null) return OperationResult.Fail("Only an active Admin can change assignments.");

        var order = await _db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId);
        if (order is null) return OperationResult.Fail("Order not found.");

        User? shipper = null;
        if (command != Command.Unassign)
        {
            shipper = await _db.Users.AsNoTracking()
                .SingleOrDefaultAsync(u => u.Id == shipperId && u.IsActive && u.Role == Role.Shipper);
            if (shipper is null) return OperationResult.Fail("Shipper not found or inactive.");
        }

        var current = await _db.ShipperAssignments.AsNoTracking()
            .SingleOrDefaultAsync(a => a.OrderId == orderId && a.UnassignedAt == null);

        switch (command)
        {
            case Command.Assign:
                if (order.Status != OrderStatus.Ready)
                    return OperationResult.Fail("A shipper can only be assigned to a Ready order.");
                if (current is not null)
                    return OperationResult.Fail("This order already has an active assignment.");
                break;
            case Command.Reassign:
                if (!IsBeforeOutForDelivery(order.Status))
                    return OperationResult.Fail("Reassignment is not allowed once the order is Out for Delivery.");
                if (current is null)
                    return OperationResult.Fail("This order has no active assignment to reassign.");
                if (current.ShipperId == shipperId)
                    return OperationResult.Fail("The shipper already owns this assignment.");
                break;
            case Command.Unassign:
                if (!IsBeforeOutForDelivery(order.Status))
                    return OperationResult.Fail("Unassignment is not allowed once the order is Out for Delivery.");
                if (current is null)
                    return OperationResult.Fail("This order has no active assignment to unassign.");
                break;
        }

        if ((command is Command.Assign or Command.Reassign) && shipper is not null)
        {
            // A-04: counted at assignment time, only for orders currently Out for Delivery.
            var outForDelivery = await (from a in _db.ShipperAssignments.AsNoTracking()
                                        join o in _db.Orders.AsNoTracking() on a.OrderId equals o.Id
                                        where a.ShipperId == shipperId && a.UnassignedAt == null
                                              && o.Status == OrderStatus.OutForDelivery
                                        select a.Id).CountAsync();
            if (outForDelivery >= MaxOutForDelivery)
                return OperationResult.Fail($"This shipper already has {MaxOutForDelivery} orders Out for Delivery (A-04).");
        }

        var now = VietnamTime.FromUtc(_clock.UtcNow);
        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var trackedOrder = await _db.Orders.SingleAsync(o => o.Id == orderId);
            if (command == Command.Assign)
            {
                _db.ShipperAssignments.Add(new ShipperAssignment
                {
                    OrderId = orderId, ShipperId = shipperId, AssignedAt = now, UnassignedAt = null
                });
                trackedOrder.AssignedShipperId = shipperId;
            }
            else
            {
                var trackedCurrent = await _db.ShipperAssignments.SingleAsync(a => a.Id == current!.Id);
                trackedCurrent.UnassignedAt = now; // keep the old assignment as history (A-01)
                if (command == Command.Reassign)
                {
                    _db.ShipperAssignments.Add(new ShipperAssignment
                    {
                        OrderId = orderId, ShipperId = shipperId, AssignedAt = now, UnassignedAt = null
                    });
                    trackedOrder.AssignedShipperId = shipperId;
                }
                else
                {
                    trackedOrder.AssignedShipperId = null;
                }
            }

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            return OperationResult.Ok();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static bool IsBeforeOutForDelivery(OrderStatus status) =>
        status is OrderStatus.Placed or OrderStatus.Confirmed or OrderStatus.Preparing or OrderStatus.Ready;
}
