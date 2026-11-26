using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Core.Payments;
using SaigonBite.Core.Pricing;
using SaigonBite.Web.Data;

namespace SaigonBite.Web.Services;

/// <summary>
/// R1/R2. A-09: reports use IgnoreQueryFilters() and totals from snapshots, so soft-deleted
/// users/menu items do not change historical revenue. Cancelled/Failed are excluded (A-13/A-14).
/// </summary>
public sealed class ReportService
{
    private readonly SaigonBiteDbContext _db;
    public ReportService(SaigonBiteDbContext db) => _db = db;

    public async Task<R1Report> GetRevenueAsync(DateTime from, DateTime to)
    {
        // A-16: both arguments are Vietnam-local DATES. 'to' is inclusive, so query the half-open
        // range [from 00:00, to + 1 day 00:00). from == to therefore returns the whole 'to' day.
        var start = from.Date;
        var exclusiveEnd = to.Date.AddDays(1);

        var orders = await _db.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(o => o.PlacedAt >= start && o.PlacedAt < exclusiveEnd
                        && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Failed)
            .ToListAsync();

        var ids = orders.Select(o => o.Id).ToHashSet();
        var totals = orders.ToDictionary(o => o.Id, o => o.Total);

        var lines = await _db.OrderLines
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(l => ids.Contains(l.OrderId))
            .Select(l => new { l.OrderId, l.Quantity, l.UnitPrice, Category = l.MenuItem.Category })
            .ToListAsync();

        var byCategory = lines
            .GroupBy(l => l.Category)
            .Select(g => new RevenueByCategoryDto(g.Key, g.Sum(l => l.UnitPrice * l.Quantity)))
            .OrderBy(x => x.Category)
            .ToList();

        var payments = await _db.Payments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => ids.Contains(p.OrderId))
            .ToListAsync();

        // A-13: by payment method = sum of rounded order totals (not line totals).
        // Distinct (method, order) stops a retried payment from counting the same order twice.
        var byMethod = payments
            .Select(p => new { p.Method, p.OrderId })
            .Distinct()
            .GroupBy(p => p.Method)
            .Select(g => new RevenueByMethodDto(g.Key, g.Sum(p => totals[p.OrderId])))
            .OrderBy(x => x.Method)
            .ToList();

        return new R1Report(byCategory, byMethod);
    }

    public async Task<IReadOnlyList<R2HubDto>> GetHubPerformanceAsync()
    {
        var hubs = await _db.Hubs.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        var orders = await _db.Orders.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        var history = await _db.OrderStatusHistories.IgnoreQueryFilters().AsNoTracking().ToListAsync();

        var result = new List<R2HubDto>();
        foreach (var hub in hubs)
        {
            var hubOrders = orders.Where(o => o.HubId == hub.Id).ToList();

            // A-14: reached Confirmed or later, and not Cancelled/Failed.
            var handled = hubOrders.Where(o =>
                o.Status is not (OrderStatus.Cancelled or OrderStatus.Failed)
                && history.Any(h => h.OrderId == o.Id && h.ToStatus is
                    OrderStatus.Confirmed or OrderStatus.Preparing or OrderStatus.Ready
                    or OrderStatus.OutForDelivery or OrderStatus.Delivered)).ToList();

            var confirmedToReady = new List<TimeSpan>();
            foreach (var order in handled)
            {
                var confirmed = history.Where(h => h.OrderId == order.Id && h.ToStatus == OrderStatus.Confirmed)
                    .OrderBy(h => h.ChangedAt).FirstOrDefault();
                var ready = history.Where(h => h.OrderId == order.Id && h.ToStatus == OrderStatus.Ready)
                    .OrderBy(h => h.ChangedAt).FirstOrDefault();
                if (confirmed is not null && ready is not null)
                    confirmedToReady.Add(ready.ChangedAt - confirmed.ChangedAt);
            }

            var busy = hubOrders.Count(o => o.Status is OrderStatus.Confirmed or OrderStatus.Preparing);
            result.Add(new R2HubDto(
                hub.Id, hub.Name, handled.Count,
                confirmedToReady.Count == 0 ? null : TimeSpan.FromTicks((long)confirmedToReady.Average(t => t.Ticks)),
                HubLoad.Compute(busy, hub.Capacity),
                handled.Count(o => o.QuietDiscount > 0)));
        }

        return result.OrderBy(x => x.HubId).ToList();
    }
}

public sealed record R1Report(
    IReadOnlyList<RevenueByCategoryDto> ByCategory,
    IReadOnlyList<RevenueByMethodDto> ByMethod);

public sealed record RevenueByCategoryDto(string Category, decimal Revenue);
public sealed record RevenueByMethodDto(PaymentMethod Method, decimal Revenue);

public sealed record R2HubDto(
    int HubId,
    string HubName,
    int OrdersHandled,
    TimeSpan? AverageConfirmedToReady,
    decimal CurrentLoad,
    int QuietKitchenDiscounts);
