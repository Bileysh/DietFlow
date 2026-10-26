using DietFlow.Web.Models.Entities;
using DietFlow.Web.Models.ViewModels;
using DietFlow.Web.Services;
using DietFlow.Web.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DietFlow.Web.Controllers;

[Authorize]
public class ProfileController(
    UserManager<AppUser> users, SignInManager<AppUser> signIn, ApiTokenService tokens, AnalysisQuotaService quota) : Controller
{
    public async Task<IActionResult> Index()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();

        return View(new ProfileVm
        {
            User = user,
            IsAdmin = User.IsInRole(AppRoles.Admin),
            Quota = await quota.GetAsync(user.Id),
            Tokens = await tokens.ListAsync(user.Id),
            NewToken = TempData["NewToken"] as string,
            Logins = (await users.GetLoginsAsync(user)).Select(l => l.ProviderDisplayName ?? l.LoginProvider).ToList(),
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Goal(GoalFormVm vm)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = ModelState.Values.SelectMany(v => v.Errors).First().ErrorMessage;
            return RedirectToAction(nameof(Index));
        }

        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        user.DailyKcalGoal = vm.DailyKcalGoal;
        await users.UpdateAsync(user);
        TempData["Success"] = $"Денну норму змінено на {vm.DailyKcalGoal} ккал.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Plan(SubscriptionPlan plan)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        user.Plan = plan;
        await users.UpdateAsync(user);
        await signIn.RefreshSignInAsync(user);
        TempData["Success"] = plan == SubscriptionPlan.Premium ? "Premium активовано (демо, без оплати)." : "Повернено план Free.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateToken(string? name)
    {
        var userId = users.GetUserId(User)!;
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
        {
            TempData["Error"] = "Вкажіть назву токена (до 100 символів), напр. «Postman» або «Telegram-бот».";
            return RedirectToAction(nameof(Index));
        }
        if (await tokens.CountActiveAsync(userId) >= ApiTokenService.MaxActivePerUser)
        {
            TempData["Error"] = $"Можна мати до {ApiTokenService.MaxActivePerUser} активних токенів — відкличте непотрібні.";
            return RedirectToAction(nameof(Index));
        }

        var (_, plain) = await tokens.CreateAsync(userId, name);
        TempData["NewToken"] = plain;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeToken(int id)
    {
        if (await tokens.RevokeAsync(users.GetUserId(User)!, id)) TempData["Success"] = "Токен відкликано.";
        return RedirectToAction(nameof(Index));
    }
}
