using System.Text.Json;
using System.Text.RegularExpressions;
using DietFlow.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Data.Import;

public class OsmStoresImporter(AppDbContext db, ILogger logger, bool refresh = false)
{
    private static readonly string[] OverpassUrls =
    [
        "https://overpass-api.de/api/interpreter",
        "https://overpass.kumi.systems/api/interpreter",
        "https://overpass.private.coffee/api/interpreter",
    ];
    private const double MaxCityDistanceKm = 20;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(7);
    private static readonly string CacheDir = Path.Combine(Path.GetTempPath(), "dietflow-osm-cache");

    private static readonly (Regex Pattern, string Chain)[] Chains =
    [
        (new(@"^\s*(АТБ|ATB)", RegexOptions.IgnoreCase), "АТБ"),
        (new(@"^\s*(Фора|Fora)\b", RegexOptions.IgnoreCase), "Фора"),
        (new(@"^\s*(Сільпо|Silpo)\b", RegexOptions.IgnoreCase), "Сільпо"),
        (new(@"^\s*(Novus|Новус)\b", RegexOptions.IgnoreCase), "Novus"),
        (new(@"^\s*(Varus|Варус)\b", RegexOptions.IgnoreCase), "Varus"),
        (new(@"^\s*Рукавичка", RegexOptions.IgnoreCase), "Рукавичка"),
        (new(@"^\s*(Thrash|Траш)", RegexOptions.IgnoreCase), "Траш!"),
        (new(@"^\s*Посад", RegexOptions.IgnoreCase), "Посад"),
        (new(@"^\s*Сім\s?23", RegexOptions.IgnoreCase), "Сім23"),
        (new(@"^\s*Файно", RegexOptions.IgnoreCase), "Файно Маркет"),
        (new(@"^\s*Таврія", RegexOptions.IgnoreCase), "Таврія В"),
        (new(@"^\s*(ЕКО|Eko)\s?маркет", RegexOptions.IgnoreCase), "ЕКО маркет"),
        (new(@"^\s*Близенько", RegexOptions.IgnoreCase), "Близенько"),
        (new(@"^\s*Spar\b", RegexOptions.IgnoreCase), "Spar"),
        (new(@"^\s*Копійка", RegexOptions.IgnoreCase), "Копійка"),
        (new(@"^\s*Наш\s+Край", RegexOptions.IgnoreCase), "Наш Край"),
        (new(@"^\s*(Ашан|Auchan)", RegexOptions.IgnoreCase), "Ашан"),
        (new(@"^\s*Metro\b|^\s*МЕТРО\b", RegexOptions.IgnoreCase), "Metro"),
        (new(@"^\s*Велмарт", RegexOptions.IgnoreCase), "Велмарт"),
    ];

    private record Place(string Name, double Lat, double Lng, int Population, string? RegionCode);
    private record OsmStore(string Ref, string Chain, string Name, string? Address, string? AddrCity, double Lat, double Lng);

