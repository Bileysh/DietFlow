using DietFlow.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Data;

public static class DbSeeder
{
   private const int RandomSeed = 42;

    private static readonly (string Name, double Lat, double Lng, decimal PriceFactor)[] CitiesData =
    [
        ("Київ",   50.4501, 30.5234, 1.05m),
        ("Львів",  49.8397, 24.0297, 1.00m),
        ("Харків", 49.9935, 36.2304, 0.95m),
        ("Одеса",  46.4825, 30.7233, 1.00m),
        ("Дніпро", 48.4647, 35.0462, 0.97m),
    ];

    private static readonly Dictionary<string, decimal> ChainFactors = new()
    {
        ["АТБ"] = 0.88m,
        ["Varus"] = 0.97m,
        ["Фора"] = 1.00m,
        ["Novus"] = 1.07m,
        ["Сільпо"] = 1.15m,
    };

    private static readonly Dictionary<string, (string Chain, int Count)[]> StoresByCity = new()
    {
        ["Київ"] = [("АТБ", 2), ("Сільпо", 2), ("Novus", 2), ("Фора", 2), ("Varus", 1)],
        ["Львів"] = [("АТБ", 2), ("Сільпо", 1), ("Фора", 1)],
        ["Харків"] = [("АТБ", 2), ("Сільпо", 1), ("Фора", 1), ("Varus", 1)],
        ["Одеса"] = [("АТБ", 2), ("Сільпо", 1), ("Фора", 1)],
        ["Дніпро"] = [("АТБ", 2), ("Сільпо", 1), ("Varus", 2)],
    };

    private static readonly (string Name, string Category, decimal Kcal, decimal P, decimal F, decimal C, decimal Price)[] ProductsData =
    [
        ("Куряче філе", "М'ясо", 110, 23.1m, 1.2m, 0, 220),
        ("Куряче стегно", "М'ясо", 185, 18, 12, 0, 150),
        ("Філе індички", "М'ясо", 114, 24, 1.5m, 0, 300),
        ("Свинина (ошийок)", "М'ясо", 267, 14, 23, 0, 230),
        ("Яловичина", "М'ясо", 187, 19, 12, 0, 380),
        ("Ковбаса варена", "М'ясні вироби", 257, 12, 22, 1.5m, 250),
        ("Лосось", "Риба", 208, 20, 13, 0, 900),
        ("Хек", "Риба", 86, 16.6m, 2.2m, 0, 220),
        ("Тунець консервований", "Риба", 116, 26, 1, 0, 600),
        ("Яйця курячі", "Яйця та молочне", 155, 12.6m, 10.6m, 1.1m, 90),
        ("Сир кисломолочний 5%", "Яйця та молочне", 121, 17, 5, 1.8m, 200),
        ("Молоко 2,5%", "Яйця та молочне", 52, 2.8m, 2.5m, 4.7m, 40),
        ("Кефір 1%", "Яйця та молочне", 40, 3, 1, 4, 45),
        ("Йогурт грецький", "Яйця та молочне", 66, 9, 2, 3.6m, 160),
        ("Сир твердий", "Яйця та молочне", 350, 25, 27, 0, 450),
        ("Масло вершкове 82%", "Яйця та молочне", 748, 0.5m, 82, 0.8m, 450),
        ("Сметана 15%", "Яйця та молочне", 162, 2.6m, 15, 3, 140),
        ("Гречка", "Крупи", 343, 13, 3.4m, 72, 60),
        ("Рис", "Крупи", 344, 6.7m, 0.7m, 78, 70),
        ("Вівсяні пластівці", "Крупи", 366, 12, 6, 62, 50),
        ("Булгур", "Крупи", 342, 12, 1.3m, 76, 90),
        ("Макарони з твердих сортів", "Крупи", 350, 12, 1.5m, 71, 80),
        ("Хліб цільнозерновий", "Хліб", 247, 13, 3.4m, 41, 80),
        ("Батон білий", "Хліб", 262, 7.5m, 2.9m, 51, 55),
        ("Картопля", "Овочі", 77, 2, 0.1m, 17, 20),
        ("Морква", "Овочі", 41, 0.9m, 0.2m, 9.6m, 20),
        ("Цибуля", "Овочі", 40, 1.1m, 0.1m, 9.3m, 20),
        ("Помідори", "Овочі", 18, 0.9m, 0.2m, 3.9m, 80),
        ("Огірки", "Овочі", 15, 0.7m, 0.1m, 3.6m, 70),
        ("Броколі", "Овочі", 34, 2.8m, 0.4m, 7, 120),
        ("Капуста білокачанна", "Овочі", 25, 1.3m, 0.1m, 5.8m, 18),
        ("Шпинат", "Овочі", 23, 2.9m, 0.4m, 3.6m, 250),
        ("Перець болгарський", "Овочі", 27, 1, 0.3m, 6, 110),
        ("Кабачки", "Овочі", 24, 0.6m, 0.3m, 4.6m, 40),
        ("Яблука", "Фрукти", 52, 0.3m, 0.2m, 14, 35),
        ("Банани", "Фрукти", 89, 1.1m, 0.3m, 23, 65),
        ("Апельсини", "Фрукти", 47, 0.9m, 0.1m, 12, 70),
        ("Ягоди заморожені", "Фрукти", 50, 0.7m, 0.3m, 12, 180),
        ("Квасоля консервована", "Бобові", 100, 6.5m, 0.5m, 17, 90),
        ("Сочевиця червона", "Бобові", 352, 24, 1, 60, 100),
        ("Нут", "Бобові", 364, 19, 6, 61, 110),
        ("Олія соняшникова", "Олії", 899, 0, 99.9m, 0, 75),
        ("Олія оливкова", "Олії", 884, 0, 100, 0, 450),
        ("Горіхи волоські", "Горіхи", 654, 15, 65, 14, 350),
        ("Мигдаль", "Горіхи", 579, 21, 50, 22, 500),
        ("Арахісова паста", "Горіхи", 588, 25, 50, 20, 300),
        ("Мед", "Інше", 304, 0.3m, 0, 82, 250),
        ("Цукор", "Інше", 398, 0, 0, 99.8m, 35),
        ("Майонез", "Соуси", 629, 1, 67, 3, 120),
    ];

