using SaigonBite.Core;
using SaigonBite.Core.Payments;

namespace SaigonBite.Web.Payments;

public sealed class LiveVnPayPayment : IPaymentStrategy
{
    private readonly VnPayPayment _rules = new();
    private readonly VnPayGateway _gateway;

    public LiveVnPayPayment(VnPayGateway gateway) => _gateway = gateway;

    public PaymentMethod Method => _rules.Method;
    public bool IsAllowedFor(Role payer) => _rules.IsAllowedFor(payer);
    public string? CheckAmount(decimal roundedTotal) => _rules.CheckAmount(roundedTotal);

    public PaymentResult Pay(PaymentRequest request)
    {
        if (!_gateway.IsConfigured)
            return new PaymentResult(false, PaymentStatus.Failed, string.Empty,
                "VNPay sandbox is not configured. Set Payments:VnPay in appsettings or use Payments:Mode Mock.");

        var txnRef = request.TransactionRef ?? Guid.NewGuid().ToString("N")[..12];
        var url = _gateway.BuildPaymentUrl(txnRef, request.Amount, DateTime.Now);
        return new PaymentResult(true, PaymentStatus.Pending, txnRef, null, url);
    }

    public PaymentResult Refund(PaymentRequest request) => _rules.Refund(request);
}