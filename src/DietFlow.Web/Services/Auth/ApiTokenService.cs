using System.Security.Cryptography;
using System.Text;
using DietFlow.Web.Data;
using DietFlow.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Services.Auth;

public class ApiTokenService(AppDbContext db)
{
    private const string TokenPrefix = "df_";
    public const int MaxActivePerUser = 10;

    public async Task<(ApiToken Token, string PlainText)> CreateAsync(string userId, string name)
    {
        var plain = TokenPrefix + Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var token = new ApiToken
        {
            UserId = userId, Name = name.Trim(), TokenHash = Hash(plain), Prefix = plain[..10],
        };
        db.ApiTokens.Add(token);
        await db.SaveChangesAsync();
        return (token, plain);
    }

    public Task<int> CountActiveAsync(string userId) =>
        db.ApiTokens.CountAsync(t => t.UserId == userId && t.RevokedAt == null);

    public Task<List<ApiToken>> ListAsync(string userId) =>
        db.ApiTokens.AsNoTracking().Where(t => t.UserId == userId).OrderByDescending(t => t.CreatedAt).ToListAsync();

    public async Task<bool> RevokeAsync(string userId, int id) =>
        await db.ApiTokens.Where(t => t.Id == id && t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow)) > 0;

    public async Task<ApiToken?> ValidateAsync(string plain)
    {
        if (!plain.StartsWith(TokenPrefix, StringComparison.Ordinal) || plain.Length > 100) return null;
        var hash = Hash(plain);
        var token = await db.ApiTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null);
        if (token is null) return null;

        if (token.LastUsedAt is null || DateTime.UtcNow - token.LastUsedAt > TimeSpan.FromMinutes(1))
        {
            token.LastUsedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        return token;
    }

    private static string Hash(string plain) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plain)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
