namespace SaigonBite.Core.Payments;

/// <summary>ApplePay: online method for the Tourist customer group (BR13).</summary>
public sealed class ApplePayPayment : MockOnlinePaymentBase
{
    public override PaymentMethod Method => PaymentMethod.ApplePay;
    public override bool IsAllowedFor(Role payer) => payer == Role.Tourist;
}
