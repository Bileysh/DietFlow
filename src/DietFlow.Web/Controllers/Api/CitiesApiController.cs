using DietFlow.Web.Data;
using DietFlow.Web.Models.Api;
using DietFlow.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Controllers.Api;

[Route("api/v1/cities")]
[Tags("Cities")]
public class CitiesApiController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [OutputCache(PolicyName = CacheTags.Policy, Tags = [CacheTags.Cities])]
    public async Task<IReadOnlyList<CityDto>> List() =>
        await db.Cities.AsNoTracking().OrderBy(c => c.Name)
            .Select(c => new CityDto(c.Id, c.Name, c.Latitude, c.Longitude))
            .ToListAsync();
}
