using System.ComponentModel.DataAnnotations;

namespace DietFlow.Web.Models.ViewModels;

public class StoreFormVm
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Вкажіть назву"), MaxLength(200)]
    [Display(Name = "Назва")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Вкажіть мережу"), MaxLength(100)]
    [Display(Name = "Мережа")]
    public string Chain { get; set; } = "";

    [MaxLength(300)]
    [Display(Name = "Адреса")]
    public string? Address { get; set; }

    [Range(-90, 90, ErrorMessage = "Широта від -90 до 90")]
    [Display(Name = "Широта")]
    public double Latitude { get; set; }

    [Range(-180, 180, ErrorMessage = "Довгота від -180 до 180")]
    [Display(Name = "Довгота")]
    public double Longitude { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Оберіть місто")]
    [Display(Name = "Місто")]
    public int CityId { get; set; }
}
