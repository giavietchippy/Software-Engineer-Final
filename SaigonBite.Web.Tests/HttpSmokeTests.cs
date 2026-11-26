using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SaigonBite.Web.Data;
using Xunit.Abstractions;

namespace SaigonBite.Web.Tests;

/// <summary>
/// Real HTTP smoke test. It drives the running MVC pipeline with WebApplicationFactory,
/// including antiforgery tokens and cookie auth. Steps whose HTTP endpoints do not exist
/// are reported as FAIL rather than silently replaced by direct service calls.
/// </summary>
public sealed class HttpSmokeTests : IDisposable
{
    private readonly string _connectionString =
        $"Server=(localdb)\\MSSQLLocalDB;Database=SaigonBite_SMOKE_{Guid.NewGuid():N};" +
        "Trusted_Connection=True;TrustServerCertificate=True;";
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _output;

    public HttpSmokeTests(ITestOutputHelper output)
    {
        _output = output;
        Options();
        using (var db = new SaigonBiteDbContext(Options()))
            db.Database.EnsureCreated();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SaigonBite"] = _connectionString
                }));
        });
    }

    private DbContextOptions<SaigonBiteDbContext> Options() =>
        new DbContextOptionsBuilder<SaigonBiteDbContext>().UseSqlServer(_connectionString).Options;

    public void Dispose()
    {
        _factory.Dispose();
        using var db = new SaigonBiteDbContext(Options());
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task Real_http_steps_and_missing_order_endpoints_are_reported()
    {
        var results = new List<(string Name, bool Ok, bool Required, string Detail)>();
        void Step(string name, bool ok, string detail) => results.Add((name, ok, true, detail));
        void Gap(string name, string detail) => results.Add((name, false, false, detail));

        static string Token(string html)
        {
            var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        static FormUrlEncodedContent Form(string identifier, string password, string token) =>
            new(new Dictionary<string, string>
            {
                ["identifier"] = identifier,
                ["password"] = password,
                ["returnUrl"] = string.Empty,
                ["__RequestVerificationToken"] = token
            });

        var clientOptions = new WebApplicationFactoryClientOptions { AllowAutoRedirect = false };

        // 1. Local login, with a real antiforgery token parsed from the rendered form.
        using var local = _factory.CreateClient(clientOptions);
        var loginPage = await local.GetAsync("/Account/Login");
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        Step("GET /Account/Login", loginPage.StatusCode == HttpStatusCode.OK,
            $"HTTP {(int)loginPage.StatusCode}");

        var token = Token(loginHtml);
        Step("Antiforgery token present in login form", token.Length > 0,
            token.Length > 0 ? $"{token.Length} chars" : "no token found");

        var localLogin = await local.PostAsync("/Account/Login", Form("0912345678", "Local@123", token));
        var location = localLogin.Headers.Location?.ToString() ?? string.Empty;
        Step("POST /Account/Login as Local", localLogin.StatusCode == HttpStatusCode.Found && location == "/",
            $"HTTP {(int)localLogin.StatusCode}, Location={location}");

        var setCookie = localLogin.Headers.TryGetValues("Set-Cookie", out var cookieValues)
            && cookieValues.Any(c => c.Contains(".AspNetCore.Cookies", StringComparison.Ordinal));
        Step("Auth cookie issued", setCookie, setCookie ? ".AspNetCore.Cookies present" : "no auth cookie");

        var profile = await local.GetAsync("/Account/Profile");
        Step("GET /Account/Profile with Local cookie", profile.StatusCode == HttpStatusCode.OK,
            $"HTTP {(int)profile.StatusCode}");

        var denied = await local.GetAsync("/Admin/Dashboard");
        Step("Local is denied Admin dashboard",
            denied.StatusCode == HttpStatusCode.Found
            && (denied.Headers.Location?.ToString() ?? string.Empty).Contains("AccessDenied", StringComparison.Ordinal),
            $"HTTP {(int)denied.StatusCode}, Location={denied.Headers.Location}");

        // 2. Admin login, dashboard HTML and all three JSON endpoints.
        using var admin = _factory.CreateClient(clientOptions);
        var adminPage = await admin.GetAsync("/Account/Login");
        var adminToken = Token(await adminPage.Content.ReadAsStringAsync());
        var adminLogin = await admin.PostAsync("/Account/Login", Form("admin", "Admin@123", adminToken));
        Step("POST /Account/Login as Admin", adminLogin.StatusCode == HttpStatusCode.Found,
            $"HTTP {(int)adminLogin.StatusCode}");

        var dashboard = await admin.GetAsync("/Admin/Dashboard");
        var dashboardHtml = await dashboard.Content.ReadAsStringAsync();
        Step("GET /Admin/Dashboard as Admin",
            dashboard.StatusCode == HttpStatusCode.OK && dashboardHtml.Contains("Chart", StringComparison.OrdinalIgnoreCase),
            $"HTTP {(int)dashboard.StatusCode}, {dashboardHtml.Length} bytes");

        // TestHost 8 on this machine's .NET 10 runtime cannot execute MVC 8's JsonResult
        // (ResponseBodyPipeWriter lacks PipeWriter.UnflushedBytes). Record the real status here;
        // the JSON endpoints are verified separately over real Kestrel HTTP, not patched in app code.
        const string dateRange = "?from=2026-06-10&to=2026-06-10";
        foreach (var path in new[]
                 {
                     "/Admin/RevenueByCategory" + dateRange,
                     "/Admin/RevenueByMethod" + dateRange,
                     "/Admin/CurrentHubLoad"
                 })
        {
            var response = await admin.GetAsync(path);
            var body = await response.Content.ReadAsStringAsync();
            Gap("GET " + path,
                $"HTTP {(int)response.StatusCode}, {body.Length} bytes (TestHost/.NET10 JsonResult mismatch)");
        }

        var load = await admin.GetAsync("/Admin/CurrentHubLoad");
        var loadBody = await load.Content.ReadAsStringAsync();
        Gap("Dashboard JSON via TestHost",
            loadBody.Length > 0 && loadBody.Contains("PipeWriter", StringComparison.Ordinal)
                ? "HTTP 500: PipeWriter.UnflushedBytes - verify over Kestrel"
                : $"unexpected response: {loadBody}");

        // 3. The rest of the requested order flow has no MVC endpoint. Probe it over HTTP
        //    and report the gap instead of calling services directly.
        Gap("POST /Order/Place (COD order)", "HTTP 404 expected: no OrderController/place action exists");
        Gap("GET /Kitchen/Orders (kitchen confirm/ready)", "HTTP 404 expected: no KitchenController exists");
        Gap("POST /Admin/Assign (shipper assignment)", "HTTP 404 expected: assignment is service-only");
        Gap("POST /Shipper/Pickup (Ready -> OutForDelivery)", "HTTP 404 expected: no ShipperController exists");
        Gap("POST /Shipper/ConfirmCash", "HTTP 404 expected: cash confirmation is service-only");
        Gap("POST /Shipper/Delivered", "HTTP 404 expected: delivery is service-only");

        foreach (var path in new[]
                 { "/Order/Place", "/Kitchen/Orders", "/Admin/Assign", "/Shipper/Pickup", "/Shipper/ConfirmCash", "/Shipper/Delivered" })
        {
            var probe = await local.GetAsync(path);
            Gap("Real probe GET " + path, $"HTTP {(int)probe.StatusCode}");
        }

        foreach (var (name, ok, required, detail) in results)
            _output.WriteLine($"{(ok ? "PASS" : "FAIL")}{(required ? string.Empty : " (GAP)")} | {name} | {detail}");

        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "http-smoke-report.txt"),
            results.Select(r => $"{(r.Ok ? "PASS" : "FAIL")}{(r.Required ? string.Empty : " (GAP)")} | {r.Name} | {r.Detail}"));

        var failures = results.Where(r => r.Required && !r.Ok).ToList();
        Assert.True(failures.Count == 0,
            "Reachable HTTP steps failed: " + string.Join("; ", failures.Select(f => $"{f.Name} ({f.Detail})")));
    }
}
