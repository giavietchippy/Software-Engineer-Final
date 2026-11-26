using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Web.Data;

namespace SaigonBite.Web.Services;

/// <summary>BR18 field rules shared by registration, self-profile edits and staff accounts.</summary>
public static class AccountValidation
{
    private static readonly HashSet<string> MobilePrefixes = new(StringComparer.Ordinal)
        { "03", "05", "07", "08", "09" };

    private static readonly Regex EmailPattern = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Local mobile: exactly 10 digits with an allowed prefix (BR18).</summary>
    public static bool IsMobile(string? value) =>
        value is { Length: 10 }
        && value.All(char.IsAsciiDigit)
        && MobilePrefixes.Contains(value[..2]);

    /// <summary>Tourist passport: 6 to 9 ASCII letters or digits (BR18).</summary>
    public static bool IsPassport(string? value) =>
        value is { Length: >= 6 and <= 9 } && value.All(char.IsAsciiLetterOrDigit);

    public static bool IsEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value) && EmailPattern.IsMatch(value.Trim());

    public static bool IsPassword(string? value) => !string.IsNullOrEmpty(value) && value.Length >= 8;

    public static bool IsCustomer(Role role) => role is Role.Local or Role.Tourist;

    /// <summary>A-16: uniqueness includes soft-deleted users and active pending registrations.</summary>
    public static async Task<bool> IdentifierTakenAsync(
        SaigonBiteDbContext db, string normalized, int? excludeUserId = null, int? excludePendingId = null)
    {
        var taken = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(u => u.Identifier == normalized && u.Id != excludeUserId);
        return taken || await db.PendingRegistrations.AsNoTracking()
            .AnyAsync(p => p.Identifier == normalized && !p.IsCancelled && p.Id != excludePendingId);
    }
}
