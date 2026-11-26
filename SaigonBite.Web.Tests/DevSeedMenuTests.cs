using Microsoft.EntityFrameworkCore;
using SaigonBite.Web.Data;

namespace SaigonBite.Web.Tests;

/// <summary>
/// Review item 4: the 205,000 VND reproduction must work from ordinary dev seed data,
/// not only after --seed-demo. Fresh LocalDB databases are created from the model seed here.
/// </summary>
public sealed class DevSeedMenuTests : IDisposable
{
    private readonly WebTestHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Fresh_dev_seed_contains_Com_Tam_and_Che_Ba_Mau_for_hub_1()
    {
        using var db = _harness.NewContext();

        var comTam = await db.MenuItems.AsNoTracking().SingleAsync(i => i.Id == 2);
        Assert.Equal("Com Tam", comTam.Name);
        Assert.Equal(60_000m, comTam.Price);
        Assert.True(comTam.IsAvailable);
        Assert.False(comTam.IsDeleted);

        var cheBaMau = await db.MenuItems.AsNoTracking().SingleAsync(i => i.Id == 5);
        Assert.Equal("Che Ba Mau", cheBaMau.Name);
        Assert.Equal(40_000m, cheBaMau.Price);
        Assert.True(cheBaMau.IsAvailable);
        Assert.False(cheBaMau.IsDeleted);

        var hubOneBrands = await db.HubBrands.AsNoTracking()
            .Where(hb => hb.HubId == 1).Select(hb => hb.BrandId).ToListAsync();
        Assert.Contains(comTam.BrandId, hubOneBrands);
        Assert.Contains(cheBaMau.BrandId, hubOneBrands);
    }
}
