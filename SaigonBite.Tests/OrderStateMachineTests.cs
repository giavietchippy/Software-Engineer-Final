using SaigonBite.Core;
using SaigonBite.Core.Orders;

namespace SaigonBite.Tests;

/// <summary>BR15 / BR16 / BR14. Invalid transitions S-01 to S-12 are rejected by the business layer.</summary>
public class OrderStateMachineTests
{
    private const int Hub = 10;
    private const int ShipperId = 7;

    private static TransitionContext Ctx(Role role, int userId = 1, int? actorHub = null, int orderHub = Hub,
        int? shipper = null, bool cod = false, bool cash = false, string? reason = null) =>
        new(role, userId, actorHub, orderHub, shipper, cod, cash, reason);

    private static TransitionContext Kitchen(string? reason = null) => Ctx(Role.KitchenStaff, 2, Hub, reason: reason);
    private static TransitionContext Shipper(bool cod = false, bool cash = false, string? reason = null) =>
        Ctx(Role.Shipper, ShipperId, shipper: ShipperId, cod: cod, cash: cash, reason: reason);

    private static void AssertRejected(OrderStatus from, OrderStatus to, TransitionContext c)
    {
        Assert.False(OrderStateMachine.Evaluate(from, to, c).Allowed);
        Assert.Throws<InvalidTransitionException>(() => OrderStateMachine.Ensure(from, to, c));
    }

    // ----- valid path -----

    [Fact]
    public void Valid_happy_path_is_allowed()
    {
        Assert.True(OrderStateMachine.Evaluate(OrderStatus.Placed, OrderStatus.Confirmed, Kitchen()).Allowed);
        Assert.True(OrderStateMachine.Evaluate(OrderStatus.Confirmed, OrderStatus.Preparing, Kitchen()).Allowed);
        Assert.True(OrderStateMachine.Evaluate(OrderStatus.Preparing, OrderStatus.Ready, Kitchen()).Allowed);
        Assert.True(OrderStateMachine.Evaluate(OrderStatus.Ready, OrderStatus.OutForDelivery, Shipper()).Allowed);
        Assert.True(OrderStateMachine.Evaluate(OrderStatus.OutForDelivery, OrderStatus.Delivered, Shipper()).Allowed);
    }

    [Fact]
    public void Cancellations_follow_BR16()
    {
        Assert.True(OrderStateMachine.Evaluate(OrderStatus.Placed, OrderStatus.Cancelled, Ctx(Role.Local)).Allowed);
        Assert.True(OrderStateMachine.Evaluate(OrderStatus.Placed, OrderStatus.Cancelled, Kitchen("out of stock")).Allowed);
        Assert.True(OrderStateMachine.Evaluate(OrderStatus.Confirmed, OrderStatus.Cancelled, Ctx(Role.Tourist)).Allowed);
        Assert.True(OrderStateMachine.Evaluate(OrderStatus.Confirmed, OrderStatus.Cancelled, Ctx(Role.Admin)).Allowed);
    }

    [Fact]
    public void Kitchen_rejection_needs_a_reason()
    {
        AssertRejected(OrderStatus.Placed, OrderStatus.Cancelled, Kitchen(reason: null));
        AssertRejected(OrderStatus.Placed, OrderStatus.Cancelled, Kitchen(reason: "  "));
    }

    [Fact]
    public void Failed_delivery_needs_a_reason()
    {
        AssertRejected(OrderStatus.OutForDelivery, OrderStatus.Failed, Shipper());
        Assert.True(OrderStateMachine.Evaluate(OrderStatus.OutForDelivery, OrderStatus.Failed, Shipper(reason: "customer unreachable")).Allowed);
    }

    [Fact]
    public void Cod_order_can_be_delivered_once_cash_is_confirmed()
    {
        Assert.True(OrderStateMachine.Evaluate(OrderStatus.OutForDelivery, OrderStatus.Delivered, Shipper(cod: true, cash: true)).Allowed);
    }

