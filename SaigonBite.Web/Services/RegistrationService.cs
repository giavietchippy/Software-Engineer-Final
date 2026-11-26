using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Web.Data;

namespace SaigonBite.Web.Services;

public sealed record RegistrationInput(
    Role Role, string Identifier, string Password, string Nationality = "", string Email = "");

public sealed record RegistrationResult(bool Accepted, int? PendingId, string? Code, string? Error)
{
    public static RegistrationResult Ok(int pendingId, string code) => new(true, pendingId, code, null);
    public static RegistrationResult Fail(string error) => new(false, null, null, error);
}

public sealed record VerificationResult(bool Accepted, int? UserId, Role? Role, string? Error)
{
    public static VerificationResult Ok(int userId, Role role) => new(true, userId, role, null);
    public static VerificationResult Fail(string error) => new(false, null, null, error);
}

public sealed record LocalProfileData(string Mobile);
public sealed record TouristProfileData(string Passport, string Nationality, string Email);

/// <summary>
/// BR18/A-08/A-09: validate, hash with a random salt, stage the registration, and create the
/// User/profile only after the simulated code is verified. No SMS/e-mail is sent in this demo.
/// </summary>
public sealed class RegistrationService
{
    public const int CodeValidityMinutes = 5;
    public const int MaxAttempts = 3;

    private readonly SaigonBiteDbContext _db;
    private readonly IClock _clock;
    private readonly PasswordHasher<User> _hasher;

    public RegistrationService(SaigonBiteDbContext db, IClock clock, PasswordHasher<User> hasher)
    {
        _db = db;
        _clock = clock;
        _hasher = hasher;
    }

    public async Task<RegistrationResult> StartRegistrationAsync(RegistrationInput input)
    {
        if (input is null) return RegistrationResult.Fail("Registration data is required (BR18).");
        if (!AccountValidation.IsCustomer(input.Role))
            return RegistrationResult.Fail("Only Local and Tourist customers can self-register (BR18).");

        var identifier = AccountValidation.Normalize(input.Identifier);
        if (input.Role == Role.Local && !AccountValidation.IsMobile(identifier))
            return RegistrationResult.Fail("Local mobile must be 10 digits starting 03/05/07/08/09 (BR18).");
        if (input.Role == Role.Tourist && !AccountValidation.IsPassport(identifier))
            return RegistrationResult.Fail("Tourist passport must be 6 to 9 letters or digits (BR18).");
        if (!AccountValidation.IsPassword(input.Password))
            return RegistrationResult.Fail("Password must be at least 8 characters (BR18).");
        if (input.Role == Role.Tourist)
        {
            if (string.IsNullOrWhiteSpace(input.Nationality))
                return RegistrationResult.Fail("Nationality is required for a Tourist (BR18).");
            if (!AccountValidation.IsEmail(input.Email))
                return RegistrationResult.Fail("A valid e-mail is required for a Tourist (BR18).");
        }
        if (await AccountValidation.IdentifierTakenAsync(_db, identifier))
            return RegistrationResult.Fail("This identifier is already registered (BR18/A-09).");

        var now = VietnamTime.FromUtc(_clock.UtcNow);
        var profileJson = input.Role == Role.Local
            ? JsonSerializer.Serialize(new LocalProfileData(identifier))
            : JsonSerializer.Serialize(new TouristProfileData(
                identifier, input.Nationality.Trim(), input.Email.Trim()));

        var pending = new PendingRegistration
        {
            Identifier = identifier,
            Role = input.Role,
            ProfileJson = profileJson,
            PasswordHash = _hasher.HashPassword(new User(), input.Password), // random salt (BR18)
            Code = NewCode(),
            ExpiresAt = now.AddMinutes(CodeValidityMinutes),
            Attempts = 0,
            IsCancelled = false,
            CreatedAt = now
        };
        _db.PendingRegistrations.Add(pending);
        await _db.SaveChangesAsync();
        return RegistrationResult.Ok(pending.Id, pending.Code); // A-08: code shown for the demo
    }

    public async Task<VerificationResult> VerifyAsync(int pendingId, string? code)
    {
        var pending = await _db.PendingRegistrations.SingleOrDefaultAsync(p => p.Id == pendingId);
        if (pending is null || pending.IsCancelled)
            return VerificationResult.Fail("This pending registration no longer exists.");

        var now = VietnamTime.FromUtc(_clock.UtcNow);
        if (pending.ExpiresAt <= now)
        {
            pending.IsCancelled = true; // A-08: expired staging is cancelled, never converted
            await _db.SaveChangesAsync();
            return VerificationResult.Fail("The code has expired; please register again.");
        }

        if (!string.Equals(pending.Code, (code ?? string.Empty).Trim(), StringComparison.Ordinal))
        {
            pending.Attempts++;
            if (pending.Attempts >= MaxAttempts)
                pending.IsCancelled = true; // A-08: three failures cancel the pending row
            await _db.SaveChangesAsync();
            var left = Math.Max(0, MaxAttempts - pending.Attempts);
            return VerificationResult.Fail($"Incorrect code. {left} attempt(s) left.");
        }

        if (await AccountValidation.IdentifierTakenAsync(_db, pending.Identifier, excludePendingId: pending.Id))
        {
            pending.IsCancelled = true;
            await _db.SaveChangesAsync();
            return VerificationResult.Fail("This identifier is already registered (BR18/A-09).");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var user = new User
            {
                Role = pending.Role,
                Identifier = pending.Identifier,
                PasswordHash = pending.PasswordHash,
                IsActive = true,
                IsDeleted = false
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync(); // identity for the one profile row

            if (pending.Role == Role.Local)
            {
                var data = JsonSerializer.Deserialize<LocalProfileData>(pending.ProfileJson)!;
                _db.LocalCustomerProfiles.Add(new LocalCustomerProfile
                {
                    UserId = user.Id, Mobile = data.Mobile
                });
            }
            else
            {
                var data = JsonSerializer.Deserialize<TouristProfileData>(pending.ProfileJson)!;
                _db.TouristProfiles.Add(new TouristProfile
                {
                    UserId = user.Id, Passport = data.Passport,
                    Nationality = data.Nationality, Email = data.Email
                });
            }

            _db.PendingRegistrations.Remove(pending); // A-08: consumed staging is removed
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            return VerificationResult.Ok(user.Id, user.Role);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static string NewCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
}
