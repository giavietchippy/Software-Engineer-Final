using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SaigonBite.Core;
using SaigonBite.Web.Data;

namespace SaigonBite.Web.Services;

/// <summary>
/// A-22: finalises the eight seed accounts with real random-salt ASP.NET Core hashes. Runs in every
/// environment, including Production. Passwords come from SeedPasswords config or the matching
/// SAIGONBITE_SEED_*_PASSWORD environment variable; dev defaults are documented in README.md.
/// </summary>
public sealed class SeedUserInitializer
{
    private readonly SaigonBiteDbContext _db;
    private readonly PasswordHasher<User> _hasher;
    private readonly IConfiguration _configuration;

    public SeedUserInitializer(SaigonBiteDbContext db, PasswordHasher<User> hasher, IConfiguration configuration)
    {
        _db = db;
        _hasher = hasher;
        _configuration = configuration;
    }

    public async Task SeedAsync()
    {
        foreach (var spec in Specs())
        {
            var password = PasswordFor(spec.Key, spec.DefaultPassword);
            if (password.Length < 8)
                throw new InvalidOperationException(
                    $"Seed password for {spec.Key} must be at least 8 characters (BR18).");

            var user = await _db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Id == spec.Id);
            if (user is null)
            {
                user = new User { Id = spec.Id, Role = spec.Role, Identifier = spec.Identifier };
                _db.Users.Add(user);
            }

            user.Role = spec.Role;
            user.Identifier = spec.Identifier;
            user.HubId = spec.HubId;
            user.IsActive = true;
            user.IsDeleted = false;

            var needsHash = user.PasswordHash == SeedCredential.PendingHash
                            || _hasher.VerifyHashedPassword(user, user.PasswordHash, password)
                            == PasswordVerificationResult.Failed;
            if (needsHash)
            {
                // Every call uses a fresh random salt, so identical passwords produce different hashes.
                user.PasswordHash = _hasher.HashPassword(user, password);
            }
        }

        var local = await _db.LocalCustomerProfiles.SingleOrDefaultAsync(p => p.UserId == 7);
        if (local is null)
            _db.LocalCustomerProfiles.Add(new LocalCustomerProfile
            { Id = 1, UserId = 7, Mobile = "0912345678" });
        else
            local.Mobile = "0912345678";

        var tourist = await _db.TouristProfiles.SingleOrDefaultAsync(p => p.UserId == 8);
        if (tourist is null)
            _db.TouristProfiles.Add(new TouristProfile
            { Id = 1, UserId = 8, Passport = "A1234567", Nationality = "USA", Email = "tourist@example.com" });
        else
        {
            tourist.Passport = "A1234567";
            tourist.Nationality = "USA";
            tourist.Email = "tourist@example.com";
        }

        await _db.SaveChangesAsync();
    }

    private string PasswordFor(string key, string defaultPassword)
    {
        var environmentVariable = "SAIGONBITE_SEED_" + key.ToUpperInvariant().Replace("-", "_") + "_PASSWORD";
        return _configuration[$"SeedPasswords:{key}"]
               ?? Environment.GetEnvironmentVariable(environmentVariable)
               ?? defaultPassword;
    }

    private static List<Spec> Specs() => new()
    {
        new(1, "Admin", Role.Admin, "admin", null, "Admin@123"),
        new(2, "Kitchen-D1", Role.KitchenStaff, "kitchen-d1", 1, "KitchenD1@123"),
        new(3, "Kitchen-BT", Role.KitchenStaff, "kitchen-bt", 2, "KitchenBT@123"),
        new(4, "Kitchen-TD", Role.KitchenStaff, "kitchen-td", 3, "KitchenTD@123"),
        new(5, "Shipper1", Role.Shipper, "shipper1", null, "Shipper1@123"),
        new(6, "Shipper2", Role.Shipper, "shipper2", null, "Shipper2@123"),
        new(7, "Local", Role.Local, "0912345678", null, "Local@123"),
        new(8, "Tourist", Role.Tourist, "A1234567", null, "Tourist@123")
    };

    private sealed record Spec(
        int Id, string Key, Role Role, string Identifier, int? HubId, string DefaultPassword);
}
