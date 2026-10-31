namespace DietFlow.Web.Services;

public class SiteOptions
{
    public string Name { get; set; } = "DietFlow";
    public string ContactEmail { get; set; } = "hello@dietflow.example";
    public string ContactPhone { get; set; } = "+380 00 000 00 00";
    public string? TelegramUrl { get; set; }
    public string BaseUrl { get; set; } = "http://localhost:5213";
    public string? NormsApiUrl { get; set; }
}
