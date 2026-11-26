using System.Net;
using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Core.Payments;
using SaigonBite.Web.Data;

namespace SaigonBite.Web.Tests;

/// <summary>
/// Review item 5: full customer path through WebApplicationFactory. No skips, no fallback to
/// direct service calls: login -> cart -> checkout -> preview -> place -> assert the saved row.
/// </summary>
public sealed class CustomerPathIntegrationTests : IDisposable
{
    private static readonly DateTimeOffset FixedClock =
        new(2026, 11, 26, 15, 0, 0, TimeSpan.FromHours(7));

    private readonly WebTestHarness _harness = new(FixedClock);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Local_customer_previews_and_places_the_205000_cod_order()
    {
        using var client = await WebForms.LoginAsync(_harness, "0912345678", "Local@123");

        // Hub -> menu -> cart, mirroring the browser form (two lines, quantity two each).
        var menuToken = await WebForms.TokenFromAsync(client, "/Order/Menu?hubId=1");
        var cart = await client.PostAsync("/Order/Cart", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("hubId", "1"),
            new KeyValuePair<string, string>("itemIds", "2"),
            new KeyValuePair<string, string>("itemIds", "5"),
            new KeyValuePair<string, string>("quantities", "2"),
            new KeyValuePair<string, string>("quantities", "2"),
            new KeyValuePair<string, string>("__RequestVerificationToken", menuToken)
        }));
        Assert.Equal(HttpStatusCode.Found, cart.StatusCode);

        var checkoutUrl = cart.Headers.Location?.ToString() ?? string.Empty;
        Assert.Contains("/Order/Checkout", checkoutUrl);

        var checkoutPage = await client.GetAsync(checkoutUrl);
        var checkoutHtml = await checkoutPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, checkoutPage.StatusCode);
        Assert.Contains("Com Tam", checkoutHtml);
        Assert.Contains("Che Ba Mau", checkoutHtml);
        Assert.Contains("205,000", checkoutHtml);

        // Preview is a POST but writes nothing.
        var previewToken = WebForms.Token(checkoutHtml);
        var preview = await client.PostAsync("/Order/Preview", WebForms.CheckoutPost(previewToken));
        var previewHtml = await preview.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Contains("205,000", previewHtml);

        // Place uses the same server-calculated total and redirects to tracking.
        var placeToken = WebForms.Token(previewHtml);
        var place = await client.PostAsync("/Order/Place", WebForms.CheckoutPost(placeToken));
        Assert.Equal(HttpStatusCode.Found, place.StatusCode);
        Assert.Contains("/Order/Track/", place.Headers.Location?.ToString() ?? string.Empty);

        var track = await client.GetAsync(place.Headers.Location);
        var trackHtml = await track.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, track.StatusCode);
        Assert.Contains("Placed", trackHtml);
        Assert.Contains("205,000", trackHtml);

        using var db = _harness.NewContext();
        var localId = await db.Users.Where(u => u.Identifier == "0912345678")
            .Select(u => u.Id).SingleAsync();
        var saved = await db.Orders
            .Include(o => o.Lines)
            .Include(o => o.Payments)
            .Include(o => o.StatusHistory)
            .SingleAsync();

        Assert.Equal(localId, saved.CustomerId);
        Assert.Equal(OrderStatus.Placed, saved.Status);
        Assert.Equal(1, saved.HubId);
        Assert.Contains("District 1", saved.DeliveryAddress);
        Assert.Equal(3.2m, saved.SnapshotDistanceKm);
        Assert.Equal(new DateTime(2026, 11, 26, 15, 0, 0), saved.PlacedAt);
        Assert.Equal(200_000m, saved.Subtotal);
        Assert.Equal(20_000m, saved.QuietDiscount);
        Assert.Equal(25_000m, saved.DeliveryFee);
        Assert.Equal(0m, saved.RushSurcharge);
        Assert.Equal(0m, saved.RainSurcharge);
        Assert.Equal(205_000m, saved.Total);

        var lines = saved.Lines.OrderBy(l => l.MenuItemId).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal(2, lines[0].MenuItemId);
        Assert.Equal(2, lines[0].Quantity);
        Assert.Equal(60_000m, lines[0].UnitPrice);
        Assert.Equal(5, lines[1].MenuItemId);
        Assert.Equal(2, lines[1].Quantity);
        Assert.Equal(40_000m, lines[1].UnitPrice);

        var payment = Assert.Single(saved.Payments);
        Assert.Equal(PaymentMethod.Cod, payment.Method);
        Assert.Equal(205_000m, payment.Amount);

        var history = Assert.Single(saved.StatusHistory);
        Assert.Equal(OrderStatus.Placed, history.ToStatus);
        Assert.Equal(localId, history.ActorUserId);
    }
}
