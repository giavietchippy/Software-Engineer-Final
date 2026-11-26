# Software-Engineer-Final
This is the project of SaigonBite

# SaigonBite core (pricing, payment Strategy, order state machine)

Pure C# class library with no web or database code, so the ASP.NET MVC project only references it.

| File | Covers |
|---|---|
| Pricing/PricingService.cs | BR1 to BR11 (BR10 calculation order) |
| Pricing/HubLoad.cs | BR8 load definition |
| Orders/OrderLineValidator.cs | BR12 |
| Orders/OrderStateMachine.cs | BR14, BR15, BR16 |
| Payments/* | BR13, BR14 (Strategy pattern) |

## Create the solution and run the tests
```
dotnet new sln -n SaigonBite
dotnet sln add SaigonBite.Core/SaigonBite.Core.csproj SaigonBite.Tests/SaigonBite.Tests.csproj
dotnet test
```
If package versions in SaigonBite.Tests.csproj do not restore, create the test project with
`dotnet new xunit -n SaigonBite.Tests`, copy the four *Tests.cs files into it, and add a project reference to SaigonBite.Core.

## Register in ASP.NET Core (Program.cs)
```csharp
builder.Services.AddSingleton<PricingService>();
builder.Services.AddSingleton<IPaymentStrategy, MomoPayment>();
builder.Services.AddSingleton<IPaymentStrategy, VnPayPayment>();
builder.Services.AddSingleton<IPaymentStrategy, ApplePayPayment>();
builder.Services.AddSingleton<IPaymentStrategy, PayPalPayment>();
builder.Services.AddSingleton<IPaymentStrategy, CodPayment>();
builder.Services.AddSingleton<PaymentContext>();   // receives all IPaymentStrategy registrations
```
A new payment method = one new class + one new registration line (Change Request evidence).

## Step C database (SQL Server LocalDB)

Connection string in `SaigonBite.Web/appsettings.json`: server `(localdb)\MSSQLLocalDB`, database `SaigonBite`.
Migrations live in `SaigonBite.Web/Migrations`. Apply them with:

```
$env:DOTNET_ROLL_FORWARD = 'Major'   # this machine only has the .NET 10 runtime
dotnet ef database update --project SaigonBite.Web --startup-project SaigonBite.Web
```

### Dev seed logins (development only)

| Identifier | Role | Hub | Password |
|---|---|---|---|
| `admin` | Admin | – | `Admin@123` |
| `kitchen-d1` | KitchenStaff | Hub District 1 | `KitchenD1@123` |
| `kitchen-bt` | KitchenStaff | Hub Binh Thanh | `KitchenBT@123` |
| `kitchen-td` | KitchenStaff | Hub Thu Duc | `KitchenTD@123` |
| `shipper1` | Shipper | none (shared pool) | `Shipper1@123` |
| `shipper2` | Shipper | none (shared pool) | `Shipper2@123` |
| `0912345678` | Local | – | `Local@123` |
| `A1234567` | Tourist | – | `Tourist@123` |

Seed passwords are at least 8 characters (BR18) and stored only as deterministic PBKDF2 hashes so
`HasData` stays stable across migrations. `SeedPasswordHasher` is **seed-only**; real registration must
use ASP.NET Core `PasswordHasher<TUser>` with a fresh random salt.

Other seed data: 3 hubs (capacity 10 each), one kitchen staff per hub, two shippers with no hub,
vouchers `P10` and `F60` (valid), `EXPIRED20` (expired), `INACTIVE50` (inactive), and 6 menu items
with `Category` on `MenuItem` (`Noodles`, `Rice`, `Drinks`, `Dessert`) for R1.
`SystemSettings` contains `HeavyRain = false`, editable only by an active Admin (`SettingsService`, FR-D14/BR5).

## Tests

```
dotnet test SaigonBite.sln
```

`SaigonBite.Tests` holds the 85 unchanged Core rule tests (`T-xx`/`S-xx`).
`SaigonBite.Web.Tests` holds the Data-layer test that proves soft-deleting a user/menu item does not
change order detail, status history or R1 revenue (A-09). It uses a throwaway LocalDB database.