    public async Task RunAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DietFlow-student-project/1.0 (+https://github.com/Bileysh/DietFlow)");

        logger.LogInformation("Downloading towns from Overpass…");
        var places = await LoadPlacesAsync(http, ct);
        logger.LogInformation("{Count} towns", places.Count);

        logger.LogInformation("Downloading supermarkets from Overpass…");
        var osmStores = await LoadStoresAsync(http, ct);
        logger.LogInformation("{Count} stores of known grocery chains", osmStores.Count);

        var cities = await db.Cities.ToDictionaryAsync(c => c.Name, StringComparer.OrdinalIgnoreCase, ct);
        var placeByName = places.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        Place? ResolvePlace(OsmStore s)
        {
            if (s.AddrCity is not null && placeByName.TryGetValue(s.AddrCity, out var byTag)
                && DistanceKm(s.Lat, s.Lng, byTag.Lat, byTag.Lng) <= MaxCityDistanceKm * 2)
                return byTag;
            return places
                .Where(p => DistanceKm(s.Lat, s.Lng, p.Lat, p.Lng) <= MaxCityDistanceKm)
                .MinBy(p => DistanceKm(s.Lat, s.Lng, p.Lat, p.Lng) / Math.Sqrt(p.Population));
        }

        var assigned = osmStores.Select(s => (Store: s, Place: ResolvePlace(s))).Where(x => x.Place is not null).ToList();
        foreach (var place in assigned.Select(x => x.Place!).DistinctBy(p => p.Name.ToLowerInvariant()))
        {
            if (cities.TryGetValue(place.Name, out var known))
            {
                known.RegionCode ??= place.RegionCode;
                continue;
            }
            var city = new City { Name = place.Name, Latitude = place.Lat, Longitude = place.Lng, RegionCode = place.RegionCode };
            db.Cities.Add(city);
            cities[place.Name] = city;
        }
        await db.SaveChangesAsync(ct);

        var existing = await db.Stores.Where(s => s.OsmRef != null).ToDictionaryAsync(s => s.OsmRef!, ct);
        int created = 0, updated = 0;
        var newStores = new List<Store>();
        foreach (var (s, place) in assigned)
        {
            var cityId = cities[place!.Name].Id;
            if (existing.TryGetValue(s.Ref, out var store))
            {
                store.Name = s.Name; store.Chain = s.Chain; store.Address = s.Address;
                store.Latitude = s.Lat; store.Longitude = s.Lng; store.CityId = cityId;
                updated++;
            }
            else
            {
                store = new Store { OsmRef = s.Ref, Name = s.Name, Chain = s.Chain, Address = s.Address, Latitude = s.Lat, Longitude = s.Lng, CityId = cityId };
                db.Stores.Add(store);
                newStores.Add(store);
                created++;
            }
        }
        await db.SaveChangesAsync(ct);

        var demoRemoved = await db.Stores.Where(s => s.OsmRef == null && s.Address != null && s.Address.EndsWith("демо-точка"))
            .ExecuteDeleteAsync(ct);

        foreach (var store in newStores) store.City = cities.Values.First(c => c.Id == store.CityId);
        var prices = await DbSeeder.AddDemoPricesAsync(db, newStores);
        await StatPricesImporter.RecalculateStorePricesAsync(db, ct);

        var emptyCities = await db.Cities.Where(c => !c.Stores.Any()).ExecuteDeleteAsync(ct);

        logger.LogInformation(
            "Done: {Created} new stores, {Updated} updated, {Skipped} skipped (no town within {Km} km), {Demo} demo stores removed, " +
            "{Prices} demo prices added, {Cities} cities total ({Empty} empty removed).",
            created, updated, osmStores.Count - assigned.Count, MaxCityDistanceKm, demoRemoved, prices,
            await db.Cities.CountAsync(ct), emptyCities);
    }

    private async Task<List<Place>> LoadPlacesAsync(HttpClient http, CancellationToken ct)
    {
        var json = await QueryAsync(http, """
            [out:json][timeout:300];
            area["ISO3166-2"~"^UA-"][admin_level=4]->.regions;
            foreach.regions->.r(
              .r out tags;
              node["place"~"^(city|town)$"](area.r);
              out;
            );
            """, ct);
        var list = new List<Place>();
        string? region = null;
        foreach (var e in json.RootElement.GetProperty("elements").EnumerateArray())
        {
            if (!e.TryGetProperty("tags", out var tags)) continue;
            if (e.GetProperty("type").GetString() == "area")
            {
                region = UaRegions.FromIso(Tag(tags, "ISO3166-2"));
                continue;
            }
            var name = Tag(tags, "name:uk") ?? Tag(tags, "name");
            if (name is null) continue;
            var digits = new string((Tag(tags, "population") ?? "").Where(char.IsDigit).ToArray());
            var population = int.TryParse(digits, out var pop) && pop > 0 ? pop : Tag(tags, "place") == "city" ? 100_000 : 10_000;
            list.Add(new Place(name, e.GetProperty("lat").GetDouble(), e.GetProperty("lon").GetDouble(), population, region));
        }
        return list.OrderBy(p => p.RegionCode == "80" ? 0 : 1)
            .DistinctBy(p => (p.Name, Math.Round(p.Lat, 3), Math.Round(p.Lng, 3))).ToList();
    }

