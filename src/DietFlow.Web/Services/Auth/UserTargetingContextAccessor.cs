using System.Security.Claims;
using Microsoft.FeatureManagement.FeatureFilters;

namespace DietFlow.Web.Services.Auth;

public class UserTargetingContextAccessor(IHttpContextAccessor http) : ITargetingContextAccessor
{
    public ValueTask<TargetingContext> GetContextAsync()
    {
        var user = http.HttpContext?.User;
        return ValueTask.FromResult(For(user?.FindFirstValue(ClaimTypes.NameIdentifier), user?.FindFirstValue(AppClaims.Plan)));
    }

    public static TargetingContext For(string? userId, string? plan) => new()
    {
        UserId = userId ?? "anonymous",
        Groups = plan is null ? [] : [plan],
    };
}
