using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SaigonBite.Web.Data;
using SaigonBite.Web.Services;

namespace SaigonBite.Web.Tests;

/// <summary>
/// Shared helpers for the new customer-facing HTTP tests. Each instance owns a throwaway LocalDB
/// and an isolated WebApplicationFactory; no existing test file is modified.
/// </summary>
internal sealed class WebTestHarness : IDisposable
{
    private readonly string _connectionString =
        $"Server=(localdb)\\MSSQLLocalDB;Database=SaigonBite_UI_{Guid.NewGuid():N};" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private readonly string? _previousFixedClock;

    public WebTestHarness(DateTimeOffset? fixedClock = null, string environment = "Development")
    {
        _previousFixedClock = Environment.GetEnvironmentVariable(Services.DemoClock.EnvironmentVariableName);
        Environment.SetEnvironmentVariable(
            Services.DemoClock.EnvironmentVariableName,
            fixedClock?.ToString("O"));

        using (var db = NewContext()) db.Database.EnsureCreated();

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            // A-06: the real app draws a random km; HTTP tests pin it to 3.2 km (hub 1 price examples).
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRandomSource>();
                services.AddSingleton<IRandomSource>(FixedRandomSource.ForKm(3.2m));
            });
            builder.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SaigonBite"] = _connectionString,
                    ["Payments:Mode"] = "Mock"
                }));
        });
    }

    public WebApplicationFactory<Program> Factory { get; }

    public SaigonBiteDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SaigonBiteDbContext>().UseSqlServer(_connectionString).Options);

    public HttpClient CreateClient() =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public void Dispose()
    {
        Factory.Dispose();
        Environment.SetEnvironmentVariable(Services.DemoClock.EnvironmentVariableName, _previousFixedClock);
        using var db = NewContext();
        db.Database.EnsureDeleted();
    }
}

internal static class WebForms
{
    public static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        if (!match.Success)
            match = Regex.Match(html, "value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"");
        if (!match.Success)
            throw new InvalidOperationException(
                "No antiforgery token in response. Snippet: " +
                html.Substring(0, Math.Min(600, html.Length)));
        return match.Groups[1].Value;
    }

    public static async Task<HttpClient> LoginAsync(WebTestHarness harness, string identifier, string password)
    {
        var client = harness.CreateClient();
        var page = await client.GetAsync("/Account/Login");
        var token = Token(await page.Content.ReadAsStringAsync());

        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["identifier"] = identifier,
                ["password"] = password,
                ["returnUrl"] = string.Empty,
                ["__RequestVerificationToken"] = token
            }));

        if (login.StatusCode != HttpStatusCode.Found)
            throw new InvalidOperationException($"Login failed for {identifier}: HTTP {(int)login.StatusCode}");
        return client;
    }

    public static FormUrlEncodedContent CheckoutPost(
        string token,
        string paymentMethod = "Cod",
        string district = "District 1",
        bool touristClaimForgery = false,
        IDictionary<string, string>? tamperedFields = null,
        string? distanceTicket = null)
    {
        var fields = new Dictionary<string, string>
        {
            ["HubId"] = "1",
            ["IsTourist"] = touristClaimForgery ? "true" : "false",
            ["Lines[0].MenuItemId"] = "2",
            ["Lines[0].Name"] = "Com Tam",
            ["Lines[0].Category"] = "Rice",
            ["Lines[0].UnitPrice"] = "60000",
            ["Lines[0].Quantity"] = "2",
            ["Lines[1].MenuItemId"] = "5",
            ["Lines[1].Name"] = "Che Ba Mau",
            ["Lines[1].Category"] = "Dessert",
            ["Lines[1].UnitPrice"] = "40000",
            ["Lines[1].Quantity"] = "2",
            ["District"] = district,
            ["DeliveryAddress"] = "10 Nguyen Hue, District 1",
            ["HotelName"] = string.Empty,
            ["RoomNumber"] = string.Empty,
            ["VoucherCode"] = string.Empty,
            ["PaymentMethod"] = paymentMethod,
            ["__RequestVerificationToken"] = token
        };

        if (distanceTicket is not null) fields["DistanceTicket"] = distanceTicket;

        if (tamperedFields is not null)
            foreach (var (key, value) in tamperedFields) fields[key] = value;

        return new FormUrlEncodedContent(fields);
    }

    /// <summary>The server-signed distance ticket rendered in the Checkout form (A-06).</summary>
    public static string Ticket(string html)
    {
        var match = Regex.Match(html, "name=\"DistanceTicket\"[^>]*value=\"([^\"]*)\"");
        if (!match.Success || match.Groups[1].Value.Length == 0)
            throw new InvalidOperationException("No DistanceTicket in the Checkout page.");
        return match.Groups[1].Value;
    }

    public static async Task<string> TicketFromAsync(HttpClient client, int hubId = 1) =>
        Ticket(await (await client.GetAsync($"/Order/Checkout?hubId={hubId}")).Content.ReadAsStringAsync());

    public static async Task<string> TokenFromAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"GET {path} returned HTTP {(int)response.StatusCode} " +
                $"{response.Headers.Location}; body: {html.Substring(0, Math.Min(300, html.Length))}");
        return Token(html);
    }
}
