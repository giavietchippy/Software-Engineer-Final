using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SaigonBite.Core.Payments;
using SaigonBite.Web.Data;
using SaigonBite.Web.Payments;

namespace SaigonBite.Web.Services;

public sealed class PaymentReturnService
{
    private readonly OrderService _orders;
    private readonly SaigonBiteDbContext _db;
    private readonly VnPayGateway _vnPay;
    private readonly MomoGateway _momo;
    private readonly PayPalGateway _payPal;

    public PaymentReturnService(
        OrderService orders,
        SaigonBiteDbContext db,
        VnPayGateway vnPay,
        MomoGateway momo,
        PayPalGateway payPal)
    {
        _orders = orders;
        _db = db;
        _vnPay = vnPay;
        _momo = momo;
        _payPal = payPal;
    }

    public Task<OrderOutcome> CompleteVnPayReturnAsync(IQueryCollection query)
    {
        var validation = _vnPay.ValidateReturn(query);
        if (!validation.Success)
            return Task.FromResult(OrderOutcome.Failed(validation.Error ?? "VNPay validation failed."));

        return _orders.FinalizeStagedCheckoutAsync(
            validation.TransactionRef!,
            validation.Amount,
            new PaymentResult(true, PaymentStatus.Paid, validation.TransactionRef!, "VNPay sandbox"));
    }

    public Task<OrderOutcome> CompleteMomoReturnAsync(IQueryCollection query)
    {
        var validation = _momo.ValidateReturn(query);
        if (!validation.Success)
            return Task.FromResult(OrderOutcome.Failed(validation.Error ?? "MoMo validation failed."));

        return _orders.FinalizeStagedCheckoutAsync(
            validation.TransactionRef!,
            validation.Amount,
            new PaymentResult(true, PaymentStatus.Paid, validation.TransactionRef!, "MoMo sandbox"));
    }

    public async Task<OrderOutcome> CompletePayPalReturnAsync(string payPalOrderId)
    {
        var capture = await _payPal.CaptureAsync(payPalOrderId);
        if (!capture.Success)
            return OrderOutcome.Failed(capture.Error ?? "PayPal capture failed.");

        var pending = await _db.PendingOnlinePayments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProviderReference == payPalOrderId && !p.Completed);
        if (pending is null)
            return OrderOutcome.Failed("No staged checkout found for this PayPal order.");

        return await _orders.FinalizeStagedCheckoutAsync(
            pending.TransactionRef,
            0m,
            new PaymentResult(true, PaymentStatus.Paid, capture.Reference!, "PayPal sandbox"),
            trustPayloadAmount: true);
    }
}