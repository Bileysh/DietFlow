using DietFlow.Web.Models.Entities;
using DietFlow.Web.Services;
using DietFlow.Web.Services.Agent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DietFlow.Web.Controllers;

[Authorize]
public class AssistantController(AgentService agent, AnalysisQuotaService quota, CurrentUser currentUser, CityContext cityContext) : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewBag.Available = agent.IsAvailable;
        ViewBag.Quota = await quota.GetAsync(await currentUser.GetIdAsync(), AiUsageKind.AgentMessage);
        ViewBag.CityName = (await cityContext.GetCurrentAsync())?.Name;
        return View();
    }
}
