using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace SaigonBite.Web.Controllers;

/// <summary>NFR3: POST-only language toggle; the choice lives in the standard culture cookie.</summary>
public sealed class LocalizationController : Controller
{
    [HttpPost]
    public IActionResult Set(string culture, string? returnUrl = null)
    {
        var selected = string.Equals(culture, "vi", StringComparison.OrdinalIgnoreCase) ? "vi" : "en";
        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(selected)),
            new CookieOptions
            {
                Path = "/",
                Expires = DateTimeOffset.UtcNow.AddDays(365),
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                HttpOnly = true
            });

        return Redirect(!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : "/");
    }
}
