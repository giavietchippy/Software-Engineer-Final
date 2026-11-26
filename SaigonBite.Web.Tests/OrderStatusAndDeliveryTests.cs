using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Core.Payments;
using SaigonBite.Core.Pricing;
using SaigonBite.Web.Data;
using SaigonBite.Web.Services;

namespace SaigonBite.Web.Tests;

/// <summary>
/// Steps E+F (BR14-BR17, A-01/A-02/A-04/A-12/A-15). Throwaway LocalDB per test; rejected
/// commands must leave status, history, payments and assignments byte-for-byte unchanged.
/// </summary>
public sealed class OrderStatusAndDeliveryTests : IDisposable
{
    private static readonly DateTimeOffset DefaultUtc = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);   // 15:00 UTC+7
    private static readonly DateTimeOffset RushUtc = new(2026, 6, 1, 11, 0, 0, TimeSpan.Zero);     // 18:00 UTC+7

    private readonly string _connectionString =
        $"Server=(localdb)\\MSSQLLocalDB;Database=SaigonBite_EF_{Guid.NewGuid():N};" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private readonly SaigonBiteDbContext _db;
    private readonly MutableClock _clock = new(DefaultUtc);

    public OrderStatusAndDeliveryTests()
    {
        _db = NewContext();
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        using var cleanup = NewContext();
        cleanup.Database.EnsureDeleted();
    }

    private SaigonBiteDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SaigonBiteDbContext>().UseSqlServer(_connectionString).Options);

    private OrderStatusService Status => new(_db, new PricingService(), Payments(), _clock);
    private ShipperAssignmentService Delivery => new(_db, _clock);

    private OrderService Orders => new(_db, new PricingService(), Payments(),
        new SettingsService(_db, _clock), _clock, new DistanceService());

    private static PaymentContext Payments() => new(new IPaymentStrategy[]
    {
        new MomoPayment(), new VnPayPayment(), new ApplePayPayment(), new PayPalPayment(), new CodPayment()
    });

    private async Task<Order> SeedOrderAsync(
        OrderStatus status = OrderStatus.Placed,
        int customerId = 7,
        int hubId = 1,
        PaymentMethod method = PaymentMethod.Momo,
        PaymentStatus paymentStatus = PaymentStatus.Paid,
        int? shipperId = null,
        decimal total = 205_000m,
        decimal foodTotal = 180_000m)
    {
        var placedAt = VietnamTime.FromUtc(_clock.UtcNow);
        var order = new Order
        {
            CustomerId = customerId, HubId = hubId, Status = status,
            DeliveryAddress = "123 Le Loi, District 1", PlacedAt = placedAt,
            SnapshotHubLoad = 0.2m, SnapshotHeavyRain = false, SnapshotDistanceKm = 3.2m,
            Subtotal = 200_000m, QuietDiscount = 20_000m, FoodTotal = foodTotal,
            DeliveryFee = 25_000m, RushSurcharge = 0m, RainSurcharge = 0m,
            RawTotal = total, Total = total, AssignedShipperId = shipperId
        };
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        _db.Payments.Add(new Payment
        {
            OrderId = order.Id, Method = method, Amount = total, Status = paymentStatus,
            Reference = "seed", CreatedAt = placedAt, RefundedAmount = 0
        });
        if (shipperId is not null)
        {
            _db.ShipperAssignments.Add(new ShipperAssignment
            {
                OrderId = order.Id, ShipperId = shipperId.Value, AssignedAt = placedAt
            });
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return order;
    }

    private async Task SeedLinesAsync(int orderId, params (int MenuItemId, int Quantity)[] lines)
    {
        var ids = lines.Select(l => l.MenuItemId).ToList();
        var items = await _db.MenuItems.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        foreach (var line in lines)
        {
            _db.OrderLines.Add(new OrderLine
            {
                OrderId = orderId, MenuItemId = line.MenuItemId,
                Quantity = line.Quantity, UnitPrice = items[line.MenuItemId].Price
            });
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private sealed record OrderState(
        OrderStatus Status,
        int HistoryCount,
        int LineCount,
        decimal Total,
        int AssignmentCount,
        int? AssignedShipperId,
        PaymentStatus? PaymentStatus,
        decimal PaymentAmount,
        bool CashConfirmed,
        decimal RefundedAmount,
        DateTime? RefundedAt);

    private async Task<OrderState> StateAsync(int orderId)
    {
        var order = await _db.Orders.AsNoTracking()
            .Include(o => o.StatusHistory)
            .Include(o => o.Lines)
            .Include(o => o.ShipperAssignments)
            .SingleAsync(o => o.Id == orderId);
        var payment = await _db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == orderId);
        return new OrderState(
            order.Status, order.StatusHistory.Count, order.Lines.Count, order.Total,
            order.ShipperAssignments.Count, order.AssignedShipperId,
            payment?.Status, payment?.Amount ?? 0m, payment?.CashConfirmed ?? false,
            payment?.RefundedAmount ?? 0m, payment?.RefundedAt);
    }

    public static IEnumerable<object[]> InvalidTransitions()
    {
        // id, from, to, actorUserId, cod, shipperAssigned, reason
        yield return new object[] { "S-01", OrderStatus.Placed, OrderStatus.Preparing, 2, false, false, "x" };
        yield return new object[] { "S-02", OrderStatus.Placed, OrderStatus.Ready, 2, false, false, "x" };
        yield return new object[] { "S-03", OrderStatus.Confirmed, OrderStatus.Placed, 2, false, false, "x" };
        yield return new object[] { "S-04", OrderStatus.Preparing, OrderStatus.Cancelled, 7, false, false, "x" };
        yield return new object[] { "S-05", OrderStatus.Delivered, OrderStatus.Cancelled, 1, false, false, "x" };
        yield return new object[] { "S-06", OrderStatus.Cancelled, OrderStatus.Confirmed, 2, false, false, "x" };
        yield return new object[] { "S-07", OrderStatus.OutForDelivery, OrderStatus.Cancelled, 1, false, true, "x" };
        yield return new object[] { "S-08", OrderStatus.Ready, OrderStatus.OutForDelivery, 5, false, false, "x" };
        yield return new object[] { "S-09", OrderStatus.OutForDelivery, OrderStatus.Delivered, 5, true, true, "x" };
        yield return new object[] { "S-10", OrderStatus.Placed, OrderStatus.Confirmed, 7, false, false, "x" };
        yield return new object[] { "S-11", OrderStatus.Placed, OrderStatus.Confirmed, 3, false, false, "x" }; // hub 2
        yield return new object[] { "S-12", OrderStatus.Failed, OrderStatus.Delivered, 5, false, true, "x" };
    }

    [Theory]
    [MemberData(nameof(InvalidTransitions))]
    public async Task Every_invalid_transition_S01_to_S12_writes_nothing(string id, OrderStatus from,
        OrderStatus to, int actorUserId, bool cod, bool shipperAssigned, string? reason)
    {
        var order = await SeedOrderAsync(from,
            method: cod ? PaymentMethod.Cod : PaymentMethod.Momo,
            paymentStatus: cod ? PaymentStatus.Pending : PaymentStatus.Paid,
            shipperId: shipperAssigned ? 5 : null);
        var before = await StateAsync(order.Id);

        var result = await Status.ChangeStatusAsync(order.Id, to, actorUserId, reason);

        Assert.False(result.Accepted, $"{id}: {result.Error}");
        Assert.Equal(before, await StateAsync(order.Id));
    }

    [Fact]
    public async Task Kitchen_of_another_hub_and_customer_cannot_confirm()
    {
        var order = await SeedOrderAsync();
        var before = await StateAsync(order.Id);

        var otherHub = await Status.ChangeStatusAsync(order.Id, OrderStatus.Confirmed, 3);
        Assert.False(otherHub.Accepted);
        Assert.Equal(before, await StateAsync(order.Id));

        var customer = await Status.ChangeStatusAsync(order.Id, OrderStatus.Confirmed, 7);
        Assert.False(customer.Accepted);
        Assert.Equal(before, await StateAsync(order.Id));
    }

    [Fact]
    public async Task Kitchen_rejection_without_a_reason_is_refused()
    {
        var order = await SeedOrderAsync();
        var before = await StateAsync(order.Id);

        var noReason = await Status.ChangeStatusAsync(order.Id, OrderStatus.Cancelled, 2);
        Assert.False(noReason.Accepted);
        Assert.Equal(before, await StateAsync(order.Id));

        var withReason = await Status.ChangeStatusAsync(order.Id, OrderStatus.Cancelled, 2, "out of stock");
        Assert.True(withReason.Accepted, withReason.Error);
        var after = await StateAsync(order.Id);
        Assert.Equal(OrderStatus.Cancelled, after.Status);
        Assert.Equal(before.HistoryCount + 1, after.HistoryCount);
        var row = await _db.OrderStatusHistories.AsNoTracking().SingleAsync(h => h.ToStatus == OrderStatus.Cancelled);
        Assert.Equal("out of stock", row.Reason);
    }

    [Fact]
    public async Task Edit_after_Confirmed_is_refused_BR17()
    {
        var order = await SeedOrderAsync(OrderStatus.Confirmed);
        await SeedLinesAsync(order.Id, (2, 2));
        var before = await StateAsync(order.Id);

        var result = await Status.EditItemsAsync(order.Id, 7, new[] { new OrderLineInput(1, 1) });

        Assert.False(result.Accepted);
        Assert.Equal(before, await StateAsync(order.Id));
    }

    [Fact]
    public async Task Edit_uses_the_old_snapshot_after_rain_and_rush_change_BR17()
    {
        await SeedOrderAsync(OrderStatus.Confirmed); // 1 / 10 busy; this order adds another below
        await SeedOrderAsync(OrderStatus.Confirmed); // 2 / 10 = 20 % load at placement
        var placed = await Orders.PlaceOrderAsync(new PlaceOrderInput(
            7, 1, "District 1",
            new[] { new OrderLineInput(2, 2), new OrderLineInput(5, 2) }, // S = 200,000
            PaymentMethod.Momo));
        Assert.True(placed.Accepted, placed.Message);
        Assert.Equal(205_000m, placed.Price!.Total);
        _db.ChangeTracker.Clear();

        await new SettingsService(_db, _clock).SetHeavyRainAsync(true, 1);
        _clock.UtcNow = RushUtc; // 18:00 local, rush hour
        _db.ChangeTracker.Clear();

        // New basket S = 215,000; with the OLD snapshot: Q=21,500, F=193,500, D=25,000 => 219,000.
        var edited = await Status.EditItemsAsync(placed.OrderId!.Value, 7,
            new[] { new OrderLineInput(1, 2), new OrderLineInput(3, 3) });

        Assert.True(edited.Accepted, edited.Error);
        Assert.Equal(219_000m, edited.Price!.Total);
        Assert.Equal(0m, edited.Price.RainSurcharge);
        Assert.Equal(0m, edited.Price.RushSurcharge);
        var order = await _db.Orders.AsNoTracking().SingleAsync(o => o.Id == placed.OrderId);
        Assert.Equal(0.2m, order.SnapshotHubLoad);
        Assert.False(order.SnapshotHeavyRain);
        Assert.Equal(3.2m, order.SnapshotDistanceKm);
        Assert.Equal(15, order.PlacedAt.Hour);
        Assert.Equal(219_000m, order.Total);
        Assert.Equal(25_000m, order.DeliveryFee);
        var lines = await _db.OrderLines.AsNoTracking().Where(l => l.OrderId == order.Id).ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.MenuItemId == 1 && l.Quantity == 2 && l.UnitPrice == 55_000m);
        Assert.Contains(lines, l => l.MenuItemId == 3 && l.Quantity == 3 && l.UnitPrice == 35_000m);
        var payment = await _db.Payments.AsNoTracking().SingleAsync(p => p.OrderId == order.Id);
        Assert.Equal(219_000m, payment.Amount); // A-15: simulated online adjustment
    }

    [Fact]
    public async Task Cancel_at_Preparing_is_refused_BR16()
    {
        var order = await SeedOrderAsync(OrderStatus.Preparing);
        var before = await StateAsync(order.Id);

        var result = await Status.CancelAsync(order.Id, 7);

        Assert.False(result.Accepted);
        Assert.Equal(before, await StateAsync(order.Id));
    }

    [Fact]
    public async Task Cancel_online_paid_order_refunds_in_full()
    {
        var order = await SeedOrderAsync(); // Momo, Paid, 205,000
        var before = await StateAsync(order.Id);

        var result = await Status.CancelAsync(order.Id, 7, "changed mind");

        Assert.True(result.Accepted, result.Error);
        var after = await StateAsync(order.Id);
        Assert.Equal(OrderStatus.Cancelled, after.Status);
        Assert.Equal(PaymentStatus.Refunded, after.PaymentStatus);
        Assert.Equal(205_000m, after.RefundedAmount);
        Assert.NotNull(after.RefundedAt);
        Assert.Equal(before.HistoryCount + 1, after.HistoryCount);
    }

    [Fact]
    public async Task Cancel_COD_order_creates_no_refund()
    {
        var order = await SeedOrderAsync(method: PaymentMethod.Cod, paymentStatus: PaymentStatus.Pending);
        var before = await StateAsync(order.Id);

        var result = await Status.CancelAsync(order.Id, 7);

        Assert.True(result.Accepted, result.Error);
        var after = await StateAsync(order.Id);
        Assert.Equal(OrderStatus.Cancelled, after.Status);
        Assert.Equal(before.PaymentStatus, after.PaymentStatus);
        Assert.Equal(0m, after.RefundedAmount);
        Assert.Null(after.RefundedAt);
    }

    [Fact]
    public async Task Failed_delivery_of_online_paid_order_refunds_food_total_only_A02()
    {
        var order = await SeedOrderAsync(OrderStatus.OutForDelivery, shipperId: 5);
        var before = await StateAsync(order.Id);

        var missingReason = await Status.FailDeliveryAsync(order.Id, 5, string.Empty);
        Assert.False(missingReason.Accepted);
        Assert.Equal(before, await StateAsync(order.Id));

        var result = await Status.FailDeliveryAsync(order.Id, 5, "customer unreachable");
        Assert.True(result.Accepted, result.Error);
        var after = await StateAsync(order.Id);
        Assert.Equal(OrderStatus.Failed, after.Status);
        Assert.Equal(PaymentStatus.Refunded, after.PaymentStatus);
        Assert.Equal(180_000m, after.RefundedAmount); // FoodTotal, not Total
        Assert.Equal(205_000m, after.PaymentAmount);
        Assert.Equal(before.HistoryCount + 1, after.HistoryCount);
    }

    [Fact]
    public async Task Third_out_for_delivery_order_for_one_shipper_is_refused_A04()
    {
        await SeedOrderAsync(OrderStatus.OutForDelivery, shipperId: 5);
        await SeedOrderAsync(OrderStatus.OutForDelivery, shipperId: 5);
        var ready = await SeedOrderAsync(OrderStatus.Ready);
        var before = await StateAsync(ready.Id);

        var third = await Delivery.AssignAsync(ready.Id, 5, 1);

        Assert.False(third.Accepted);
        Assert.Contains("A-04", third.Error);
        Assert.Equal(before, await StateAsync(ready.Id));

        var otherShipper = await Delivery.AssignAsync(ready.Id, 6, 1);
        Assert.True(otherShipper.Accepted, otherShipper.Error);
    }

    [Fact]
    public async Task Reassign_keeps_the_old_assignment_and_non_admin_is_refused()
    {
        var order = await SeedOrderAsync(OrderStatus.Ready);
        var before = await StateAsync(order.Id);

        var nonAdmin = await Delivery.AssignAsync(order.Id, 5, 7); // customer 7
        Assert.False(nonAdmin.Accepted);
        Assert.Equal(before, await StateAsync(order.Id));

        Assert.True((await Delivery.AssignAsync(order.Id, 5, 1)).Accepted);
        Assert.True((await Delivery.ReassignAsync(order.Id, 6, 1)).Accepted);
        var rows = await _db.ShipperAssignments.AsNoTracking()
            .Where(a => a.OrderId == order.Id).OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(5, rows[0].ShipperId);
        Assert.NotNull(rows[0].UnassignedAt);
        Assert.Equal(6, rows[1].ShipperId);
        Assert.Null(rows[1].UnassignedAt);
        Assert.Equal(6, (await StateAsync(order.Id)).AssignedShipperId);

        Assert.True((await Status.ChangeStatusAsync(order.Id, OrderStatus.OutForDelivery, 6)).Accepted);
        var outForDelivery = await StateAsync(order.Id);
        var late = await Delivery.ReassignAsync(order.Id, 5, 1);
        Assert.False(late.Accepted);
        Assert.Equal(outForDelivery, await StateAsync(order.Id));
    }

    [Fact]
    public async Task Unassign_closes_the_current_assignment()
    {
        var order = await SeedOrderAsync(OrderStatus.Ready);
        Assert.True((await Delivery.AssignAsync(order.Id, 5, 1)).Accepted);

        var result = await Delivery.UnassignAsync(order.Id, 1);

        Assert.True(result.Accepted, result.Error);
        var row = await _db.ShipperAssignments.AsNoTracking().SingleAsync(a => a.OrderId == order.Id);
        Assert.NotNull(row.UnassignedAt);
        Assert.Null((await StateAsync(order.Id)).AssignedShipperId);
    }

    [Fact]
    public async Task Shipper_cannot_deliver_an_order_assigned_to_another_shipper()
    {
        var order = await SeedOrderAsync(OrderStatus.OutForDelivery, shipperId: 5);
        var before = await StateAsync(order.Id);

        var other = await Status.ChangeStatusAsync(order.Id, OrderStatus.Delivered, 6);

        Assert.False(other.Accepted);
        Assert.Equal(before, await StateAsync(order.Id));
        Assert.True((await Status.ChangeStatusAsync(order.Id, OrderStatus.Delivered, 5)).Accepted);
    }

    [Fact]
    public async Task Cod_cannot_be_Delivered_before_ConfirmCash_BR14()
    {
        var order = await SeedOrderAsync(OrderStatus.OutForDelivery,
            method: PaymentMethod.Cod, paymentStatus: PaymentStatus.Pending, shipperId: 5);
        var before = await StateAsync(order.Id);

        var early = await Status.ChangeStatusAsync(order.Id, OrderStatus.Delivered, 5);
        Assert.False(early.Accepted);
        Assert.Equal(before, await StateAsync(order.Id));

        var wrongShipper = await Delivery.ConfirmCashAsync(order.Id, 6);
        Assert.False(wrongShipper.Accepted);
        Assert.Equal(before, await StateAsync(order.Id));

        var cash = await Delivery.ConfirmCashAsync(order.Id, 5);
        Assert.True(cash.Accepted, cash.Error);
        var confirmed = await StateAsync(order.Id);
        Assert.True(confirmed.CashConfirmed);
        Assert.Equal(PaymentStatus.Paid, confirmed.PaymentStatus);
        Assert.Equal(before.HistoryCount, confirmed.HistoryCount); // not a status change

        var delivered = await Status.ChangeStatusAsync(order.Id, OrderStatus.Delivered, 5);
        Assert.True(delivered.Accepted, delivered.Error);
        Assert.Equal(OrderStatus.Delivered, (await StateAsync(order.Id)).Status);
    }

    [Fact]
    public async Task Shipper_lists_only_their_own_orders()
    {
        var mine = await SeedOrderAsync(OrderStatus.Ready, shipperId: 5);
        var theirs = await SeedOrderAsync(OrderStatus.Ready, shipperId: 6);

        var list5 = await Delivery.ListOwnOrdersAsync(5);
        var list6 = await Delivery.ListOwnOrdersAsync(6);

        Assert.Single(list5);
        Assert.Equal(mine.Id, list5[0].OrderId);
        Assert.Single(list6);
        Assert.Equal(theirs.Id, list6[0].OrderId);
        Assert.Empty(await Delivery.ListOwnOrdersAsync(7)); // a customer sees nothing
    }

    private sealed class MutableClock : IClock
    {
        public MutableClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; set; }
    }
}
