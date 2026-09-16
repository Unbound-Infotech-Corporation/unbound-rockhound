namespace GeoMineralTrace.Core.Geology;

/// <summary>
/// Ranks gem/mineral guesses from a CNGM unit plus nearby curated/USGS locality minerals.
/// Geology alone never yields a Stronger band.
/// </summary>
public static class ProspectGuessScorer
{
    public const double NearbyRadiusKm = 25;
    public const double StrongerMaxDistanceKm = 15;
    public const int MaxHints = 12;

    public static ProspectGuessResult Score(
        GeologicMapUnit? unit,
        IReadOnlyList<NearbyMineralOccurrence> nearby,
        bool usedOnlineGeology,
        string? statusNote = null)
    {
        nearby ??= [];
        var nearbySummaries = nearby
            .OrderBy(n => n.DistanceKm)
            .Take(8)
            .Select(FormatNearby)
            .ToList();

        if (unit is { LooksWaterOrIce: true } or { LooksArtificial: true } or { LooksUnmapped: true })
        {
            return new ProspectGuessResult(
                unit,
                [],
                nearbySummaries,
                ProspectGuessResult.StandardDisclaimer,
                usedOnlineGeology,
                statusNote ?? "This map unit is water, ice, artificial ground, or unmapped — no gem/mineral heuristic was applied.");
        }

        var geologyHits = GeoMaterialMineralCatalog.Match(unit?.SearchText);
        var geologyMinerals = new Dictionary<string, GeoMaterialAssociationRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in geologyHits)
        {
            foreach (var mineral in rule.Minerals)
                geologyMinerals.TryAdd(mineral, rule);
        }

        var nearbyByMineral = new Dictionary<string, List<NearbyMineralOccurrence>>(StringComparer.OrdinalIgnoreCase);
        foreach (var occurrence in nearby)
        {
            foreach (var mineral in occurrence.Minerals)
            {
                var key = NormalizeMineral(mineral);
                if (key.Length < 3)
                    continue;
                if (!nearbyByMineral.TryGetValue(key, out var list))
                {
                    list = [];
                    nearbyByMineral[key] = list;
                }

                list.Add(occurrence);
            }
        }

