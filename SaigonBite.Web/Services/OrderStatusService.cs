using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Core.Orders;
using SaigonBite.Core.Payments;
using SaigonBite.Core.Pricing;
using SaigonBite.Web.Data;

namespace SaigonBite.Web.Services;

/// <summary>
/// BR14-BR17, A-02/A-12/A-15: every status change goes through Core.OrderStateMachine and writes
/// one history row in the same transaction. Actor role/hub and payment facts come from the database.
/// </summary>
public sealed class OrderStatusService
{
    private enum RefundMode { None, FullIfOnline, FoodOnlyIfOnline }

    private readonly SaigonBiteDbContext _db;
    private readonly PricingService _pricing;
    private readonly PaymentContext _payments;
    private readonly IClock _clock;

    public OrderStatusService(SaigonBiteDbContext db, PricingService pricing, PaymentContext payments, IClock clock)
    {
        _db = db;
        _pricing = pricing;
        _payments = payments;
        _clock = clock;
    }

    /// <summary>BR15: the only write path for Order.Status plus its history row.</summary>
    public Task<OperationResult> ChangeStatusAsync(int orderId, OrderStatus newStatus, int actorUserId,
        string? reason = null) => TransitionAsync(orderId, newStatus, actorUserId, reason, RefundMode.None);

    /// <summary>BR16: customer (Placed/Confirmed), owning-hub kitchen (Placed + reason), Admin (Confirmed).</summary>
    public Task<OperationResult> CancelAsync(int orderId, int actorUserId, string? reason = null) =>
        TransitionAsync(orderId, OrderStatus.Cancelled, actorUserId, reason, RefundMode.FullIfOnline);

    /// <summary>A-02: failed delivery refunds FoodTotal only; delivery fee, surcharges and rounding are kept.</summary>
    public Task<OperationResult> FailDeliveryAsync(int orderId, int shipperUserId, string reason) =>
        TransitionAsync(orderId, OrderStatus.Failed, shipperUserId, reason, RefundMode.FoodOnlyIfOnline);

    /// <summary>
    /// BR17: while Placed only the owning customer may edit. Pricing is recalculated from the order's
    /// own snapshot, never from current hub load, rain flag or clock.
    /// </summary>
    public async Task<EditResult> EditItemsAsync(int orderId, int actorUserId,
        IReadOnlyList<OrderLineInput> lines)
    {
        var actor = await _db.Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == actorUserId && u.IsActive);
        if (actor is null || !actor.Role.IsCustomer())
            return EditResult.Fail("Only a customer can edit order items.");
        if (lines is null) return EditResult.Fail("Order lines are required (BR12).");

