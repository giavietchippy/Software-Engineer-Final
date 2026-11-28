using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SaigonBite.Core;
using SaigonBite.Web.Models;
using SaigonBite.Web.Services;

namespace SaigonBite.Web.Controllers;

/// <summary>Shipper screen: pick up, confirm COD cash, mark delivered or failed. Only the assigned shipper succeeds.</summary>
[Authorize(Policy = nameof(Role.Shipper))]
public sealed class ShipperController : Controller
{
    private readonly StaffOrderReadService _read;
    private readonly OrderStatusService _status;
    private readonly ShipperAssignmentService _assignments;

    public ShipperController(StaffOrderReadService read, OrderStatusService status, ShipperAssignmentService assignments)
    {
        _read = read;
        _status = status;
        _assignments = assignments;
    }

    [HttpGet]
    public async Task<IActionResult> Deliveries() =>
        View(new ShipperViewModel
        {
            Orders = await _read.GetShipperOrdersAsync(CurrentUserId()),
            OutForDeliveryLimit = ShipperAssignmentService.MaxOutForDelivery
        });

    [HttpPost]
    public async Task<IActionResult> Pickup(int id)
    {
        var result = await _status.ChangeStatusAsync(id, OrderStatus.OutForDelivery, CurrentUserId());
        Notify(result.Accepted, "ShipperPickedUp", result.Error);
        return RedirectToAction(nameof(Deliveries));
    }

    [HttpPost]
    public async Task<IActionResult> ConfirmCash(int id)
    {
        var result = await _assignments.ConfirmCashAsync(id, CurrentUserId());
        Notify(result.Accepted, "ShipperCashConfirmed", result.Error);
        return RedirectToAction(nameof(Deliveries));
    }

    [HttpPost]
    public async Task<IActionResult> Delivered(int id)
    {
        var result = await _status.ChangeStatusAsync(id, OrderStatus.Delivered, CurrentUserId());
        Notify(result.Accepted, "ShipperDelivered", result.Error);
        return RedirectToAction(nameof(Deliveries));
    }

    [HttpPost]
    public async Task<IActionResult> Fail(int id, string? reason)
    {
        var result = await _status.FailDeliveryAsync(id, CurrentUserId(), reason ?? string.Empty);
        Notify(result.Accepted, "ShipperFailed", result.Error);
        return RedirectToAction(nameof(Deliveries));
    }

    private void Notify(bool accepted, string successKey, string? error)
    {
        TempData["Toast"] = accepted ? successKey : (error ?? "Error");
        TempData["ToastType"] = accepted ? "success" : "error";
    }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;
}
