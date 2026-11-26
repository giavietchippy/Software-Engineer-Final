using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SaigonBite.Core;
using SaigonBite.Web.Data;
using SaigonBite.Web.Services;

namespace SaigonBite.Web.Tests;

/// <summary>
/// N-02: seed credentials get random-salt hashes; Production requires environment-only passwords.
/// </summary>
public sealed class SeedCredentialTests : IDisposable
{
    private static readonly string[] Keys =
    {
        "Admin", "Kitchen-D1", "Kitchen-BT", "Kitchen-TD",
        "Shipper1", "Shipper2", "Local", "Tourist"
    };

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
    public async Task Two_seed_users_with_the_same_password_get_different_hashes_and_can_log_in_N02()
    {
        var hasher = new PasswordHasher<User>();
        await Initializer("Development", DevConfig(), NullLogger<SeedUserInitializer>.Instance).SeedAsync();

        var adminHash = (await _db.Users.SingleAsync(u => u.Id == 1)).PasswordHash;
        var shipperHash = (await _db.Users.SingleAsync(u => u.Id == 5)).PasswordHash;
        Assert.NotEqual(adminHash, shipperHash); // random salt, not a deterministic seed hash
        Assert.False(adminHash.StartsWith("PBKDF2-SHA256$", StringComparison.Ordinal));

        var login = new LoginService(_db, hasher);
        Assert.True((await login.LoginAsync("admin", "Shared@123")).Accepted);
        Assert.True((await login.LoginAsync("shipper1", "Shared@123")).Accepted);
    }

    [Fact]
    public async Task Production_without_environment_passwords_skips_accounts_and_warns_N02()
    {
        using var env = UsePasswords(Keys.ToDictionary(k => k, _ => (string?)null));
        var logger = new RecordingLogger();

        await Initializer("Production", DevConfig(), logger).SeedAsync();

        Assert.Equal(8, logger.Warnings.Count);
        Assert.All(logger.Warnings, w => Assert.Contains("SAIGONBITE_SEED_", w));
        var admin = await _db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == 1);
        var tourist = await _db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == 8);
        Assert.Equal(SeedCredential.PendingHash, admin.PasswordHash);
        Assert.Equal(SeedCredential.PendingHash, tourist.PasswordHash);

        var login = new LoginService(_db, new PasswordHasher<User>());
        Assert.False((await login.LoginAsync("admin", "Admin@123")).Accepted);
    }

    [Fact]
    public async Task Production_uses_environment_password_not_config_or_default_N02()
    {
        using var env = UsePasswords(Keys.ToDictionary(k => k, _ => (string?)"ProdOnly@2026"));
        var logger = new RecordingLogger();

        await Initializer("Production", DevConfig(), logger).SeedAsync();

        Assert.Empty(logger.Warnings);
        var hasher = new PasswordHasher<User>();
        var login = new LoginService(_db, hasher);
        Assert.True((await login.LoginAsync("admin", "ProdOnly@2026")).Accepted);
        Assert.False((await login.LoginAsync("admin", "Admin@123")).Accepted);
    }

    private SeedUserInitializer Initializer(string environment, IConfiguration config,
        ILogger<SeedUserInitializer> logger) =>
        new(_db, new PasswordHasher<User>(), config, new TestEnvironment(environment), logger);

    private static IConfiguration DevConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SeedPasswords:Admin"] = "Shared@123",
            ["SeedPasswords:Shipper1"] = "Shared@123",
            ["SeedPasswords:Kitchen-D1"] = "KitchenD1@123",
            ["SeedPasswords:Kitchen-BT"] = "KitchenBT@123",
            ["SeedPasswords:Kitchen-TD"] = "KitchenTD@123",
            ["SeedPasswords:Shipper2"] = "Shipper2@123",
            ["SeedPasswords:Local"] = "Local@123",
            ["SeedPasswords:Tourist"] = "Tourist@123"
        }).Build();

    private static string Variable(string key) =>
        "SAIGONBITE_SEED_" + key.ToUpperInvariant().Replace("-", "_") + "_PASSWORD";

    private static IDisposable UsePasswords(Dictionary<string, string?> values)
    {
        var saved = Keys.ToDictionary(Variable, Environment.GetEnvironmentVariable);
        foreach (var (key, value) in values)
            Environment.SetEnvironmentVariable(Variable(key), value);
        return new RestoreEnvironment(saved);
    }

    private sealed class RestoreEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> _saved;

        public RestoreEnvironment(Dictionary<string, string?> saved) => _saved = saved;

        public void Dispose()
        {
            foreach (var (name, value) in _saved)
                Environment.SetEnvironmentVariable(name, value);
        }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public TestEnvironment(string name) => EnvironmentName = name;

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "SaigonBite.Web.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class RecordingLogger : ILogger<SeedUserInitializer>
    {
        public List<string> Warnings { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
