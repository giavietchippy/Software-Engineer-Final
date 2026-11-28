using SaigonBite.Core;
using SaigonBite.Core.Orders;
using SaigonBite.Core.Pricing;

namespace SaigonBite.Tests;

public class HubAndLineRuleTests
{
    [Fact]
    public void T29_placed_confirmed_and_preparing_orders_count_towards_load()
    {
        var statuses = Enumerable.Repeat(OrderStatus.Confirmed, 25)
            .Concat(Enumerable.Repeat(OrderStatus.Preparing, 5))
            .Concat(Enumerable.Repeat(OrderStatus.Placed, 40))
            .Concat(Enumerable.Repeat(OrderStatus.Ready, 20));
        // 25 Confirmed + 5 Preparing + 40 Placed = 70; Ready does not count.
        Assert.Equal(0.70m, HubLoad.FromStatuses(statuses, 100));
    }

    [Fact]
    public void A_new_Placed_order_raises_the_load()
    {
        Assert.Equal(0.0m, HubLoad.FromStatuses(Array.Empty<OrderStatus>(), 10));
        Assert.Equal(0.3m, HubLoad.FromStatuses(Enumerable.Repeat(OrderStatus.Placed, 3), 10)); // 3 / 10
    }

    [Theory]
    [InlineData(OrderStatus.Placed, true)]
    [InlineData(OrderStatus.Confirmed, true)]
    [InlineData(OrderStatus.Preparing, true)]
    [InlineData(OrderStatus.Ready, false)]
    [InlineData(OrderStatus.OutForDelivery, false)]
    [InlineData(OrderStatus.Delivered, false)]
    [InlineData(OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Failed, false)]
    public void Only_Placed_Confirmed_Preparing_count_towards_load(OrderStatus status, bool counts)
    {
        Assert.Equal(counts ? 0.1m : 0m, HubLoad.FromStatuses(new[] { status }, 10));
    }

    [Fact]
    public void Load_boundaries_29_89_90_percent()
    {
        Assert.Equal(0.29m, HubLoad.Compute(29, 100));
        Assert.Equal(0.89m, HubLoad.Compute(89, 100));
        Assert.Equal(0.90m, HubLoad.Compute(90, 100));
    }

    [Fact]
    public void Zero_capacity_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HubLoad.Compute(1, 0));
    }

    [Theory]
    [InlineData("T-43", 0, true, true, false)]    // BR12 quantity 0
    [InlineData("T-44", 1, true, true, true)]
    [InlineData("T-45", 20, true, true, true)]
    [InlineData("T-46", 21, true, true, false)]
    [InlineData("T-51", 1, false, true, false)]   // item not available
    [InlineData("T-52", 1, true, false, false)]   // brand not operated by the hub
    public void Single_line_rules(string id, int quantity, bool available, bool ownBrand, bool expectedValid)
    {
        var errors = OrderLineValidator.Validate(new[] { new OrderLineInfo(quantity, available, ownBrand) });
        Assert.Equal(expectedValid, errors.Count == 0);
    }

    [Theory]
    [InlineData("T-47", 0, false)]
    [InlineData("T-48", 1, true)]
    [InlineData("T-49", 30, true)]
    [InlineData("T-50", 31, false)]
    public void Line_count_rules(string id, int lineCount, bool expectedValid)
    {
        var lines = Enumerable.Range(0, lineCount).Select(_ => new OrderLineInfo(1, true, true)).ToList();
        Assert.Equal(expectedValid, OrderLineValidator.Validate(lines).Count == 0);
    }
}
