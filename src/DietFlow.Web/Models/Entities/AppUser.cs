using Microsoft.AspNetCore.Identity;

namespace DietFlow.Web.Models.Entities;

public enum SubscriptionPlan
{
    Free = 0,
    Premium = 1
}

public class AppUser : IdentityUser
{
    public string? DisplayName { get; set; }
    public int DailyKcalGoal { get; set; } = 2000;
    public SubscriptionPlan Plan { get; set; } = SubscriptionPlan.Free;
    public ICollection<Meal> Meals { get; set; } = new List<Meal>();
}
