using Markdig;
using Microsoft.AspNetCore.Html;

namespace DietFlow.Web.Services;

public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseSoftlineBreakAsHardlineBreak() 
        .Build();

    public static IHtmlContent ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return HtmlString.Empty;
        var html = Markdown.ToHtml(markdown, Pipeline);
        
        html = System.Text.RegularExpressions.Regex.Replace(html,
            @"(href|src)\s*=\s*""\s*(javascript|data|vbscript):", "$1=\"#blocked:",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return new HtmlString(html);
    }
}
