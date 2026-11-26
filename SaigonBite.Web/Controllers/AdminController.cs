using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SaigonBite.Core;
using SaigonBite.Web.Services;

namespace SaigonBite.Web.Controllers;

/// <summary>Minimal Admin wiring: Chart.js dashboard JSON plus account management (FR-R3, BR18).</summary>
[Authorize(Policy = "Admin")]
public sealed class AdminController : Controller
{
    private readonly ReportService _reports;
    private readonly AccountManagementService _accounts;

    public AdminController(ReportService reports, AccountManagementService accounts)
    {
        _reports = reports;
        _accounts = accounts;
    }

    public IActionResult Dashboard() => View();

    [HttpGet]
    public async Task<IActionResult> RevenueByCategory(DateTime? from, DateTime? to)
    {
        var range = Range(from, to); // A-21: [from, to), Vietnam local time
        var report = await _reports.GetRevenueAsync(range.From, range.To);
        return Json(report.ByCategory);
    }

    [HttpGet]
    public async Task<IActionResult> RevenueByMethod(DateTime? from, DateTime? to)
    {
        var range = Range(from, to);
        var report = await _reports.GetRevenueAsync(range.From, range.To);
        return Json(report.ByMethod);
    }

    [HttpGet]
    public async Task<IActionResult> CurrentHubLoad() =>
        Json((await _reports.GetHubPerformanceAsync())
            .Select(h => new { h.HubId, h.HubName, h.CurrentLoad }));

    [HttpGet]
    public async Task<IActionResult> Accounts(string? search = null) =>
        View(await _accounts.ListAsync(search));

    [HttpPost]
    public async Task<IActionResult> CreateStaff(StaffAccountInput model)
    {
        var result = await _accounts.CreateStaffAsync(model, CurrentUserId());
        TempData["Notice"] = result.Accepted ? "Account created." : result.Error;
        return RedirectToAction(nameof(Accounts));
    }

    [HttpPost]
    public async Task<IActionResult> Deactivate(int id)
    {
        var result = await _accounts.DeactivateAsync(id, CurrentUserId());
        TempData["Notice"] = result.Accepted ? "Account deactivated." : result.Error;
        return RedirectToAction(nameof(Accounts));
    }

    [HttpPost]
    public async Task<IActionResult> Reactivate(int id)
    {
        var result = await _accounts.ReactivateAsync(id, CurrentUserId());
        TempData["Notice"] = result.Accepted ? "Account reactivated." : result.Error;
        return RedirectToAction(nameof(Accounts));
    }

    private static (DateTime From, DateTime To) Range(DateTime? from, DateTime? to) =>
        (from ?? DateTime.Today.AddDays(-30), to ?? DateTime.Today.AddDays(1));

    private int CurrentUserId()
    {
        var value = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(value, out var id) ? id : 0;
    }
}
