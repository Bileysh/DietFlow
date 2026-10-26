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
    public DbSet<City> Cities => Set<City>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StorePrice> StorePrices => Set<StorePrice>();
    public DbSet<OfficialPrice> OfficialPrices => Set<OfficialPrice>();
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();
    public DbSet<AiAnalysisLog> AiAnalysisLogs => Set<AiAnalysisLog>();
    public DbSet<MealRecommendation> MealRecommendations => Set<MealRecommendation>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        
        b.HasPostgresExtension("pg_trgm");

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

        b.Entity<Store>()
            .HasOne(s => s.City).WithMany(c => c.Stores)
            .HasForeignKey(s => s.CityId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Store>().HasIndex(s => new { s.CityId, s.Chain });
        b.Entity<Store>().HasIndex(s => s.OsmRef).IsUnique().HasFilter("\"OsmRef\" IS NOT NULL");
        b.Entity<Store>().HasIndex(s => new { s.Latitude, s.Longitude });
        b.Entity<City>().HasIndex(c => c.Name).IsUnique();

        b.Entity<StorePrice>().HasKey(sp => new { sp.StoreId, sp.ProductId });
        b.Entity<ApiToken>().HasIndex(t => t.TokenHash).IsUnique();
        b.Entity<ApiToken>().HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<AiAnalysisLog>().HasIndex(l => new { l.UserId, l.CreatedAt });
        b.Entity<AiAnalysisLog>().HasOne<AppUser>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<OfficialPrice>().HasKey(op => new { op.RegionCode, op.ProductId });
        b.Entity<OfficialPrice>().Property(op => op.PricePerKg).HasPrecision(10, 2);
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
        b.Entity<Product>().HasIndex(p => p.Name, "IX_Products_Name_trgm").HasMethod("gin").HasOperators("gin_trgm_ops");
        b.Entity<Product>().Property(p => p.SortRank).HasComputedColumnSql(
            "CASE WHEN \"Barcode\" IS NULL THEN 0 WHEN \"Name\" ~ '[А-Яа-яЁёЇїІіЄєҐґ]' THEN 1 ELSE 2 END", stored: true);
        b.Entity<Product>().HasIndex(p => new { p.SortRank, p.Name, p.Id });
        b.Entity<Product>().HasIndex(p => p.Category); 
        b.Entity<Product>().HasIndex(p => p.Barcode).IsUnique().HasFilter("\"Barcode\" IS NOT NULL");
        foreach (var prop in new[] { "KcalPer100g", "ProteinPer100g", "FatPer100g", "CarbsPer100g" })
            b.Entity<Product>().Property(prop).HasPrecision(7, 2);

        b.Entity<Meal>().Property(m => m.ProteinG).HasPrecision(7, 2);
        b.Entity<Meal>().Property(m => m.FatG).HasPrecision(7, 2);
        b.Entity<Meal>().Property(m => m.CarbsG).HasPrecision(7, 2);
        b.Entity<Meal>().Property(m => m.AnalysisJson).HasColumnType("jsonb");
    }
}
