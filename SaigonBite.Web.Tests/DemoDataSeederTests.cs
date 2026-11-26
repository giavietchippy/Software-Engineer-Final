using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Web.Data;
using SaigonBite.Web.Services;

namespace SaigonBite.Web.Tests;

/// <summary>N-01: CLI-only demo data covers every state, both customer groups and all payment methods.</summary>
public sealed class DemoDataSeederTests : IDisposable
{
    private readonly string _connectionString =
        $"Server=(localdb)\\MSSQLLocalDB;Database=SaigonBite_DEMO_{Guid.NewGuid():N};" +
        "Trusted_Connection=True;TrustServerCertificate=True;";
    private readonly SaigonBiteDbContext _db;

    public DemoDataSeederTests()
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
    public async Task Demo_seed_creates_twelve_orders_and_is_resettable()
    {
        var seeder = new DemoDataSeeder(_db, new SystemClock());

        Assert.Equal(12, await seeder.SeedAsync());
        var orders = await _db.Orders.AsNoTracking().ToListAsync();
        Assert.Equal(12, orders.Count);
        Assert.All(orders, o => Assert.StartsWith(DemoDataSeeder.DemoPrefix, o.DeliveryAddress));
        Assert.Equal(8, orders.Select(o => o.Status).Distinct().Count());
        Assert.Equal(2, orders.Select(o => o.CustomerId).Distinct().Count());

        var methods = await _db.Payments.AsNoTracking().Select(p => p.Method).Distinct().CountAsync();
        Assert.Equal(5, methods);
        var shippers = await _db.ShipperAssignments.AsNoTracking()
            .Select(a => a.ShipperId).Distinct().CountAsync();
        Assert.Equal(2, shippers);
        Assert.True(await _db.OrderStatusHistories.AsNoTracking().AnyAsync());

        _db.ChangeTracker.Clear();
        Assert.Equal(0, await seeder.SeedAsync()); // idempotent without --reset-demo
        Assert.Equal(12, await _db.Orders.AsNoTracking().CountAsync());

        _db.ChangeTracker.Clear();
        Assert.Equal(12, await seeder.SeedAsync(reset: true));
        Assert.Equal(12, await _db.Orders.AsNoTracking().CountAsync());
    }
}
