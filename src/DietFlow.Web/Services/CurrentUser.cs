using DietFlow.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Services;

public class CurrentUser(AppDbContext db)
{
    private string? _id;

    public async Task<string> GetIdAsync()
    {
        _id ??= await db.Users.Where(u => u.Email == DbSeeder.DemoUserEmail).Select(u => u.Id).FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("Демо-користувача не знайдено — сідер не виконався?");
        return _id;
    }
}
