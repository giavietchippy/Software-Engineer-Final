# Software-Engineer-Final
This is the project of SaigonBite

# SaigonBite core (pricing, payment Strategy, order state machine)

Pure C# class library with no web or database code, so the ASP.NET MVC project only references it.

| File | Covers |
|---|---|
| Pricing/PricingService.cs | BR1 to BR11 (BR10 calculation order) |
| Pricing/HubLoad.cs | BR8 load definition: Placed + Confirmed + Preparing orders / capacity (a new order makes the hub busier at once) |
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

Seed passwords are at least 8 characters (BR18). `HasData` only fixes the seed identities with a
`!SEED-PENDING!` placeholder; `SeedUserInitializer` runs on **every** startup and writes real
random-salt ASP.NET Core `PasswordHasher<User>` hashes. No deterministic `PBKDF2-SHA256$` hash or
dev-only login branch remains.

Outside Production, passwords are read from `SeedPasswords:<key>` in `appsettings*.json`, overridden
by `SAIGONBITE_SEED_<KEY>_PASSWORD` environment variables, falling back to the dev defaults above.
**In Production the dev defaults and `appsettings` values are ignored**: every account requires its
`SAIGONBITE_SEED_<KEY>_PASSWORD` variable (keys: `Admin`, `Kitchen-D1`, `Kitchen-BT`, `Kitchen-TD`,
`Shipper1`, `Shipper2`, `Local`, `Tourist`). A missing variable logs a warning and skips that account
instead of creating it with a documented password. Set all eight before starting in Production. Example:

```
$env:SAIGONBITE_SEED_ADMIN_PASSWORD = 'ProdAdmin!2026'
$env:ASPNETCORE_ENVIRONMENT = 'Production'
dotnet run --project SaigonBite.Web
```

Other seed data: 3 hubs (capacity 10 each), one kitchen staff per hub, two shippers with no hub,
vouchers `P10` and `F60` (valid), `EXPIRED20` (expired), `INACTIVE50` (inactive), and 6 menu items
with `Category` on `MenuItem` (`Noodles`, `Rice`, `Drinks`, `Dessert`) for R1.
`SystemSettings` contains `HeavyRain = false`, editable only by an active Admin (`SettingsService`, FR-D14/BR5).

## Tests

```
dotnet test SaigonBite.sln
```

`SaigonBite.Tests` holds the 85 unchanged Core rule tests (`T-xx`/`S-xx`).
`SaigonBite.Web.Tests` holds 88 tests: the Data-layer test that proves soft-deleting a user/menu item
does not change order detail, status history or R1 revenue (A-09), plus Steps D–H services. Each suite
uses throwaway LocalDB databases; `AccountTests` shares one and resets it per case. `xunit.runner.json`
runs the Web cases serially because this machine's LocalDB times out under parallel database creation.

## Step D service layer (no controllers or views yet)

`OrderService.PreviewPriceAsync` validates lines, reads hub load from the database, the Admin rain flag,
the injected clock (UTC+7) and the simulated distance, then calls `Core.PricingService` without saving.
`OrderService.PlaceOrderAsync` pays first and only then writes the Order (Placed), its lines, its Payment
and the first `null → Placed` history row inside one transaction (A-12). The customer's role is read from
`Users`, never from the request. One voucher code is allowed; unknown/unusable codes give V = 0 plus a note.

Distances come from the replaceable `DistanceService` (A-06): a random value from 0.5 to 10.0 km (one
decimal) taken from an injected `IRandomSource` (tests use a fixed source). `CheckoutDistanceService`
draws it once when Checkout opens and seals it in a tamper-proof Data Protection ticket (hidden field
`DistanceTicket`, bound to user and hub, 30 minutes), so "Apply voucher" and "Place order" keep the same
km and price. A posted km value is ignored; a missing, forged or expired ticket is never used to place an
order. The delivery radius is 10 km (BR2); delivery fee beyond 5 km is 30,000 + 7,000 per started km
(8.0 km = 51,000, 10.0 km = 65,000). Hub load (BR8) counts Placed, Confirmed and Preparing orders; the hub
page refreshes it every 10 s from `GET /Order/HubLoads` (customers only). Time comes from `IClock`; `SystemClock` is used in production
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
wrong attempts cancel the row (A-08). A User/profile is created only after a successful verification,
inside one transaction; failure leaves `Users` untouched. Identifiers are trimmed, upper-cased and unique
across every user and every active pending row (A-09).

`AccountManagementService` lets an active Admin create/edit/list KitchenStaff (with hub), Shipper (no
hub) and Admin accounts, and deactivate/reactivate via soft delete (A-09). The last active Admin cannot
be deactivated. Self-profile edits reuse the same BR18 checks; staff have no extra profile fields (A-17).

