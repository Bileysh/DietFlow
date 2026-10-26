using Microsoft.AspNetCore.Authentication;
using DietFlow.Web.Data;
using DietFlow.Web.Models.Entities;
using DietFlow.Web.Models.ViewModels;
using DietFlow.Web.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DietFlow.Web.Controllers;

[AllowAnonymous]
public class AccountController(
    SignInManager<AppUser> signIn, UserManager<AppUser> users, UserAccountService accounts, IWebHostEnvironment env) : Controller
{
    public async Task<IActionResult> Login(string? returnUrl, string? error)
    {
        if (User.Identity?.IsAuthenticated == true) return LocalRedirect(SafeReturnUrl(returnUrl));

        var providers = (await signIn.GetExternalAuthenticationSchemesAsync())
            .Select(s => (s.Name, s.DisplayName ?? s.Name)).ToList();
        return View(new LoginVm
        {
            ReturnUrl = returnUrl, Providers = providers, DemoLoginAvailable = env.IsDevelopment(), Error = error,
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ExternalLogin(string provider, string? returnUrl)
    {
        var redirectUrl = Url.Action(nameof(ExternalLoginCallback), new { returnUrl });
        var properties = signIn.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return Challenge(properties, provider);
    }

    public async Task<IActionResult> ExternalLoginCallback(string? returnUrl, string? remoteError)
    {
        if (remoteError is not null)
            return RedirectToAction(nameof(Login), new { returnUrl, error = "Google повернув помилку: " + remoteError });

        var info = await signIn.GetExternalLoginInfoAsync();
        if (info is null)
            return RedirectToAction(nameof(Login), new { returnUrl, error = "Не вдалося отримати дані від Google. Спробуйте ще раз." });

        var (user, error) = await accounts.FindOrCreateExternalAsync(info);
        if (user is null) return RedirectToAction(nameof(Login), new { returnUrl, error });

        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        await signIn.SignInAsync(user, isPersistent: true, info.LoginProvider);
        TempData["Success"] = $"Вітаємо, {user.DisplayName}!";
        return LocalRedirect(SafeReturnUrl(returnUrl));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DemoLogin(bool admin, string? returnUrl)
    {
        if (!env.IsDevelopment()) return NotFound();

        var user = await users.FindByEmailAsync(admin ? DbSeeder.DemoUserEmail : DbSeeder.DemoFreeUserEmail);
        if (user is null) return RedirectToAction(nameof(Login), new { returnUrl, error = "Демо-користувача не знайдено." });

        await accounts.SyncAdminRoleAsync(user);
        await signIn.SignInAsync(user, isPersistent: true, "demo");
        TempData["Success"] = $"Ви увійшли як «{user.DisplayName}».";
        return LocalRedirect(SafeReturnUrl(returnUrl));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        TempData["Success"] = "Ви вийшли з акаунта.";
        return RedirectToAction("Index", "Home");
    }

    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    private string SafeReturnUrl(string? returnUrl) => Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
}