    public const string DemoUserEmail = "demo@dietflow.local";

    public static async Task SeedAsync(AppDbContext db, string webRootPath)
    {
        var rnd = new Random(RandomSeed);

        await SeedDemoUserAsync(db);

        if (!await db.Cities.AnyAsync())
        {
            db.Cities.AddRange(CitiesData.Select(c => new City { Name = c.Name, Latitude = c.Lat, Longitude = c.Lng }));
            await db.SaveChangesAsync();
        }

        if (!await db.Products.AnyAsync())
        {
            db.Products.AddRange(ProductsData.Select(p => new Product
            {
                Name = p.Name, Category = p.Category,
                KcalPer100g = p.Kcal, ProteinPer100g = p.P, FatPer100g = p.F, CarbsPer100g = p.C,
            }));
            await db.SaveChangesAsync();
        }

        if (!await db.Stores.AnyAsync())
        {
            var cities = await db.Cities.ToListAsync();
            foreach (var city in cities)
            {
                if (!StoresByCity.TryGetValue(city.Name, out var chains)) continue;
                foreach (var (chain, count) in chains)
                {
                    for (var i = 1; i <= count; i++)
                    {
                        db.Stores.Add(new Store
                        {
                            Name = $"{chain} №{i}",
                            Chain = chain,
                            Address = $"{city.Name}, демо-точка",
                            CityId = city.Id,
                            Latitude = Math.Round(city.Latitude + (rnd.NextDouble() - 0.5) * 0.08, 6),
                            Longitude = Math.Round(city.Longitude + (rnd.NextDouble() - 0.5) * 0.12, 6),
                        });
                    }
                }
            }
            await db.SaveChangesAsync();
        }

        if (!await db.StorePrices.AnyAsync())
        {
            var products = await db.Products.ToListAsync();
            var stores = await db.Stores.Include(s => s.City).ToListAsync();
            var basePrice = ProductsData.ToDictionary(p => p.Name, p => p.Price);
            var cityFactor = CitiesData.ToDictionary(c => c.Name, c => c.PriceFactor);

            foreach (var store in stores)
            {
                foreach (var product in products)
                {
                    if (rnd.NextDouble() < 0.15) continue;
                    if (!basePrice.TryGetValue(product.Name, out var price)) continue;

                    var noise = 1m + (decimal)((rnd.NextDouble() - 0.5) * 0.16); 
                    var final = price
                                * ChainFactors.GetValueOrDefault(store.Chain, 1m)
                                * cityFactor.GetValueOrDefault(store.City.Name, 1m)
                                * noise;

                    db.StorePrices.Add(new StorePrice
                    {
                        StoreId = store.Id,
                        ProductId = product.Id,
                        PricePerKg = Math.Round(final, 2),
                    });
                }
            }
            await db.SaveChangesAsync();
        }

        await SeedRecipesAsync(db, webRootPath);
        await SeedDemoMealsAsync(db);
    }

