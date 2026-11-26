using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Web.Data;

namespace SaigonBite.Web.Services;

/// <summary>FR-D14 / BR5: Admin-controlled system flags. No UI or HTTP code here.</summary>
public sealed class SettingsService
{
    public const string HeavyRainKey = "HeavyRain";

    private readonly SaigonBiteDbContext _db;
    public SettingsService(SaigonBiteDbContext db) => _db = db;

    public bool GetHeavyRain() =>
        bool.Parse(_db.SystemSettings.AsNoTracking().Single(s => s.Key == HeavyRainKey).Value);

    /// <summary>Only an active Admin may set the rain flag (BR5, A-09).</summary>
    public async Task SetHeavyRainAsync(bool value, int actorUserId)
    {
        var actor = await _db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == actorUserId);
        if (actor is null || actor.Role != Role.Admin || !actor.IsActive)
            throw new UnauthorizedAccessException("Only an active Admin can change system settings (BR5).");

        var setting = await _db.SystemSettings.SingleAsync(s => s.Key == HeavyRainKey);
        setting.Value = value ? "true" : "false";
        setting.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified); // UTC+7
        setting.UpdatedByUserId = actorUserId;
        await _db.SaveChangesAsync();
    }
}
