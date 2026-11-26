# SaigonBite assumption log (single reconciled table)

This is the only assumption table. `README.md` links here instead of duplicating IDs.
Part 1 IDs `A-01`–`A-14` keep their original meanings. Step D's legacy `A-17` label is
retained as an implementation note and is **not** reused. Code comments were renumbered to
the canonical IDs below: `A-16` and `A-19` were merged into `A-09` and `A-08`, and the
injected-clock note moved from `A-12` to `A-27`.

| ID | Classification | One-line assumption / note (single meaning) | Where used |
|---|---|---|---|
| A-01 | SPEC ASSUMPTION (Part 1) | Only an Admin assigns or reassigns the shipper for an order. | `ShipperAssignmentService`, `OrderStatusService` |
| A-02 | SPEC ASSUMPTION (Part 1) | A failed delivery refunds `FoodTotal` only; delivery fee, surcharges and rounding are kept. | `OrderStatusService.FailDeliveryAsync`, `OrderStatusAndDeliveryTests` |
| A-03 | SPEC ASSUMPTION (Part 1) | An Admin may cancel an order that is still `Confirmed`. | `OrderStateMachine`, `SaigonBite.Tests` |
| A-04 | SPEC ASSUMPTION (Part 1) | At most two `OutForDelivery` orders per shipper, enforced at assignment **and** at `Ready → OutForDelivery`. | `ShipperAssignmentService`, `OrderStatusService`, `PickupCapacityTests` |
| A-05 | RESERVED (Part 1) | Reserved Part 1 ID; no Steps A–H code path references it. | none |
| A-06 | SPEC ASSUMPTION (Part 1) | Hub-to-district distance is simulated by one replaceable service, not a live map API. | `IDistanceService`, `DistanceService`, `OrderService` |
| A-07 | SPEC ASSUMPTION (Part 1) | Editing a `Placed` order recomputes price from the stored snapshot, not from live hub/rain/distance values. | `OrderService.EditItemsAsync`, `OrderServiceTests` |
| A-08 | SPEC ASSUMPTION (Part 1) | The verification code is a simulated 6-digit value shown in the UI, valid 5 minutes, cancelled after 3 wrong attempts; consumed staging is removed after success. | `RegistrationService`, `PendingRegistration`, `Views/Account/Verify.cshtml` |
| A-09 | SPEC ASSUMPTION (Part 1) | Identifiers are unique across all users including deactivated/soft-deleted ones and active pending rows; deactivation is a soft delete, history is retained, and the last active Admin cannot be deactivated. | `AccountValidation`, `RegistrationService`, `AccountManagementService`, `LoginService`, `OrderReadService`, `SaigonBiteDbContext` |
| A-10 | RESERVED (Part 1) | Reserved Part 1 ID; no Steps A–H code path references it. | none |
| A-11 | SPEC ASSUMPTION (Part 1) | An unknown or unusable voucher is ignored with `V = 0` and a note; the order still continues. | `PricingService`, `OrderService`, `Entities.Order.VoucherNote` |
| A-12 | SPEC ASSUMPTION (Part 1) | Payment is attempted before any order row is saved; a failed payment creates no order or history row. | `OrderService.PlaceOrderAsync`, `OrderServiceTests` |
| A-13 | SPEC ASSUMPTION (Part 1) | R1 revenue is the sum of rounded order totals by category/payment method, excluding `Cancelled`. | `ReportService`, `ReportHandCheckTests` |
| A-14 | SPEC ASSUMPTION (Part 1) | R2 “orders handled” means the order reached `Confirmed` or later and is not `Cancelled`/`Failed`. | `ReportService`, `ReportHandCheckTests` |
| A-15 | NEW SPEC ASSUMPTION | Editing an online-paid order adjusts `Payment.Amount` to the new total; no second gateway charge or partial refund is simulated. | `OrderStatusService.EditItemsAsync`, `OrderStatusAndDeliveryTests` |
| A-16 | IMPLEMENTATION NOTE | Retired alias: the old uniqueness note was merged into `A-09`; the ID is not reused. | README history only |
| A-17 | IMPLEMENTATION NOTE | Step D used this ID for stored-snapshot edits; canonical Part 1 ID is `A-07`. `A-17` is reserved legacy and is **not** reused for deactivation. | Step D `OrderServiceTests` header history |
| A-19 | IMPLEMENTATION NOTE | Retired alias: the old pending-registration cancellation note was merged into `A-08`; the ID is not reused. | README history only |
| A-20 | IMPLEMENTATION NOTE | Demo data is dev-only and CLI-driven (`--seed-demo` / `--reset-demo`), tagged with a `DEMO-` address prefix; there is no public seeding endpoint. | `DemoDataSeeder`, `Program.cs`, `DemoDataSeederTests` |
| A-21 | NEW SPEC ASSUMPTION | Dashboard date inputs use inclusive Vietnam-local dates: `[from 00:00, to + 1 day 00:00)`, so `from == to` returns the whole day. | `AdminController.Range`, `ReportService`, `ReportDateRangeTests`, `Views/Admin/Dashboard.cshtml` |
| A-22 | IMPLEMENTATION NOTE | `HasData` fixes seed identities with a placeholder; `SeedUserInitializer` writes real random-salt `PasswordHasher<User>` hashes from config/env in every environment, including Production. | `Entities.SeedCredential`, `SeedUserInitializer`, `LoginService` |
| A-23 | NEW SPEC ASSUMPTION | Staff accounts have no extra BR18 profile fields; only the identifier (and KitchenStaff hub) is editable. | `AccountManagementService.UpdateOwnProfileAsync` |
| A-24 | IMPLEMENTATION NOTE | Chart.js 4.4.1 is vendored under `wwwroot/lib`, so the minimal dashboard works offline. | `Views/Admin/Dashboard.cshtml` |
| A-25 | IMPLEMENTATION NOTE | Every POST is protected by global `AutoValidateAntiforgeryToken`; auth cookies are `HttpOnly`, `SameSite=Lax`, 8-hour sliding, and `SecurePolicy.Always` in Production. | `Program.cs`, all Razor POST forms |
| A-26 | IMPLEMENTATION NOTE | Demo fixture rows are written directly, bypassing `OrderStateMachine`, so all eight statuses are visible immediately. | `DemoDataSeeder` |
| A-27 | IMPLEMENTATION NOTE | Services never read the system clock; callers inject `IClock` and convert UTC to Vietnam local time at the boundary. | `IClock`, `SystemClock`, `OrderStatusService`, `Program.cs` |
