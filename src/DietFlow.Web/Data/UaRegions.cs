namespace DietFlow.Web.Data;

public static class UaRegions
{
    public const string Ukraine = "00";

    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        ["01"] = "АР Крим", ["05"] = "Вінницька область", ["07"] = "Волинська область",
        ["12"] = "Дніпропетровська область", ["14"] = "Донецька область", ["18"] = "Житомирська область",
        ["21"] = "Закарпатська область", ["23"] = "Запорізька область", ["26"] = "Івано-Франківська область",
        ["32"] = "Київська область", ["35"] = "Кіровоградська область", ["44"] = "Луганська область",
        ["46"] = "Львівська область", ["48"] = "Миколаївська область", ["51"] = "Одеська область",
        ["53"] = "Полтавська область", ["56"] = "Рівненська область", ["59"] = "Сумська область",
        ["61"] = "Тернопільська область", ["63"] = "Харківська область", ["65"] = "Херсонська область",
        ["68"] = "Хмельницька область", ["71"] = "Черкаська область", ["73"] = "Чернівецька область",
        ["74"] = "Чернігівська область", ["80"] = "м. Київ", ["85"] = "м. Севастополь",
        [Ukraine] = "Україна",
    };

    private static readonly Dictionary<string, string> IsoExceptions = new()
    {
        ["09"] = "44", ["30"] = "80", ["40"] = "85", ["43"] = "01", ["77"] = "73",
    };

    public static string? FromIso(string? iso)
    {
        if (iso is not { Length: 5 } || !iso.StartsWith("UA-")) return null;
        var code = iso[3..];
        code = IsoExceptions.GetValueOrDefault(code, code);
        return Names.ContainsKey(code) ? code : null;
    }
}
