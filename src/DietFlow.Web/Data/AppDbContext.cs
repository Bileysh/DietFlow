using DietFlow.Web.Models.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Meal> Meals => Set<Meal>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StorePrice> StorePrices => Set<StorePrice>();
    public DbSet<MealRecommendation> MealRecommendations => Set<MealRecommendation>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b); 

        b.Entity<Meal>()
            .HasOne(m => m.User).WithMany(u => u.Meals)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<Meal>().HasIndex(m => new { m.UserId, m.EatenAt }); 

        b.Entity<RecipeIngredient>().HasKey(ri => new { ri.RecipeId, ri.ProductId });
        b.Entity<RecipeIngredient>()
            .HasOne(ri => ri.Recipe).WithMany(r => r.Ingredients)
            .HasForeignKey(ri => ri.RecipeId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RecipeIngredient>()
            .HasOne(ri => ri.Product).WithMany(p => p.UsedIn)
            .HasForeignKey(ri => ri.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RecipeIngredient>().Property(ri => ri.Grams).HasPrecision(8, 2);

        b.Entity<StorePrice>().HasKey(sp => new { sp.StoreId, sp.ProductId });
        b.Entity<StorePrice>()
            .HasOne(sp => sp.Store).WithMany(s => s.Prices)
            .HasForeignKey(sp => sp.StoreId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<StorePrice>()
            .HasOne(sp => sp.Product).WithMany(p => p.Prices)
            .HasForeignKey(sp => sp.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<StorePrice>().Property(sp => sp.PricePerKg).HasPrecision(10, 2);
        b.Entity<StorePrice>().HasIndex(sp => sp.ProductId); 

        b.Entity<MealRecommendation>().HasKey(mr => new { mr.MealId, mr.RecipeId });
        b.Entity<MealRecommendation>()
            .HasOne(mr => mr.Meal).WithMany(m => m.Recommendations)
            .HasForeignKey(mr => mr.MealId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<MealRecommendation>()
            .HasOne(mr => mr.Recipe).WithMany(r => r.RecommendedFor)
            .HasForeignKey(mr => mr.RecipeId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Recipe>().HasIndex(r => r.Slug).IsUnique();

        b.Entity<Product>().HasIndex(p => p.Name); 
        b.Entity<Product>().HasIndex(p => p.Barcode).IsUnique().HasFilter("\"Barcode\" IS NOT NULL");
        foreach (var prop in new[] { "KcalPer100g", "ProteinPer100g", "FatPer100g", "CarbsPer100g" })
            b.Entity<Product>().Property(prop).HasPrecision(7, 2);

        b.Entity<Meal>().Property(m => m.ProteinG).HasPrecision(7, 2);
        b.Entity<Meal>().Property(m => m.FatG).HasPrecision(7, 2);
        b.Entity<Meal>().Property(m => m.CarbsG).HasPrecision(7, 2);
        b.Entity<Meal>().Property(m => m.AnalysisJson).HasColumnType("jsonb");
    }
}
