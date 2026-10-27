using DietFlow.Web.Data;
using DietFlow.Web.Models.Entities;
using DietFlow.Web.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.FeatureManagement;

namespace DietFlow.Web.Services;

public record AnalysisQuota(bool Unlimited, int Used, int Limit)
{
    public int Left => Unlimited ? int.MaxValue : Math.Max(0, Limit - Used);
    public bool Exhausted => !Unlimited && Used >= Limit;
}

public class AnalysisQuotaService(AppDbContext db, IFeatureManager features, IConfiguration config)
{
    public int FreeDailyLimit(AiUsageKind kind) => kind == AiUsageKind.AgentMessage
        ? config.GetValue("Plans:FreeDailyAgentMessages", 20)
        : config.GetValue("Plans:FreeDailyAnalyses", 5);

    public async Task<AnalysisQuota> GetAsync(string userId, CancellationToken ct = default) =>
        await GetAsync(userId, AiUsageKind.PhotoAnalysis, ct);

    public async Task<AnalysisQuota> GetAsync(string userId, AiUsageKind kind, CancellationToken ct = default)
    {
        var plan = await db.Users.Where(u => u.Id == userId).Select(u => u.Plan).FirstAsync(ct);
        var unlimited = await features.IsEnabledAsync(
            AppFeatures.UnlimitedAiAnalysis, UserTargetingContextAccessor.For(userId, plan.ToString()));

        var since = AppTime.ToUtc(AppTime.NowLocal.Date);
        var used = await db.AiAnalysisLogs.CountAsync(l => l.UserId == userId && l.Kind == kind && l.CreatedAt >= since, ct);
        return new AnalysisQuota(unlimited, used, FreeDailyLimit(kind));
    }
}
