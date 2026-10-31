using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DietFlow.Functions;

public class NormsFunction(ILogger<NormsFunction> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Function("Norms")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "norms")] HttpRequest req)
    {
        NormsRequest? request;
        try
        {
            request = HttpMethods.IsPost(req.Method)
                ? await JsonSerializer.DeserializeAsync<NormsRequest>(req.Body, Json)
                : FromQuery(req.Query);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            return Problem("Некоректні параметри: " + ex.Message);
        }
        if (request is null) return Problem("Порожній запит.");

        var errors = NutritionCalculator.Validate(request);
        if (errors.Count > 0) return Problem(string.Join(" ", errors));

        var result = NutritionCalculator.Calculate(request);
        logger.LogInformation("Norms: {Sex}, {Age}y, {Goal} → {Kcal} kcal", request.Sex, request.Age, request.Goal, result.TargetKcal);
        return new JsonResult(result, Json);
    }

    private static NormsRequest FromQuery(IQueryCollection q) => new(
        Enum.Parse<Sex>(q["sex"].ToString(), ignoreCase: true),
        int.Parse(q["age"].ToString()),
        decimal.Parse(q["weightKg"].ToString(), System.Globalization.CultureInfo.InvariantCulture),
        decimal.Parse(q["heightCm"].ToString(), System.Globalization.CultureInfo.InvariantCulture),
        q.ContainsKey("activity") ? Enum.Parse<Activity>(q["activity"].ToString(), ignoreCase: true) : Activity.Moderate,
        q.ContainsKey("goal") ? Enum.Parse<Goal>(q["goal"].ToString(), ignoreCase: true) : Goal.Maintain);

    private static ObjectResult Problem(string detail) =>
        new(new ProblemDetails { Title = "Invalid request", Detail = detail, Status = StatusCodes.Status400BadRequest })
        { StatusCode = StatusCodes.Status400BadRequest };
}
