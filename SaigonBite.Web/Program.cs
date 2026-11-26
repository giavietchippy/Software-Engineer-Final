using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SaigonBite.Core;
using SaigonBite.Core.Payments;
using SaigonBite.Core.Pricing;
using SaigonBite.Web.Data;
using SaigonBite.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Cookie auth + per-role policies (BR18). Customer = Local OR Tourist.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
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
builder.Services.AddSingleton<IPaymentStrategy, MomoPayment>();        // BR13
builder.Services.AddSingleton<IPaymentStrategy, VnPayPayment>();       // BR13
builder.Services.AddSingleton<IPaymentStrategy, ApplePayPayment>();    // BR13
builder.Services.AddSingleton<IPaymentStrategy, PayPalPayment>();      // BR13
builder.Services.AddSingleton<IPaymentStrategy, CodPayment>();         // BR13, BR14
builder.Services.AddSingleton<PaymentContext>();                       // BR13: receives all IPaymentStrategy registrations
builder.Services.AddScoped<SettingsService>();                        // FR-D14, BR5
builder.Services.AddScoped<OrderReadService>();                       // A-09: detail/history ignore soft-delete filters
builder.Services.AddScoped<ReportService>();                          // R1, R2
builder.Services.AddSingleton<IClock, SystemClock>();                  // A-12: no clock reads inside services
builder.Services.AddSingleton<IDistanceService, DistanceService>();    // A-06: deterministic demo distances
builder.Services.AddScoped<OrderService>();                            // BR12-BR17, A-11/A-12
builder.Services.AddScoped<OrderStatusService>();                      // BR14-BR17, A-02/A-12/A-15
builder.Services.AddScoped<ShipperAssignmentService>();                // A-01/A-04, BR14
builder.Services.AddSingleton<PasswordHasher<User>>();                  // BR18: random-salt hashing
builder.Services.AddScoped<RegistrationService>();                     // BR18, A-08
builder.Services.AddScoped<AccountManagementService>();                // BR18, A-16/A-17
builder.Services.AddScoped<LoginService>();                            // BR18, A-17/A-22
builder.Services.AddScoped<DemoDataSeeder>();                          // A-20: CLI-only dev seed

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// A-20: dev demo data is CLI-only, never a public endpoint.
if (args.Any(a => string.Equals(a, "--seed-demo", StringComparison.OrdinalIgnoreCase)))
{
    using var scope = app.Services.CreateScope();
    var reset = args.Any(a => string.Equals(a, "--reset-demo", StringComparison.OrdinalIgnoreCase));
    var added = await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync(reset);
    Console.WriteLine($"SaigonBite demo data: {added} orders {(reset ? "(reset) " : string.Empty)}seeded.");
}

app.Run();
