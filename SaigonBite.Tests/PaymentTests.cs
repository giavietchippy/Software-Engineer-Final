using SaigonBite.Core;
using SaigonBite.Core.Payments;
using SaigonBite.Core.Pricing;

namespace SaigonBite.Tests;

public class PaymentTests
{
    private static PaymentContext Context(params IPaymentStrategy[] extra) =>
        new(new IPaymentStrategy[]
        {
            new MomoPayment(), new VnPayPayment(), new ApplePayPayment(), new PayPalPayment(), new CodPayment()
        }.Concat(extra));

    private static decimal TotalFor(decimal subtotal) =>
        new PricingService().Calculate(new PricingInput(subtotal, 0.50m, 1.0m, new DateTime(2026, 11, 25, 15, 0, 0), false))
            .Breakdown!.Total;

    [Fact]
    public void T53_local_customer_cannot_use_apple_pay()
    {
        Assert.Throws<PaymentNotAllowedException>(() => Context().Resolve(PaymentMethod.ApplePay, Role.Local, 205_000m));
    }

    [Fact]
    public void T54_tourist_cannot_use_momo()
    {
        Assert.Throws<PaymentNotAllowedException>(() => Context().Resolve(PaymentMethod.Momo, Role.Tourist, 205_000m));
    }

    [Fact]
    public void Local_customer_sees_momo_vnpay_and_cod_only()
    {
        var methods = Context().AllowedMethods(Role.Local, 205_000m);
        Assert.Equal(new[] { PaymentMethod.Momo, PaymentMethod.VnPay, PaymentMethod.Cod }, methods);
    }

    [Fact]
    public void Tourist_sees_apple_pay_paypal_and_cod_only()
    {
        var methods = Context().AllowedMethods(Role.Tourist, 205_000m);
        Assert.Equal(new[] { PaymentMethod.ApplePay, PaymentMethod.PayPal, PaymentMethod.Cod }, methods);
    }

    [Fact]
    public void T55_cod_allowed_at_exactly_1_000_000()
    {
        Assert.Equal(1_000_000m, TotalFor(1_000_000m));
        Assert.NotNull(Context().Resolve(PaymentMethod.Cod, Role.Local, TotalFor(1_000_000m)));
    }

    [Fact]
    public void T56_cod_allowed_when_total_rounds_down_to_1_000_000()
    {
        Assert.Equal(1_000_000m, TotalFor(1_000_499m));
        Assert.NotNull(Context().Resolve(PaymentMethod.Cod, Role.Tourist, TotalFor(1_000_499m)));
    }

    [Fact]
    public void T57_cod_refused_when_total_rounds_up_to_1_001_000_but_online_is_allowed()
    {
        var total = TotalFor(1_000_500m);
        Assert.Equal(1_001_000m, total);
        Assert.Throws<PaymentNotAllowedException>(() => Context().Resolve(PaymentMethod.Cod, Role.Local, total));
        Assert.NotNull(Context().Resolve(PaymentMethod.Momo, Role.Local, total));
    }

    [Fact]
    public void Cod_stays_pending_and_online_is_paid()
    {
        var req = new PaymentRequest(1, 205_000m, Role.Local);
        Assert.Equal(PaymentStatus.Pending, new CodPayment().Pay(req).Status);
        Assert.Equal(PaymentStatus.Paid, new MomoPayment().Pay(req).Status);
    }

    // ----- Change Request evidence (NFR2): a new method needs one new class, no existing class edited -----

    private sealed class ZaloPayTestPayment : MockOnlinePaymentBase
    {
        public override PaymentMethod Method => (PaymentMethod)99;   // stands in for a new enum value
        public override bool IsAllowedFor(Role payer) => payer == Role.Local;
    }

    [Fact]
    public void New_payment_method_is_added_with_one_class_and_no_existing_class_changes()
    {
        var ctx = Context(new ZaloPayTestPayment());
        Assert.Contains((PaymentMethod)99, ctx.AllowedMethods(Role.Local, 100_000m));
        Assert.DoesNotContain((PaymentMethod)99, ctx.AllowedMethods(Role.Tourist, 100_000m));
    }
}
