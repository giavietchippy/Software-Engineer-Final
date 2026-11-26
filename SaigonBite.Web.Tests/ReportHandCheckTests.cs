using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Core.Payments;
using SaigonBite.Web.Data;
using SaigonBite.Web.Services;

namespace SaigonBite.Web.Tests;

/// <summary>
/// A-13/A-14: six fixed orders (4 counted, 1 Cancelled, 1 Failed) with numbers small enough
/// to verify by hand. R1 uses snapshots; R2 uses Confirmed -> Ready history and current status.
/// </summary>
public sealed class ReportHandCheckTests : IDisposable
{
    private readonly string _connectionString =
        $"Server=(localdb)\\MSSQLLocalDB;Database=SaigonBite_RPT_{Guid.NewGuid():N};" +
        "Trusted_Connection=True;TrustServerCertificate=True;";
    private readonly SaigonBiteDbContext _db;

    public ReportHandCheckTests()
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

    private async Task SeedAsync()
    {
        var t = new DateTime(2026, 6, 10, 10, 0, 0);

        // Counted: Local + Momo, two categories.
        await SeedOrderAsync(7, 1, OrderStatus.Delivered, 205_000m, 20_000m, t,
            PaymentMethod.Momo, PaymentStatus.Paid,
            new[] { (1, 1), (2, 1) },
            new (OrderStatus?, OrderStatus, DateTime)[]
            {
                (null, OrderStatus.Placed, t),
                (OrderStatus.Placed, OrderStatus.Confirmed, t.AddMinutes(5)),
                (OrderStatus.Confirmed, OrderStatus.Ready, t.AddMinutes(20))
            });

        // Counted: Tourist + ApplePay, two dessert lines.
        await SeedOrderAsync(8, 1, OrderStatus.OutForDelivery, 300_000m, 0m, t.AddHours(1),
            PaymentMethod.ApplePay, PaymentStatus.Paid,
            new[] { (5, 2), (6, 1) },
            new (OrderStatus?, OrderStatus, DateTime)[]
            {
                (null, OrderStatus.Placed, t.AddHours(1)),
                (OrderStatus.Placed, OrderStatus.Confirmed, t.AddHours(1).AddMinutes(10)),
                (OrderStatus.Confirmed, OrderStatus.Ready, t.AddHours(1).AddMinutes(40))
            });

        // Counted: Local + VnPay, confirmed but no Ready row.
        await SeedOrderAsync(7, 2, OrderStatus.Confirmed, 150_000m, 15_000m, t.AddHours(2),
            PaymentMethod.VnPay, PaymentStatus.Paid,
            new[] { (3, 2) },
            new (OrderStatus?, OrderStatus, DateTime)[]
            {
                (null, OrderStatus.Placed, t.AddHours(2)),
                (OrderStatus.Placed, OrderStatus.Confirmed, t.AddHours(2).AddMinutes(5))
            });

        // Excluded: Tourist + PayPal, cancelled.
        await SeedOrderAsync(8, 2, OrderStatus.Cancelled, 100_000m, 0m, t.AddHours(3),
            PaymentMethod.PayPal, PaymentStatus.Refunded,
            new[] { (4, 1) },
            new (OrderStatus?, OrderStatus, DateTime)[]
            {
                (null, OrderStatus.Placed, t.AddHours(3)),
                (OrderStatus.Placed, OrderStatus.Cancelled, t.AddHours(3).AddMinutes(2))
            });

        // Excluded: Local + Cod, failed.
        await SeedOrderAsync(7, 3, OrderStatus.Failed, 120_000m, 0m, t.AddHours(4),
            PaymentMethod.Cod, PaymentStatus.Failed,
            new[] { (1, 2) },
            new (OrderStatus?, OrderStatus, DateTime)[]
            {
                (null, OrderStatus.Placed, t.AddHours(4)),
                (OrderStatus.Placed, OrderStatus.Failed, t.AddHours(4).AddMinutes(3))
            });

        // Counted: Tourist + Cod, 30-minute Confirmed -> Ready.
        await SeedOrderAsync(8, 3, OrderStatus.Delivered, 250_000m, 25_000m, t.AddHours(5),
            PaymentMethod.Cod, PaymentStatus.Paid,
            new[] { (2, 2), (5, 1) },
            new (OrderStatus?, OrderStatus, DateTime)[]
            {
                (null, OrderStatus.Placed, t.AddHours(5)),
                (OrderStatus.Placed, OrderStatus.Confirmed, t.AddHours(5).AddMinutes(5)),
                (OrderStatus.Confirmed, OrderStatus.Ready, t.AddHours(5).AddMinutes(35))
            });
        _db.ChangeTracker.Clear();
    }

