namespace SaigonBite.Core.Payments;

/// <summary>Cash on Delivery: both customer groups, rounded total at most 1,000,000 (BR13, BR14).</summary>
public sealed class CodPayment : IPaymentStrategy
{
    public const decimal Limit = 1_000_000m;

    public PaymentMethod Method => PaymentMethod.Cod;

    public bool IsAllowedFor(Role payer) => payer.IsCustomer();

    public string? CheckAmount(decimal roundedTotal) =>
        roundedTotal > Limit ? "Cash on Delivery is only allowed up to 1,000,000 VND (BR14)." : null;

    /// <summary>Nothing is charged online. The payment stays Pending until the shipper confirms the cash.</summary>
    public PaymentResult Pay(PaymentRequest request) =>
        new(true, PaymentStatus.Pending, $"COD-{request.OrderId}-PENDING");

    public PaymentResult Refund(PaymentRequest request) =>
        new(true, PaymentStatus.NotCharged, $"COD-{request.OrderId}-NOTHING-TO-REFUND");
}