        var hints = new List<ProspectGuessHint>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (mineral, rule) in geologyMinerals)
        {
            if (!seen.Add(mineral))
                continue;

            nearbyByMineral.TryGetValue(mineral, out var hits);
            hits ??= [];
            var closest = hits.OrderBy(h => h.DistanceKm).FirstOrDefault();
            var citations = BuildCitations(unit, closest);

            if (closest is not null && closest.DistanceKm <= StrongerMaxDistanceKm)
            {
                hints.Add(new ProspectGuessHint(
                    mineral,
                    ProspectGuessBand.Stronger,
                    $"{rule.Reason} Nearby records within {closest.DistanceKm:0.0} km list {mineral} ({closest.LocalityName}).",
                    citations));
            }
            else
            {
                var band = rule.GeologyOnlyBand;
                if (string.Equals(unit?.GeoMaterialConfidence, "High", StringComparison.OrdinalIgnoreCase)
                    && band == ProspectGuessBand.Speculative)
                {
                    // High-confidence lithology still does not promote geology-only guesses to Stronger.
                    band = ProspectGuessBand.Plausible;
                }

                var extra = closest is null
                    ? "No nearby curated/USGS locality in this corridor lists it — treat as a lithology hint only."
                    : $"Nearest listing is {closest.DistanceKm:0.0} km away ({closest.LocalityName}), so the local match is weaker.";
                hints.Add(new ProspectGuessHint(
                    mineral,
                    band,
                    $"{rule.Reason} {extra}",
                    citations));
            }
        }

        foreach (var (mineral, hits) in nearbyByMineral.OrderBy(kv => kv.Value.Min(v => v.DistanceKm)))
        {
            if (!seen.Add(mineral))
                continue;

            var closest = hits.OrderBy(h => h.DistanceKm).First();
            var band = closest.DistanceKm <= 10 && closest.IsCurated
                ? ProspectGuessBand.Plausible
                : ProspectGuessBand.Speculative;
            var geologyNote = unit is null
                ? "No geologic-unit identify was available; this is a corridor listing only."
                : "The identified map unit did not independently flag this mineral; the hint comes from nearby records.";
            hints.Add(new ProspectGuessHint(
                mineral,
                band,
                $"{geologyNote} {closest.LocalityName} ({closest.DistanceKm:0.0} km) reports {mineral}.",
                BuildCitations(unit, closest)));
        }

        var ranked = hints
            .OrderByDescending(h => (int)h.Band)
            .ThenBy(h => h.Mineral, StringComparer.OrdinalIgnoreCase)
            .Take(MaxHints)
            .ToList();

        return new ProspectGuessResult(
            unit,
            ranked,
            nearbySummaries,
            ProspectGuessResult.StandardDisclaimer,
            usedOnlineGeology,
            statusNote);
    }

    public static string FormatPanel(ProspectGuessResult result)
    {
        var lines = new List<string>();
        if (result.Unit is { } unit)
        {
            lines.Add($"Geology (CNGM {unit.ThemeLabel})");
            lines.Add(unit.DisplayTitle);
            if (!string.IsNullOrWhiteSpace(unit.GeoMaterial))
            {
                var conf = string.IsNullOrWhiteSpace(unit.GeoMaterialConfidence)
                    ? ""
                    : $" ({unit.GeoMaterialConfidence} confidence)";
                lines.Add($"GeoMaterial: {unit.GeoMaterial}{conf}");
            }

            if (!string.IsNullOrWhiteSpace(unit.SynthesisDescription))
                lines.Add(unit.SynthesisDescription);
            else if (!string.IsNullOrWhiteSpace(unit.Description))
                lines.Add(Truncate(unit.Description, 280));

            var age = unit.Age;
            if (string.IsNullOrWhiteSpace(age) && (!string.IsNullOrWhiteSpace(unit.MinAge) || !string.IsNullOrWhiteSpace(unit.MaxAge)))
                age = string.Join(" to ", new[] { unit.MinAge, unit.MaxAge }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(age))
                lines.Add($"Age: {age}");

            if (!string.IsNullOrWhiteSpace(unit.MapCitation))
                lines.Add($"Source map: {unit.MapCitation}");
            if (!string.IsNullOrWhiteSpace(unit.NgmdbUrl))
                lines.Add(unit.NgmdbUrl);
        }
        else if (!string.IsNullOrWhiteSpace(result.StatusNote))
        {
            lines.Add(result.StatusNote);
        }

        lines.Add("");
        lines.Add("Prospect guess (not a permit)");
        if (result.Hints.Count == 0)
        {
            lines.Add("No ranked mineral hints for this point. Import USGS/curated localities or try another tap.");
        }
        else
        {
            foreach (var hint in result.Hints)
            {
                var band = hint.Band switch
                {
                    ProspectGuessBand.Stronger => "Stronger",
                    ProspectGuessBand.Plausible => "Plausible",
                    _ => "Speculative"
                };
                lines.Add($"• {band}: {hint.Mineral} — {hint.Reason}");
            }
        }

        if (result.NearbySummaries.Count > 0)
        {
            lines.Add("");
            lines.Add("Nearby records");
            lines.AddRange(result.NearbySummaries.Select(s => "• " + s));
        }

        lines.Add("");
        lines.Add(result.Disclaimer);
        return string.Join(Environment.NewLine, lines);
    }

    private static IReadOnlyList<string> BuildCitations(GeologicMapUnit? unit, NearbyMineralOccurrence? nearest)
    {
        var citations = new List<string>();
        if (unit is not null)
        {
            citations.Add($"CNGM {unit.ThemeLabel}: {unit.DisplayTitle}");
            if (!string.IsNullOrWhiteSpace(unit.GeoMaterial))
                citations.Add("GeoMaterial: " + unit.GeoMaterial);
            if (!string.IsNullOrWhiteSpace(unit.MapCitation))
                citations.Add(unit.MapCitation);
            if (!string.IsNullOrWhiteSpace(unit.NgmdbUrl))
                citations.Add(unit.NgmdbUrl);
        }

        if (nearest is not null)
        {
            var src = string.IsNullOrWhiteSpace(nearest.SourceDataset) ? "locality" : nearest.SourceDataset;
            citations.Add($"{src} “{nearest.LocalityName}” ({nearest.DistanceKm:0.0} km)");
        }

        return citations;
    }

    private static string FormatNearby(NearbyMineralOccurrence n)
    {
        var minerals = n.Minerals.Count == 0 ? "minerals not listed" : string.Join(", ", n.Minerals.Take(5));
        var src = string.IsNullOrWhiteSpace(n.SourceDataset) ? "locality" : n.SourceDataset;
        return $"{n.LocalityName} · {n.DistanceKm:0.0} km · {src} · {minerals}";
    }

    private static string NormalizeMineral(string mineral)
    {
        var trimmed = mineral.Trim();
        var comma = trimmed.IndexOf(',');
        if (comma > 0)
            trimmed = trimmed[..comma].Trim();
        return trimmed;
    }

    private static string Truncate(string text, int max)
    {
        var flat = text.Replace('\n', ' ').Trim();
        if (flat.Length <= max)
            return flat;
        return flat[..(max - 1)].TrimEnd() + "…";
    }
}
