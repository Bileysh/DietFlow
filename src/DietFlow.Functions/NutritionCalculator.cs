namespace DietFlow.Functions;

public enum Sex { Male, Female }
public enum Activity { Sedentary, Light, Moderate, Active, VeryActive }
public enum Goal { Lose, Maintain, Gain }

public record NormsRequest(Sex Sex, int Age, decimal WeightKg, decimal HeightCm, Activity Activity = Activity.Moderate, Goal Goal = Goal.Maintain);

public record NormsResult(
    int Bmr, int Tdee, int TargetKcal, int ProteinG, int FatG, int CarbsG, int WaterMl,
    decimal Bmi, string BmiCategory, string Method);

public static class NutritionCalculator
{
    private static readonly Dictionary<Activity, decimal> ActivityFactor = new()
    {
        [Activity.Sedentary] = 1.2m, [Activity.Light] = 1.375m, [Activity.Moderate] = 1.55m,
        [Activity.Active] = 1.725m, [Activity.VeryActive] = 1.9m,
    };

    public static IReadOnlyList<string> Validate(NormsRequest r)
    {
        var errors = new List<string>();
        if (r.Age is < 14 or > 100) errors.Add("Вік — від 14 до 100 років.");
        if (r.WeightKg is < 30 or > 300) errors.Add("Вага — від 30 до 300 кг.");
        if (r.HeightCm is < 120 or > 250) errors.Add("Зріст — від 120 до 250 см.");
        return errors;
    }

    public static NormsResult Calculate(NormsRequest r)
    {
        var bmr = 10 * r.WeightKg + 6.25m * r.HeightCm - 5 * r.Age + (r.Sex == Sex.Male ? 5 : -161);
        var tdee = bmr * ActivityFactor[r.Activity];
        var target = r.Goal switch { Goal.Lose => tdee * 0.85m, Goal.Gain => tdee * 1.10m, _ => tdee };

        var protein = r.WeightKg * (r.Goal == Goal.Lose ? 2.0m : 1.6m);
        var fat = target * 0.25m / 9;
        var carbs = Math.Max(0, (target - protein * 4 - fat * 9) / 4);

        var heightM = r.HeightCm / 100;
        var bmi = Math.Round(r.WeightKg / (heightM * heightM), 1);

        return new NormsResult(
            (int)Math.Round(bmr), (int)Math.Round(tdee), RoundTo50(target),
            (int)Math.Round(protein), (int)Math.Round(fat), (int)Math.Round(carbs),
            (int)Math.Round(r.WeightKg * 30 / 50) * 50,
            bmi, BmiCategory(bmi), "Mifflin-St Jeor");
    }

    private static int RoundTo50(decimal kcal) => (int)Math.Round(kcal / 50) * 50;

    private static string BmiCategory(decimal bmi) => bmi switch
    {
        < 18.5m => "недостатня вага",
        < 25m => "норма",
        < 30m => "надлишкова вага",
        _ => "ожиріння",
    };
}
