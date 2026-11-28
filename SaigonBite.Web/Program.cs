using System.Globalization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SaigonBite.Core;
using SaigonBite.Core.Payments;
using SaigonBite.Core.Pricing;
using Microsoft.Extensions.Options;
using SaigonBite.Web.Data;
using SaigonBite.Web.Payments;
using SaigonBite.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container. N-04: AutoValidateAntiforgeryToken covers EVERY POST action,
// including language toggle, cart, preview, place, edit and cancel actions.
builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

// NFR3: shared EN/VI .resx files, selected by a cookie-only request-culture provider.
// The SDK embeds Resources/SharedResource*.resx under the marker type's full name,
// so no ResourcesPath is set here (setting it duplicates the namespace segment).
builder.Services.AddLocalization();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var cultures = new[] { new CultureInfo("en"), new CultureInfo("vi") };
    options.DefaultRequestCulture = new RequestCulture("en");
    options.SupportedCultures = cultures;
    options.SupportedUICultures = cultures;
    options.RequestCultureProviders.Clear();
    options.RequestCultureProviders.Insert(0, new CookieRequestCultureProvider());
});

// N-04: hardened cookie auth + per-role policies (BR18). Customer = Local OR Tourist.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsProduction()
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Customer", p => p.RequireRole(nameof(Role.Local), nameof(Role.Tourist)));
    options.AddPolicy(nameof(Role.KitchenStaff), p => p.RequireRole(nameof(Role.KitchenStaff)));
    options.AddPolicy(nameof(Role.Shipper), p => p.RequireRole(nameof(Role.Shipper)));
    options.AddPolicy(nameof(Role.Admin), p => p.RequireRole(nameof(Role.Admin)));
});

// Data layer (EF Core Code First, SQL Server LocalDB). Controllers never build SQL (architecture rule).
builder.Services.AddDbContext<SaigonBiteDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("SaigonBite")));

// Core business services stay in SaigonBite.Core; controllers only call services (architecture rule).
builder.Services.AddSingleton<PricingService>();                       // BR1-BR11
builder.Services.AddSaigonBitePaymentStrategies(builder.Configuration);  // BR13: Mock or Live sandbox
builder.Services.AddScoped<SettingsService>();                        // FR-D14, BR5
builder.Services.AddScoped<OrderReadService>();                       // A-09: detail/history ignore soft-delete filters
builder.Services.AddScoped<ReportService>();                          // R1, R2
// N-06: real clock by default. SAIGONBITE_FIXED_CLOCK is opt-in, must be a valid ISO-8601
// instant (e.g. 2026-11-26T15:00:00+07:00), and is deliberately ignored in Production.
var fixedClockSetting = Environment.GetEnvironmentVariable(DemoClock.EnvironmentVariableName);
if (!builder.Environment.IsProduction() && DemoClock.TryCreate(fixedClockSetting, out var fixedClock))
    builder.Services.AddSingleton<IClock>(fixedClock!);
else
    builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IRandomSource, SystemRandomSource>();     // A-06: injectable randomness
builder.Services.AddSingleton<IDistanceService, DistanceService>();    // A-06: random 0.5-10.0 km
builder.Services.AddSingleton<CheckoutDistanceService>();              // A-06: one signed distance per checkout
builder.Services.AddSingleton<IDistrictCatalog, DistrictCatalog>();    // FR-C3 dropdown source
builder.Services.AddScoped<CustomerCatalogService>();                 // FR-C1/C2 catalog reads
builder.Services.AddScoped<OrderService>();                            // BR12-BR17, A-11/A-12
builder.Services.AddScoped<PendingPaymentService>();
builder.Services.AddScoped<PaymentReturnService>();
builder.Services.AddScoped<OrderStatusService>();                      // BR14-BR17, A-02/A-12/A-15
builder.Services.AddScoped<ShipperAssignmentService>();                // A-01/A-04, BR14
builder.Services.AddScoped<StaffOrderReadService>();                   // read-only lists for kitchen/shipper/admin screens
builder.Services.AddSingleton<PasswordHasher<User>>();                  // BR18: random-salt hashing
builder.Services.AddScoped<RegistrationService>();                     // BR18, A-08
builder.Services.AddScoped<AccountManagementService>();                // BR18, A-09
builder.Services.AddScoped<LoginService>();                            // BR18, A-09/N-02
builder.Services.AddScoped<DemoDataSeeder>();                          // N-01: CLI-only dev seed
builder.Services.AddScoped<SeedUserInitializer>();                      // N-02: real hashes; env-only passwords in Production

var app = builder.Build();

var paymentOpts = app.Services.GetRequiredService<IOptions<PaymentGatewayOptions>>().Value;
Console.WriteLine("── Payments ──");
Console.WriteLine($"  Mode: {paymentOpts.Mode}  (Mock = no redirect; Live = real sandbox)");
Console.WriteLine($"  PublicBaseUrl: {paymentOpts.PublicBaseUrl}");
Console.WriteLine(paymentOpts.Mode.Equals("Live", StringComparison.OrdinalIgnoreCase)
    ? "  Order + MoMo/VNPay/PayPal → browser leaves site. Guide: scripts/LIVE-PAYMENTS.txt"
    : "  To redirect at checkout: set Payments:Mode to Live in appsettings.Development.json");

// N-02: finalise seed hashes; in Production only SAIGONBITE_SEED_*_PASSWORD is used (missing = skip + warn).
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<SeedUserInitializer>().SeedAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRequestLocalization(app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value);
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// N-01: dev demo data is CLI-only, never a public endpoint.
if (args.Any(a => string.Equals(a, "--seed-demo", StringComparison.OrdinalIgnoreCase)))
{
    using var scope = app.Services.CreateScope();
    var reset = args.Any(a => string.Equals(a, "--reset-demo", StringComparison.OrdinalIgnoreCase));
    var added = await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync(reset);
    Console.WriteLine($"SaigonBite demo data: {added} orders {(reset ? "(reset) " : string.Empty)}seeded.");
}

app.Run();

// WebApplicationFactory needs a public entry-point type for real HTTP smoke tests.
public partial class Program { }
