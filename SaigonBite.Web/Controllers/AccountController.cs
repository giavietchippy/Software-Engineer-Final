using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SaigonBite.Core;
using SaigonBite.Web.Services;

namespace SaigonBite.Web.Controllers;

/// <summary>Minimal UI over the Step G services; all BR18 rules stay in the service layer.</summary>
public sealed class AccountController : Controller
{
    private readonly LoginService _login;
    private readonly RegistrationService _registration;
    private readonly AccountManagementService _accounts;

    public AccountController(LoginService login, RegistrationService registration,
        AccountManagementService accounts)
    {
        _login = login;
        _registration = registration;
        _accounts = accounts;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Login(string identifier, string password, string? returnUrl = null)
    {
        var result = await _login.LoginAsync(identifier, password);
        if (!result.Accepted)
        {
            ViewData["Error"] = result.Error;
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.UserId!.Value.ToString()),
            new(ClaimTypes.Name, result.Identifier!),
            new(ClaimTypes.Role, result.Role!.Value.ToString())
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));
        return Redirect(!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    public IActionResult AccessDenied() => View();

    [HttpGet]
    public IActionResult Register() => View(new RegistrationInput(Role.Local, string.Empty, string.Empty));

    [HttpPost]
    public async Task<IActionResult> Register(RegistrationInput model)
    {
        var result = await _registration.StartRegistrationAsync(model);
        if (!result.Accepted)
        {
            ViewData["Error"] = result.Error;
            return View(model);
        }
        TempData["DemoCode"] = result.Code; // A-08: shown in the demo instead of SMS/e-mail
        return RedirectToAction(nameof(Verify), new { pendingId = result.PendingId });
    }

    [HttpGet]
    public IActionResult Verify(int pendingId)
    {
        ViewBag.PendingId = pendingId;
        ViewBag.DemoCode = TempData["DemoCode"];
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Verify(int pendingId, string code)
    {
        var result = await _registration.VerifyAsync(pendingId, code);
        if (!result.Accepted)
        {
            ViewBag.PendingId = pendingId;
            ViewData["Error"] = result.Error;
            return View();
        }
        TempData["Notice"] = "Registration complete. Please log in.";
        return RedirectToAction(nameof(Login));
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var user = await _accounts.GetOwnProfileAsync(CurrentUserId());
        return View(user);
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Profile(SelfProfileInput model)
    {
        var result = await _accounts.UpdateOwnProfileAsync(CurrentUserId(), model);
        ViewData["Notice"] = result.Accepted ? "Profile saved." : result.Error;
        return View(await _accounts.GetOwnProfileAsync(CurrentUserId()));
    }

    private int CurrentUserId()
    {
        var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(value, out var id) ? id : 0;
    }
}
