using System.Text;
using System.Xml.Linq;
using DietFlow.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Controllers;

public class SeoController(AppDbContext db) : Controller
{
    [HttpGet("/robots.txt")]
    [OutputCache(Duration = 3600)]
    public ContentResult Robots()
    {
        var sb = new StringBuilder()
            .AppendLine("User-agent: *")
            .AppendLine("Allow: /")
            .AppendLine("Disallow: /api/")
            .AppendLine("Disallow: /Meals")      
            .AppendLine("Disallow: /Cities/")    
            .AppendLine("Disallow: /scalar")
            .AppendLine("Disallow: /openapi/")
            .AppendLine("Disallow: /*/Edit")
            .AppendLine("Disallow: /*/Delete")
            .AppendLine("Disallow: /*/Create")
            .AppendLine($"Sitemap: {BaseUrl}/sitemap.xml");
        return Content(sb.ToString(), "text/plain", Encoding.UTF8);
    }

    [HttpGet("/sitemap.xml")]
    [OutputCache(Duration = 3600)]
    public async Task<ContentResult> Sitemap()
    {
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        XElement Url(string path, string changefreq, string priority) =>
            new(ns + "url",
                new XElement(ns + "loc", BaseUrl + path),
                new XElement(ns + "changefreq", changefreq),
                new XElement(ns + "priority", priority));

        var slugs = await db.Recipes.AsNoTracking().OrderBy(r => r.Id).Select(r => r.Slug).ToListAsync();

        var urlset = new XElement(ns + "urlset",
            Url("/", "weekly", "1.0"),
            Url("/Recipes", "daily", "0.9"),
            slugs.Select(slug => Url($"/recipes/{slug}", "weekly", "0.8")),
            Url("/Products", "weekly", "0.5"),
            Url("/Stores", "weekly", "0.5"));

        var xml = new XDocument(new XDeclaration("1.0", "utf-8", null), urlset);
        return Content(xml.Declaration + Environment.NewLine + xml.Root, "application/xml", Encoding.UTF8);
    }

    private string BaseUrl => $"{Request.Scheme}://{Request.Host}";
}
