using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Web.Data;

namespace SaigonBite.Web.Services;

public sealed record LoginResult(bool Accepted, int? UserId, Role? Role, string? Identifier, string? Error)
{
    public static LoginResult Ok(User user) => new(true, user.Id, user.Role, user.Identifier, null);
    public static LoginResult Fail(string error) => new(false, null, null, null, error);
}

/// <summary>
/// BR18/A-17/A-22: login reads the identifier from the database (case-insensitive normalisation),
/// refuses deactivated accounts, and verifies new random-salt hashes or legacy seed hashes.
/// </summary>
public sealed class LoginService
{
    private const string LegacyPrefix = "PBKDF2-SHA256$";

    private readonly SaigonBiteDbContext _db;
    private readonly PasswordHasher<User> _hasher;

    public LoginService(SaigonBiteDbContext db, PasswordHasher<User> hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    public async Task<LoginResult> LoginAsync(string? identifier, string? password)
    {
        var normalized = AccountValidation.Normalize(identifier);
        var user = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(u => u.Identifier == normalized);
        if (user is null || user.IsDeleted || !user.IsActive)
            return LoginResult.Fail("Account not found or deactivated.");
        if (string.IsNullOrEmpty(password) || !VerifyPassword(user, password))
            return LoginResult.Fail("Incorrect password.");
        return LoginResult.Ok(user);
    }

    private bool VerifyPassword(User user, string password)
    {
        // A-22: the deterministic seed hash is still accepted for DEV seed logins only.
        // New accounts always use ASP.NET Core PasswordHasher<User> with a random salt (BR18).
        if (user.PasswordHash.StartsWith(LegacyPrefix, StringComparison.Ordinal))
        {
            var parts = user.PasswordHash.Split('$');
            if (parts.Length != 4) return false;
            var candidate = SeedPasswordHasher.Hash(password, parts[2]).Split('$')[3];
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(parts[3]), Encoding.UTF8.GetBytes(candidate));
        }

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return result is PasswordVerificationResult.Success
            or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
