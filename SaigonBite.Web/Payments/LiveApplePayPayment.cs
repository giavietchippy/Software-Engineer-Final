using SaigonBite.Core;
using SaigonBite.Core.Payments;

namespace SaigonBite.Web.Payments;

/// <summary>
/// Apple Pay needs a merchant certificate and browser Payment Request API. In Live mode the
/// mock capture remains; use PayPal Live for a real Tourist sandbox redirect.
/// </summary>
public sealed class LiveApplePayPayment : IPaymentStrategy
{
    private readonly ApplePayPayment _inner = new();

    public PaymentMethod Method => _inner.Method;
    public bool IsAllowedFor(Role payer) => _inner.IsAllowedFor(payer);
    public string? CheckAmount(decimal roundedTotal) => _inner.CheckAmount(roundedTotal);
    public PaymentResult Pay(PaymentRequest request) => _inner.Pay(request);
    public PaymentResult Refund(PaymentRequest request) => _inner.Refund(request);
}