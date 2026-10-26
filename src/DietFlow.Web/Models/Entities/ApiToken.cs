using System.ComponentModel.DataAnnotations;

namespace DietFlow.Web.Models.Entities;

public class ApiToken
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = null!;
    public AppUser User { get; set; } = null!;

    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    [Required, MaxLength(64)]
    public string TokenHash { get; set; } = null!;

    [Required, MaxLength(12)]
    public string Prefix { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
