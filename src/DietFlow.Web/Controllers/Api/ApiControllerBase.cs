using DietFlow.Web.Models.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace DietFlow.Web.Controllers.Api;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    protected static (int Skip, int Limit) Normalize(int? skip, int? limit) =>
        (Math.Max(0, skip ?? 0), Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit));
    
    protected PagedResult<T> Page<T>(IReadOnlyList<T> items, int total, int skip, int limit)
    {
        string? next = null;
        if (skip + items.Count < total)
        {
            var query = Request.Query
                .Where(q => !q.Key.Equals("skip", StringComparison.OrdinalIgnoreCase) && !q.Key.Equals("limit", StringComparison.OrdinalIgnoreCase))
                .SelectMany(q => q.Value.Select(v => new KeyValuePair<string, string?>(q.Key, v)))
                .Append(new("skip", (skip + limit).ToString()))
                .Append(new("limit", limit.ToString()));
            var baseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}{Request.Path}";
            next = QueryHelpers.AddQueryString(baseUrl, query);
        }
        return new PagedResult<T>(items, total, skip, limit, next);
    }

    protected ObjectResult ConflictProblem(string detail) =>
        Problem(detail: detail, statusCode: StatusCodes.Status409Conflict, title: "Conflict");

    protected ActionResult FieldError(string field, string message)
    {
        ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }
}