`LoginService` reads the identifier from the database, refuses deactivated users and verifies only
random-salt hashes finalised at startup by `SeedUserInitializer` (N-02). Cookie auth issues `NameIdentifier`,
`Name` and `Role` claims; policies are `Customer` (Local **or** Tourist), `KitchenStaff`, `Shipper` and
`Admin`. Routes: `/Account/Login`, `/Account/Register`, `/Account/Verify`, `/Account/Profile`,
`/Admin/Accounts` (Admin only). Rejected transitions still write nothing; no business rules live in
controllers or views.

**A-04 prerequisite fix:** `OrderStatusService.ChangeStatusAsync` now checks the 2-order cap a second
time at `Ready → OutForDelivery` for a Shipper, in addition to the Admin assignment-time check. Three
Ready orders can be assigned, but only two can be picked up; the third stays Ready with no history row.

## Step H: reports, minimal dashboard and demo data

`ReportService.GetRevenueAsync(from, to)` returns R1 by category and payment method from stored
snapshots, excluding Cancelled and Failed orders (A-13). `GetHubPerformanceAsync` returns R2: orders
handled, average `Confirmed → Ready` from `OrderStatusHistory` (null/N/A when there is no such row),
current load from live statuses and the count of orders with `QuietDiscount > 0`. Both ignore query
filters so soft deletes do not rewrite history (A-09). `ReportHandCheckTests` uses six fixed orders with
hand-checkable totals.

The Admin dashboard at `/Admin/Dashboard` is intentionally small: two Chart.js bar charts and a load
table, fed by JSON endpoints `RevenueByCategory`, `RevenueByMethod` and `CurrentHubLoad`. The date filter
is an inclusive Vietnam-local date range `[from 00:00, to + 1 day 00:00)` (A-16); Chart.js 4.4.1 is
vendored locally, so the dashboard works offline (N-03).

Demo data is CLI-only, never a public endpoint:

```
dotnet run --project SaigonBite.Web -- --seed-demo
dotnet run --project SaigonBite.Web -- --seed-demo --reset-demo
```

It creates 12 orders covering all eight states, both customer groups, all five payment methods and two
shippers (N-01). Rows are tagged with a `DEMO-` address prefix so reset deletes only demo rows; without
`--reset-demo` a second call is a no-op. Fixture rows are written directly to show every state (N-05).

## Fixed demo clock (development only)

By default the app uses the real clock (`SystemClock`). To repeat the spec example at 15:00 Vietnam
time, set `SAIGONBITE_FIXED_CLOCK` to an ISO-8601 instant with an explicit offset:

```
$env:SAIGONBITE_FIXED_CLOCK = '2026-11-26T15:00:00+07:00'
dotnet run --project SaigonBite.Web
```

Clear the variable to return to real time:

```
Remove-Item Env:SAIGONBITE_FIXED_CLOCK
```

The variable is ignored when `ASPNETCORE_ENVIRONMENT=Production`, and an invalid or empty value also
falls back to `SystemClock`. The old `DemoClock:LocalTime` setting is no longer read. New tests assert
that the default resolution is `SystemClock`; no Core class is involved.

## Real HTTP smoke check

`HttpSmokeTests` drives the MVC pipeline with `WebApplicationFactory` (real antiforgery token and
cookie auth) against a throwaway LocalDB database; raw `--list-tests` inventories are in
`docs/evidence/`. `Real_http_steps_and_missing_order_endpoints_are_reported` is currently skipped
(`Skip = "enabled after the web layer is built"`) and will be replaced by a real end-to-end test
once the order-flow endpoints exist. Verified over HTTP: Local and Admin login, role denial, dashboard HTML and all three
JSON endpoints. Two gaps are reported rather than hidden: there is no HTTP endpoint for COD order
placement, kitchen confirm/ready, admin assignment or shipper pickup/cash/delivered (all return 404 —
those flows are service-layer only), and TestHost 8 on this machine's .NET 10 runtime returns HTTP 500
for MVC `JsonResult` (`PipeWriter.UnflushedBytes`); the same endpoints return HTTP 200 over real
Kestrel, so no application code was changed to work around the harness.

## Ambiguity log

The single reconciled assumption table lives in [`docs/ASSUMPTIONS.md`](docs/ASSUMPTIONS.md).
IDs `A-01`–`A-17` are spec assumptions; implementation details use `N-01`–`N-06`.
Retired labels `A-16`, `A-17` and `A-19` were merged into `A-09`, `A-07` and `A-08`
and are not reused; only a short history note remains.
