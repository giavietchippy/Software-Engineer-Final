using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SaigonBite.Web.Data;
using SaigonBite.Web.Services;

namespace SaigonBite.Web.Tests;

/// <summary>A-22: seed credentials are generated at runtime with random salt in every environment.</summary>
public sealed class SeedCredentialTests : IDisposable
{
    private readonly string _connectionString =
        $"Server=(localdb)\\MSSQLLocalDB;Database=SaigonBite_SEED_{Guid.NewGuid():N};" +
        "Trusted_Connection=True;TrustServerCertificate=True;";
    private readonly SaigonBiteDbContext _db;

    public SeedCredentialTests()
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
    public async Task Two_seed_users_with_the_same_password_get_different_hashes_and_can_log_in_A22()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SeedPasswords:Admin"] = "Shared@123",       // same password...
            ["SeedPasswords:Shipper1"] = "Shared@123",   // ...as this seed account
            ["SeedPasswords:Kitchen-D1"] = "KitchenD1@123",
            ["SeedPasswords:Kitchen-BT"] = "KitchenBT@123",
            ["SeedPasswords:Kitchen-TD"] = "KitchenTD@123",
            ["SeedPasswords:Shipper2"] = "Shipper2@123",
            ["SeedPasswords:Local"] = "Local@123",
            ["SeedPasswords:Tourist"] = "Tourist@123"
        }).Build();
        var hasher = new PasswordHasher<User>();
        await new SeedUserInitializer(_db, hasher, config).SeedAsync();

        var adminHash = (await _db.Users.SingleAsync(u => u.Id == 1)).PasswordHash;
        var shipperHash = (await _db.Users.SingleAsync(u => u.Id == 5)).PasswordHash;
        Assert.NotEqual(adminHash, shipperHash); // random salt, not a deterministic seed hash
        Assert.False(adminHash.StartsWith("PBKDF2-SHA256$", StringComparison.Ordinal));

        var login = new LoginService(_db, hasher);
        Assert.True((await login.LoginAsync("admin", "Shared@123")).Accepted);
        Assert.True((await login.LoginAsync("shipper1", "Shared@123")).Accepted);
    }
}
