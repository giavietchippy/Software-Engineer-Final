using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Web.Data;

namespace SaigonBite.Web.Services;

public sealed record StaffAccountInput(Role Role, string Identifier, string Password, int? HubId);
public sealed record AccountDto(
    int Id, Role Role, string Identifier, int? HubId, bool IsActive, bool IsDeleted);
public sealed record SelfProfileDto(
    int Id, Role Role, string Identifier, string? Mobile, string? Passport,
    string? Nationality, string? Email);
public sealed record SelfProfileInput(
    string Identifier, string? Password = null, string Nationality = "", string Email = "");
public sealed record AccountResult(bool Accepted, int? UserId, string? Error)
{
    public static AccountResult Ok(int userId) => new(true, userId, null);
    public static AccountResult Fail(string error) => new(false, null, error);
}

/// <summary>
/// BR18/A-09: Admin staff management and self-service profile edits. Accounts are never
/// hard-deleted; deactivation sets IsActive=false and IsDeleted=true and keeps order history intact.
/// </summary>
public sealed class AccountManagementService
{
    private readonly SaigonBiteDbContext _db;
    private readonly PasswordHasher<User> _hasher;

    public AccountManagementService(SaigonBiteDbContext db, PasswordHasher<User> hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    public async Task<AccountResult> CreateStaffAsync(StaffAccountInput input, int adminUserId)
    {
        if (!await IsAdminAsync(adminUserId))
            return AccountResult.Fail("Only an active Admin can manage accounts.");
        var error = await ValidateStaffAsync(input, null);
        if (error is not null) return AccountResult.Fail(error);

        var user = new User
        {
            Role = input.Role,
            Identifier = AccountValidation.Normalize(input.Identifier),
            PasswordHash = _hasher.HashPassword(new User(), input.Password), // random salt (BR18)
            HubId = input.Role == Role.KitchenStaff ? input.HubId : null,
            IsActive = true,
            IsDeleted = false
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return AccountResult.Ok(user.Id);
    }

    public async Task<IReadOnlyList<AccountDto>> ListAsync(string? search = null)
    {
        var users = await _db.Users.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        var term = (search ?? string.Empty).Trim();
        return users
            .Where(u => term.Length == 0
                        || u.Identifier.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(u => u.Id)
            .Select(u => new AccountDto(u.Id, u.Role, u.Identifier, u.HubId, u.IsActive, u.IsDeleted))
            .ToList();
    }

    public async Task<OperationResult> UpdateAsync(int userId, StaffAccountInput input, int adminUserId)
    {
        if (!await IsAdminAsync(adminUserId))
            return OperationResult.Fail("Only an active Admin can manage accounts.");
        var user = await _db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Id == userId);
        if (user is null) return OperationResult.Fail("User not found.");
        var error = await ValidateStaffAsync(input, userId);
        if (error is not null) return OperationResult.Fail(error);

        user.Role = input.Role;
        user.Identifier = AccountValidation.Normalize(input.Identifier);
        user.HubId = input.Role == Role.KitchenStaff ? input.HubId : null;
        if (!string.IsNullOrEmpty(input.Password))
            user.PasswordHash = _hasher.HashPassword(user, input.Password);
        await _db.SaveChangesAsync();
        return OperationResult.Ok();
    }

    public async Task<OperationResult> DeactivateAsync(int userId, int adminUserId)
    {
        if (!await IsAdminAsync(adminUserId))
            return OperationResult.Fail("Only an active Admin can manage accounts.");
        var user = await _db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Id == userId);
        if (user is null) return OperationResult.Fail("User not found.");
        if (user.Role == Role.Admin && user.IsActive && !user.IsDeleted
            && await ActiveAdminCountAsync() <= 1)
            return OperationResult.Fail("The last active Admin cannot be deactivated.");

        user.IsActive = false;
        user.IsDeleted = true; // A-09: soft delete, never a hard delete
        await _db.SaveChangesAsync();
        return OperationResult.Ok();
    }

    public async Task<OperationResult> ReactivateAsync(int userId, int adminUserId)
    {
        if (!await IsAdminAsync(adminUserId))
            return OperationResult.Fail("Only an active Admin can manage accounts.");
        var user = await _db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Id == userId);
        if (user is null) return OperationResult.Fail("User not found.");

        user.IsDeleted = false;
        user.IsActive = true; // A-09: reactivation restores both flags
        await _db.SaveChangesAsync();
        return OperationResult.Ok();
    }

