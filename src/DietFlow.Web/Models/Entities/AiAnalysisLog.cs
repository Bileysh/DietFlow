namespace DietFlow.Web.Models.Entities;

public enum AiUsageKind
{
    PhotoAnalysis = 0,
    AgentMessage = 1,
}

public class AiAnalysisLog
{
    public long Id { get; set; }
    public string UserId { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public AiUsageKind Kind { get; set; } = AiUsageKind.PhotoAnalysis;
}
