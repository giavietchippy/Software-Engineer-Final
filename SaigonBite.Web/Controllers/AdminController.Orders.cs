using Microsoft.AspNetCore.Mvc;
using SaigonBite.Core;
using SaigonBite.Web.Models;
using SaigonBite.Web.Services;

namespace SaigonBite.Web.Controllers;

// Order management and system settings for Admin. Services come per action ([FromServices]) so the
// existing constructor, and the tests that call it, stay exactly as they were.
public sealed partial class AdminController
{
    [HttpGet]
    public async Task<IActionResult> Orders(OrderStatus? status, int? hubId, [FromServices] StaffOrderReadService read) =>
        View(new AdminOrdersViewModel
        {
            Orders = await read.GetAdminOrdersAsync(status, hubId),
            Shippers = await read.GetShippersAsync(),
            Hubs = await read.GetHubsAsync(),
            Status = status,
            HubId = hubId
        });

    [HttpPost]
    public async Task<IActionResult> Assign(int orderId, int shipperId, string? returnUrl,
        [FromServices] ShipperAssignmentService assignments)
    {
        var result = await assignments.AssignAsync(orderId, shipperId, CurrentUserId());
        Notify(result.Accepted, "AdminAssigned", result.Error);
        return BackToOrders(returnUrl);
    }

    [HttpPost]
    public async Task<IActionResult> Reassign(int orderId, int shipperId, string? returnUrl,
        [FromServices] ShipperAssignmentService assignments)
    {
        var result = await assignments.ReassignAsync(orderId, shipperId, CurrentUserId());
        Notify(result.Accepted, "AdminReassigned", result.Error);
        return BackToOrders(returnUrl);
    }

    [HttpPost]
    public async Task<IActionResult> Unassign(int orderId, string? returnUrl,
        [FromServices] ShipperAssignmentService assignments)
    {
        var result = await assignments.UnassignAsync(orderId, CurrentUserId());
        Notify(result.Accepted, "AdminUnassigned", result.Error);
        return BackToOrders(returnUrl);
    }

    // BR16: an Admin may cancel only a Confirmed order; the state machine rejects every other status.
    [HttpPost]
    public async Task<IActionResult> CancelOrder(int id, string? reason, string? returnUrl,
        [FromServices] OrderStatusService status)
    {
        var result = await status.CancelAsync(id, CurrentUserId(), reason);
        Notify(result.Accepted, "OrderCancelled", result.Error);
        return BackToOrders(returnUrl);
    }

    [HttpGet]
    public IActionResult Settings([FromServices] SettingsService settings) =>
        View(new AdminSettingsViewModel { HeavyRain = settings.GetHeavyRain() });

    [HttpPost]
    public async Task<IActionResult> Settings(bool heavyRain, [FromServices] SettingsService settings)
    {
        await settings.SetHeavyRainAsync(heavyRain, CurrentUserId());
        Notify(true, "SettingsSaved", null);
        return RedirectToAction(nameof(Settings));
    }

    private IActionResult BackToOrders(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction(nameof(Orders));

    private void Notify(bool accepted, string successKey, string? error)
    {
        TempData["Toast"] = accepted ? successKey : (error ?? "Error");
        TempData["ToastType"] = accepted ? "success" : "error";
    }
}
