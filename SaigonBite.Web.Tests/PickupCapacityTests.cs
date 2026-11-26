using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Core.Payments;
using SaigonBite.Core.Pricing;
using SaigonBite.Web.Data;
using SaigonBite.Web.Services;

namespace SaigonBite.Web.Tests;

/// <summary>A-04 prerequisite: the 2-order cap is enforced at assignment AND at Ready -> OutForDelivery.</summary>
public sealed class PickupCapacityTests : IDisposable
{
    private readonly string _connectionString =
        $"Server=(localdb)\\MSSQLLocalDB;Database=SaigonBite_PU_{Guid.NewGuid():N};" +
        "Trusted_Connection=True;TrustServerCertificate=True;";
    private readonly SaigonBiteDbContext _db;

    public PickupCapacityTests()
    {
        _db = new SaigonBiteDbContext(new DbContextOptionsBuilder<SaigonBiteDbContext>()
            .UseSqlServer(_connectionString).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        using var cleanup = new SaigonBiteDbContext(new DbContextOptionsBuilder<SaigonBiteDbContext>()
            .UseSqlServer(_connectionString).Options);
        cleanup.Database.EnsureDeleted();
    }

    private OrderStatusService Status => new(_db, new PricingService(), Payments(), new SystemClock());
    private ShipperAssignmentService Delivery => new(_db, new SystemClock());

    private static PaymentContext Payments() => new(new IPaymentStrategy[]
    {
        new MomoPayment(), new VnPayPayment(), new ApplePayPayment(), new PayPalPayment(), new CodPayment()
    });

    private async Task<Order> SeedReadyOrderAsync()
    {
        var now = new DateTime(2026, 6, 1, 15, 0, 0);
        var order = new Order
        {
            CustomerId = 7, HubId = 1, Status = OrderStatus.Ready,
            DeliveryAddress = "A-04 pickup test", PlacedAt = now,
            Subtotal = 60_000m, FoodTotal = 60_000m, RawTotal = 60_000m, Total = 60_000m
        };
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        _db.Payments.Add(new Payment
        {
            OrderId = order.Id, Method = PaymentMethod.Momo, Amount = 60_000m,
            Status = PaymentStatus.Paid, Reference = "PU", CreatedAt = now
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return order;
    }

    private async Task<(OrderStatus Status, int History)> StateAsync(int orderId)
    {
        var order = await _db.Orders.AsNoTracking()
            .Include(o => o.StatusHistory).SingleAsync(o => o.Id == orderId);
        return (order.Status, order.StatusHistory.Count);
    }

    [Fact]
    public async Task Assign_three_Ready_orders_pick_up_two_third_pickup_is_refused_A04()
    {
        var orders = new List<Order>();
        for (var i = 0; i < 3; i++)
        {
            var order = await SeedReadyOrderAsync();
            orders.Add(order);
            var assigned = await Delivery.AssignAsync(order.Id, 5, 1);
            Assert.True(assigned.Accepted, assigned.Error); // assignment cap counts OutForDelivery only
        }

        for (var i = 0; i < 2; i++)
        {
            var pickedUp = await Status.ChangeStatusAsync(orders[i].Id, OrderStatus.OutForDelivery, 5);
            Assert.True(pickedUp.Accepted, pickedUp.Error);
        }

        var before = await StateAsync(orders[2].Id);
        var third = await Status.ChangeStatusAsync(orders[2].Id, OrderStatus.OutForDelivery, 5);

        Assert.False(third.Accepted);
        Assert.Contains("A-04", third.Error);
        Assert.Equal(before, await StateAsync(orders[2].Id));

        // The assignment-time check still applies to a new Ready order.
        var fourth = await SeedReadyOrderAsync();
        var beforeFourth = await StateAsync(fourth.Id);
        var assignFourth = await Delivery.AssignAsync(fourth.Id, 5, 1);
        Assert.False(assignFourth.Accepted);
        Assert.Contains("A-04", assignFourth.Error);
        Assert.Equal(beforeFourth, await StateAsync(fourth.Id));
    }
}