    private static async Task SeedDemoUserAsync(AppDbContext db)
    {
        if (await db.Users.AnyAsync(u => u.Email == DemoUserEmail)) return;

        db.Users.Add(new AppUser
        {
            UserName = DemoUserEmail,
            NormalizedUserName = DemoUserEmail.ToUpperInvariant(),
            Email = DemoUserEmail,
            NormalizedEmail = DemoUserEmail.ToUpperInvariant(),
            DisplayName = "Демо-користувач",
            SecurityStamp = Guid.NewGuid().ToString(),
            DailyKcalGoal = 2000,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedRecipesAsync(AppDbContext db, string webRootPath)
    {
        if (await db.Recipes.AnyAsync()) return;

        var productIds = await db.Products.ToDictionaryAsync(p => p.Name, p => p.Id);

        foreach (var data in SeedRecipes.All)
        {
            var slug = Services.SlugGenerator.Slugify(data.Title);
            var recipe = new Recipe
            {
                Title = data.Title,
                Slug = slug,
                Description = data.Description,
                StepsMarkdown = data.Steps,
                Method = data.Method,
                CookTimeMinutes = data.Minutes,
                Servings = 1,
                PhotoPath = FindSeedPhoto(webRootPath, slug),
            };

            foreach (var (product, grams) in data.Ingredients)
            {
                if (productIds.TryGetValue(product, out var pid))
                    recipe.Ingredients.Add(new RecipeIngredient { ProductId = pid, Grams = grams });
            }
            db.Recipes.Add(recipe);
        }
        await db.SaveChangesAsync();
    }

    private static string? FindSeedPhoto(string webRootPath, string slug)
    {
        foreach (var ext in new[] { ".jpg", ".webp", ".png", ".svg" })
        {
            if (File.Exists(Path.Combine(webRootPath, "images", "recipes", slug + ext)))
                return $"/images/recipes/{slug}{ext}";
        }
        return null;
    }

    private static async Task SeedDemoMealsAsync(AppDbContext db)
    {
        if (await db.Meals.AnyAsync()) return;

        var userId = await db.Users.Where(u => u.Email == DemoUserEmail).Select(u => u.Id).FirstAsync();
        var today = DateOnly.FromDateTime(Services.AppTime.NowLocal);
        DateTime At(int daysAgo, int hour) =>
            Services.AppTime.ToUtc(today.AddDays(-daysAgo).ToDateTime(new TimeOnly(hour, 0)));

        db.Meals.AddRange(
            new Meal { UserId = userId, Type = MealType.Breakfast, EatenAt = At(1, 8), DishName = "Вівсянка з ягодами",
                Kcal = 384, ProteinG = 14, FatG = 9, CarbsG = 64, HealthScore = 9, Verdict = "Збалансований сніданок з клітковиною." },
            new Meal { UserId = userId, Type = MealType.Lunch, EatenAt = At(1, 13), DishName = "Смажена картопля з ковбасою",
                Kcal = 980, ProteinG = 17, FatG = 74, CarbsG = 47, HealthScore = 3, Verdict = "Забагато жиру, мало білка та овочів." },
            new Meal { UserId = userId, Type = MealType.Dinner, EatenAt = At(1, 19), DishName = "Хек на пару з рисом",
                Kcal = 418, ProteinG = 35, FatG = 5, CarbsG = 60, HealthScore = 8, Verdict = "Легка вечеря з нежирним білком." },
            new Meal { UserId = userId, Type = MealType.Breakfast, EatenAt = At(0, 8), DishName = "Омлет зі шпинатом",
                Kcal = 302, ProteinG = 21, FatG = 22, CarbsG = 5, HealthScore = 8, Verdict = "Білковий сніданок." });
        await db.SaveChangesAsync();
    }

    public static IReadOnlyCollection<string> KnownChains => ChainFactors.Keys;
}
