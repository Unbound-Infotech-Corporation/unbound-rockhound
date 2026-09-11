using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;
using GeoMineralTrace.Core.App;
using GeoMineralTrace.Core.News;
using GeoMineralTrace_App.Helpers;

namespace GeoMineralTrace_App.Services;

public sealed class RockhoundNewsService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(45);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _cachePath = AppDataPaths.Sub("news-headlines-cache.json");

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(18) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"{AppBranding.UserAgentPrefix}/0.6 (+{AppBranding.CompanyWebsite}; rockhound news)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/rss+xml, application/atom+xml, application/xml, text/xml, */*");
        return client;
    }

    public async Task<IReadOnlyList<RockhoundNewsHeadline>> GetHeadlinesAsync(
        int maxItems = 18,
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        if (!forceRefresh)
        {
            var cached = TryLoadCache();
            if (cached is { } hit && DateTimeOffset.UtcNow - hit.FetchedAtUtc < CacheTtl && hit.Items.Count > 0)
                return hit.Items.Take(maxItems).ToList();
        }

        if (!AppPreferences.OnlineEnrichmentAllowed)
        {
            var offline = TryLoadCache();
            return offline?.Items.Take(maxItems).ToList()
                   ?? [];
        }

        var enabled = NewsSourcePreferences.GetEnabledSourceIds();
        var sources = RockhoundNewsCatalog.All.Where(s => enabled.Contains(s.Id)).ToList();
        if (sources.Count == 0)
            sources = RockhoundNewsCatalog.All.ToList();

        var bag = new List<RockhoundNewsHeadline>();
        foreach (var source in sources)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var items = await FetchFeedAsync(source, ct).ConfigureAwait(false);
                bag.AddRange(items);
            }
            catch
            {
                // Skip failed feeds; others still show.
            }
        }

        var ranked = bag
            .GroupBy(h => h.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(h => h.PublishedAt ?? DateTimeOffset.MinValue)
            .Take(Math.Clamp(maxItems * 2, 10, 60))
            .ToList();

        SaveCache(ranked);
        return ranked.Take(maxItems).ToList();
    }

    private async Task<IReadOnlyList<RockhoundNewsHeadline>> FetchFeedAsync(
        RockhoundNewsSource source,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, source.FeedUrl);
        using var res = await Http.SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
            return [];

        var xml = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(xml))
            return [];

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml, LoadOptions.None);
        }
        catch
        {
            return [];
        }

        var items = ParseRssOrAtom(doc, source);
        if (source.FilterToGenreKeywords)
        {
            items = items
                .Where(h => MatchesGenre(h.Title) || MatchesGenre(h.Summary))
                .ToList();
        }

        return items.Take(12).ToList();
    }

    private static bool MatchesGenre(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        return RockhoundNewsCatalog.GenreKeywords.Any(k =>
            text.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<RockhoundNewsHeadline> ParseRssOrAtom(XDocument doc, RockhoundNewsSource source)
    {
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var list = new List<RockhoundNewsHeadline>();

        foreach (var item in doc.Descendants("item"))
        {
            var title = (string?)item.Element("title");
            var link = (string?)item.Element("link")
                       ?? item.Elements().FirstOrDefault(e => e.Name.LocalName == "link")?.Attribute("href")?.Value;
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link))
                continue;

            DateTimeOffset? published = null;
            var pub = (string?)item.Element("pubDate");
            if (DateTimeOffset.TryParse(pub, out var dto))
                published = dto;

            var summary = StripHtml((string?)item.Element("description"));
            list.Add(new RockhoundNewsHeadline
            {
                Title = title.Trim(),
                Url = link.Trim(),
                SourceId = source.Id,
                SourceName = source.DisplayName,
                PublishedAt = published,
                Summary = summary
            });
        }

        if (list.Count > 0)
            return list;

        foreach (var entry in doc.Descendants(atom + "entry"))
        {
            var title = (string?)entry.Element(atom + "title");
            var link = entry.Elements(atom + "link")
                .Select(l => (string?)l.Attribute("href"))
                .FirstOrDefault(h => !string.IsNullOrWhiteSpace(h))
                ?? (string?)entry.Element(atom + "id");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link))
                continue;

            DateTimeOffset? published = null;
            var updated = (string?)entry.Element(atom + "updated") ?? (string?)entry.Element(atom + "published");
            if (DateTimeOffset.TryParse(updated, out var dto))
                published = dto;

            var summary = StripHtml((string?)entry.Element(atom + "summary") ?? (string?)entry.Element(atom + "content"));
            list.Add(new RockhoundNewsHeadline
            {
                Title = title.Trim(),
                Url = link.Trim(),
                SourceId = source.Id,
                SourceName = source.DisplayName,
                PublishedAt = published,
                Summary = summary
            });
        }

        return list;
    }

    private static string? StripHtml(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var text = System.Text.RegularExpressions.Regex.Replace(raw, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length > 180)
            text = text[..177] + "…";
        return text;
    }

    private CacheFile? TryLoadCache()
    {
        try
        {
            if (!File.Exists(_cachePath))
                return null;
            var json = File.ReadAllText(_cachePath);
            return JsonSerializer.Deserialize<CacheFile>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private void SaveCache(IReadOnlyList<RockhoundNewsHeadline> items)
    {
        try
        {
            var dir = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var file = new CacheFile
            {
                FetchedAtUtc = DateTimeOffset.UtcNow,
                Items = items.ToList()
            };
            File.WriteAllText(_cachePath, JsonSerializer.Serialize(file, JsonOptions));
        }
        catch
        {
            // ignore cache write failures
        }
    }

    private sealed class CacheFile
    {
        public DateTimeOffset FetchedAtUtc { get; set; }
        public List<RockhoundNewsHeadline> Items { get; set; } = [];
    }
}
