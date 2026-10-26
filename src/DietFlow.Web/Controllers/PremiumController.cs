using Microsoft.AspNetCore.Mvc;

namespace DietFlow.Web.Controllers;

public class PremiumController(IConfiguration config) : Controller
{
    public IActionResult Index(string? feature)
    {
        ViewBag.Feature = feature;
        ViewBag.FreeDailyAnalyses = config.GetValue("Plans:FreeDailyAnalyses", 5);
        return View();
    }
}
