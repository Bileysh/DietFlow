using System.ComponentModel.DataAnnotations;

namespace DietFlow.Web.Models.Entities;

public class Store
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = null!;

    [Required, MaxLength(100)]
    public string Chain { get; set; } = null!;

    [MaxLength(300)]
    public string? Address { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public int CityId { get; set; }
    public City City { get; set; } = null!;

    public ICollection<StorePrice> Prices { get; set; } = new List<StorePrice>();
}
