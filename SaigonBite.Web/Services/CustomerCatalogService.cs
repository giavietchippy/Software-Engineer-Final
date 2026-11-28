using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Core.Pricing;
using SaigonBite.Web.Data;

namespace SaigonBite.Web.Services;

// FR-C1/C2: read-only catalog data for the customer UI. All thresholds come from Core constants;
// controllers and Razor views contain no business rules.
public sealed record HubCard(int Id, string Name, int Busy, int Capacity, decimal Load,
    bool IsBusy, bool IsQuiet);

public sealed record MenuItemView(int Id, string Name, string Category, decimal Price);

public sealed record MenuBrand(int Id, string Name, IReadOnlyList<MenuItemView> Items);

public sealed record DistrictOption(string Value, string LabelKey);

public interface IDistrictCatalog
{
    IReadOnlyList<DistrictOption> Districts { get; }
}

// A-06: this is only the dropdown source; DistanceService remains the single distance authority.
public sealed class DistrictCatalog : IDistrictCatalog
{
    public IReadOnlyList<DistrictOption> Districts => new[]
    {
        new DistrictOption("District 1", "District1"),
        new DistrictOption("District 2", "District2"),
        new DistrictOption("Binh Thanh", "BinhThanh"),
        new DistrictOption("Thu Duc", "ThuDuc"),
        new DistrictOption("District 7", "District7")
    };
}

/// <summary>
/// FR-C1/C2: hub cards (BR8/BR7 flags) and the hub's available, non-deleted menu (BR12/A-09).
/// </summary>
public sealed class CustomerCatalogService
{
    private readonly SaigonBiteDbContext _db;

    public CustomerCatalogService(SaigonBiteDbContext db) => _db = db;

    public async Task<IReadOnlyList<HubCard>> GetHubCardsAsync()
    {
        var hubs = await _db.Hubs.AsNoTracking()
            .Where(h => !h.IsDeleted).OrderBy(h => h.Id).ToListAsync();
        var busy = await _db.Orders.AsNoTracking()
            .Where(o => HubLoad.BusyStatuses.Contains(o.Status))
            .GroupBy(o => o.HubId)
            .Select(g => new { HubId = g.Key, Count = g.Count() })
            .ToListAsync();

        return hubs.Select(h =>
        {
            var count = busy.FirstOrDefault(b => b.HubId == h.Id)?.Count ?? 0;
            var load = HubLoad.Compute(count, h.Capacity);
            return new HubCard(h.Id, h.Name, count, h.Capacity, load,
                load >= PricingService.HubProtectionAtOrAbove,
                load < PricingService.QuietKitchenBelow);
        }).ToList();
    }

    public async Task<IReadOnlyList<MenuBrand>> GetMenuAsync(int hubId)
    {
        return await _db.Brands.AsNoTracking()
            .Where(b => !b.IsDeleted && b.HubBrands.Any(hb => hb.HubId == hubId && !hb.Hub.IsDeleted))
            .OrderBy(b => b.Name)
            .Select(b => new MenuBrand(b.Id, b.Name, b.MenuItems
                .Where(i => !i.IsDeleted && i.IsAvailable)
                .OrderBy(i => i.Name)
                .Select(i => new MenuItemView(i.Id, i.Name, i.Category, i.Price)).ToList()))
            .ToListAsync();
    }

    public async Task<Dictionary<int, MenuItemView>> GetItemsAsync(IEnumerable<int> ids)
    {
        var wanted = ids.Distinct().ToList();
        var items = await _db.MenuItems.AsNoTracking()
            .Where(i => wanted.Contains(i.Id))
            .Select(i => new MenuItemView(i.Id, i.Name, i.Category, i.Price))
            .ToListAsync();
        return items.ToDictionary(i => i.Id);
    }
}
