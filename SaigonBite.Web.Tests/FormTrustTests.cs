using System.Net;
using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Core.Payments;
using SaigonBite.Web.Data;

namespace SaigonBite.Web.Tests;

/// <summary>
/// Review item 3: every posted price/role/total field is untrusted. The saved order must come
/// from the database catalogue, the database role, and OrderService pricing only.
/// </summary>
public sealed class FormTrustTests : IDisposable
{
    private static readonly DateTimeOffset FixedClock =
        new(2026, 11, 26, 15, 0, 0, TimeSpan.FromHours(7));

    private readonly WebTestHarness _harness = new(FixedClock);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Tampered_total_role_price_and_unit_price_do_not_change_the_saved_order()
    {
        using var client = await WebForms.LoginAsync(_harness, "0912345678", "Local@123");
        var token = await WebForms.TokenFromAsync(client, "/Order/Hubs");
        var ticket = await WebForms.TicketFromAsync(client);

        var response = await client.PostAsync("/Order/Place", WebForms.CheckoutPost(
            token,
            distanceTicket: ticket,
            paymentMethod: "Cod",
            touristClaimForgery: true, // posted IsTourist=true and Role=Tourist are ignored
            tamperedFields: new Dictionary<string, string>
            {
                ["Total"] = "1",
                ["Role"] = "Tourist",
                ["Price"] = "1",
                ["Lines[0].Price"] = "1",
                ["Lines[1].Price"] = "1",
                ["Lines[0].UnitPrice"] = "1",
                ["Lines[1].UnitPrice"] = "1"
            }));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("/Order/Track/", response.Headers.Location?.ToString() ?? string.Empty);

        using var db = _harness.NewContext();
        var localId = await db.Users.Where(u => u.Identifier == "0912345678")
            .Select(u => u.Id).SingleAsync();
        var saved = await db.Orders
            .Include(o => o.Lines)
            .Include(o => o.Payments)
            .SingleAsync();

        Assert.Equal(localId, saved.CustomerId);
        Assert.Equal(OrderStatus.Placed, saved.Status);
        Assert.Equal(200_000m, saved.Subtotal);
        Assert.Equal(20_000m, saved.QuietDiscount);
        Assert.Equal(0m, saved.VoucherDiscount);
        Assert.Equal(25_000m, saved.DeliveryFee);
        Assert.Equal(0m, saved.RushSurcharge);
        Assert.Equal(0m, saved.RainSurcharge);
        Assert.Equal(205_000m, saved.Total);
        Assert.Equal(new DateTime(2026, 11, 26, 15, 0, 0), saved.PlacedAt);
        Assert.Equal(0m, saved.SnapshotHubLoad); // fresh dev seed has no in-flight orders
        Assert.Equal(3.2m, saved.SnapshotDistanceKm);
        Assert.False(saved.SnapshotHeavyRain);

        var lines = saved.Lines.OrderBy(l => l.MenuItemId).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal(2, lines[0].MenuItemId);
        Assert.Equal(2, lines[0].Quantity);
        Assert.Equal(60_000m, lines[0].UnitPrice); // database price, not posted UnitPrice=1
        Assert.Equal(5, lines[1].MenuItemId);
        Assert.Equal(2, lines[1].Quantity);
        Assert.Equal(40_000m, lines[1].UnitPrice);

        var payment = Assert.Single(saved.Payments);
        Assert.Equal(PaymentMethod.Cod, payment.Method);
        Assert.Equal(205_000m, payment.Amount); // server total, not posted Total=1
    }

    [Theory]
    [InlineData("0912345678", "Local@123", "ApplePay", true)]  // Local claims Tourist
    [InlineData("A1234567", "Tourist@123", "VnPay", false)]     // Tourist claims Local
    public async Task Payment_method_for_the_other_customer_type_is_rejected(
        string identifier, string password, string paymentMethod, bool claimTourist)
    {
        using var client = await WebForms.LoginAsync(_harness, identifier, password);
        var token = await WebForms.TokenFromAsync(client, "/Order/Hubs");
        var ticket = await WebForms.TicketFromAsync(client);

        var response = await client.PostAsync("/Order/Place", WebForms.CheckoutPost(
            token,
            distanceTicket: ticket,
            paymentMethod: paymentMethod,
            touristClaimForgery: claimTourist,
            tamperedFields: new Dictionary<string, string>
            {
                ["Role"] = claimTourist ? "Tourist" : "Local",
                ["Total"] = "1"
            }));

        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("alert-danger", html);
        Assert.Contains("cannot be used by this customer type (BR13)", html);

        using var db = _harness.NewContext();
        Assert.Empty(await db.Orders.ToListAsync());
        Assert.Empty(await db.Payments.ToListAsync());
    }

    [Fact]
    public async Task Posted_km_field_is_ignored_the_signed_ticket_decides_the_distance()
    {
        using var client = await WebForms.LoginAsync(_harness, "0912345678", "Local@123");
        var token = await WebForms.TokenFromAsync(client, "/Order/Hubs");
        var ticket = await WebForms.TicketFromAsync(client);

        var response = await client.PostAsync("/Order/Place", WebForms.CheckoutPost(
            token,
            distanceTicket: ticket,
            tamperedFields: new Dictionary<string, string>
            {
                ["DistanceKm"] = "0.5",
                ["SnapshotDistanceKm"] = "0.5",
                ["Breakdown.DeliveryFee"] = "0"
            }));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        using var db = _harness.NewContext();
        var saved = await db.Orders.SingleAsync();
        Assert.Equal(3.2m, saved.SnapshotDistanceKm);
        Assert.Equal(25_000m, saved.DeliveryFee);
    }

    [Theory]
    [InlineData(null)]                       // no ticket at all
    [InlineData("not-a-real-ticket")]        // forged / garbage ticket
    public async Task Place_without_a_valid_distance_ticket_saves_nothing(string? forged)
    {
        using var client = await WebForms.LoginAsync(_harness, "0912345678", "Local@123");
        var token = await WebForms.TokenFromAsync(client, "/Order/Hubs");

        var response = await client.PostAsync("/Order/Place",
            WebForms.CheckoutPost(token, distanceTicket: forged));

        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("alert-danger", html);
        Assert.Contains("DistanceTicket", html); // a fresh ticket is issued for the retry
        using var db = _harness.NewContext();
        Assert.Empty(await db.Orders.ToListAsync());
    }
}
