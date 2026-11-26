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
/// BR18/A-09/N-02: login reads the identifier from the database (case-insensitive normalisation),
/// refuses deactivated accounts, and verifies only ASP.NET Core random-salt hashes. A skipped
/// Production seed account keeps its placeholder hash and simply fails login.
/// </summary>
public sealed class LoginService
{
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
        if (string.IsNullOrEmpty(password))
            return LoginResult.Fail("Incorrect password.");
        // N-02: a Production seed account skipped for a missing env password stays unprovisioned.
        if (string.IsNullOrWhiteSpace(user.PasswordHash) || user.PasswordHash == SeedCredential.PendingHash)
            return LoginResult.Fail("Incorrect password.");

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded
            ? LoginResult.Ok(user)
            : LoginResult.Fail("Incorrect password.");
    }
}
