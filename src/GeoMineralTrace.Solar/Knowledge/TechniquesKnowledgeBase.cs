namespace GeoMineralTrace.Solar.Knowledge;

/// <summary>
/// Loads the internal solar/shadow techniques knowledge base shipped with the app.
/// Documents methods, formulas, assumptions, error sources, and references.
/// </summary>
public sealed class TechniquesKnowledgeBase
{
    private readonly IReadOnlyList<TechniqueArticle> _articles;

    public TechniquesKnowledgeBase(IEnumerable<TechniqueArticle>? articles = null)
    {
        _articles = articles?.ToList() ?? LoadEmbeddedDefaults();
    }

    public IReadOnlyList<TechniqueArticle> All => _articles;

    public TechniqueArticle? GetById(string id) =>
        _articles.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<TechniqueArticle> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _articles;

        var q = query.Trim();
        return _articles
            .Where(a =>
                a.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Summary.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.BodyMarkdown.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static IReadOnlyList<TechniqueArticle> LoadEmbeddedDefaults()
    {
        // Prefer markdown files under knowledge/techniques when present at runtime;
        // fall back to the curated in-memory catalog so unit tests and headless
        // runs never depend on file layout.
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "knowledge", "techniques")),
            Path.GetFullPath(Path.Combine(baseDir, "knowledge", "techniques")),
            Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "knowledge", "techniques"))
        };

        foreach (var dir in candidates.Distinct())
        {
            if (!Directory.Exists(dir))
                continue;

            var loaded = Directory.GetFiles(dir, "*.md")
                .Select(ParseMarkdownArticle)
                .Where(a => a is not null)
                .Cast<TechniqueArticle>()
                .ToList();

            if (loaded.Count > 0)
                return loaded;
        }

        return BuiltInCatalog.Articles;
    }

    private static TechniqueArticle? ParseMarkdownArticle(string path)
    {
        var text = File.ReadAllText(path);
        var fileId = Path.GetFileNameWithoutExtension(path);
        string title = fileId;
        var tags = new List<string>();
        var body = text;

        if (text.StartsWith("---", StringComparison.Ordinal))
        {
            var end = text.IndexOf("---", 3, StringComparison.Ordinal);
            if (end > 0)
            {
                var front = text[3..end];
                body = text[(end + 3)..].TrimStart();
                foreach (var line in front.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (line.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
                        title = line["title:".Length..].Trim().Trim('"');
                    else if (line.StartsWith("tags:", StringComparison.OrdinalIgnoreCase))
                    {
                        var raw = line["tags:".Length..].Trim();
                        tags = raw.Trim('[', ']')
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(t => t.Trim('"'))
                            .ToList();
                    }
                }
            }
        }

        var summary = body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#')) ?? title;

        return new TechniqueArticle(fileId, title, summary, body, tags);
    }
}

public sealed record TechniqueArticle(
    string Id,
    string Title,
    string Summary,
    string BodyMarkdown,
    IReadOnlyList<string> Tags);

internal static class BuiltInCatalog
{
    public static IReadOnlyList<TechniqueArticle> Articles { get; } =
    [
        new(
            "shadow-elevation-basics",
            "Shadow elevation from height/length ratio",
            "Solar elevation ≈ arctan(object_height / shadow_length) on level ground.",
            """
            # Shadow elevation basics

            For a vertical object of height `h` casting a shadow of length `L` on level ground:

            \[
            \alpha = \arctan(h / L)
            \]

            where `\alpha` is the solar elevation angle.

            ## Assumptions
            - Object is vertical (plumb).
            - Ground is locally flat (or slope is measured and corrected).
            - Shadow tip and object base are correctly identified in the image.
            - Lengths share the same scale (pixels are fine if both are in pixels).

            ## Primary error sources
            - Perspective / lens distortion
            - Soft shadow penumbra (uncertain tip)
            - Non-level terrain
            - Incorrect time zone / UTC offset on the observation timestamp
            """,
            ["shadow", "elevation", "geometry"]),
        new(
            "solar-position-noaa",
            "Solar position (NOAA / Meeus-class)",
            "Forward computation of elevation and azimuth from lat, lon, and UTC time.",
            """
            # Solar position algorithm

            GeoMineral Trace uses a NOAA Solar Calculator–class algorithm (Astronomical
            Algorithms lineage) for forward solar elevation and azimuth.

            For sub-arcminute work or historical edge cases, prefer NREL SPA
            (Reda & Andreas, Solar Position Algorithm for Solar Radiation Applications).

            ## Inputs
            - WGS84 latitude / longitude
            - UTC timestamp
            - Optional refraction correction near horizon

            ## Outputs
            - Elevation (degrees)
            - Azimuth (degrees clockwise from true north)
            - Declination, equation of time (diagnostics)
            """,
            ["spa", "noaa", "solar-position"]),
        new(
            "inverse-locus",
            "Inverse solar locus / probability map",
            "Elevation (± azimuth) constraints map to geographic bands; multi-frame trajectories tighten the fix.",
            """
            # Inverse locus methodology

            Inspired by ShadowFinder and related forensic solar-matching work:

            1. Measure height/shadow → elevation α (± azimuth if available).
            2. For each grid cell, compute solar position at the observation UTC.
            3. Score cells by Gaussian residual on elevation (and azimuth).
            4. Normalize to a relative probability map.
            5. With multiple times at a fixed site, multiply scores (trajectory fusion).

            ## Limitations
            - Elevation-only yields arcs/bands, not points.
            - Grid bounds and step size truncate and quantize the true locus.
            - Correlated measurement errors inflate joint confidence.
            """,
            ["locus", "shadowfinder", "trajectory"]),
        new(
            "measurement-best-practices",
            "Measurement best practices for video keyframes",
            "How to pick frames, mark tips, and record uncertainty honestly.",
            """
            # Measurement best practices

            - Prefer hard noon-ish shadows over near-horizon (refraction + soft tips).
            - Use multiple frames when the camera is static.
            - Record confidence based on tip clarity and perspective.
            - Always store raw pixel endpoints for auditability.
            - Document assumed slope and object verticality.
            - Never treat a locus peak as a confirmed location without cross-evidence.
            """,
            ["measurement", "best-practices", "uncertainty"])
    ];
}
