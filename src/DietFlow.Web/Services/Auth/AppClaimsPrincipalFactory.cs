using System.Security.Claims;
using DietFlow.Web.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace DietFlow.Web.Services.Auth;

public class AppClaimsPrincipalFactory(
    UserManager<AppUser> users, RoleManager<IdentityRole> roles, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser, IdentityRole>(users, roles, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(AppClaims.Plan, user.Plan.ToString()));
        identity.AddClaim(new Claim(AppClaims.DisplayName, user.DisplayName ?? user.Email ?? ""));
        return identity;
    }
}
