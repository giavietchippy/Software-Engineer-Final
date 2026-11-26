namespace SaigonBite.Core.Payments;

/// <summary>VnPay: online method for the Local customer group (BR13).</summary>
public sealed class VnPayPayment : MockOnlinePaymentBase
{
    public override PaymentMethod Method => PaymentMethod.VnPay;
    public override bool IsAllowedFor(Role payer) => payer == Role.Local;
}
