using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Core.Payments;
using SaigonBite.Core.Pricing;
using SaigonBite.Web.Data;

namespace SaigonBite.Web.Services;

/// <summary>
/// A-20/A-26: dev-only demo data. Invoked from Program.cs with --seed-demo / --reset-demo; there is
/// no public endpoint. Demo orders are tagged by DeliveryAddress so reset removes only these rows.
/// A-26: fixture rows are written directly (not through OrderStateMachine) to present all states.
/// </summary>
public sealed class DemoDataSeeder
{
    public const string DemoPrefix = "DEMO-";

    private readonly SaigonBiteDbContext _db;
    private readonly IClock _clock;

    public DemoDataSeeder(SaigonBiteDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<int> SeedAsync(bool reset = false)
    {
        var existing = await _db.Orders.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(o => o.DeliveryAddress.StartsWith(DemoPrefix));
        if (existing > 0 && !reset) return 0; // idempotent unless explicitly reset

        if (reset)
        {
            var old = await _db.Orders.IgnoreQueryFilters()
                .Where(o => o.DeliveryAddress.StartsWith(DemoPrefix)).ToListAsync();
            _db.Orders.RemoveRange(old); // child rows cascade
            await _db.SaveChangesAsync();
        }

        var today = VietnamTime.FromUtc(_clock.UtcNow);
        var specs = Specs();
        for (var i = 0; i < specs.Count; i++)
        {
            AddOrder(specs[i], i, today.AddDays(-i / 3.0).AddHours(i));
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return specs.Count;
    }

    private void AddOrder(Spec spec, int index, DateTime placedAt)
    {
        var items = _db.MenuItems.AsNoTracking().Where(i => i.Id == spec.ItemA || i.Id == spec.ItemB)
            .ToDictionary(i => i.Id);
        var subtotal = items[spec.ItemA].Price * spec.QtyA
                       + (spec.QtyB > 0 ? items[spec.ItemB].Price * spec.QtyB : 0m);
        var quiet = spec.Quiet ? PricingService.RoundToThousand(subtotal * 0.1m) : 0m;
        var food = subtotal - quiet;
        var delivery = 15_000m;
        var total = PricingService.RoundToThousand(food + delivery);
        var isCod = spec.Method == PaymentMethod.Cod;

        var order = new Order
        {
            CustomerId = spec.CustomerId,
            HubId = spec.HubId,
            Status = spec.Status,
            DeliveryAddress = $"{DemoPrefix}{index + 1:00} Le Loi, District 1",
            HotelName = spec.CustomerId == 8 ? "Demo Hotel" : string.Empty,
            RoomNumber = spec.CustomerId == 8 ? $"{100 + index}" : string.Empty,
            PlacedAt = placedAt,
            SnapshotHubLoad = spec.Quiet ? 0.2m : 0.5m,
            SnapshotHeavyRain = false,
            SnapshotDistanceKm = 1.5m,
            Subtotal = subtotal,
            QuietDiscount = quiet,
            FoodTotal = food,
            DeliveryFee = delivery,
            RawTotal = food + delivery,
            Total = total,
            AssignedShipperId = spec.ShipperId,
            CashConfirmed = isCod && spec.Status == OrderStatus.Delivered
        };
        _db.Orders.Add(order);
        _db.SaveChanges(); // save per spec so child rows have a real OrderId

        if (spec.QtyB > 0 && items.ContainsKey(spec.ItemB))
        {
            _db.OrderLines.Add(new OrderLine
            {
                OrderId = order.Id, MenuItemId = spec.ItemB,
                Quantity = spec.QtyB, UnitPrice = items[spec.ItemB].Price
            });
        }
        _db.OrderLines.Add(new OrderLine
        {
            OrderId = order.Id, MenuItemId = spec.ItemA,
            Quantity = spec.QtyA, UnitPrice = items[spec.ItemA].Price
        });

        _db.Payments.Add(new Payment
        {
            OrderId = order.Id, Method = spec.Method, Amount = total,
            Status = isCod && spec.Status != OrderStatus.Delivered ? PaymentStatus.Pending : PaymentStatus.Paid,
            CashConfirmed = order.CashConfirmed,
            Reference = $"{DemoPrefix}{order.Id}",
            CreatedAt = placedAt
        });

        if (spec.ShipperId is { } shipper)
        {
            _db.ShipperAssignments.Add(new ShipperAssignment
            {
                OrderId = order.Id, ShipperId = shipper, AssignedAt = placedAt.AddMinutes(30)
            });
        }

        AddHistory(order.Id, placedAt, spec, index);
        _db.SaveChanges();
    }

    private void AddHistory(int orderId, DateTime t, Spec spec, int index)
    {
        var customer = spec.CustomerId;
        var kitchen = spec.HubId switch { 1 => 2, 2 => 3, _ => 4 };
        var shipper = spec.ShipperId ?? 5;
        var admin = 1;

        Hist(orderId, null, OrderStatus.Placed, t, customer, null);
        if (spec.Status == OrderStatus.Placed) return;

        Hist(orderId, OrderStatus.Placed, OrderStatus.Confirmed, t.AddMinutes(5), kitchen, null);
        if (spec.Status == OrderStatus.Cancelled)
        {
            Hist(orderId, OrderStatus.Placed, OrderStatus.Cancelled, t.AddMinutes(7), admin, "demo cancellation");
            return;
        }
        if (spec.Status == OrderStatus.Confirmed) return;

        Hist(orderId, OrderStatus.Confirmed, OrderStatus.Preparing, t.AddMinutes(10), kitchen, null);
        if (spec.Status == OrderStatus.Preparing) return;

        Hist(orderId, OrderStatus.Preparing, OrderStatus.Ready, t.AddMinutes(25), kitchen, null);
        if (spec.Status == OrderStatus.Ready) return;

        Hist(orderId, OrderStatus.Ready, OrderStatus.OutForDelivery, t.AddMinutes(30), shipper, null);
        if (spec.Status == OrderStatus.Failed)
        {
            Hist(orderId, OrderStatus.OutForDelivery, OrderStatus.Failed,
                t.AddMinutes(40), shipper, "demo failed delivery");
            return;
        }
        Hist(orderId, OrderStatus.OutForDelivery, OrderStatus.Delivered, t.AddMinutes(45), shipper, null);
    }

    private void Hist(int orderId, OrderStatus? from, OrderStatus to, DateTime at, int actor, string? reason) =>
        _db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = orderId, FromStatus = from, ToStatus = to,
            ActorUserId = actor, ChangedAt = at, Reason = reason
        });

