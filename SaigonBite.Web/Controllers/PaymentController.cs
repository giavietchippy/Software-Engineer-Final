using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SaigonBite.Web.Services;

namespace SaigonBite.Web.Controllers;

/// <summary>Return URLs for live sandbox gateways. No business rules here.</summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public sealed class PaymentController : Controller
{
    private readonly PaymentReturnService _returns;

    public PaymentController(PaymentReturnService returns) => _returns = returns;

    [HttpGet]
    public async Task<IActionResult> VnPayReturn()
    {
        var outcome = await _returns.CompleteVnPayReturnAsync(Request.Query);
        return ToOrderResult(outcome);
    }

    [HttpGet]
    public async Task<IActionResult> MomoReturn()
    {
        var outcome = await _returns.CompleteMomoReturnAsync(Request.Query);
        return ToOrderResult(outcome);
    }

    [HttpGet]
    public async Task<IActionResult> PayPalReturn(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return RedirectToAction("Hubs", "Order");

        var outcome = await _returns.CompletePayPalReturnAsync(token);
        return ToOrderResult(outcome);
    }

    [HttpGet]
    public IActionResult PayPalCancel() =>
        RedirectToAction("Hubs", "Order");

    [HttpPost]
    public IActionResult MomoIpn() => Ok();

    private IActionResult ToOrderResult(OrderOutcome outcome)
    {
        if (outcome.Accepted && outcome.OrderId is int id)
        {
            TempData["NoticeKey"] = "OrderPlaced";
            return RedirectToAction("Track", "Order", new { id });
        }

        TempData["PaymentError"] = outcome.Message ?? "Payment was not completed.";
        return RedirectToAction("Hubs", "Order");
    }
}