    // ----- invalid transitions S-01 to S-12 -----

    [Fact] public void S01_Placed_to_Preparing() => AssertRejected(OrderStatus.Placed, OrderStatus.Preparing, Kitchen());
    [Fact] public void S02_Placed_to_Ready() => AssertRejected(OrderStatus.Placed, OrderStatus.Ready, Kitchen());
    [Fact] public void S03_Confirmed_to_Placed() => AssertRejected(OrderStatus.Confirmed, OrderStatus.Placed, Kitchen());
    [Fact] public void S04_Preparing_to_Cancelled_by_customer() => AssertRejected(OrderStatus.Preparing, OrderStatus.Cancelled, Ctx(Role.Local));
    [Fact] public void S05_Delivered_to_Cancelled() => AssertRejected(OrderStatus.Delivered, OrderStatus.Cancelled, Ctx(Role.Admin));
    [Fact] public void S06_Cancelled_to_Confirmed() => AssertRejected(OrderStatus.Cancelled, OrderStatus.Confirmed, Kitchen());
    [Fact] public void S07_OutForDelivery_to_Cancelled() => AssertRejected(OrderStatus.OutForDelivery, OrderStatus.Cancelled, Ctx(Role.Admin));

    [Fact]
    public void S08_Ready_to_OutForDelivery_without_assigned_shipper() =>
        AssertRejected(OrderStatus.Ready, OrderStatus.OutForDelivery, Ctx(Role.Shipper, ShipperId, shipper: null));

    [Fact]
    public void S09_OutForDelivery_to_Delivered_for_COD_without_cash() =>
        AssertRejected(OrderStatus.OutForDelivery, OrderStatus.Delivered, Shipper(cod: true, cash: false));

    [Fact]
    public void S10_Placed_to_Confirmed_by_a_customer() =>
        AssertRejected(OrderStatus.Placed, OrderStatus.Confirmed, Ctx(Role.Local));

    [Fact]
    public void S11_Placed_to_Confirmed_by_kitchen_staff_of_another_hub() =>
        AssertRejected(OrderStatus.Placed, OrderStatus.Confirmed, Ctx(Role.KitchenStaff, 3, actorHub: 99));

    [Fact] public void S12_Failed_to_Delivered() => AssertRejected(OrderStatus.Failed, OrderStatus.Delivered, Shipper());

    [Fact]
    public void Another_shipper_cannot_take_the_order()
    {
        var other = Ctx(Role.Shipper, userId: 8, shipper: ShipperId);
        AssertRejected(OrderStatus.Ready, OrderStatus.OutForDelivery, other);
    }

    // ----- exhaustive: every pair that is not in the specification is rejected for every role -----

    [Fact]
    public void Every_transition_outside_section_C_is_rejected_for_every_role()
    {
        var allowed = new HashSet<(OrderStatus, OrderStatus)>
        {
            (OrderStatus.Placed, OrderStatus.Confirmed),
            (OrderStatus.Placed, OrderStatus.Cancelled),
            (OrderStatus.Confirmed, OrderStatus.Preparing),
            (OrderStatus.Confirmed, OrderStatus.Cancelled),
            (OrderStatus.Preparing, OrderStatus.Ready),
            (OrderStatus.Ready, OrderStatus.OutForDelivery),
            (OrderStatus.OutForDelivery, OrderStatus.Delivered),
            (OrderStatus.OutForDelivery, OrderStatus.Failed),
        };

        foreach (var from in Enum.GetValues<OrderStatus>())
        foreach (var to in Enum.GetValues<OrderStatus>())
        foreach (var role in Enum.GetValues<Role>())
        {
            if (allowed.Contains((from, to))) continue;
            var ctx = Ctx(role, userId: ShipperId, actorHub: Hub, shipper: ShipperId, cod: false, cash: true, reason: "x");
            Assert.False(OrderStateMachine.Evaluate(from, to, ctx).Allowed, $"{from} -> {to} as {role} must be rejected");
        }
    }
}
