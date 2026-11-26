using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Core.Orders;
using SaigonBite.Core.Payments;
using SaigonBite.Core.Pricing;
using SaigonBite.Web.Data;

namespace SaigonBite.Web.Services;

public sealed record OrderLineInput(int MenuItemId, int Quantity);

public sealed record PlaceOrderInput(
    int CustomerId,
    int HubId,
    string District,
    IReadOnlyList<OrderLineInput> Lines,
    PaymentMethod PaymentMethod,
    IReadOnlyList<string>? VoucherCodes = null,
    string DeliveryAddress = "",
    string HotelName = "",
    string RoomNumber = "");

public sealed record PreviewResult(
    bool Accepted,
    RejectReason Reason,
    string? Message,
    IReadOnlyList<string> LineErrors,
    PriceBreakdown? Breakdown)
{
    public static PreviewResult Ok(PriceBreakdown breakdown) =>
        new(true, RejectReason.None, null, Array.Empty<string>(), breakdown);
    public static PreviewResult Rejected(RejectReason reason) =>
        new(false, reason, reason.ToString(), Array.Empty<string>(), null);
    public static PreviewResult InvalidLines(IReadOnlyList<string> errors) =>
        new(false, RejectReason.None, string.Join(" ", errors), errors, null);
    public static PreviewResult Failed(string message) =>
        new(false, RejectReason.None, message, Array.Empty<string>(), null);
}

public sealed record OrderOutcome(
    bool Accepted,
    RejectReason RuleReason,
    string? Message,
    int? OrderId,
    PriceBreakdown? Price,
    IReadOnlyList<string> LineErrors)
{
    public static OrderOutcome Ok(int orderId, PriceBreakdown price) =>
        new(true, RejectReason.None, null, orderId, price, Array.Empty<string>());
    public static OrderOutcome Failed(string message) =>
        new(false, RejectReason.None, message, null, null, Array.Empty<string>());
    public static OrderOutcome FromPreview(PreviewResult preview) =>
        new(false, preview.Reason, preview.Message, null, null, preview.LineErrors);
}

/// <summary>
/// BR12-BR17, A-11/A-12: the only place that turns a validated basket into a stored order.
/// Preview and placement share PrepareAsync so they can never price differently (BR17).
/// </summary>
public sealed class OrderService
{
    private readonly SaigonBiteDbContext _db;
    private readonly PricingService _pricing;
    private readonly PaymentContext _payments;
    private readonly SettingsService _settings;
    private readonly IClock _clock;
    private readonly IDistanceService _distance;
    private readonly OrderReadService _read;
    private readonly CustomerCatalogService _catalog;
    private readonly IDistrictCatalog _districts;

    // Existing six-argument constructor is preserved for the unit tests.
    public OrderService(SaigonBiteDbContext db, PricingService pricing, PaymentContext payments,
        SettingsService settings, IClock clock, IDistanceService distance)
        : this(db, pricing, payments, settings, clock, distance,
            new OrderReadService(db), new CustomerCatalogService(db), new DistrictCatalog())
    {
    }

    public OrderService(SaigonBiteDbContext db, PricingService pricing, PaymentContext payments,
        SettingsService settings, IClock clock, IDistanceService distance,
        OrderReadService read, CustomerCatalogService catalog, IDistrictCatalog districts)
    {
        _db = db;
        _pricing = pricing;
        _payments = payments;
        _settings = settings;
        _clock = clock;
        _distance = distance;
        _read = read;
        _catalog = catalog;
        _districts = districts;
    }

    /// <summary>FR-C1/C2/C3/C5: read-only UI data is exposed here so controllers only call this service.</summary>
    public IReadOnlyList<DistrictOption> Districts => _districts.Districts;

    public Task<IReadOnlyList<HubCard>> GetHubCardsAsync() => _catalog.GetHubCardsAsync();

    public Task<IReadOnlyList<MenuBrand>> GetMenuAsync(int hubId) => _catalog.GetMenuAsync(hubId);

    public Task<Dictionary<int, MenuItemView>> GetItemsAsync(IEnumerable<int> ids) => _catalog.GetItemsAsync(ids);

    public Task<OrderDetailDto> GetDetailAsync(int orderId) => _read.GetDetailAsync(orderId);

    public Task<IReadOnlyList<OrderHistoryDto>> GetHistoryAsync(int orderId) => _read.GetHistoryAsync(orderId);

    public Task<IReadOnlyList<OrderListItemDto>> GetCustomerOrdersAsync(int customerId) =>
        _read.GetCustomerOrdersAsync(customerId);

