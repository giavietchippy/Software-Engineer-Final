namespace SaigonBite.Core.Pricing;

/// <summary>
/// BR8: hub load = orders in Placed, Confirmed or Preparing / kitchen capacity.
/// A newly placed order (Placed) already occupies the kitchen queue, so the hub gets busier the
/// moment an order is accepted. Ready and later states, Cancelled and Failed do not count.
/// </summary>
public static class HubLoad
{
    /// <summary>Single source of truth for which statuses count towards BR8 (used by every query).</summary>
    public static readonly OrderStatus[] BusyStatuses =
        { OrderStatus.Placed, OrderStatus.Confirmed, OrderStatus.Preparing };

    public static decimal Compute(int busyOrders, int kitchenCapacity)
    {
        if (kitchenCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(kitchenCapacity));
        return (decimal)busyOrders / kitchenCapacity;
    }

    /// <summary>Counts Placed, Confirmed and Preparing; Ready and later states do not count.</summary>
    public static decimal FromStatuses(IEnumerable<OrderStatus> statuses, int kitchenCapacity) =>
        Compute(statuses.Count(s => BusyStatuses.Contains(s)), kitchenCapacity);
}
