using System.ComponentModel;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.SemanticKernel;

namespace Chrono.Net.AppGen.Ai.Plugins;

/// <summary>Internet search (Tavily) and web page text extraction.</summary>
public sealed partial class WebPlugin(Func<string> tavilyKey)
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) ChronoAppGen/0.1");
        return c;
    }

    [KernelFunction, Description("Searches the internet with Tavily. Returns a short answer plus the top results (title, URL, snippet).")]
    public async Task<string> SearchInternet(string query, [Description("1-10")] int maxResults = 5, CancellationToken ct = default)
    {
        string key = tavilyKey();
        if (string.IsNullOrWhiteSpace(key)) return "Internet search is not configured: add a Tavily API key in Settings → Tools.";
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.tavily.com/search")
        {
            Content = JsonContent.Create(new { query, max_results = Math.Clamp(maxResults, 1, 10), include_answer = true, search_depth = "basic" }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
        using var res = await Http.SendAsync(req, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) return $"Search failed ({(int)res.StatusCode}): {Truncate(body, 300)}";

        using var doc = JsonDocument.Parse(body);
        var sb = new StringBuilder();
        if (doc.RootElement.TryGetProperty("answer", out var answer) && answer.ValueKind == JsonValueKind.String)
            sb.AppendLine("Answer: " + answer.GetString()).AppendLine();
        if (doc.RootElement.TryGetProperty("results", out var results))
            foreach (var r in results.EnumerateArray())
            {
                sb.AppendLine($"- {Str(r, "title")}\n  {Str(r, "url")}");
                sb.AppendLine("  " + Truncate(Str(r, "content"), 500));
            }
        return sb.Length == 0 ? "No results." : sb.ToString();
    }

    [KernelFunction, Description("Downloads a web page and returns its readable text (scripts, styles and navigation removed).")]
    public async Task<string> ScrapeWebPage(string url, [Description("Maximum characters to return")] int maxChars = 8000, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            return "Give a full http(s) URL.";
        using var res = await Http.GetAsync(uri, ct);
        if (!res.IsSuccessStatusCode) return $"Download failed ({(int)res.StatusCode}).";
        string html = await res.Content.ReadAsStringAsync(ct);
        if (res.Content.Headers.ContentType?.MediaType is { } mt && !mt.Contains("html")) return Truncate(html, maxChars);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        foreach (var n in doc.DocumentNode.SelectNodes("//script|//style|//noscript|//nav|//footer|//header|//svg|//form") ?? Enumerable.Empty<HtmlNode>())
            n.Remove();
        string title = HtmlEntity.DeEntitize(doc.DocumentNode.SelectSingleNode("//title")?.InnerText ?? "").Trim();
        var main = doc.DocumentNode.SelectSingleNode("//main") ?? doc.DocumentNode.SelectSingleNode("//article") ?? doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;
        var sb = new StringBuilder();
        foreach (var node in main.DescendantsAndSelf().Where(n => n.NodeType == HtmlNodeType.Element &&
                     n.Name is "h1" or "h2" or "h3" or "h4" or "p" or "li" or "td" or "th" or "pre" or "blockquote"))
        {
            string text = Whitespace().Replace(HtmlEntity.DeEntitize(node.InnerText), " ").Trim();
            if (text.Length < 2) continue;
            sb.AppendLine(node.Name.StartsWith('h') ? $"\n## {text}" : node.Name == "li" ? "- " + text : text);
            if (sb.Length > maxChars) break;
        }
        if (sb.Length == 0) sb.Append(Whitespace().Replace(HtmlEntity.DeEntitize(main.InnerText), " ").Trim());
        return $"# {title}\n{uri}\n{Truncate(sb.ToString(), maxChars)}";
    }

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
