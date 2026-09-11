using System.Text;
using System.Text.RegularExpressions;

namespace GeoMineralTrace.Pipeline.Ner;

/// <summary>
/// Strips YouTube description spam (related-video link farms, social CTAs, playlists)
/// so SOURCE-NER only sees this video's own prose — not sidebar/end-screen clones.
/// </summary>
public static class RemoteDescriptionSanitizer
{
    private static readonly Regex UrlLine = new(
        @"https?://\S+|www\.\S+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex YouTubeVideoUrl = new(
        @"(?:https?://)?(?:www\.)?(?:youtube\.com/watch\S*|youtu\.be/\S+|youtube\.com/shorts/\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] PromoPrefixes =
    [
        "subscribe", "check out", "follow me", "follow us", "like and", "smash",
        "music provided", "about ", "my po box", "p.o. box", "po box",
        "have any questions", "email:", "twitter", "instagram", "playlist",
        "top 10 most popular", "watch more", "click the like", "let's aim for",
        "merch", "sponsorship", "collaboration"
    ];

    /// <summary>
    /// Returns title + cleaned description (+ optional tags / location) suitable for NER.
    /// </summary>
    public static string BuildNerCorpus(
        string? title,
        string? description,
        IEnumerable<string>? tags = null,
        string? locationLabel = null)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(title))
            parts.Add(title.Trim());

        var cleaned = SanitizeDescription(description);
        if (!string.IsNullOrWhiteSpace(cleaned))
            parts.Add(cleaned);

        if (!string.IsNullOrWhiteSpace(locationLabel))
            parts.Add("Location: " + locationLabel.Trim());

        var tagList = tags?
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(40)
            .ToList() ?? [];
        if (tagList.Count > 0)
            parts.Add("Tags: " + string.Join(", ", tagList));

        return string.Join("\n", parts);
    }

    public static string SanitizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return string.Empty;

        var kept = new List<string>();
        var consecutiveLinkLines = 0;
        var skippingContactBlock = false;

        foreach (var rawLine in description.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                skippingContactBlock = false;
                if (kept.Count > 0 && kept[^1].Length > 0)
                    kept.Add("");
                continue;
            }

            var lower = line.ToLowerInvariant();

            // Drop PO Box / mailing / about-channel contact blocks (often inject home-state noise).
            if (lower.StartsWith("my po box") || lower.StartsWith("p.o. box") || lower.StartsWith("po box") ||
                lower.StartsWith("about ") || lower.StartsWith("follow me") || lower.StartsWith("follow us"))
            {
                skippingContactBlock = true;
                continue;
            }

            if (skippingContactBlock)
            {
                if (IsPromoLine(lower) || Regex.IsMatch(line, @"^\d{5}(?:-\d{4})?$") ||
                    Regex.IsMatch(lower, @"\b(?:alabama|street|ave|road|box)\b"))
                    continue;
                // Channel-name-only lines inside contact block
                if (line.Length <= 32 && !line.Contains(' '))
                    continue;
                skippingContactBlock = false;
            }

            // Drop pure / dominant video-link lines (related videos, end screens).
            var withoutUrls = UrlLine.Replace(line, "").Trim();
            var hasYt = YouTubeVideoUrl.IsMatch(line);
            if (hasYt)
            {
                consecutiveLinkLines++;
                // Keep a short caption before a URL only if it looks like original prose
                // and we haven't entered a link farm yet.
                if (consecutiveLinkLines <= 1 &&
                    withoutUrls.Length >= 40 &&
                    !LooksLikeRelatedVideoTitle(withoutUrls) &&
                    !IsPromoLine(lower))
                {
                    kept.Add(withoutUrls);
                }

                continue;
            }

            consecutiveLinkLines = 0;

            if (IsPromoLine(lower))
                continue;

            // Hashtag-only lines: keep tags as words without #
            if (line.StartsWith('#') || line.All(c => c is '#' or ' ' || char.IsLetterOrDigit(c)))
            {
                if (line.Contains('#'))
                {
                    var tags = Regex.Matches(line, @"#(\w+)")
                        .Select(m => m.Groups[1].Value)
                        .Where(t => t.Length > 2);
                    var joined = string.Join(" ", tags);
                    if (!string.IsNullOrWhiteSpace(joined))
                        kept.Add(joined);
                    continue;
                }
            }

            kept.Add(line);

            // Cap retained body — first ~1200 chars of real prose is enough for place NER.
            if (kept.Sum(l => l.Length) > 1200)
                break;
        }

        // Collapse excess blank lines
        var sb = new StringBuilder();
        var blank = false;
        foreach (var line in kept)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (blank) continue;
                blank = true;
                sb.AppendLine();
                continue;
            }

            blank = false;
            sb.AppendLine(line);
        }

        return sb.ToString().Trim();
    }

    private static bool IsPromoLine(string lower) =>
        PromoPrefixes.Any(p => lower.StartsWith(p, StringComparison.Ordinal) ||
                               lower.Contains("http://bit.ly", StringComparison.Ordinal) ||
                               lower.Contains("https://bit.ly", StringComparison.Ordinal));

    /// <summary>
    /// Related-video titles are often Title Case clickbait ending with "(...Find)" etc.
    /// </summary>
    private static bool LooksLikeRelatedVideoTitle(string text)
    {
        if (text.Length is < 20 or > 120) return false;
        if (Regex.IsMatch(text, @"\((?:Unbelievable|Incredible|How to|Explored|Returned|Police)", RegexOptions.IgnoreCase))
            return true;
        if (Regex.IsMatch(text, @"^(?:Found|I Found|Finding|Searching)\b", RegexOptions.IgnoreCase) &&
            Regex.IsMatch(text, @"\b(?:While|Underwater|Scuba|River|Mine)\b", RegexOptions.IgnoreCase))
            return true;
        return false;
    }
}
