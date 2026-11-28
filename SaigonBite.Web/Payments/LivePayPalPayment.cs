using SaigonBite.Core;
using SaigonBite.Core.Payments;

namespace SaigonBite.Web.Payments;

public sealed class LivePayPalPayment : IPaymentStrategy
{
    private readonly PayPalPayment _rules = new();
    private readonly PayPalGateway _gateway;

    public LivePayPalPayment(PayPalGateway gateway) => _gateway = gateway;

    public PaymentMethod Method => _rules.Method;
    public bool IsAllowedFor(Role payer) => _rules.IsAllowedFor(payer);
    public string? CheckAmount(decimal roundedTotal) => _rules.CheckAmount(roundedTotal);

    public PaymentResult Pay(PaymentRequest request)
    {
        if (!_gateway.IsConfigured)
            return new PaymentResult(false, PaymentStatus.Failed, string.Empty,
                "PayPal sandbox is not configured. Set Payments:PayPal in appsettings or use Payments:Mode Mock.");

        var txnRef = request.TransactionRef ?? Guid.NewGuid().ToString("N")[..12];
        try
        {
            var created = _gateway.CreateCheckoutAsync(txnRef, request.Amount).GetAwaiter().GetResult();
            return new PaymentResult(true, PaymentStatus.Pending, txnRef, created.OrderId, created.ApproveUrl);
        }
        catch (Exception ex)
        {
            return new PaymentResult(false, PaymentStatus.Failed, txnRef, ex.Message);
        }
    }

    public PaymentResult Refund(PaymentRequest request) => _rules.Refund(request);
}