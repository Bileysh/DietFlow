using System.ComponentModel.DataAnnotations;

namespace DietFlow.Web.Models.Entities;

public class Product
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = null!;

    [MaxLength(100)]
    public string? Category { get; set; }
    public decimal KcalPer100g { get; set; }
    public decimal ProteinPer100g { get; set; }
    public decimal FatPer100g { get; set; }
    public decimal CarbsPer100g { get; set; }

    [MaxLength(50)]
    public string? Barcode { get; set; }
    public ICollection<RecipeIngredient> UsedIn { get; set; } = new List<RecipeIngredient>();
    public ICollection<StorePrice> Prices { get; set; } = new List<StorePrice>();
}
