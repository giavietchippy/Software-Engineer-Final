namespace SaigonBite.Core.Pricing;

/// <summary>BR8: hub load = orders in Confirmed or Preparing / kitchen capacity.</summary>
public static class HubLoad
{
    public static decimal Compute(int busyOrders, int kitchenCapacity)
    {
        if (kitchenCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(kitchenCapacity));
        return (decimal)busyOrders / kitchenCapacity;
    }

    /// <summary>Counts only Confirmed and Preparing; Placed, Ready and later states do not count.</summary>
    public static decimal FromStatuses(IEnumerable<OrderStatus> statuses, int kitchenCapacity) =>
        Compute(statuses.Count(s => s is OrderStatus.Confirmed or OrderStatus.Preparing), kitchenCapacity);
}
