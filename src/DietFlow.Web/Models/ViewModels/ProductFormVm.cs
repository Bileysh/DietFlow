using System.ComponentModel.DataAnnotations;

namespace DietFlow.Web.Models.ViewModels;

public class ProductFormVm : IValidatableObject
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Вкажіть назву"), MaxLength(200)]
    [Display(Name = "Назва")]
    public string Name { get; set; } = "";

    [MaxLength(100)]
    [Display(Name = "Категорія")]
    public string? Category { get; set; }

    [Range(0, 900, ErrorMessage = "Від 0 до 900 ккал (900 — чистий жир)")]
    [Display(Name = "Ккал / 100 г")]
    public decimal KcalPer100g { get; set; }

    [Range(0, 100, ErrorMessage = "Від 0 до 100 г")]
    [Display(Name = "Білки, г")]
    public decimal ProteinPer100g { get; set; }

    [Range(0, 100, ErrorMessage = "Від 0 до 100 г")]
    [Display(Name = "Жири, г")]
    public decimal FatPer100g { get; set; }

    [Range(0, 100, ErrorMessage = "Від 0 до 100 г")]
    [Display(Name = "Вуглеводи, г")]
    public decimal CarbsPer100g { get; set; }

    [MaxLength(50)]
    [Display(Name = "Штрихкод")]
    public string? Barcode { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ProteinPer100g + FatPer100g + CarbsPer100g > 100)
            yield return new ValidationResult(
                "Сума білків, жирів і вуглеводів не може перевищувати 100 г на 100 г продукту",
                [nameof(ProteinPer100g), nameof(FatPer100g), nameof(CarbsPer100g)]);
    }
}