    private async Task SeedOrderAsync(int customerId, int hubId, OrderStatus status, decimal total,
        decimal quiet, DateTime placedAt, PaymentMethod method, PaymentStatus paymentStatus,
        (int ItemId, int Quantity)[] lines, (OrderStatus? From, OrderStatus To, DateTime At)[] history)
    {
        var order = new Order
        {
            CustomerId = customerId, HubId = hubId, Status = status,
            DeliveryAddress = "hand-checked fixture", PlacedAt = placedAt,
            SnapshotHubLoad = 0.2m, SnapshotHeavyRain = false, SnapshotDistanceKm = 1.5m,
            Subtotal = total, QuietDiscount = quiet, VoucherDiscount = 0m,
            FoodTotal = total - quiet, DeliveryFee = 0m, RushSurcharge = 0m, RainSurcharge = 0m,
            RawTotal = total, Total = total
        };
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        foreach (var (itemId, quantity) in lines)
            _db.OrderLines.Add(new OrderLine { OrderId = order.Id, MenuItemId = itemId, Quantity = quantity, UnitPrice = SeedPrice(itemId) });
        _db.Payments.Add(new Payment
        {
            OrderId = order.Id, Method = method, Amount = total, Status = paymentStatus,
            Reference = $"RPT-{order.Id}", CreatedAt = placedAt
        });
        foreach (var (from, to, at) in history)
            _db.OrderStatusHistories.Add(new OrderStatusHistory
            {
                OrderId = order.Id, FromStatus = from, ToStatus = to,
                ActorUserId = 1, ChangedAt = at
            });
        await _db.SaveChangesAsync();
    }

    private static decimal SeedPrice(int itemId) => itemId switch
    {
        1 => 55_000m, // Noodles
        2 => 60_000m, // Rice
        3 => 35_000m, // Drinks
        4 => 30_000m, // Drinks
        5 => 40_000m, // Dessert
        _ => 45_000m  // Dessert
    };

    [Fact]
    public async Task R1_exact_numbers_from_six_hand_checked_orders()
    {
        await SeedAsync();
        var reports = new ReportService(_db);

        var r1 = await reports.GetRevenueAsync(new DateTime(2026, 6, 1), new DateTime(2026, 7, 1));

        // A-13/A-14: Cancelled (30,000 of lines) and Failed (110,000 of lines) are excluded.
        // Hand-check total is 470,000, not 440,000: item 2 is Com Tam at 60,000 in the canonical
        // seed (also required by the 205,000 spec example), not Bun Cha at 50,000. The three
        // counted units of item 2 add exactly 3 x 10,000 = 30,000 to the 440,000 baseline.
        var category = r1.ByCategory.ToDictionary(x => x.Category, x => x.Revenue);
        Assert.Equal(4, category.Count);
        Assert.Equal(55_000m, category["Noodles"]);                 // only order 1
        Assert.Equal(180_000m, category["Rice"]);                  // 60,000 + 2 x 60,000
        Assert.Equal(165_000m, category["Dessert"]);               // 2 x 40,000 + 45,000 + 40,000
        Assert.Equal(70_000m, category["Drinks"]);                 // only 2 x 35,000
        Assert.Equal(470_000m, category.Values.Sum());             // 440,000 + 30,000 (item 2)

        var byMethod = r1.ByMethod.ToDictionary(x => x.Method, x => x.Revenue);
        Assert.Equal(4, byMethod.Count); // PayPal appears only on the Cancelled order
        Assert.Equal(205_000m, byMethod[PaymentMethod.Momo]);
        Assert.Equal(150_000m, byMethod[PaymentMethod.VnPay]);
        Assert.Equal(300_000m, byMethod[PaymentMethod.ApplePay]);
        Assert.Equal(250_000m, byMethod[PaymentMethod.Cod]);
        Assert.DoesNotContain(r1.ByMethod, x => x.Method == PaymentMethod.PayPal);
        Assert.Equal(new[] { "Dessert", "Drinks", "Noodles", "Rice" },
            r1.ByCategory.Select(x => x.Category));
    }

    [Fact]
    public async Task R2_exact_numbers_from_six_hand_checked_orders()
    {
        await SeedAsync();
        var reports = new ReportService(_db);

        var r2 = await reports.GetHubPerformanceAsync();
        var hub1 = r2.Single(x => x.HubId == 1);
        var hub2 = r2.Single(x => x.HubId == 2);
        var hub3 = r2.Single(x => x.HubId == 3);

        Assert.Equal(new[] { 1, 2, 3 }, r2.Select(x => x.HubId));

        Assert.Equal(2, hub1.OrdersHandled);                    // Delivered + OutForDelivery
        Assert.Equal(TimeSpan.FromMinutes(22.5), hub1.AverageConfirmedToReady); // (15 + 30) / 2
        Assert.Equal(0m, hub1.CurrentLoad);                     // no Confirmed/Preparing left
        Assert.Equal(1, hub1.QuietKitchenDiscounts);

        Assert.Equal(1, hub2.OrdersHandled);                    // Cancelled excluded
        Assert.Null(hub2.AverageConfirmedToReady);              // no Ready row
        Assert.Equal(0.1m, hub2.CurrentLoad);                   // 1 Confirmed / capacity 10
        Assert.Equal(1, hub2.QuietKitchenDiscounts);

        Assert.Equal(1, hub3.OrdersHandled);                    // Failed excluded
        Assert.Equal(TimeSpan.FromMinutes(30), hub3.AverageConfirmedToReady);
        Assert.Equal(0m, hub3.CurrentLoad);
        Assert.Equal(1, hub3.QuietKitchenDiscounts);
    }
}
