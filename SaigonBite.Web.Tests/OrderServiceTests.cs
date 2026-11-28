using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Core.Payments;
using SaigonBite.Core.Pricing;
using SaigonBite.Web.Data;
using SaigonBite.Web.Services;

namespace SaigonBite.Web.Tests;

/// <summary>
/// Step D service tests (BR12-BR17, A-06/A-07/A-11/A-12). Each test uses a throwaway LocalDB database.
/// </summary>
public sealed class OrderServiceTests : IDisposable
{
    private static readonly DateTimeOffset ClockUtc = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero); // 15:00 UTC+7

    private readonly string _connectionString =
        $"Server=(localdb)\\MSSQLLocalDB;Database=SaigonBite_D_{Guid.NewGuid():N};" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private readonly SaigonBiteDbContext _db;

    public OrderServiceTests()
    {
        _db = NewContext();
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        using var cleanup = NewContext();
        cleanup.Database.EnsureDeleted();
    }

    private SaigonBiteDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SaigonBiteDbContext>().UseSqlServer(_connectionString).Options);

    private OrderService Service(PaymentContext? payments = null) =>
        new(_db, new PricingService(), payments ?? RealPayments(),
            new SettingsService(_db, new FakeClock(ClockUtc)), new FakeClock(ClockUtc),
            FixedRandomSource.DistanceOf(3.2m));

    private static PaymentContext RealPayments() => new(new IPaymentStrategy[]
    {
        new MomoPayment(), new VnPayPayment(), new ApplePayPayment(), new PayPalPayment(), new CodPayment()
    });

    private static PlaceOrderInput Input(
        int customerId = 7,
        int hubId = 1,
        string district = "District 1",
        IReadOnlyList<OrderLineInput>? lines = null,
        PaymentMethod method = PaymentMethod.Momo,
        IReadOnlyList<string>? vouchers = null) =>
        new(customerId, hubId, district,
            lines ?? new[] { new OrderLineInput(2, 2), new OrderLineInput(5, 2) }, // S = 200,000
            method, vouchers, "123 Le Loi, District 1", string.Empty, string.Empty);

    private async Task AddBusyOrdersAsync(int count, OrderStatus status = OrderStatus.Confirmed)
    {
        for (var i = 0; i < count; i++)
        {
            _db.Orders.Add(new Order
            {
                CustomerId = 7,
                HubId = 1,
                Status = status,
                DeliveryAddress = "seed",
                PlacedAt = ClockUtc.AddHours(7).DateTime,
                Subtotal = 55_000m,
                FoodTotal = 55_000m,
                RawTotal = 55_000m,
                Total = 55_000m
            });
        }
        await _db.SaveChangesAsync();
    }

    private async Task<(int Orders, int Lines, int Payments, int History)> CountsAsync() =>
        (await _db.Orders.CountAsync(), await _db.OrderLines.CountAsync(),
            await _db.Payments.CountAsync(), await _db.OrderStatusHistories.CountAsync());

    [Fact]
    public async Task Spec_example_saves_205000_with_full_snapshot()
    {
        await AddBusyOrdersAsync(2); // 2 / 10 = 20 % load
        var before = await CountsAsync();

        var result = await Service().PlaceOrderAsync(Input());

        Assert.True(result.Accepted, result.Message);
        Assert.Equal(205_000m, result.Price!.Total);
        var order = await _db.Orders
            .Include(o => o.Lines)
            .Include(o => o.Payments)
            .Include(o => o.StatusHistory)
            .SingleAsync(o => o.Id == result.OrderId);
        Assert.Equal(OrderStatus.Placed, order.Status);
        Assert.Equal(200_000m, order.Subtotal);
        Assert.Equal(20_000m, order.QuietDiscount);
        Assert.Equal(0m, order.VoucherDiscount);
        Assert.Equal(180_000m, order.FoodTotal);
        Assert.Equal(25_000m, order.DeliveryFee);
        Assert.Equal(0m, order.RushSurcharge);
        Assert.Equal(0m, order.RainSurcharge);
        Assert.Equal(205_000m, order.RawTotal);
        Assert.Equal(205_000m, order.Total);
        Assert.Equal(0.2m, order.SnapshotHubLoad);
        Assert.Equal(3.2m, order.SnapshotDistanceKm);
        Assert.False(order.SnapshotHeavyRain);
        Assert.Equal(15, order.PlacedAt.Hour);
        Assert.Null(order.VoucherId);
        Assert.Equal(2, order.Lines.Count);
        Assert.Contains(order.Lines, l => l.MenuItemId == 2 && l.Quantity == 2 && l.UnitPrice == 60_000m);
        Assert.Contains(order.Lines, l => l.MenuItemId == 5 && l.Quantity == 2 && l.UnitPrice == 40_000m);
        var payment = Assert.Single(order.Payments);
        Assert.Equal(PaymentMethod.Momo, payment.Method);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.False(payment.CashConfirmed);
        var history = Assert.Single(order.StatusHistory);
        Assert.Null(history.FromStatus);
        Assert.Equal(OrderStatus.Placed, history.ToStatus);
        Assert.Equal(7, history.ActorUserId);
        Assert.Equal(before.Orders + 1, (await CountsAsync()).Orders);
    }

    [Fact]
    public async Task Preview_prices_without_writing_anything()
    {
        await AddBusyOrdersAsync(2);
        var before = await CountsAsync();

        var preview = await Service().PreviewPriceAsync(Input());

        Assert.True(preview.Accepted, preview.Message);
        Assert.Equal(205_000m, preview.Breakdown!.Total);
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task Hub_at_90_percent_is_rejected_and_saves_nothing()
    {
        await AddBusyOrdersAsync(9);
        var before = await CountsAsync();

        var outcome = await Service().PlaceOrderAsync(
            Input(lines: new[] { new OrderLineInput(1, 1) }));

        Assert.False(outcome.Accepted);
        Assert.Equal(RejectReason.HubBusy, outcome.RuleReason);
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task Hub_load_counts_placed_confirmed_and_preparing()
    {
        await AddBusyOrdersAsync(3, OrderStatus.Confirmed); // 30 % -> no quiet-kitchen discount

        var busyPreview = await Service().PreviewPriceAsync(Input());
        Assert.True(busyPreview.Accepted, busyPreview.Message);
        Assert.Equal(225_000m, busyPreview.Breakdown!.Total);

        var seeded = await _db.Orders.ToListAsync();
        // Placed still counts (1 / 10 = 10 %: quiet discount); Ready and Delivered do not.
        seeded[0].Status = OrderStatus.Placed;
        seeded[1].Status = OrderStatus.Ready;
        seeded[2].Status = OrderStatus.Delivered;
        await _db.SaveChangesAsync();

        var idlePreview = await Service().PreviewPriceAsync(Input());
        Assert.True(idlePreview.Accepted, idlePreview.Message);
        Assert.Equal(205_000m, idlePreview.Breakdown!.Total);
        var card = Assert.Single(await Service().GetHubCardsAsync(), h => h.Id == 1);
        Assert.Equal(1, card.Busy);
    }

    [Fact]
    public async Task Placed_orders_raise_the_hub_load_shown_on_the_hub_card()
    {
        var before = Assert.Single(await Service().GetHubCardsAsync(), h => h.Id == 1);
        Assert.Equal(0, before.Busy);

        var placed = await Service().PlaceOrderAsync(Input());
        Assert.True(placed.Accepted, placed.Message);

        var after = Assert.Single(await Service().GetHubCardsAsync(), h => h.Id == 1);
        Assert.Equal(1, after.Busy);
        Assert.Equal(10, after.Capacity);
        Assert.Equal(0.1m, after.Load);
        Assert.True(after.IsQuiet);
        Assert.False(after.IsBusy);

        await AddBusyOrdersAsync(2, OrderStatus.Placed); // 3 / 10
        var three = Assert.Single(await Service().GetHubCardsAsync(), h => h.Id == 1);
        Assert.Equal(3, three.Busy);
        Assert.Equal(0.3m, three.Load);
        Assert.False(three.IsQuiet); // 30 % is no longer "quiet" (BR7)
    }

    [Fact]
    public async Task Nine_Placed_orders_make_the_hub_busy_and_block_the_next_order()
    {
        await AddBusyOrdersAsync(9, OrderStatus.Placed); // 9 / 10 = 90 %
        var card = Assert.Single(await Service().GetHubCardsAsync(), h => h.Id == 1);
        Assert.True(card.IsBusy);
        var before = await CountsAsync();

        var outcome = await Service().PlaceOrderAsync(Input(lines: new[] { new OrderLineInput(1, 1) }));

        Assert.False(outcome.Accepted);
        Assert.Equal(RejectReason.HubBusy, outcome.RuleReason);
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task Below_minimum_is_rejected_and_saves_nothing()
    {
        await AddBusyOrdersAsync(2);
        var before = await CountsAsync();

        var outcome = await Service().PlaceOrderAsync(
            Input(lines: new[] { new OrderLineInput(3, 1) })); // 35,000 VND

        Assert.False(outcome.Accepted);
        Assert.Equal(RejectReason.BelowMinimum, outcome.RuleReason);
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task Beyond_10_km_is_rejected_and_saves_nothing()
    {
        await AddBusyOrdersAsync(2);
        var before = await CountsAsync();

        var outcome = await Service().PlaceOrderAsync(Input() with { DistanceKm = 10.1m });

        Assert.False(outcome.Accepted);
        Assert.Equal(RejectReason.OutOfCoverage, outcome.RuleReason);
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task Exactly_10_km_is_accepted_with_the_65000_delivery_fee()
    {
        await AddBusyOrdersAsync(2); // 20 % load: quiet discount 20,000

        var outcome = await Service().PlaceOrderAsync(Input() with { DistanceKm = 10.0m });

        Assert.True(outcome.Accepted, outcome.Message);
        Assert.Equal(245_000m, outcome.Price!.Total); // 200,000 - 20,000 + 65,000
        var order = await _db.Orders.AsNoTracking().SingleAsync(o => o.Id == outcome.OrderId);
        Assert.Equal(10.0m, order.SnapshotDistanceKm);
        Assert.Equal(65_000m, order.DeliveryFee);
    }

    [Fact]
    public async Task Preview_and_place_use_the_same_distance_when_it_is_supplied()
    {
        await AddBusyOrdersAsync(2);
        var input = Input() with { DistanceKm = 4.4m };

        var preview = await Service().PreviewPriceAsync(input);
        var placed = await Service().PlaceOrderAsync(input);

        Assert.True(preview.Accepted, preview.Message);
        Assert.True(placed.Accepted, placed.Message);
        Assert.Equal(preview.Breakdown!.Total, placed.Price!.Total);
        Assert.Equal(4.4m, (await _db.Orders.AsNoTracking().SingleAsync(o => o.Id == placed.OrderId)).SnapshotDistanceKm);
    }

    [Fact]
    public async Task Cod_above_1000000_is_refused_and_saves_nothing()
    {
        await AddBusyOrdersAsync(3); // 30 % load keeps the quiet discount off
        var addon = new MenuItem { BrandId = 1, Name = "Test add-on", Category = "Drinks", Price = 10_500m };
        _db.MenuItems.Add(addon);
        await _db.SaveChangesAsync();
        var before = await CountsAsync();

        var outcome = await Service().PlaceOrderAsync(Input(
            lines: new[] { new OrderLineInput(1, 18), new OrderLineInput(addon.Id, 1) },
            method: PaymentMethod.Cod)); // S = 1,000,500 -> rounded total 1,001,000

        Assert.False(outcome.Accepted);
        Assert.Contains("1,000,000", outcome.Message);
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task Tourist_paying_with_momo_is_refused_and_saves_nothing()
    {
        await AddBusyOrdersAsync(2);
        var before = await CountsAsync();

        var outcome = await Service().PlaceOrderAsync(Input(customerId: 8)); // seeded Tourist

        Assert.False(outcome.Accepted);
        Assert.Contains("cannot be used", outcome.Message);
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task Failed_payment_saves_no_order_lines_payment_or_history()
    {
        await AddBusyOrdersAsync(2);
        var before = await CountsAsync();
        var failing = new PaymentContext(new IPaymentStrategy[] { new FailingPayment() });

        var outcome = await Service(failing).PlaceOrderAsync(Input());

        Assert.False(outcome.Accepted);
        Assert.Equal("simulated gateway decline", outcome.Message);
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task Rain_flag_adds_10000_and_is_snapshotted()
    {
        await AddBusyOrdersAsync(2);
        await new SettingsService(_db, new FakeClock(ClockUtc)).SetHeavyRainAsync(true, 1);
        _db.ChangeTracker.Clear();

        var outcome = await Service().PlaceOrderAsync(Input());

        Assert.True(outcome.Accepted, outcome.Message);
        Assert.Equal(215_000m, outcome.Price!.Total);
        var order = await _db.Orders.SingleAsync(o => o.Id == outcome.OrderId);
        Assert.True(order.SnapshotHeavyRain);
        Assert.Equal(10_000m, order.RainSurcharge);
    }

    [Fact]
    public async Task Two_vouchers_are_not_accepted()
    {
        await AddBusyOrdersAsync(2);

        var outcome = await Service().PlaceOrderAsync(Input(vouchers: new[] { "P10", "F60" }));

        Assert.True(outcome.Accepted, outcome.Message);
        Assert.Equal(0m, outcome.Price!.VoucherDiscount);
        Assert.Equal(205_000m, outcome.Price!.Total);
        var order = await _db.Orders.SingleAsync(o => o.Id == outcome.OrderId);
        Assert.Null(order.VoucherId);
        Assert.Contains("one voucher", order.VoucherNote);
    }

    [Fact]
    public async Task Unknown_voucher_is_ignored_with_a_note()
    {
        await AddBusyOrdersAsync(2);

        var outcome = await Service().PlaceOrderAsync(Input(vouchers: new[] { "NOPE" }));

        Assert.True(outcome.Accepted, outcome.Message);
        Assert.Equal(0m, outcome.Price!.VoucherDiscount);
        var order = await _db.Orders.SingleAsync(o => o.Id == outcome.OrderId);
        Assert.Null(order.VoucherId);
        Assert.Contains("not found", order.VoucherNote);
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class FailingPayment : IPaymentStrategy
    {
        public PaymentMethod Method => PaymentMethod.Momo;
        public bool IsAllowedFor(Role payer) => true;
        public string? CheckAmount(decimal roundedTotal) => null;

        public PaymentResult Pay(PaymentRequest request) =>
            new(false, PaymentStatus.Failed, "DECLINED", "simulated gateway decline");

        public PaymentResult Refund(PaymentRequest request) =>
            new(true, PaymentStatus.Refunded, "REFUND");
    }
}
