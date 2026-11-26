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
/// BR18/A-08/A-09/N-02: field boundaries, staged registration, random-salt hashes,
/// deactivation and staff management. Every rejection leaves the user table untouched.
/// </summary>
public sealed class AccountTests : IClassFixture<AccountTestDatabase>
{
    private readonly SaigonBiteDbContext _db;
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero));
    private readonly PasswordHasher<User> _hasher = new();

    public AccountTests(AccountTestDatabase database)
    {
        _db = database.CreateContext();
        database.Reset(); // one throwaway DB per class, restored to the seed state per test
    }

    private RegistrationService Registration => new(_db, _clock, _hasher);
    private LoginService Login => new(_db, _hasher);
    private AccountManagementService Accounts => new(_db, _hasher);

    private Task<int> UserCountAsync() => _db.Users.IgnoreQueryFilters().AsNoTracking().CountAsync();

    private static RegistrationInput Local(string mobile, string password = "Password1") =>
        new(Role.Local, mobile, password);

    private static RegistrationInput Tourist(string passport, string password = "Password1",
        string email = "tourist@example.com", string nationality = "USA") =>
        new(Role.Tourist, passport, password, nationality, email);

    [Theory]
    [InlineData("0912345679", true)]   // 10 digits, prefix 09 (not the seeded 0912345678)
    [InlineData("0312345679", true)]   // prefix 03
    [InlineData("0512345679", true)]   // prefix 05
    [InlineData("0712345679", true)]   // prefix 07
    [InlineData("0812345679", true)]   // prefix 08
    [InlineData("091234567", false)]   // 9 digits
    [InlineData("09123456789", false)] // 11 digits
    [InlineData("0212345679", false)]  // wrong prefix
    [InlineData("09123A5679", false)]  // not all digits
    public async Task Local_mobile_boundaries_BR18(string mobile, bool accepted)
    {
        var before = await UserCountAsync();

        var result = await Registration.StartRegistrationAsync(Local(mobile));

        Assert.Equal(accepted, result.Accepted);
        Assert.Equal(before, await UserCountAsync()); // no User before verification either way
        if (accepted) Assert.Equal(6, result.Code!.Length); // A-08 simulated code
        else Assert.Empty(await _db.PendingRegistrations.ToListAsync());
    }

    [Theory]
    [InlineData("ABC123", true)]      // 6 chars
    [InlineData("ABC123456", true)]   // 9 chars
    [InlineData("AB-123", false)]     // punctuation
    [InlineData("ABC12", false)]      // 5 chars
    [InlineData("ABC1234567", false)] // 10 chars
    public async Task Tourist_passport_boundaries_BR18(string passport, bool accepted)
    {
        var before = await UserCountAsync();

        var result = await Registration.StartRegistrationAsync(Tourist(passport));

        Assert.Equal(accepted, result.Accepted);
        Assert.Equal(before, await UserCountAsync());
        if (!accepted) Assert.Empty(await _db.PendingRegistrations.ToListAsync());
    }

    [Theory]
    [InlineData("1234567", false)] // 7 chars
    [InlineData("12345678", true)] // 8 chars
    public async Task Password_length_boundary_BR18(string password, bool accepted)
    {
        var before = await UserCountAsync();

        var result = await Registration.StartRegistrationAsync(Local("0911111111", password));

        Assert.Equal(accepted, result.Accepted);
        Assert.Equal(before, await UserCountAsync());
        if (!accepted) Assert.Empty(await _db.PendingRegistrations.ToListAsync());
    }

    [Theory]
    [InlineData("not-an-email", false)]
    [InlineData("tourist@example.com", true)]
    public async Task Tourist_email_validation_BR18(string email, bool accepted)
    {
        var before = await UserCountAsync();

        var result = await Registration.StartRegistrationAsync(Tourist("XYZ123", "Password1", email));

        Assert.Equal(accepted, result.Accepted);
        Assert.Equal(before, await UserCountAsync());
        if (!accepted) Assert.Empty(await _db.PendingRegistrations.ToListAsync());
    }

    [Fact]
    public async Task Tourist_nationality_is_required_BR18()
    {
        var before = await UserCountAsync();

        var result = await Registration.StartRegistrationAsync(
            Tourist("XYZ123", "Password1", "a@b.com", string.Empty));

        Assert.False(result.Accepted);
        Assert.Equal(before, await UserCountAsync());
        Assert.Empty(await _db.PendingRegistrations.ToListAsync());
    }

    [Fact]
    public async Task Duplicate_identifier_across_types_and_case_is_rejected_A09()
    {
        var before = await UserCountAsync();
        // Staff identifiers are not BR18-validated, so these can be Shipper identifiers.
        _db.Users.AddRange(
            new User { Role = Role.Shipper, Identifier = "0911111111", PasswordHash = "x", IsActive = true },
            new User { Role = Role.Shipper, Identifier = "XYZ1234", PasswordHash = "x", IsActive = true });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var local = await Registration.StartRegistrationAsync(Local("0911111111"));
        var touristLower = await Registration.StartRegistrationAsync(Tourist("xyz1234")); // normalises to XYZ1234

        Assert.False(local.Accepted);
        Assert.False(touristLower.Accepted);
        Assert.Equal(before + 2, await UserCountAsync());
        Assert.Empty(await _db.PendingRegistrations.ToListAsync());
    }

    [Fact]
    public async Task User_and_profile_are_created_only_after_verification_BR18_A08()
    {
        var before = await UserCountAsync();

        var started = await Registration.StartRegistrationAsync(Local("0911111111"));
        Assert.True(started.Accepted, started.Error);
        Assert.Equal(before, await UserCountAsync());
        var pending = await _db.PendingRegistrations.SingleAsync();
        Assert.Equal("0911111111", pending.Identifier);
        Assert.Equal(6, pending.Code.Length);

        var verified = await Registration.VerifyAsync(started.PendingId!.Value, started.Code);
        Assert.True(verified.Accepted, verified.Error);
        Assert.Equal(before + 1, await UserCountAsync());
        var user = await _db.Users.SingleAsync(u => u.Id == verified.UserId);
        Assert.NotEqual("Password1", user.PasswordHash);
        Assert.False(user.PasswordHash.StartsWith("PBKDF2-SHA256$", StringComparison.Ordinal)); // random salt
        var profile = await _db.LocalCustomerProfiles.SingleAsync(p => p.UserId == user.Id);
        Assert.Equal("0911111111", profile.Mobile);
        Assert.Empty(await _db.PendingRegistrations.ToListAsync());
    }

    [Fact]
    public async Task Equal_passwords_produce_different_random_salt_hashes_BR18()
    {
        var a = await Registration.StartRegistrationAsync(Local("0911111111"));
        var b = await Registration.StartRegistrationAsync(Local("0922222222"));
        var verifiedA = await Registration.VerifyAsync(a.PendingId!.Value, a.Code);
        var verifiedB = await Registration.VerifyAsync(b.PendingId!.Value, b.Code);
        Assert.True(verifiedA.Accepted && verifiedB.Accepted);

        var hashA = (await _db.Users.SingleAsync(u => u.Id == verifiedA.UserId)).PasswordHash;
        var hashB = (await _db.Users.SingleAsync(u => u.Id == verifiedB.UserId)).PasswordHash;
        Assert.NotEqual(hashA, hashB);
        Assert.NotEqual("Password1", hashA);
    }

    [Fact]
    public async Task Wrong_code_does_not_create_a_user_A08()
    {
        var before = await UserCountAsync();
        var started = await Registration.StartRegistrationAsync(Local("0911111111"));
        var wrong = started.Code == "000000" ? "999999" : "000000";

        var result = await Registration.VerifyAsync(started.PendingId!.Value, wrong);

        Assert.False(result.Accepted);
        Assert.Equal(before, await UserCountAsync());
        var pending = await _db.PendingRegistrations.SingleAsync();
        Assert.Equal(1, pending.Attempts);
        Assert.False(pending.IsCancelled);
    }

    [Fact]
    public async Task Expired_code_creates_no_user_A08()
    {
        var before = await UserCountAsync();
        var started = await Registration.StartRegistrationAsync(Local("0911111111"));
        _clock.UtcNow = _clock.UtcNow.AddMinutes(RegistrationService.CodeValidityMinutes + 1);

        var result = await Registration.VerifyAsync(started.PendingId!.Value, started.Code);

        Assert.False(result.Accepted);
        Assert.Equal(before, await UserCountAsync());
        Assert.True((await _db.PendingRegistrations.SingleAsync()).IsCancelled);
    }

    [Fact]
    public async Task Three_failed_attempts_cancel_the_pending_registration_A08()
    {
        var before = await UserCountAsync();
        var started = await Registration.StartRegistrationAsync(Local("0911111111"));
        var wrong = started.Code == "000000" ? "999999" : "000000";

        for (var i = 0; i < RegistrationService.MaxAttempts; i++)
            Assert.False((await Registration.VerifyAsync(started.PendingId!.Value, wrong)).Accepted);

        Assert.Equal(before, await UserCountAsync());
        var pending = await _db.PendingRegistrations.SingleAsync();
        Assert.Equal(RegistrationService.MaxAttempts, pending.Attempts);
        Assert.True(pending.IsCancelled);
        Assert.False((await Registration.VerifyAsync(started.PendingId!.Value, started.Code)).Accepted);
    }

    [Fact]
    public async Task Deactivated_user_cannot_log_in_A09()
    {
        var ok = await Login.LoginAsync("0912345678", "Local@123"); // dev seed random-salt hash (N-02)
        Assert.True(ok.Accepted, ok.Error);

        Assert.True((await Accounts.DeactivateAsync(7, 1)).Accepted);
        var after = await Login.LoginAsync("0912345678", "Local@123");

        Assert.False(after.Accepted);
        var user = await _db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == 7);
        Assert.False(user.IsActive);
        Assert.True(user.IsDeleted);
    }

    [Fact]
    public async Task Newly_registered_user_can_log_in_BR18()
    {
        var started = await Registration.StartRegistrationAsync(Local("0911111111"));
        Assert.True((await Registration.VerifyAsync(started.PendingId!.Value, started.Code)).Accepted);

        var login = await Login.LoginAsync("0911111111", "Password1");

        Assert.True(login.Accepted, login.Error);
        Assert.Equal(Role.Local, login.Role);
    }

    [Fact]
    public async Task Staff_creation_edit_and_listing_BR18()
    {
        var noHub = await Accounts.CreateStaffAsync(
            new StaffAccountInput(Role.KitchenStaff, "KITCHEN9", "Password1", null), 1);
        Assert.False(noHub.Accepted);

        var withHub = await Accounts.CreateStaffAsync(
            new StaffAccountInput(Role.KitchenStaff, "KITCHEN9", "Password1", 1), 1);
        var shipper = await Accounts.CreateStaffAsync(
            new StaffAccountInput(Role.Shipper, "SHIPPER9", "Password1", null), 1);
        Assert.True(withHub.Accepted, withHub.Error);
        Assert.True(shipper.Accepted, shipper.Error);

        var shipperUser = await _db.Users.SingleAsync(u => u.Id == shipper.UserId);
        Assert.Null(shipperUser.HubId);
        Assert.NotEqual("Password1", shipperUser.PasswordHash);

        var edited = await Accounts.UpdateAsync(shipper.UserId!.Value,
            new StaffAccountInput(Role.KitchenStaff, "SHIPPER9", "Password1", 2), 1);
        Assert.True(edited.Accepted, edited.Error);
        Assert.Equal(2, (await _db.Users.SingleAsync(u => u.Id == shipper.UserId)).HubId);

        var found = await Accounts.ListAsync("kitchen");
        Assert.Contains(found, a => a.Identifier == "KITCHEN9");
    }

    [Fact]
    public async Task Last_active_admin_cannot_be_deactivated_A09()
    {
        var second = await Accounts.CreateStaffAsync(
            new StaffAccountInput(Role.Admin, "ADMIN9", "Password1", null), 1);
        Assert.True(second.Accepted, second.Error);

        Assert.True((await Accounts.DeactivateAsync(second.UserId!.Value, 1)).Accepted);
        var last = await Accounts.DeactivateAsync(1, 1);
        Assert.False(last.Accepted);
        Assert.True((await _db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == 1)).IsActive);
        Assert.True((await Accounts.ReactivateAsync(second.UserId!.Value, 1)).Accepted);
    }

    [Fact]
    public async Task Self_profile_edit_uses_the_same_BR18_rules()
    {
        var bad = await Accounts.UpdateOwnProfileAsync(7, new SelfProfileInput("091234567"));
        Assert.False(bad.Accepted);

        var good = await Accounts.UpdateOwnProfileAsync(7, new SelfProfileInput("0911111111"));
        Assert.True(good.Accepted, good.Error);
        var profile = await _db.LocalCustomerProfiles.SingleAsync(p => p.UserId == 7);
        Assert.Equal("0911111111", profile.Mobile);
    }

    [Theory]
    [InlineData("admin", "Admin@123")]
    [InlineData("kitchen-d1", "KitchenD1@123")]
    [InlineData("kitchen-bt", "KitchenBT@123")]
    [InlineData("kitchen-td", "KitchenTD@123")]
    [InlineData("shipper1", "Shipper1@123")]
    [InlineData("shipper2", "Shipper2@123")]
    [InlineData("0912345678", "Local@123")]
    [InlineData("A1234567", "Tourist@123")]
    public async Task Every_seed_account_can_log_in_with_random_salt_hash_N02(
        string identifier, string password)
    {
        var result = await Login.LoginAsync(identifier, password);

        Assert.True(result.Accepted, result.Error);
        var hash = (await _db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == result.UserId)).PasswordHash;
        Assert.NotEqual(SeedCredential.PendingHash, hash);
        Assert.False(hash.StartsWith("PBKDF2-SHA256$", StringComparison.Ordinal));
        Assert.False(await _db.PendingRegistrations.AnyAsync());
    }

    private sealed class TestClock : IClock
    {
        public TestClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; set; }
    }
}

