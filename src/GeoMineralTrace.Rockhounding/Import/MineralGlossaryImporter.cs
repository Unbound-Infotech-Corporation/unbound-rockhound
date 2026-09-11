using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using GeoMineralTrace.Core.Rockhounding;
using GeoMineralTrace.Rockhounding.Glossary;
using GeoMineralTrace.Rockhounding.Storage;

namespace GeoMineralTrace.Rockhounding.Import;

/// <summary>
/// Offline seed import plus optional Wikidata/Commons enrichment (network only when caller asks).
/// </summary>
public sealed class MineralGlossaryImporter
{
    private static readonly Uri WikidataSparql = new("https://query.wikidata.org/sparql");
    private static readonly Uri CommonsApi = new("https://commons.wikimedia.org/w/api.php");

    private static readonly HashSet<string> AllowedLicenseTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "CC0", "CC-BY", "CC-BY-SA", "Public Domain", "PD", "Public domain"
    };

    public async Task<int> ImportFromSeedAsync(
        MineralGlossaryStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var count = 0;
        foreach (var species in PriorityGlossarySeed.All)
        {
            await store.UpsertSpeciesAsync(species, cancellationToken).ConfigureAwait(false);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Optional online enrichment: resolve Wikidata id by English label; optionally fetch Commons images
    /// limited to CC0 / CC-BY / CC-BY-SA / Public Domain. Network failures are logged and skipped.
    /// </summary>
    public async Task<bool> TryEnrichFromWikidataAndCommonsAsync(
        MineralGlossaryStore store,
        string speciesId,
        HttpClient http,
        bool fetchImages,
        int maxImagesPerSpecies = 1,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);

        var species = await store.GetByIdOrNameAsync(speciesId, cancellationToken).ConfigureAwait(false);
        if (species is null)
        {
            progress?.Report($"Species not found: {speciesId}");
            return false;
        }

        try
        {
            var qid = species.WikidataId;
            if (string.IsNullOrWhiteSpace(qid))
            {
                qid = await ResolveWikidataIdAsync(http, species.Name, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(qid))
                {
                    var updated = new MineralSpecies
                    {
                        Id = species.Id,
                        Name = species.Name,
                        Aliases = species.Aliases,
                        Formula = species.Formula,
                        CrystalSystem = species.CrystalSystem,
                        MohsMin = species.MohsMin,
                        MohsMax = species.MohsMax,
                        ColorRange = species.ColorRange,
                        Luster = species.Luster,
                        ImaStatus = species.ImaStatus,
                        ImaYear = species.ImaYear,
                        Description = species.Description,
                        WikidataId = qid,
                        UpdatedUtc = DateTimeOffset.UtcNow,
                        ImageCount = species.ImageCount
                    };
                    await store.UpsertSpeciesAsync(updated, cancellationToken).ConfigureAwait(false);
                    species = updated;
                    progress?.Report($"{species.Name}: Wikidata {qid}");
                }
                else
                {
                    progress?.Report($"{species.Name}: no Wikidata match");
                }
            }
            else
            {
                progress?.Report($"{species.Name}: using Wikidata {qid}");
            }

            if (!fetchImages || maxImagesPerSpecies <= 0)
                return true;

            var images = await SearchAndDownloadCommonsImagesAsync(
                store, http, species, Math.Clamp(maxImagesPerSpecies, 1, 5), progress, cancellationToken)
                .ConfigureAwait(false);

            if (images.Count > 0)
            {
                await store.ReplaceImagesAsync(species.Id, images, cancellationToken).ConfigureAwait(false);
                progress?.Report($"{species.Name}: stored {images.Count} image(s)");
            }
            else
            {
                progress?.Report($"{species.Name}: no allowed Commons image");
            }

            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException)
        {
            progress?.Report($"{species.Name}: network/parse skipped — {ex.Message}");
            return false;
        }
    }

    private static async Task<string?> ResolveWikidataIdAsync(
        HttpClient http,
        string englishLabel,
        CancellationToken ct)
    {
        var escaped = englishLabel.Replace("\"", "\\\"", StringComparison.Ordinal);
        var sparql =
            "SELECT ?item WHERE {\n" +
            $"  ?item rdfs:label \"{escaped}\"@en.\n" +
            "  { ?item wdt:P31/wdt:P279* wd:Q12089225. }\n" +
            "  UNION { ?item wdt:P31/wdt:P279* wd:Q11303. }\n" +
            "  UNION { ?item wdt:P31 wd:Q39546. }\n" +
            "  UNION { ?item wdt:P279* wd:Q12089225. }\n" +
            "} LIMIT 1";

        var url = WikidataSparql + "?format=json&query=" + Uri.EscapeDataString(sparql);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "UnboundRockhound/1.0 (glossary import; educational)");
        req.Headers.TryAddWithoutValidation("Accept", "application/sparql-results+json");

        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return null;

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("results", out var results)
            || !results.TryGetProperty("bindings", out var bindings)
            || bindings.GetArrayLength() == 0)
            return null;

        var uri = bindings[0].GetProperty("item").GetProperty("value").GetString();
        if (string.IsNullOrWhiteSpace(uri))
            return null;
        var slash = uri.LastIndexOf('/');
        return slash >= 0 ? uri[(slash + 1)..] : uri;
    }

    private static async Task<IReadOnlyList<MineralImage>> SearchAndDownloadCommonsImagesAsync(
        MineralGlossaryStore store,
        HttpClient http,
        MineralSpecies species,
        int maxImages,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var query = species.Name + " mineral";
        var searchUrl =
            CommonsApi
            + "?action=query&format=json&generator=search"
            + "&gsrnamespace=6"
            + "&gsrlimit=12"
            + "&gsrsearch=" + Uri.EscapeDataString(query)
            + "&prop=imageinfo"
            + "&iiprop=url|extmetadata|mime|size"
            + "&iiurlwidth=800";

        using var req = new HttpRequestMessage(HttpMethod.Get, searchUrl);
        req.Headers.TryAddWithoutValidation("User-Agent", "UnboundRockhound/1.0 (glossary import; educational)");
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            progress?.Report($"{species.Name}: Commons HTTP {(int)resp.StatusCode}");
            return [];
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("query", out var queryEl)
            || !queryEl.TryGetProperty("pages", out var pages))
            return [];

        var collected = new List<MineralImage>();
        var order = 0;
        foreach (var page in pages.EnumerateObject())
        {
            if (collected.Count >= maxImages)
                break;

            if (!page.Value.TryGetProperty("imageinfo", out var infos) || infos.GetArrayLength() == 0)
                continue;

            var info = infos[0];
            var mime = info.TryGetProperty("mime", out var mimeEl) ? mimeEl.GetString() : null;
            if (mime is not ("image/jpeg" or "image/png" or "image/webp"))
                continue;

            string? licenseShort = null;
            string? licenseUrl = null;
            string? artist = null;
            string? credit = null;
            if (info.TryGetProperty("extmetadata", out var meta))
            {
                licenseShort = MetaValue(meta, "LicenseShortName");
                licenseUrl = MetaValue(meta, "LicenseUrl");
                artist = StripHtml(MetaValue(meta, "Artist"));
                credit = StripHtml(MetaValue(meta, "Credit"));
            }

            var normalized = NormalizeLicense(licenseShort, licenseUrl);
            if (normalized is null)
                continue;

            var downloadUrl = info.TryGetProperty("thumburl", out var thumb)
                ? thumb.GetString()
                : null;
            downloadUrl ??= info.TryGetProperty("url", out var full) ? full.GetString() : null;
            if (string.IsNullOrWhiteSpace(downloadUrl))
                continue;

            var sourcePage = info.TryGetProperty("descriptionurl", out var desc)
                ? desc.GetString()
                : downloadUrl;

            var imageId = $"{species.Id}-{order + 1:00}";
            var ext = mime switch
            {
                "image/png" => ".png",
                "image/webp" => ".webp",
                _ => ".jpg"
            };
            var relative = Path.Combine(species.Id, imageId + ext).Replace('\\', '/');
            var absoluteDir = Path.Combine(store.ImagesRoot, species.Id);
            Directory.CreateDirectory(absoluteDir);
            var absolutePath = Path.Combine(absoluteDir, imageId + ext);

            try
            {
                using var imgReq = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                imgReq.Headers.TryAddWithoutValidation("User-Agent", "UnboundRockhound/1.0 (glossary import; educational)");
                using var imgResp = await http.SendAsync(imgReq, ct).ConfigureAwait(false);
                if (!imgResp.IsSuccessStatusCode)
                    continue;
                var bytes = await imgResp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                if (bytes.Length < 100)
                    continue;
                await File.WriteAllBytesAsync(absolutePath, bytes, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                progress?.Report($"{species.Name}: image download skipped — {ex.Message}");
                continue;
            }

            var photographer = string.IsNullOrWhiteSpace(artist) ? null : artist.Trim();
            var attribution = BuildAttribution(species.Name, photographer, credit, normalized, sourcePage!);

            collected.Add(new MineralImage
            {
                Id = imageId,
                SpeciesId = species.Id,
                FilePath = relative,
                SourceUrl = sourcePage!,
                LicenseType = normalized,
                AttributionText = attribution,
                Photographer = photographer,
                SortOrder = order
            });
            order++;
        }

        return collected;
    }

    private static string? MetaValue(JsonElement meta, string key)
    {
        if (!meta.TryGetProperty(key, out var el))
            return null;
        return el.TryGetProperty("value", out var v) ? v.GetString() : null;
    }

    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;
        var text = Regex.Replace(html, "<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    /// <summary>Returns normalized license or null if not allowed.</summary>
    public static string? NormalizeLicense(string? shortName, string? licenseUrl)
    {
        var blob = $"{shortName} {licenseUrl}".ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(blob))
            return null;

        if (blob.Contains("CC0", StringComparison.Ordinal)
            || (blob.Contains("ZERO", StringComparison.Ordinal) && blob.Contains("CREATIVE", StringComparison.Ordinal)))
            return "CC0";

        if (blob.Contains("BY-SA", StringComparison.Ordinal) || blob.Contains("BY_SA", StringComparison.Ordinal))
            return "CC-BY-SA";

        if (Regex.IsMatch(blob, @"\bCC[- ]?BY\b") || blob.Contains("ATTRIBUTION", StringComparison.Ordinal)
            && blob.Contains("CREATIVE COMMONS", StringComparison.Ordinal)
            && !blob.Contains("SA", StringComparison.Ordinal))
            return "CC-BY";

        if (blob.Contains("PUBLIC DOMAIN", StringComparison.Ordinal)
            || blob.Contains("PD-OLD", StringComparison.Ordinal)
            || blob.Contains("PD-US", StringComparison.Ordinal)
            || Regex.IsMatch(blob, @"\bPD\b"))
            return "Public Domain";

        // Exact short-name tokens
        var trimmed = (shortName ?? "").Trim();
        if (AllowedLicenseTokens.Contains(trimmed))
        {
            return trimmed.Equals("PD", StringComparison.OrdinalIgnoreCase)
                   || trimmed.Equals("Public domain", StringComparison.OrdinalIgnoreCase)
                ? "Public Domain"
                : trimmed.ToUpperInvariant() switch
                {
                    "CC0" => "CC0",
                    "CC-BY" => "CC-BY",
                    "CC-BY-SA" => "CC-BY-SA",
                    _ => NormalizeLicense(trimmed.Replace(' ', '-'), null)
                };
        }

        return null;
    }

    private static string BuildAttribution(
        string speciesName,
        string? photographer,
        string? credit,
        string license,
        string sourceUrl)
    {
        var who = !string.IsNullOrWhiteSpace(photographer)
            ? photographer
            : !string.IsNullOrWhiteSpace(credit)
                ? credit
                : "Wikimedia Commons contributor";
        return $"{speciesName} — {who}; {license}; {sourceUrl}";
    }
}
