using System.ComponentModel.DataAnnotations;

namespace DietFlow.Web.Models.Entities;

public class OfficialPrice
{
    [MaxLength(2)]
    public string RegionCode { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    [MaxLength(32)]
    public string StatCode { get; set; } = null!;
    [MaxLength(200)]
    public string StatName { get; set; } = null!;

    public decimal PricePerKg { get; set; }
    public DateOnly Period { get; set; }
}
