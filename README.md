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
`SaigonBite.Web.Tests` holds 76 tests: the Data-layer test that proves soft-deleting a user/menu item
does not change order detail, status history or R1 revenue (A-09), plus Steps D–H services. Each suite
uses throwaway LocalDB databases; `AccountTests` shares one and resets it per case to avoid LocalDB timeouts.

## Step D service layer (no controllers or views yet)

`OrderService.PreviewPriceAsync` validates lines, reads hub load from the database, the Admin rain flag,
the injected clock (UTC+7) and the simulated distance, then calls `Core.PricingService` without saving.
`OrderService.PlaceOrderAsync` pays first and only then writes the Order (Placed), its lines, its Payment
and the first `null → Placed` history row inside one transaction (A-12). The customer's role is read from
`Users`, never from the request. One voucher code is allowed; unknown/unusable codes give V = 0 plus a note.

Distances come from the replaceable `DistanceService` (A-06): hub 1 + `District 1` = 3.2 km, while
`District 7` is 10.5 km (out of coverage). Time comes from `IClock`; `SystemClock` is used in production
and a fake in tests. `OrderStatusHistories.FromStatus` is nullable since Step D for the initial row
(migration `20261126031657_MakeHistoryFromStatusNullable`).

## Steps E+F service layer (no controllers or views yet)

`OrderStatusService.ChangeStatusAsync` loads the actor's role/hub, payment method and COD cash flag from
the database, calls `OrderStateMachine.Ensure`, then writes the new status and one history row in a single
transaction; rejected transitions write nothing. `EditItemsAsync` recomputes from the stored BR17 snapshot
(`SnapshotHubLoad`, `SnapshotHeavyRain`, `SnapshotDistanceKm`, `PlacedAt`) while the order is `Placed`,
revalidates BR1/BR12, replaces the lines and updates the price parts. `CancelAsync` gives a full simulated
refund for online-paid orders; `FailDeliveryAsync` refunds only `FoodTotal` (A-02); COD orders never refund.

`ShipperAssignmentService` is Admin-only: initial assignment on `Ready`, reassign/unassign until
`OutForDelivery`, at most 2 `OutForDelivery` orders per shipper (A-04), old rows kept with `UnassignedAt`,
and shipper-scoped order listing. `ConfirmCashAsync` lets only the assigned shipper mark a COD order's
payment `Paid`/`CashConfirmed`, which is what allows `Delivered` (BR14).

**A-15 (assumption):** when an online-paid order is edited, its `Payment.Amount` is set to the new total
to keep the stored figures consistent, but no second gateway charge or partial refund is simulated.

## Step G: accounts, registration and login (services first, then minimal MVC wiring)

`RegistrationService` validates BR18 by role, hashes with ASP.NET Core `PasswordHasher<User>` (random
salt, never `SeedPasswordHasher`) and stages the row in `PendingRegistrations`. The 6-digit code is
returned in `RegistrationResult.Code` and shown on the Verify page (A-08); it lasts 5 minutes and three
wrong attempts cancel the row (A-19). A User/profile is created only after a successful verification,
inside one transaction; failure leaves `Users` untouched. Identifiers are trimmed, upper-cased and unique
across every user and every active pending row (A-16).

`AccountManagementService` lets an active Admin create/edit/list KitchenStaff (with hub), Shipper (no
hub) and Admin accounts, and deactivate/reactivate via soft delete (A-17). The last active Admin cannot
be deactivated. Self-profile edits reuse the same BR18 checks; staff have no extra profile fields (A-23).

`LoginService` reads the identifier from the database, refuses deactivated users and verifies both new
random-salt hashes and the deterministic DEV seed hashes (A-22). Cookie auth issues `NameIdentifier`,
`Name` and `Role` claims; policies are `Customer` (Local **or** Tourist), `KitchenStaff`, `Shipper` and
`Admin`. Routes: `/Account/Login`, `/Account/Register`, `/Account/Verify`, `/Account/Profile`,
`/Admin/Accounts` (Admin only). Rejected transitions still write nothing; no business rules live in
controllers or views.

