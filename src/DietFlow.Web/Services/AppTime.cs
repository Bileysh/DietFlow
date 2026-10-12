namespace DietFlow.Web.Services;


public static class AppTime
{
    public static readonly TimeZoneInfo Zone = FindZone("Europe/Kyiv", "Europe/Kiev", "FLE Standard Time");

    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public static DateTime ToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);

    public static DateTime NowLocal => ToLocal(DateTime.UtcNow);

    private static TimeZoneInfo FindZone(params string[] ids)
    {
        foreach (var id in ids)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }
        return TimeZoneInfo.Utc;
    }
}
