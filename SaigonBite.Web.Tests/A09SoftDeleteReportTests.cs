using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Core.Payments;
using SaigonBite.Web.Data;
using SaigonBite.Web.Services;

namespace SaigonBite.Web.Tests;

/// <summary>
/// A-09: soft-deleting a menu item or a user must not rewrite order detail, status history or R1.
/// Each test gets a throwaway LocalDB database, so the dev seed database is never touched.
/// </summary>
public sealed class A09SoftDeleteReportTests : IDisposable
{
    private readonly string _connectionString =
        $"Server=(localdb)\\MSSQLLocalDB;Database=SaigonBite_A09_{Guid.NewGuid():N};" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private readonly SaigonBiteDbContext _db;

    public A09SoftDeleteReportTests()
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

    [Fact]
    public async Task SoftDeletedMenuItemAndCustomer_KeepDetailHistoryAndR1Unchanged()
    {
        // Arrange: one confirmed order built from seeded hub/customer/menu item.
        // Test fixture only; production status changes go through OrderStateMachine (BR15).
        var placedAt = new DateTime(2026, 6, 1, 12, 0, 0);
        var item = await _db.MenuItems.SingleAsync(x => x.Id == 1);
        var customer = await _db.Users.SingleAsync(x => x.Id == 7);
        var admin = await _db.Users.SingleAsync(x => x.Id == 1);
        var hub = await _db.Hubs.SingleAsync(x => x.Id == 1);

        var order = new Order
        {
            CustomerId = customer.Id,
            HubId = hub.Id,
            Status = OrderStatus.Confirmed,
            DeliveryAddress = "123 Le Loi, District 1",
            HotelName = string.Empty,
            RoomNumber = string.Empty,
            PlacedAt = placedAt,
            SnapshotHubLoad = 0m,
            SnapshotHeavyRain = false,
            SnapshotDistanceKm = 1.5m,
            Subtotal = 55_000m,
            QuietDiscount = 0m,
            VoucherDiscount = 0m,
            FoodTotal = 55_000m,
            DeliveryFee = 0m,
            RushSurcharge = 0m,
            RainSurcharge = 0m,
            RawTotal = 55_000m,
            Total = 55_000m
        };
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        _db.OrderLines.Add(new OrderLine
        {
            OrderId = order.Id, MenuItemId = item.Id, Quantity = 1, UnitPrice = 55_000m
        });
        _db.Payments.Add(new Payment
        {
            OrderId = order.Id,
            Method = PaymentMethod.Momo,
            Amount = 55_000m,
            Status = PaymentStatus.Paid,
            Reference = "TEST-MOMO-1",
            CreatedAt = placedAt
        });
        _db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = OrderStatus.Placed,
            ToStatus = OrderStatus.Confirmed,
            ActorUserId = admin.Id,
            ChangedAt = placedAt.AddMinutes(5)
        });
        await _db.SaveChangesAsync();

        var reads = new OrderReadService(_db);
        var reports = new ReportService(_db);
        var from = new DateTime(2026, 1, 1);
        var to = new DateTime(2027, 1, 1);

        var detailBefore = await reads.GetDetailAsync(order.Id);
        var historyBefore = await reads.GetHistoryAsync(order.Id);
        var r1Before = await reports.GetRevenueAsync(from, to);

        Assert.Equal("Pho Bo", detailBefore.Lines[0].ItemName);
        Assert.Equal("Noodles", detailBefore.Lines[0].Category);
        Assert.Equal(55_000m, r1Before.ByCategory.Single(x => x.Category == "Noodles").Revenue);
        Assert.Equal(55_000m, r1Before.ByMethod.Single(x => x.Method == PaymentMethod.Momo).Revenue);

        // Act: soft-delete the menu item and the customer that appear in this order (A-09).
        item.IsDeleted = true;
        customer.IsDeleted = true;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Assert: snapshots and IgnoreQueryFilters() keep detail, history and R1 identical.
        var detailAfter = await reads.GetDetailAsync(order.Id);
        var historyAfter = await reads.GetHistoryAsync(order.Id);
        var r1After = await reports.GetRevenueAsync(from, to);

        Assert.Equal(detailBefore.Total, detailAfter.Total);
        Assert.Equal(detailBefore.Lines[0].ItemName, detailAfter.Lines[0].ItemName);
        Assert.Equal(detailBefore.Lines[0].Category, detailAfter.Lines[0].Category);
        Assert.Equal(detailBefore.Lines[0].UnitPrice, detailAfter.Lines[0].UnitPrice);
        Assert.Single(historyAfter);
        Assert.Equal(historyBefore[0].ActorIdentifier, historyAfter[0].ActorIdentifier);
        Assert.Equal(
            r1Before.ByCategory.Single(x => x.Category == "Noodles").Revenue,
            r1After.ByCategory.Single(x => x.Category == "Noodles").Revenue);
        Assert.Equal(
            r1Before.ByMethod.Single(x => x.Method == PaymentMethod.Momo).Revenue,
            r1After.ByMethod.Single(x => x.Method == PaymentMethod.Momo).Revenue);
    }
}
