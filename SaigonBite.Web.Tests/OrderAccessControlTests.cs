using System.Net;
using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Web.Data;

namespace SaigonBite.Web.Tests;

/// <summary>
/// FR-C access control: ownership is checked by the owning service on writes, and the Customer
/// policy blocks anonymous and non-customer roles before any action runs.
/// </summary>
public sealed class OrderAccessControlTests : IDisposable
{
    private readonly WebTestHarness _harness = new();
    private readonly int _touristOrderId;

    public OrderAccessControlTests()
    {
        using var db = _harness.NewContext();
        var order = new Order
        {
            CustomerId = 8, // seeded Tourist, while the attacker logs in as Local (7)
            HubId = 1,
            Status = OrderStatus.Placed,
            DeliveryAddress = "Tourist-owned order",
            PlacedAt = new DateTime(2026, 11, 26, 15, 0, 0),
            SnapshotHubLoad = 0.1m,
            SnapshotDistanceKm = 1.5m,
            Subtotal = 60_000m,
            FoodTotal = 60_000m,
            RawTotal = 75_000m,
            Total = 75_000m
        };
        db.Orders.Add(order);
        db.SaveChanges();
        _touristOrderId = order.Id;

        db.OrderLines.Add(new OrderLine { OrderId = order.Id, MenuItemId = 2, Quantity = 1, UnitPrice = 60_000m });
        db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = null,
            ToStatus = OrderStatus.Placed,
            ActorUserId = 8,
            ChangedAt = order.PlacedAt
        });
        db.SaveChanges();
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Anonymous_customer_order_pages_redirect_to_login()
    {
        using var client = _harness.CreateClient();
        foreach (var path in new[]
                 {
                     "/Order/Hubs",
                     $"/Order/Track/{_touristOrderId}",
                     $"/Order/Edit/{_touristOrderId}",
                     "/Order/MyOrders"
                 })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.Contains("/Account/Login", response.Headers.Location?.ToString() ?? string.Empty);
        }

        var loginPage = await client.GetAsync("/Account/Login");
        var token = WebForms.Token(await loginPage.Content.ReadAsStringAsync());
        var place = await client.PostAsync("/Order/Place", WebForms.CheckoutPost(token));
        Assert.Equal(HttpStatusCode.Found, place.StatusCode);
        Assert.Contains("/Account/Login", place.Headers.Location?.ToString() ?? string.Empty);

        using var db = _harness.NewContext();
        Assert.Equal(1, await db.Orders.CountAsync()); // only the Tourist-owned fixture
    }

    [Fact]
    public async Task Local_customer_cannot_open_edit_or_cancel_another_customers_order()
    {
        using var client = await WebForms.LoginAsync(_harness, "0912345678", "Local@123");

        AssertDenied(await client.GetAsync($"/Order/Track/{_touristOrderId}"));
        AssertDenied(await client.GetAsync($"/Order/Edit/{_touristOrderId}"));

        var token = await WebForms.TokenFromAsync(client, "/Order/Hubs");
        var edit = await client.PostAsync("/Order/Edit", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["OrderId"] = _touristOrderId.ToString(),
                ["Lines[0].MenuItemId"] = "2",
                ["Lines[0].Quantity"] = "1",
                ["__RequestVerificationToken"] = token
            }));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Contains("Only the owning customer can edit this order.", await edit.Content.ReadAsStringAsync());

        token = await WebForms.TokenFromAsync(client, "/Order/Hubs");
        var cancel = await client.PostAsync("/Order/Cancel", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["id"] = _touristOrderId.ToString(),
                ["reason"] = "forged",
                ["__RequestVerificationToken"] = token
            }));
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Contains("Only the owning customer can change this order.", await cancel.Content.ReadAsStringAsync());

        using var db = _harness.NewContext();
        var saved = await db.Orders.Include(o => o.Lines).SingleAsync(o => o.Id == _touristOrderId);
        Assert.Equal(OrderStatus.Placed, saved.Status);
        Assert.Equal(75_000m, saved.Total);
        var line = Assert.Single(saved.Lines);
        Assert.Equal(2, line.MenuItemId);
        Assert.Single(await db.OrderStatusHistories.Where(h => h.OrderId == _touristOrderId).ToListAsync());
    }

    [Theory]
    [InlineData("kitchen-d1", "KitchenD1@123")]
    [InlineData("shipper1", "Shipper1@123")]
    [InlineData("admin", "Admin@123")]
    public async Task Kitchen_shipper_and_admin_cannot_place_orders(string identifier, string password)
    {
        using var client = await WebForms.LoginAsync(_harness, identifier, password);
        var token = await WebForms.TokenFromAsync(client, "/");

        var place = await client.PostAsync("/Order/Place", WebForms.CheckoutPost(token));

        AssertDenied(place);
        using var db = _harness.NewContext();
        Assert.Equal(1, await db.Orders.CountAsync()); // no order created by staff/admin
    }

    private static void AssertDenied(HttpResponseMessage response)
    {
        var location = response.Headers.Location?.ToString() ?? string.Empty;
        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden ||
            (response.StatusCode == HttpStatusCode.Found &&
             location.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase)),
            $"Expected denial, got HTTP {(int)response.StatusCode} {location}");
    }
}
