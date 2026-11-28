using SaigonBite.Core;
using SaigonBite.Core.Payments;

namespace SaigonBite.Web.Payments;

public sealed class LiveMomoPayment : IPaymentStrategy
{
    private readonly MomoPayment _rules = new();
    private readonly MomoGateway _gateway;

    public LiveMomoPayment(MomoGateway gateway) => _gateway = gateway;

    public PaymentMethod Method => _rules.Method;
    public bool IsAllowedFor(Role payer) => _rules.IsAllowedFor(payer);
    public string? CheckAmount(decimal roundedTotal) => _rules.CheckAmount(roundedTotal);

    public PaymentResult Pay(PaymentRequest request)
    {
        if (!_gateway.IsConfigured)
            return new PaymentResult(false, PaymentStatus.Failed, string.Empty,
                "MoMo sandbox is not configured. Set Payments:Momo in appsettings or use Payments:Mode Mock.");

        var txnRef = request.TransactionRef ?? Guid.NewGuid().ToString("N")[..12];
        try
        {
            var created = _gateway.CreatePaymentAsync(txnRef, request.Amount).GetAwaiter().GetResult();
            return new PaymentResult(true, PaymentStatus.Pending, created.OrderId, null, created.PayUrl);
        }
        catch (Exception ex)
        {
            return new PaymentResult(false, PaymentStatus.Failed, txnRef, ex.Message);
        }
    }

    public PaymentResult Refund(PaymentRequest request) => _rules.Refund(request);
}