using Microsoft.EntityFrameworkCore;
using SaigonBite.Web.Data;
using SaigonBite.Web.Services;

namespace SaigonBite.Web.Tests;

/// <summary>FR-D14 / BR5: rain flag defaults to false and only an active Admin may change it.</summary>
public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _connectionString =
        $"Server=(localdb)\\MSSQLLocalDB;Database=SaigonBite_Settings_{Guid.NewGuid():N};" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private readonly SaigonBiteDbContext _db;

    public SettingsServiceTests()
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
    public void HeavyRain_Defaults_To_False()
    {
        Assert.False(new SettingsService(_db).GetHeavyRain());
    }

    [Fact]
    public async Task NonAdmin_Cannot_Set_HeavyRain()
    {
        var service = new SettingsService(_db);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SetHeavyRainAsync(true, 7));

        Assert.False(service.GetHeavyRain());
    }

    [Fact]
    public async Task Admin_Can_Set_HeavyRain_And_It_Persists()
    {
        var service = new SettingsService(_db);

        await service.SetHeavyRainAsync(true, 1);
        _db.ChangeTracker.Clear();

        Assert.True(service.GetHeavyRain());
        var row = await _db.SystemSettings.AsNoTracking()
            .SingleAsync(x => x.Key == SettingsService.HeavyRainKey);
        Assert.Equal(1, row.UpdatedByUserId);
    }
}
