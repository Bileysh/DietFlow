using System.Globalization;
using System.Text;
using DietFlow.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Data.Import;

public class StatPricesImporter(AppDbContext db, ILogger logger)
{
    private const string DataUrl =
        "https://stat.gov.ua/sdmx/workspaces/default:integration/registry/sdmx/2.1/data/" +
        "DF_PRICE_CHANGE_CONSUMER_GOODS_SERVICE/AVG_CONS_PRCS....M/SSSU?lastNObservations=1";
    
    private static readonly (string Product, string StatCode, string StatName, decimal KgPerUnit)[] Map =
    [
        ("Куряче філе", "01_1_2_4_33", "Філе куряче", 1),
        ("Свинина (ошийок)", "01_1_2_2_28", "Свинина", 1),
        ("Яловичина", "01_1_2_1_25", "Яловичина", 1),
        ("Ковбаса варена", "01_1_2_5_36", "Ковбаси варені першого ґатунку", 1),
        ("Хек", "01_1_3_1_50", "Риба морожена", 1),
        ("Яйця курячі", "01_1_4_7_73", "Яйця", 0.6m),          
        ("Сир кисломолочний 5%", "01_1_4_5_68", "Сири м’які жирні", 1),
        ("Молоко 2,5%", "01_1_4_1_59", "Молоко пастеризоване жирністю до 2,6%", 1),
        ("Сметана 15%", "01_1_4_6_70", "Сметана жирністю до 15%", 1),
        ("Масло вершкове 82%", "01_1_5_1_74", "Масло вершкове", 0.2m),   
        ("Гречка", "01_1_1_5_20", "Крупи гречані", 1),
        ("Рис", "01_1_1_1_1", "Рис", 1),
        ("Батон білий", "01_1_1_2_6", "Батон", 0.5m),           
        ("Картопля", "01_1_7_7_103", "Картопля", 1),
        ("Морква", "01_1_7_4_98", "Морква", 1),
        ("Цибуля", "01_1_7_4_96", "Цибуля ріпчаста", 1),
        ("Капуста білокачанна", "01_1_7_2_90", "Капуста білокачанна", 1),
        ("Яблука", "01_1_6_3_83", "Яблука", 1),
        ("Олія соняшникова", "01_1_5_4_78", "Олія соняшникова", 0.92m),    
        ("Цукор", "01_1_8_1_104", "Цукор", 1),
    ];

    public async Task RunAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DietFlow-student-project/1.0 (+https://github.com/Bileysh/DietFlow)");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.sdmx.data+csv;version=1.0.0");

        logger.LogInformation("Downloading average consumer prices from Derzhstat SDMX API…");
        var csv = await http.GetStringAsync(DataUrl, ct);
        var rows = ParseCsv(csv);
        var header = rows[0];
        int Col(string name) => Array.IndexOf(header, name);
        int cIndicator = Col("INDICATOR"), cRegion = Col("REGION"), cGoods = Col("GOODS_SERVICES_TYPE"),
            cPeriod = Col("TIME_PERIOD"), cValue = Col("OBS_VALUE");

        var productIds = await db.Products.Where(p => p.Barcode == null)
            .ToDictionaryAsync(p => p.Name, p => p.Id, ct);
        var byCode = Map.Where(m => productIds.ContainsKey(m.Product)).ToDictionary(m => m.StatCode);

        var official = new List<OfficialPrice>();
        foreach (var r in rows.Skip(1))
        {
            if (r.Length != header.Length || r[cIndicator] != "AVG_CONS_PRCS") continue;
            if (!byCode.TryGetValue(r[cGoods], out var m)) continue;
            if (!decimal.TryParse(r[cValue], NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) continue;

            var region = r[cRegion].Substring(2, 2);
            if (!UaRegions.Names.ContainsKey(region)) continue;

            var period = new DateOnly(int.Parse(r[cPeriod][..4]), int.Parse(r[cPeriod][6..]), 1);
            official.Add(new OfficialPrice
            {
                RegionCode = region, ProductId = productIds[m.Product], StatCode = m.StatCode,
                StatName = m.StatName, PricePerKg = Math.Round(value / m.KgPerUnit, 2), Period = period,
            });
        }
        if (official.Count == 0) throw new InvalidOperationException("Держстат не повернув жодної ціни — формат відповіді змінився?");

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.OfficialPrices.ExecuteDeleteAsync(ct);
            db.OfficialPrices.AddRange(official);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        var updated = await RecalculateStorePricesAsync(db, ct);
        logger.LogInformation(
            "Done: {Prices} official prices for {Products} products in {Regions} regions (period {Period:yyyy-MM}); {Store} store prices recalculated.",
            official.Count, official.Select(o => o.ProductId).Distinct().Count(), official.Select(o => o.RegionCode).Distinct().Count(),
            official.Max(o => o.Period), updated);
    }
    
    public static async Task<int> RecalculateStorePricesAsync(AppDbContext db, CancellationToken ct = default)
    {
        var official = await db.OfficialPrices.AsNoTracking()
            .ToDictionaryAsync(o => (o.RegionCode, o.ProductId), o => o.PricePerKg, ct);
        if (official.Count == 0) return 0;
        var productIds = official.Keys.Select(k => k.ProductId).ToHashSet();

        var prices = await db.StorePrices
            .Where(sp => sp.Source != PriceSource.Manual && productIds.Contains(sp.ProductId))
            .Select(sp => new { Price = sp, sp.Store.Chain, sp.Store.City.RegionCode })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        foreach (var x in prices)
        {
            var key = (x.RegionCode ?? UaRegions.Ukraine, x.Price.ProductId);
            if (!official.TryGetValue(key, out var basePrice)
                && !official.TryGetValue((UaRegions.Ukraine, x.Price.ProductId), out basePrice)) continue;

            var h = (uint)(x.Price.StoreId * 7919 + x.Price.ProductId * 104729) % 1000 / 1000m;
            var noise = 1m + (h - 0.5m) * 0.08m;
            x.Price.PricePerKg = Math.Round(basePrice * DbSeeder.ChainFactor(x.Chain) * noise, 2);
            x.Price.Source = PriceSource.Stat;
            x.Price.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        return prices.Count;
    }

    private static List<string[]> ParseCsv(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear();
                rows.Add(row.ToArray()); row.Clear();
            }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row.ToArray()); }
        return rows;
    }
}
