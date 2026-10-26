namespace DietFlow.Web.Models.Entities;

public class AiAnalysisLog
{
    public long Id { get; set; }
    public string UserId { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