    public async Task<SelfProfileDto?> GetOwnProfileAsync(int userId)
    {
        var user = await _db.Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId && u.IsActive);
        if (user is null) return null;

        var local = await _db.LocalCustomerProfiles.AsNoTracking()
            .SingleOrDefaultAsync(p => p.UserId == userId);
        var tourist = await _db.TouristProfiles.AsNoTracking()
            .SingleOrDefaultAsync(p => p.UserId == userId);
        return new SelfProfileDto(user.Id, user.Role, user.Identifier,
            local?.Mobile, tourist?.Passport, tourist?.Nationality, tourist?.Email);
    }

    public async Task<OperationResult> UpdateOwnProfileAsync(int userId, SelfProfileInput input)
    {
        var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive);
        if (user is null) return OperationResult.Fail("User not found or inactive.");
        if (input is null) return OperationResult.Fail("Profile data is required (BR18).");

        var identifier = AccountValidation.Normalize(input.Identifier);
        if (user.Role == Role.Local && !AccountValidation.IsMobile(identifier))
            return OperationResult.Fail("Local mobile must be 10 digits starting 03/05/07/08/09 (BR18).");
        if (user.Role == Role.Tourist && !AccountValidation.IsPassport(identifier))
            return OperationResult.Fail("Tourist passport must be 6 to 9 letters or digits (BR18).");
        if (user.Role == Role.Tourist)
        {
            if (string.IsNullOrWhiteSpace(input.Nationality))
                return OperationResult.Fail("Nationality is required for a Tourist (BR18).");
            if (!AccountValidation.IsEmail(input.Email))
                return OperationResult.Fail("A valid e-mail is required for a Tourist (BR18).");
        }
        if (identifier.Length == 0)
            return OperationResult.Fail("Identifier is required (BR18).");
        if (await AccountValidation.IdentifierTakenAsync(_db, identifier, userId))
            return OperationResult.Fail("This identifier is already registered (BR18/A-09).");
        if (!string.IsNullOrEmpty(input.Password) && !AccountValidation.IsPassword(input.Password))
            return OperationResult.Fail("Password must be at least 8 characters (BR18).");

        user.Identifier = identifier;
        if (!string.IsNullOrEmpty(input.Password))
            user.PasswordHash = _hasher.HashPassword(user, input.Password);

        if (user.Role == Role.Local)
        {
            var local = await _db.LocalCustomerProfiles.SingleAsync(p => p.UserId == userId);
            local.Mobile = identifier;
        }
        else if (user.Role == Role.Tourist)
        {
            var tourist = await _db.TouristProfiles.SingleAsync(p => p.UserId == userId);
            tourist.Passport = identifier;
            tourist.Nationality = input.Nationality.Trim();
            tourist.Email = input.Email.Trim();
        }
        // A-23: staff profiles have no extra BR18 fields; only the identifier can change.

        await _db.SaveChangesAsync();
        return OperationResult.Ok();
    }

    private async Task<bool> IsAdminAsync(int adminUserId) =>
        await _db.Users.AsNoTracking().AnyAsync(u =>
            u.Id == adminUserId && u.Role == Role.Admin && u.IsActive && !u.IsDeleted);

    private Task<int> ActiveAdminCountAsync() =>
        _db.Users.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(u => u.Role == Role.Admin && u.IsActive && !u.IsDeleted);

    private async Task<string?> ValidateStaffAsync(StaffAccountInput input, int? excludeUserId)
    {
        if (input is null) return "Account data is required.";
        if (input.Role is not (Role.KitchenStaff or Role.Shipper or Role.Admin))
            return "Admin can only create KitchenStaff, Shipper or Admin accounts.";
        if (string.IsNullOrWhiteSpace(input.Identifier))
            return "Identifier is required (BR18).";
        if (excludeUserId is null && !AccountValidation.IsPassword(input.Password))
            return "Password must be at least 8 characters (BR18).";
        if (excludeUserId is not null && !string.IsNullOrEmpty(input.Password)
            && !AccountValidation.IsPassword(input.Password))
            return "Password must be at least 8 characters (BR18).";
        if (input.Role == Role.KitchenStaff)
        {
            if (input.HubId is null || !await _db.Hubs.AnyAsync(h => h.Id == input.HubId && !h.IsDeleted))
                return "Kitchen staff must belong to an active hub.";
        }
        if (await AccountValidation.IdentifierTakenAsync(
                _db, AccountValidation.Normalize(input.Identifier), excludeUserId))
            return "This identifier is already registered (BR18/A-09).";
        return null;
    }
}
