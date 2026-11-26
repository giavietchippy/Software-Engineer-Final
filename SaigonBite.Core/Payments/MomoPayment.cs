namespace SaigonBite.Core.Payments;

/// <summary>Momo: online method for the Local customer group (BR13).</summary>
public sealed class MomoPayment : MockOnlinePaymentBase
{
    public override PaymentMethod Method => PaymentMethod.Momo;
    public override bool IsAllowedFor(Role payer) => payer == Role.Local;
}
