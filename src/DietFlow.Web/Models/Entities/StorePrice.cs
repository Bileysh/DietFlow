namespace DietFlow.Web.Models.Entities;

public enum PriceSource
{
    Demo = 0,
    User = 1,
    Stat = 2,
}

public class StorePrice
{
    public int StoreId { get; set; }
    public Store Store { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public decimal PricePerKg { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public PriceSource Source { get; set; } = PriceSource.Demo;
}