    /// <summary>BR13: checkout options come from the single Core payment registry (no UI rules).</summary>
    public IReadOnlyList<PaymentMethod> AllowedPaymentMethods(Role payer, decimal total) =>
        _payments.AllowedMethods(payer, total);

    /// <summary>Prices a basket without writing anything.</summary>
    public async Task<PreviewResult> PreviewPriceAsync(PlaceOrderInput input)
    {
        var prepared = await PrepareAsync(input);
        return prepared.Data is not null
            ? PreviewResult.Ok(prepared.Data.Pricing.Breakdown!)
            : prepared.Preview ?? PreviewResult.Failed(prepared.Message ?? "The order cannot be priced.");
    }

    /// <summary>Pays first; only a successful payment opens a transaction and writes the order (A-12).</summary>
    public async Task<OrderOutcome> PlaceOrderAsync(PlaceOrderInput input)
    {
        var prepared = await PrepareAsync(input);
        if (prepared.Data is null)
            return prepared.Preview is not null
                ? OrderOutcome.FromPreview(prepared.Preview)
                : OrderOutcome.Failed(prepared.Message ?? "The order cannot be placed.");

        var p = prepared.Data;
        var price = p.Pricing.Breakdown!;

        IPaymentStrategy strategy;
        try
        {
            // Payer role comes from the database, never from a form field (rule 7).
            strategy = _payments.Resolve(input.PaymentMethod, p.Customer.Role, price.Total);
        }
        catch (PaymentNotAllowedException ex)
        {
            return OrderOutcome.Failed(ex.Message);
        }

        // OrderId 0 is a placeholder: the gateway decides before any row is saved (A-12).
        var paymentResult = strategy.Pay(new PaymentRequest(0, price.Total, p.Customer.Role));
        if (!paymentResult.Success)
            return OrderOutcome.Failed(paymentResult.Message ?? "Payment was declined.");

        var voucherId = p.VoucherId is not null && price.VoucherDiscount > 0 ? p.VoucherId : null;
        var voucherNote = p.NoteOverride ?? price.VoucherNote;

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var order = new Order
            {
                CustomerId = p.Customer.Id,
                HubId = p.Hub.Id,
                Status = OrderStatus.Placed, // initial state, not a transition (BR15)
                DeliveryAddress = input.DeliveryAddress,
                HotelName = input.HotelName,
                RoomNumber = input.RoomNumber,
                PlacedAt = p.PlacedAt,
                SnapshotHubLoad = p.HubLoad,
                SnapshotHeavyRain = p.HeavyRain,
                SnapshotDistanceKm = p.DistanceKm,
                VoucherId = voucherId,
                VoucherNote = voucherNote,
                Subtotal = price.Subtotal,
                QuietDiscount = price.QuietDiscount,
                VoucherDiscount = price.VoucherDiscount,
                FoodTotal = price.FoodTotal,
                DeliveryFee = price.DeliveryFee,
                RushSurcharge = price.RushSurcharge,
                RainSurcharge = price.RainSurcharge,
                RawTotal = price.RawTotal,
                Total = price.Total
            };
            _db.Orders.Add(order);
            await _db.SaveChangesAsync(); // identity for the child rows

            foreach (var (item, quantity) in p.Items)
            {
                _db.OrderLines.Add(new OrderLine
                {
                    OrderId = order.Id,
                    MenuItemId = item.Id,
                    Quantity = quantity,
                    UnitPrice = item.Price // BR17: price at placement, not the current price
                });
            }

            _db.Payments.Add(new Payment
            {
                OrderId = order.Id,
                Method = input.PaymentMethod,
                Amount = price.Total,
                Status = paymentResult.Status, // online Paid, COD Pending (BR13/BR14)
                CashConfirmed = false,
                Reference = paymentResult.Reference,
                Message = paymentResult.Message,
                CreatedAt = p.PlacedAt
            });

            _db.OrderStatusHistories.Add(new OrderStatusHistory
            {
                OrderId = order.Id,
                FromStatus = null, // null -> Placed, by the customer (BR15)
                ToStatus = OrderStatus.Placed,
                ActorUserId = p.Customer.Id,
                ChangedAt = p.PlacedAt
            });

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            return OrderOutcome.Ok(order.Id, price);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task<Preparation> PrepareAsync(PlaceOrderInput input)
    {
        var customer = await _db.Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == input.CustomerId && u.IsActive);
        if (customer is null || !customer.Role.IsCustomer())
            return new Preparation(null, null, "The customer does not exist or cannot place orders.");

        var hub = await _db.Hubs.AsNoTracking().SingleOrDefaultAsync(h => h.Id == input.HubId);
        if (hub is null) return new Preparation(null, null, "The hub does not exist.");

        var itemIds = input.Lines.Select(l => l.MenuItemId).Distinct().ToList();
        // IgnoreQueryFilters so a deleted item is reported as "not available" instead of "missing".
        var items = await _db.MenuItems.IgnoreQueryFilters().AsNoTracking()
            .Where(i => itemIds.Contains(i.Id)).ToListAsync();
        var hubBrands = (await _db.HubBrands.AsNoTracking()
            .Where(hb => hb.HubId == hub.Id).Select(hb => hb.BrandId).ToListAsync()).ToHashSet();

        var lineInfos = new List<OrderLineInfo>();
        var pricedLines = new List<(MenuItem Item, int Quantity)>();
        foreach (var line in input.Lines)
        {
            var item = items.SingleOrDefault(i => i.Id == line.MenuItemId);
            lineInfos.Add(new OrderLineInfo(
                line.Quantity,
                item is not null && item.IsAvailable && !item.IsDeleted, // A-09: deleted/inactive is unavailable
                item is not null && hubBrands.Contains(item.BrandId)));   // BR12
            if (item is not null) pricedLines.Add((item, line.Quantity));
        }

        var lineErrors = OrderLineValidator.Validate(lineInfos);
        if (lineErrors.Count > 0)
            return new Preparation(null, PreviewResult.InvalidLines(lineErrors), null);

        var subtotal = pricedLines.Sum(x => x.Item.Price * x.Quantity);
        var busy = await _db.Orders.AsNoTracking().CountAsync(o =>
            o.HubId == hub.Id && (o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Preparing));
        var load = HubLoad.Compute(busy, hub.Capacity); // BR8; the new order is not counted
        var placedAt = VietnamTime.FromUtc(_clock.UtcNow);
        var heavyRain = _settings.GetHeavyRain();      // FR-D14/BR5
        var distanceKm = _distance.GetDistanceKm(hub.Id, input.District); // A-06
        var voucher = await ResolveVoucherAsync(input.VoucherCodes);      // A-11

        var pricing = _pricing.Calculate(new PricingInput(
            subtotal, load, distanceKm, placedAt, heavyRain, voucher.Info));
        if (!pricing.Accepted)
            return new Preparation(null, PreviewResult.Rejected(pricing.Reason), null);

        return new Preparation(new Prepared(
            customer, hub, pricedLines, placedAt, load, heavyRain, distanceKm,
            voucher.Info, voucher.Id, voucher.NoteOverride, pricing), null, null);
    }

