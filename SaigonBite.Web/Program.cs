using Microsoft.EntityFrameworkCore;
using SaigonBite.Core.Payments;
using SaigonBite.Core.Pricing;
using SaigonBite.Web.Data;
using SaigonBite.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

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

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
