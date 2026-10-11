using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DietFlow.Web.Models.ViewModels;

public class StorePriceFormVm
{
    public int StoreId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Оберіть продукт")]
    [Display(Name = "Продукт")]
    public int ProductId { get; set; }

    [Range(0.01, 100000, ErrorMessage = "Ціна від 0.01 до 100 000 грн")]
    [Display(Name = "Ціна, грн/кг")]
    public decimal PricePerKg { get; set; }

    [ValidateNever] public string StoreName { get; set; } = "";
    [ValidateNever] public string? ProductName { get; set; }
}