**A-04 prerequisite fix:** `OrderStatusService.ChangeStatusAsync` now checks the 2-order cap a second
time at `Ready → OutForDelivery` for a Shipper, in addition to the Admin assignment-time check. Three
Ready orders can be assigned, but only two can be picked up; the third stays Ready with no history row.

## Step H: reports, minimal dashboard and demo data

`ReportService.GetRevenueAsync(from, to)` returns R1 by category and payment method from stored
snapshots, excluding Cancelled (A-13) and Failed (A-14). `GetHubPerformanceAsync` returns R2: orders
handled, average `Confirmed → Ready` from `OrderStatusHistory` (null/N/A when there is no such row),
current load from live statuses and the count of orders with `QuietDiscount > 0`. Both ignore query
filters so soft deletes do not rewrite history (A-09). `ReportHandCheckTests` uses six fixed orders with
hand-checkable totals.

The Admin dashboard at `/Admin/Dashboard` is intentionally small: two Chart.js bar charts and a load
table, fed by JSON endpoints `RevenueByCategory`, `RevenueByMethod` and `CurrentHubLoad`. The date filter
is `[from, to)` in Vietnam-local dates (A-21); Chart.js comes from a public CDN (A-24).

Demo data is CLI-only, never a public endpoint:

```
dotnet run --project SaigonBite.Web -- --seed-demo
dotnet run --project SaigonBite.Web -- --seed-demo --reset-demo
```

It creates 12 orders covering all eight states, both customer groups, all five payment methods and two
shippers (A-20). Rows are tagged with a `DEMO-` address prefix so reset deletes only demo rows; without
`--reset-demo` a second call is a no-op. Fixture rows are written directly to show every state (A-26).

## NEW assumptions for the Ambiguity Log

| ID | One-line assumption | Where used |
|---|---|---|
| A-08 | The verification code is a simulated 6-digit value shown in the UI instead of SMS/e-mail; valid 5 minutes, 3 wrong attempts cancel. | `PendingRegistration`, `RegistrationService`, `Views/Account/Verify.cshtml` |
| A-16 | Identifier uniqueness is case-insensitive across all users (including soft-deleted) and all active pending registrations; cancelled rows are kept. | `AccountValidation`, `RegistrationService`, `AccountManagementService`, `SaigonBiteDbContext` |
| A-17 | Deactivation is a soft delete (`IsActive=false`, `IsDeleted=true`), order history is kept, and the last active Admin cannot be deactivated. | `AccountManagementService`, `LoginService` |
| A-19 | Expired or thrice-failed pending registrations are cancelled; a verified pending row is deleted after the User/profile is created. | `RegistrationService`, `PendingRegistration` |
| A-20 | Demo data is dev-only and CLI-driven (`--seed-demo` / `--reset-demo`), identified by a `DEMO-` address prefix; there is no public seeding endpoint. | `DemoDataSeeder`, `Program.cs`, `DemoDataSeederTests` |
| A-21 | Dashboard date filters are `[from, to)` Vietnam-local dates parsed from HTML date inputs. | `AdminController.Range`, `Views/Admin/Dashboard.cshtml` |
| A-22 | Deterministic `PBKDF2-SHA256$` seed hashes remain accepted for DEV logins only; all new passwords use ASP.NET Core `PasswordHasher<User>` with a random salt. | `LoginService`, `SeedPasswordHasher`, `RegistrationService` |
| A-23 | Staff accounts have no extra BR18 profile fields; only the identifier (and KitchenStaff hub) can be edited. | `AccountManagementService.UpdateOwnProfileAsync` |
| A-24 | Chart.js is loaded from a public CDN, so the minimal Admin dashboard needs internet access in the demo. | `Views/Admin/Dashboard.cshtml` |
| A-26 | Demo fixture rows are written directly, bypassing `OrderStateMachine`, so all eight statuses are visible immediately. | `DemoDataSeeder` |
