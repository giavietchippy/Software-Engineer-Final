using S = SaigonBite.Core.OrderStatus;
using R = SaigonBite.Core.Role;

namespace SaigonBite.Core.Orders;

/// <summary>Facts the state machine needs. Ownership of the order by the customer is checked by the caller.</summary>
public sealed record TransitionContext(
    Role ActorRole,
    int ActorUserId,
    int? ActorHubId,          // hub of the kitchen staff
    int OrderHubId,
    int? AssignedShipperId,
    bool IsCod,
    bool CashConfirmed,
    string? Reason);

public sealed record TransitionResult(bool Allowed, string? Error)
{
    public static TransitionResult Ok() => new(true, null);
    public static TransitionResult Fail(string error) => new(false, error);
}

public sealed class InvalidTransitionException : Exception
{
    public InvalidTransitionException(OrderStatus from, OrderStatus to, string message)
        : base($"{from} -> {to}: {message}") { }
}

/// <summary>
/// The only place that decides whether a status change is allowed (BR15, section C).
/// The business layer calls this; hiding buttons in the UI is not enough.
/// </summary>
public static class OrderStateMachine
{
    public static TransitionResult Evaluate(OrderStatus from, OrderStatus to, TransitionContext c)
    {
        switch (from, to)
        {
            case (S.Placed, S.Confirmed):
                return KitchenOfOrderHub(c);

            case (S.Placed, S.Cancelled):   // customer, or kitchen staff with a reason (BR16)
                if (c.ActorRole.IsCustomer()) return TransitionResult.Ok();
                if (c.ActorRole == R.KitchenStaff)
                {
                    var k = KitchenOfOrderHub(c);
                    return k.Allowed ? RequireReason(c) : k;
                }
                return TransitionResult.Fail("Only the customer or kitchen staff can cancel a Placed order.");

            case (S.Confirmed, S.Preparing):
            case (S.Preparing, S.Ready):
                return KitchenOfOrderHub(c);

            case (S.Confirmed, S.Cancelled):
                return c.ActorRole.IsCustomer() || c.ActorRole == R.Admin
                    ? TransitionResult.Ok()
                    : TransitionResult.Fail("Only the customer or an admin can cancel a Confirmed order.");

            case (S.Ready, S.OutForDelivery):
                return AssignedShipper(c);

            case (S.OutForDelivery, S.Delivered):
            {
                var s = AssignedShipper(c);
                if (!s.Allowed) return s;
                return c.IsCod && !c.CashConfirmed
                    ? TransitionResult.Fail("COD cash must be confirmed before Delivered (BR14).")
                    : TransitionResult.Ok();
            }

            case (S.OutForDelivery, S.Failed):
            {
                var s = AssignedShipper(c);
                return s.Allowed ? RequireReason(c) : s;
            }

            default:
                return TransitionResult.Fail($"Transition {from} to {to} is not allowed (BR15).");
        }
    }

    /// <summary>Throws InvalidTransitionException when the change is not allowed.</summary>
    public static void Ensure(OrderStatus from, OrderStatus to, TransitionContext c)
    {
        var r = Evaluate(from, to, c);
        if (!r.Allowed) throw new InvalidTransitionException(from, to, r.Error ?? "Not allowed.");
    }

    private static TransitionResult KitchenOfOrderHub(TransitionContext c)
    {
        if (c.ActorRole != R.KitchenStaff)
            return TransitionResult.Fail("Only kitchen staff can do this.");
        return c.ActorHubId == c.OrderHubId
            ? TransitionResult.Ok()
            : TransitionResult.Fail("Kitchen staff must belong to the hub of the order.");
    }

    private static TransitionResult AssignedShipper(TransitionContext c)
    {
        if (c.ActorRole != R.Shipper)
            return TransitionResult.Fail("Only a shipper can do this.");
        if (c.AssignedShipperId is null)
            return TransitionResult.Fail("A shipper must be assigned first.");
        return c.AssignedShipperId == c.ActorUserId
            ? TransitionResult.Ok()
            : TransitionResult.Fail("This order is assigned to another shipper.");
    }

    private static TransitionResult RequireReason(TransitionContext c) =>
        string.IsNullOrWhiteSpace(c.Reason)
            ? TransitionResult.Fail("A reason is required.")
            : TransitionResult.Ok();
}
