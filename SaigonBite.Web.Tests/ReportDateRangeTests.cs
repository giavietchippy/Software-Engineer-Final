using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Core.Payments;
using SaigonBite.Web.Controllers;
using SaigonBite.Web.Data;
using SaigonBite.Web.Services;

namespace SaigonBite.Web.Tests;

/// <summary>
/// A-16: 'to' is an inclusive Vietnam-local DATE. from == to returns the whole day:
/// [from 00:00, to + 1 day 00:00). These are separate fixtures so the R1/R2 hand-check totals
/// in ReportHandCheckTests stay unchanged.
/// </summary>
public sealed class ReportDateRangeTests : IDisposable
{
    private readonly string _connectionString =
        $"Server=(localdb)\\MSSQLLocalDB;Database=SaigonBite_DATE_{Guid.NewGuid():N};" +
        "Trusted_Connection=True;TrustServerCertificate=True;";
    private readonly SaigonBiteDbContext _db;

    public ReportDateRangeTests()
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
        await AddAsync(new DateTime(2026, 11, 11, 0, 0, 0), PaymentMethod.Momo, 10_000m);
        await AddAsync(new DateTime(2026, 11, 11, 23, 59, 0), PaymentMethod.VnPay, 20_000m);
        await AddAsync(new DateTime(2026, 11, 12, 0, 0, 0), PaymentMethod.Cod, 40_000m);
    }

    private async Task AddAsync(DateTime placedAt, PaymentMethod method, decimal total)
    {
        var order = new Order
        {
            CustomerId = 7, HubId = 1, Status = OrderStatus.Delivered,
            DeliveryAddress = "A-16 inclusive date test", PlacedAt = placedAt,
            SnapshotHubLoad = 0.2m, SnapshotHeavyRain = false, SnapshotDistanceKm = 1.5m,
            Subtotal = total, QuietDiscount = 0m, VoucherDiscount = 0m,
            FoodTotal = total, DeliveryFee = 0m, RushSurcharge = 0m, RainSurcharge = 0m,
            RawTotal = total, Total = total
        };
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        _db.OrderLines.Add(new OrderLine
        {
            OrderId = order.Id, MenuItemId = 1, Quantity = 1, UnitPrice = total
        });
        _db.Payments.Add(new Payment
        {
            OrderId = order.Id, Method = method, Amount = total, Status = PaymentStatus.Paid,
            Reference = $"DATE-{order.Id}", CreatedAt = placedAt
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task From_equals_to_returns_the_whole_day_A16()
    {
        await SeedAsync();
        var reports = new ReportService(_db);

        var r1 = await reports.GetRevenueAsync(
            new DateTime(2026, 11, 11), new DateTime(2026, 11, 11));

        Assert.Single(r1.ByCategory);
        Assert.Equal("Noodles", r1.ByCategory[0].Category);
        Assert.Equal(30_000m, r1.ByCategory[0].Revenue); // 00:00 + 23:59, not the next day
    }

    [Fact]
    public async Task Boundary_2359_included_0000_next_day_excluded_A16()
    {
        await SeedAsync();
        var reports = new ReportService(_db);

        var r1 = await reports.GetRevenueAsync(
            new DateTime(2026, 11, 11), new DateTime(2026, 11, 11));
        var byMethod = r1.ByMethod.ToDictionary(x => x.Method, x => x.Revenue);

        Assert.Equal(10_000m, byMethod[PaymentMethod.Momo]);   // 11/11 00:00
        Assert.Equal(20_000m, byMethod[PaymentMethod.VnPay]);  // 11/11 23:59
        Assert.False(byMethod.ContainsKey(PaymentMethod.Cod)); // 12/11 00:00
        Assert.Equal(30_000m, byMethod.Values.Sum());
    }

    [Fact]
    public async Task Admin_JSON_endpoints_use_the_same_inclusive_to_date_A16()
    {
        await SeedAsync();
        var reports = new ReportService(_db);
        var controller = new AdminController(reports, null!);
        var day = new DateTime(2026, 11, 11);

        var categoryResult = Assert.IsType<JsonResult>(await controller.RevenueByCategory(day, day));
        var category = Assert.IsType<List<RevenueByCategoryDto>>(categoryResult.Value);
        Assert.Equal(30_000m, category.Single().Revenue);

        var methodResult = Assert.IsType<JsonResult>(await controller.RevenueByMethod(day, day));
        var methods = Assert.IsType<List<RevenueByMethodDto>>(methodResult.Value);
        Assert.Equal(2, methods.Count);
        Assert.DoesNotContain(methods, x => x.Method == PaymentMethod.Cod);
    }
}
