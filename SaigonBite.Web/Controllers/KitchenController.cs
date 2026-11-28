using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SaigonBite.Core;
using SaigonBite.Web.Models;
using SaigonBite.Web.Services;

namespace SaigonBite.Web.Controllers;

/// <summary>Kitchen screen: confirm, start preparing, mark ready, or cancel a Placed order with a reason.</summary>
[Authorize(Policy = nameof(Role.KitchenStaff))]
public sealed class KitchenController : Controller
{
    private readonly StaffOrderReadService _read;
    private readonly OrderStatusService _status;

    public KitchenController(StaffOrderReadService read, OrderStatusService status)
    {
        _read = read;
        _status = status;
    }

    [HttpGet]
    public async Task<IActionResult> Orders()
    {
        var hubId = await _read.GetHubIdAsync(CurrentUserId());
        if (hubId is null) return View(new KitchenViewModel { HasHub = false });

        return View(new KitchenViewModel
        {
            HasHub = true,
            HubName = await _read.GetHubNameAsync(hubId.Value) ?? string.Empty,
            Orders = await _read.GetKitchenOrdersAsync(hubId.Value)
        });
    }

    // Only the three kitchen steps are accepted here; cancelling has its own action so the refund runs.
    [HttpPost]
    public async Task<IActionResult> Advance(int id, OrderStatus to)
    {
        if (to is not (OrderStatus.Confirmed or OrderStatus.Preparing or OrderStatus.Ready))
            return BadRequest();

        var result = await _status.ChangeStatusAsync(id, to, CurrentUserId());
        Notify(result.Accepted, "KitchenUpdated", result.Error);
        return RedirectToAction(nameof(Orders));
    }

    [HttpPost]
    public async Task<IActionResult> Cancel(int id, string? reason)
    {
        var result = await _status.CancelAsync(id, CurrentUserId(), reason);
        Notify(result.Accepted, "OrderCancelled", result.Error);
        return RedirectToAction(nameof(Orders));
    }

    private void Notify(bool accepted, string successKey, string? error)
    {
        TempData["Toast"] = accepted ? successKey : (error ?? "Error");
        TempData["ToastType"] = accepted ? "success" : "error";
    }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;
}
