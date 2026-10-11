using DietFlow.Web.Data;
using DietFlow.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Services;

public class CityContext(IHttpContextAccessor httpContextAccessor, AppDbContext db)
{
    public const string CookieName = "df_city";

    private City? _current;
    private bool _resolved;

    public async Task<City?> GetCurrentAsync()
    {
        if (_resolved) return _current;

        var cookie = httpContextAccessor.HttpContext?.Request.Cookies[CookieName];
        if (int.TryParse(cookie, out var id))
            _current = await db.Cities.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);

        _current ??= await db.Cities.AsNoTracking().OrderBy(c => c.Id).FirstOrDefaultAsync();
        _resolved = true;
        return _current;
    }

    public void SetCurrent(int cityId)
    {
        httpContextAccessor.HttpContext?.Response.Cookies.Append(CookieName, cityId.ToString(), new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            HttpOnly = true,              
            SameSite = SameSiteMode.Lax,
            IsEssential = true,          
        });
    }
}
