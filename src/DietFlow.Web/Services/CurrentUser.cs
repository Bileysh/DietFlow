using System.Security.Claims;

namespace DietFlow.Web.Services;

public class CurrentUser(IHttpContextAccessor http)
{
    public string? Id => http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

    public Task<string> GetIdAsync() =>
        Task.FromResult(Id ?? throw new InvalidOperationException("Користувач не увійшов — дія має бути позначена [Authorize]."));
}