        var order = await _db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId);
        if (order is null) return EditResult.Fail("Order not found.");
        if (order.CustomerId != actorUserId) return EditResult.Fail("Only the owning customer can edit this order.");
        if (order.Status != OrderStatus.Placed)
            return EditResult.Fail("Items can only be edited while the order is Placed (BR17).");

        var itemIds = lines.Select(l => l.MenuItemId).Distinct().ToList();
        var items = await _db.MenuItems.IgnoreQueryFilters().AsNoTracking()
            .Where(i => itemIds.Contains(i.Id)).ToListAsync();
        var hubBrands = (await _db.HubBrands.AsNoTracking()
            .Where(hb => hb.HubId == order.HubId).Select(hb => hb.BrandId).ToListAsync()).ToHashSet();

        var pricedLines = new List<(MenuItem Item, int Quantity)>();
        var lineInfos = new List<OrderLineInfo>();
        foreach (var line in lines)
        {
            var item = items.SingleOrDefault(i => i.Id == line.MenuItemId);
            lineInfos.Add(new OrderLineInfo(
                line.Quantity,
                item is not null && item.IsAvailable && !item.IsDeleted,
                item is not null && hubBrands.Contains(item.BrandId))); // BR12
            if (item is not null) pricedLines.Add((item, line.Quantity));
        }
        var errors = OrderLineValidator.Validate(lineInfos);
        if (errors.Count > 0) return EditResult.Fail(string.Join(" ", errors));

        VoucherInfo? voucher = null;
        if (order.VoucherId is { } voucherId)
        {
            var stored = await _db.Vouchers.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(v => v.Id == voucherId);
            if (stored is not null)
                voucher = new VoucherInfo(stored.Code, stored.Type, stored.Value, stored.MaxDiscount,
                    stored.MinSubtotal, stored.ValidFrom, stored.ValidTo, stored.IsActive);
        }

        // BR17: these four values are exactly the placement snapshot - no current-condition lookups.
        var pricing = _pricing.Calculate(new PricingInput(
            pricedLines.Sum(x => x.Item.Price * x.Quantity),
            order.SnapshotHubLoad, order.SnapshotDistanceKm, order.PlacedAt, order.SnapshotHeavyRain,
            voucher));
        if (!pricing.Accepted) return EditResult.Fail(pricing.Reason.ToString());
        var price = pricing.Breakdown!;

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var tracked = await _db.Orders.Include(o => o.Lines).SingleAsync(o => o.Id == orderId);
            _db.OrderLines.RemoveRange(tracked.Lines);
            foreach (var (item, quantity) in pricedLines)
            {
                _db.OrderLines.Add(new OrderLine
                {
                    OrderId = orderId,
                    MenuItemId = item.Id,
                    Quantity = quantity,
                    UnitPrice = item.Price
                });
            }

            tracked.Subtotal = price.Subtotal;
            tracked.QuietDiscount = price.QuietDiscount;
            tracked.VoucherDiscount = price.VoucherDiscount;
            tracked.FoodTotal = price.FoodTotal;
            tracked.DeliveryFee = price.DeliveryFee;
            tracked.RushSurcharge = price.RushSurcharge;
            tracked.RainSurcharge = price.RainSurcharge;
            tracked.RawTotal = price.RawTotal;
            tracked.Total = price.Total;
            tracked.VoucherId = price.VoucherDiscount > 0 ? order.VoucherId : null;
            tracked.VoucherNote = price.VoucherNote;

            // A-15: an online payment difference is simulated; no second gateway call is made.
            var payments = await _db.Payments.Where(p => p.OrderId == orderId).ToListAsync();
            foreach (var payment in payments)
            {
                if (payment.Status is PaymentStatus.Paid or PaymentStatus.Pending)
                    payment.Amount = price.Total;
            }

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            return EditResult.Ok(price);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task<OperationResult> TransitionAsync(int orderId, OrderStatus to, int actorUserId,
        string? reason, RefundMode refund)
    {
        var actor = await _db.Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == actorUserId && u.IsActive);
        if (actor is null) return OperationResult.Fail("Actor not found or inactive.");

        var order = await _db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId);
        if (order is null) return OperationResult.Fail("Order not found.");
        if (actor.Role.IsCustomer() && order.CustomerId != actor.Id)
            return OperationResult.Fail("Only the owning customer can change this order.");

        var payment = await _db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == orderId);
        var isCod = payment?.Method == PaymentMethod.Cod;
        var context = new TransitionContext(
            actor.Role, actor.Id, actor.HubId, order.HubId, order.AssignedShipperId,
            isCod, payment?.CashConfirmed ?? order.CashConfirmed, reason);

        try
        {
            // BR15: Core is the only place allowed to decide whether the change is legal.
            OrderStateMachine.Ensure(order.Status, to, context);
        }
        catch (InvalidTransitionException ex)
        {
            return OperationResult.Fail(ex.Message); // no write at all
        }

        PaymentResult? refundResult = null;
        var refundAmount = 0m;
        var payer = Role.Local;
        if (refund != RefundMode.None && payment is not null && !isCod && payment.Status == PaymentStatus.Paid)
        {
            payer = await _db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.Id == order.CustomerId).Select(u => u.Role).SingleAsync();
            refundAmount = refund == RefundMode.FullIfOnline ? payment.Amount : order.FoodTotal;
            try
            {
                var strategy = _payments.Resolve(payment.Method, payer, refundAmount);
                refundResult = strategy.Refund(new PaymentRequest(orderId, refundAmount, payer));
            }
            catch (PaymentNotAllowedException ex)
            {
                return OperationResult.Fail(ex.Message);
            }
            if (!refundResult.Success)
                return OperationResult.Fail(refundResult.Message ?? "The refund was declined.");
        }

        var changedAt = VietnamTime.FromUtc(_clock.UtcNow); // A-12: injected clock, UTC+7
        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var trackedOrder = await _db.Orders.SingleAsync(o => o.Id == orderId);
            trackedOrder.Status = to; // never set outside this service after the state machine agreed

            if (refundResult is not null && payment is not null)
            {
                var trackedPayment = await _db.Payments.SingleAsync(p => p.Id == payment.Id);
                trackedPayment.Status = refundResult.Status;
                trackedPayment.RefundedAmount = refundAmount;
                trackedPayment.RefundedAt = changedAt;
            }

            _db.OrderStatusHistories.Add(new OrderStatusHistory
            {
                OrderId = orderId,
                FromStatus = order.Status,
                ToStatus = to,
                ActorUserId = actor.Id,
                ChangedAt = changedAt,
                Reason = reason
            });

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
}
