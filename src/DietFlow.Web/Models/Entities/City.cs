using System.ComponentModel.DataAnnotations;

namespace DietFlow.Web.Models.Entities;

public class City
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public ICollection<Store> Stores { get; set; } = new List<Store>();
}