    /// <summary>Every status, both customer groups, all five methods, two shippers, 12 orders.</summary>
    private static List<Spec> Specs() => new()
    {
        new(OrderStatus.Placed, 7, 1, PaymentMethod.Momo, 1, 1, 2, 0, null, false),
        new(OrderStatus.Confirmed, 7, 1, PaymentMethod.VnPay, 2, 2, 0, 0, null, true),
        new(OrderStatus.Preparing, 8, 2, PaymentMethod.ApplePay, 5, 2, 0, 0, null, false),
        new(OrderStatus.Ready, 7, 2, PaymentMethod.Cod, 3, 2, 0, 0, null, true),
        new(OrderStatus.OutForDelivery, 8, 1, PaymentMethod.PayPal, 6, 2, 0, 0, 5, false),
        new(OrderStatus.Delivered, 7, 1, PaymentMethod.Momo, 1, 1, 4, 1, 5, true),
        new(OrderStatus.Delivered, 8, 3, PaymentMethod.ApplePay, 5, 3, 0, 0, 6, false),
        new(OrderStatus.Cancelled, 7, 3, PaymentMethod.VnPay, 2, 1, 0, 0, null, false),
        new(OrderStatus.Failed, 8, 2, PaymentMethod.PayPal, 3, 1, 6, 1, 6, false),
        new(OrderStatus.Confirmed, 8, 3, PaymentMethod.Cod, 4, 2, 0, 0, null, true),
        new(OrderStatus.Preparing, 7, 3, PaymentMethod.Momo, 1, 2, 0, 0, null, false),
        new(OrderStatus.Ready, 8, 1, PaymentMethod.Cod, 6, 1, 2, 1, null, true)
    };

    private sealed record Spec(
        OrderStatus Status,
        int CustomerId,
        int HubId,
        PaymentMethod Method,
        int ItemA,
        int QtyA,
        int ItemB,
        int QtyB,
        int? ShipperId,
        bool Quiet);
}
