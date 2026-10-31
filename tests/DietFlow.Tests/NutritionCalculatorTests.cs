using DietFlow.Functions;

namespace DietFlow.Tests;

public class NutritionCalculatorTests
{
    [Fact]
    public void Male_Moderate_Maintain_MatchesMifflinStJeor()
    {
        var r = NutritionCalculator.Calculate(new NormsRequest(Sex.Male, 30, 80, 180));

        Assert.Equal(1780, r.Bmr);
        Assert.Equal(2759, r.Tdee);
        Assert.Equal(2750, r.TargetKcal);
        Assert.Equal(24.7m, r.Bmi);
        Assert.Equal("норма", r.BmiCategory);
    }

    [Fact]
    public void Female_LoseWeight_HasDeficitAndMoreProtein()
    {
        var maintain = NutritionCalculator.Calculate(new NormsRequest(Sex.Female, 25, 70, 165, Activity.Light));
        var lose = NutritionCalculator.Calculate(new NormsRequest(Sex.Female, 25, 70, 165, Activity.Light, Goal.Lose));

        Assert.True(lose.TargetKcal < maintain.TargetKcal);
        Assert.Equal(140, lose.ProteinG);
        Assert.Equal(112, maintain.ProteinG);
    }

    [Theory]
    [InlineData(10, 70, 175)]
    [InlineData(30, 20, 175)]
    [InlineData(30, 70, 300)]
    public void Validate_RejectsUnrealisticValues(int age, int weight, int height)
    {
        Assert.NotEmpty(NutritionCalculator.Validate(new NormsRequest(Sex.Male, age, weight, height)));
    }
}
