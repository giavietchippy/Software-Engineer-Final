namespace SaigonBite.Core.Payments;

/// <summary>
/// Shared simulated gateway for online methods. Concrete classes only say who may use them.
/// For Tier 4 override Pay/Refund in VnPayPayment or PayPalPayment to call the live sandbox.
/// </summary>
public abstract class MockOnlinePaymentBase : IPaymentStrategy
{
    public abstract PaymentMethod Method { get; }
    public abstract bool IsAllowedFor(Role payer);

    public virtual string? CheckAmount(decimal roundedTotal) => null;

    public virtual PaymentResult Pay(PaymentRequest request) =>
        new(true, PaymentStatus.Paid, $"{Method}-{request.OrderId}-PAID");

    public virtual PaymentResult Refund(PaymentRequest request) =>
        new(true, PaymentStatus.Refunded, $"{Method}-{request.OrderId}-REFUND");
}