/// <summary>
/// One LocalDB database for the whole AccountTests class: 20+ per-test databases made LocalDB
/// time out on this machine. Reset() restores the seeded state before every test case.
/// </summary>
public sealed class AccountTestDatabase : IDisposable
{
    private readonly string _connectionString =
        $"Server=(localdb)\\MSSQLLocalDB;Database=SaigonBite_ACC_{Guid.NewGuid():N};" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    public AccountTestDatabase()
    {
        using var db = CreateContext();
        db.Database.EnsureCreated();

        // N-02: tests use the same random-salt seed finalisation as Program.cs.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SeedPasswords:Admin"] = "Admin@123",
            ["SeedPasswords:Kitchen-D1"] = "KitchenD1@123",
            ["SeedPasswords:Kitchen-BT"] = "KitchenBT@123",
            ["SeedPasswords:Kitchen-TD"] = "KitchenTD@123",
            ["SeedPasswords:Shipper1"] = "Shipper1@123",
            ["SeedPasswords:Shipper2"] = "Shipper2@123",
            ["SeedPasswords:Local"] = "Local@123",
            ["SeedPasswords:Tourist"] = "Tourist@123"
        }).Build();
        new SeedUserInitializer(db, new PasswordHasher<User>(), config,
            new DevelopmentEnvironment(), NullLogger<SeedUserInitializer>.Instance)
            .SeedAsync().GetAwaiter().GetResult();
    }

    public SaigonBiteDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<SaigonBiteDbContext>().UseSqlServer(_connectionString).Options);

    public void Reset()
    {
        using var db = CreateContext();
        db.PendingRegistrations.RemoveRange(db.PendingRegistrations);
        db.LocalCustomerProfiles.RemoveRange(db.LocalCustomerProfiles.Where(p => p.UserId > 8));
        db.TouristProfiles.RemoveRange(db.TouristProfiles.Where(p => p.UserId > 8));
        db.Users.RemoveRange(db.Users.IgnoreQueryFilters().Where(u => u.Id > 8));

        var seed = new Dictionary<int, (string Identifier, Role Role, int? HubId)>
        {
            [1] = ("admin", Role.Admin, null),
            [2] = ("kitchen-d1", Role.KitchenStaff, 1),
            [3] = ("kitchen-bt", Role.KitchenStaff, 2),
            [4] = ("kitchen-td", Role.KitchenStaff, 3),
            [5] = ("shipper1", Role.Shipper, null),
            [6] = ("shipper2", Role.Shipper, null),
            [7] = ("0912345678", Role.Local, null),
            [8] = ("A1234567", Role.Tourist, null)
        };
        foreach (var user in db.Users.IgnoreQueryFilters().Where(u => u.Id <= 8))
        {
            var (identifier, role, hubId) = seed[user.Id];
            user.Identifier = identifier;
            user.Role = role;
            user.HubId = hubId;
            user.IsActive = true;
            user.IsDeleted = false;
        }
        db.LocalCustomerProfiles.Single(p => p.UserId == 7).Mobile = "0912345678";
        var tourist = db.TouristProfiles.Single(p => p.UserId == 8);
        tourist.Passport = "A1234567";
        tourist.Nationality = "USA";
        tourist.Email = "tourist@example.com";
        db.SaveChanges();
    }

    public void Dispose()
    {
        using var db = CreateContext();
        db.Database.EnsureDeleted();
    }

    private sealed class DevelopmentEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "SaigonBite.Web.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
