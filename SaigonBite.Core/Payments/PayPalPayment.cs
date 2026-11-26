namespace SaigonBite.Core.Payments;

/// <summary>PayPal: online method for the Tourist customer group (BR13).</summary>
public sealed class PayPalPayment : MockOnlinePaymentBase
{
    public override PaymentMethod Method => PaymentMethod.PayPal;
    public override bool IsAllowedFor(Role payer) => payer == Role.Tourist;
}
