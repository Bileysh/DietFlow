using System.Security.Claims;
using DietFlow.Web.Data;
using DietFlow.Web.Models.Entities;
using Microsoft.AspNetCore.Identity;

namespace DietFlow.Web.Services.Auth;

public class UserAccountService(
    UserManager<AppUser> users, RoleManager<IdentityRole> roles, IConfiguration config, IWebHostEnvironment env)
{
    public bool IsAdminEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        if (env.IsDevelopment() && email.Equals(DbSeeder.DemoUserEmail, StringComparison.OrdinalIgnoreCase)) return true;
        var admins = config.GetSection("Admin:Emails").Get<string[]>() ?? [];
        return admins.Any(a => a.Trim().Equals(email, StringComparison.OrdinalIgnoreCase));
    }

    public async Task SyncAdminRoleAsync(AppUser user)
    {
        if (!await roles.RoleExistsAsync(AppRoles.Admin))
            await roles.CreateAsync(new IdentityRole(AppRoles.Admin));

        var shouldBeAdmin = IsAdminEmail(user.Email);
        var isAdmin = await users.IsInRoleAsync(user, AppRoles.Admin);
        if (shouldBeAdmin && !isAdmin) await users.AddToRoleAsync(user, AppRoles.Admin);
        if (!shouldBeAdmin && isAdmin) await users.RemoveFromRoleAsync(user, AppRoles.Admin);
    }
    
    public async Task<(AppUser? User, string? Error)> FindOrCreateExternalAsync(ExternalLoginInfo info)
    {
        var user = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (user is null)
        {
            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(email)) return (null, "Google не передав email — вхід неможливий.");

            user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                user = new AppUser
                {
                    UserName = email, Email = email, EmailConfirmed = true,
                    DisplayName = info.Principal.FindFirstValue(ClaimTypes.Name) ?? email,
                };
                var created = await users.CreateAsync(user);
                if (!created.Succeeded) return (null, string.Join(" ", created.Errors.Select(e => e.Description)));
            }

            var linked = await users.AddLoginAsync(user, info);
            if (!linked.Succeeded) return (null, string.Join(" ", linked.Errors.Select(e => e.Description)));
        }

        await SyncAdminRoleAsync(user);
        return (user, null);
    }
}