    private async Task<List<OsmStore>> LoadStoresAsync(HttpClient http, CancellationToken ct)
    {
        const string shop = "^(supermarket|convenience|hypermarket|wholesale)$";
        const string chains = "^ *(АТБ|ATB|Фора|Fora|Сільпо|Silpo|Novus|NOVUS|Новус|Varus|VARUS|Варус|Рукавичка|Thrash|Траш|Посад|Сім ?23|Файно|Таврія|ЕКО|Эко|Близенько|Spar|SPAR|Копійка|Наш Край|Ашан|Auchan|Metro|METRO|МЕТРО|Велмарт)";
        var json = await QueryAsync(http, $$"""
            [out:json][timeout:240];
            area["ISO3166-1"="UA"][admin_level=2]->.ua;
            (
              nwr["shop"~"{{shop}}"]["brand"~"{{chains}}"](area.ua);
              nwr["shop"~"{{shop}}"][!"brand"]["name"~"{{chains}}"](area.ua);
            );
            out center tags;
            """, ct);

        var list = new List<OsmStore>();
        foreach (var e in json.RootElement.GetProperty("elements").EnumerateArray())
        {
            if (!e.TryGetProperty("tags", out var tags)) continue;
            var brand = Tag(tags, "brand") ?? Tag(tags, "name");
            if (brand is null) continue;
            var chain = Chains.FirstOrDefault(c => c.Pattern.IsMatch(brand)).Chain;
            if (chain is null) continue;

            var (lat, lng) = e.TryGetProperty("lat", out var la)
                ? (la.GetDouble(), e.GetProperty("lon").GetDouble())
                : (e.GetProperty("center").GetProperty("lat").GetDouble(), e.GetProperty("center").GetProperty("lon").GetDouble());

            var street = Tag(tags, "addr:street");
            var house = Tag(tags, "addr:housenumber");
            var address = street is null ? null : house is null ? street : $"{street}, {house}";
            var name = address is null ? chain : $"{chain}, {address}";

            list.Add(new OsmStore($"{e.GetProperty("type").GetString()}/{e.GetProperty("id").GetInt64()}",
                chain, Trim(name, 200)!, Trim(address, 300), Tag(tags, "addr:city"), lat, lng));
        }
        return list;
    }

    private async Task<JsonDocument> QueryAsync(HttpClient http, string query, CancellationToken ct)
    {
        Directory.CreateDirectory(CacheDir);
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(query)))[..16];
        var cacheFile = Path.Combine(CacheDir, $"{key}.json");
        if (!refresh && File.Exists(cacheFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFile) < CacheLifetime)
        {
            logger.LogInformation("Using cached Overpass response ({File})", cacheFile);
            await using var cached = File.OpenRead(cacheFile);
            return await JsonDocument.ParseAsync(cached, cancellationToken: ct);
        }

        for (var attempt = 1; ; attempt++)
        {
            foreach (var url in OverpassUrls)
            {
                try
                {
                    using var response = await http.PostAsync(url, new FormUrlEncodedContent([new("data", query)]), ct);
                    if (response.IsSuccessStatusCode)
                    {
                        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                        await File.WriteAllBytesAsync(cacheFile, bytes, ct);
                        return JsonDocument.Parse(bytes);
                    }
                    logger.LogWarning("Overpass {Url} → {Status}", url, (int)response.StatusCode);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    logger.LogWarning("Overpass {Url} failed: {Message}", url, ex.Message);
                }
            }
            if (attempt == 3) throw new InvalidOperationException("Усі сервери Overpass недоступні. Спробуйте пізніше.");
            await Task.Delay(TimeSpan.FromSeconds(20 * attempt), ct); 
        }
    }

    private static string? Tag(JsonElement tags, string key) =>
        tags.TryGetProperty(key, out var v) && v.GetString() is { Length: > 0 } s ? s.Trim() : null;

    private static string? Trim(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];

    public static double DistanceKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double r = 6371;
        double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLng = Rad(lng2 - lng1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Pow(Math.Sin(dLng / 2), 2);
        return 2 * r * Math.Asin(Math.Sqrt(a));
    }
}
