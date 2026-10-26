namespace DietFlow.Web.Services.Auth;

public static class AppRoles
{
    public const string Admin = "Admin";
}

public static class AppFeatures
{
    public const string UnlimitedAiAnalysis = "UnlimitedAiAnalysis";
    public const string WeeklyMealPlan = "WeeklyMealPlan";
}

public static class AppClaims
{
    public const string Plan = "dietflow:plan";
    public const string DisplayName = "dietflow:name";
}