    private async Task<(VoucherInfo? Info, int? Id, string? NoteOverride)> ResolveVoucherAsync(
        IReadOnlyList<string>? codes)
    {
        if (codes is null || codes.Count == 0) return (null, null, null);
        if (codes.Count > 1)
            return (null, null, "Only one voucher code can be used per order (A-11).");

        var code = codes[0].Trim();
        if (code.Length == 0) return (null, null, null);

        // Case-insensitive lookup; a soft-deleted voucher is treated as unknown.
        // Materialise the small voucher table: ToUpperInvariant is not translated by EF Core 8.
        var vouchers = await _db.Vouchers.AsNoTracking().ToListAsync();
        var voucher = vouchers.SingleOrDefault(v =>
            string.Equals(v.Code, code, StringComparison.OrdinalIgnoreCase));
        if (voucher is null) return (null, null, $"Voucher '{code}' was not found (A-11).");

        // An unusable voucher is still passed in so PricingService supplies the A-11 note and V = 0.
        return (new VoucherInfo(voucher.Code, voucher.Type, voucher.Value, voucher.MaxDiscount,
            voucher.MinSubtotal, voucher.ValidFrom, voucher.ValidTo, voucher.IsActive), voucher.Id, null);
    }

    private sealed record Prepared(
        User Customer,
        Hub Hub,
        List<(MenuItem Item, int Quantity)> Items,
        DateTime PlacedAt,
        decimal HubLoad,
        bool HeavyRain,
        decimal DistanceKm,
        VoucherInfo? Voucher,
        int? VoucherId,
        string? NoteOverride,
        PricingResult Pricing);

    private sealed record Preparation(Prepared? Data, PreviewResult? Preview, string? Message);
}
