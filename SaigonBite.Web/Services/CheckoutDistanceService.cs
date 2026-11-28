using System.Globalization;
using Microsoft.AspNetCore.DataProtection;

namespace SaigonBite.Web.Services;

/// <summary>A distance drawn for one checkout, and the signed ticket that carries it between requests.</summary>
public sealed record DistanceQuote(decimal Km, string Ticket, bool IsReused);

/// <summary>
/// Keeps the random distance stable during one checkout. The distance is drawn once, then sealed
/// with ASP.NET Core Data Protection (tamper-proof, bound to user + hub, expires after 30 minutes).
/// The client only ever sends the opaque ticket back; a km value posted by the client is ignored.
/// </summary>
public sealed class CheckoutDistanceService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly IDistanceService _distance;
    private readonly IDataProtector _protector;
    private readonly IClock _clock;

    public CheckoutDistanceService(IDistanceService distance, IDataProtectionProvider provider, IClock clock)
    {
        _distance = distance;
        _protector = provider.CreateProtector("SaigonBite.CheckoutDistance.v1");
        _clock = clock;
    }

    /// <summary>Reuses the distance in a valid ticket, otherwise draws a new one and seals it.</summary>
    public DistanceQuote Resolve(int userId, int hubId, string district, string? ticket)
    {
        if (TryRead(userId, hubId, ticket, out var km))
            return new DistanceQuote(km, ticket!, true);

        var fresh = _distance.GetDistanceKm(hubId, district);
        return new DistanceQuote(fresh, Seal(userId, hubId, fresh), false);
    }

    public string Seal(int userId, int hubId, decimal km)
    {
        var expires = _clock.UtcNow.Add(Lifetime).ToUnixTimeSeconds();
        var payload = string.Join('|', userId, hubId, km.ToString("0.0", CultureInfo.InvariantCulture), expires);
        return _protector.Protect(payload);
    }

    public bool TryRead(int userId, int hubId, string? ticket, out decimal km)
    {
        km = 0m;
        if (string.IsNullOrWhiteSpace(ticket)) return false;
        try
        {
            var parts = _protector.Unprotect(ticket).Split('|');
            if (parts.Length != 4
                || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var u) || u != userId
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var h) || h != hubId
                || !decimal.TryParse(parts[2], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out km)
                || !long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var expires))
                return false;
            return _clock.UtcNow.ToUnixTimeSeconds() <= expires && km > 0m;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false; // forged, corrupted or from another key ring
        }
    }
}
