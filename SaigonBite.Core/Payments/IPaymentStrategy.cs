namespace SaigonBite.Core.Payments;

public enum PaymentStatus { Paid, Pending, Failed, Refunded, NotCharged }

public sealed record PaymentRequest(int OrderId, decimal Amount, Role Payer);

public sealed record PaymentResult(bool Success, PaymentStatus Status, string Reference, string? Message = null);

/// <summary>Strategy interface: one implementation class per payment method (BR13).</summary>
public interface IPaymentStrategy
{
    PaymentMethod Method { get; }

    /// <summary>BR13: which customer group may use this method.</summary>
    bool IsAllowedFor(Role payer);

    /// <summary>Method-specific amount rule (BR14 for COD). Null means OK, otherwise an error message.</summary>
    string? CheckAmount(decimal roundedTotal);

    PaymentResult Pay(PaymentRequest request);
    PaymentResult Refund(PaymentRequest request);
}
