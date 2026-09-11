using System.Text;
using System.Text.Json;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Core.Hypothesis;
using GeoMineralTrace.Core.Solar;
using GeoMineralTrace.Pipeline;
using Microsoft.Extensions.Logging;

namespace GeoMineralTrace.Reporting;

/// <summary>
/// Exports an auditable research report (Markdown + JSON sidecar).
/// </summary>
public sealed class ResearchReportExporter
{
    private readonly ILogger<ResearchReportExporter>? _logger;

    public ResearchReportExporter(ILogger<ResearchReportExporter>? logger = null)
    {
        _logger = logger;
    }

    public async Task<ResearchReportPaths> ExportAsync(
        AnalysisPipelineResult result,
        string outputDirectory,
        string? methodologyNotes = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var mdPath = Path.Combine(outputDirectory, $"report-{stamp}.md");
        var jsonPath = Path.Combine(outputDirectory, $"report-{stamp}.json");

        var md = BuildMarkdown(result, methodologyNotes);
        await File.WriteAllTextAsync(mdPath, md, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        var payload = new
        {
            result.Session.Id,
            result.Session.DisplayName,
            result.Session.Status,
            Evidence = result.Evidence,
            Hypotheses = result.Hypotheses,
            result.SolarLocus,
            Nearby = result.NearbyLocalities,
            ExportedAtUtc = DateTimeOffset.UtcNow,
            Disclaimer = "Probabilistic research aid — not a definitive location fix. Verify land access independently."
        };
        await File.WriteAllTextAsync(jsonPath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation("Wrote research report {Md} / {Json}", mdPath, jsonPath);
        return new ResearchReportPaths(mdPath, jsonPath);
    }

    public static string BuildMarkdown(AnalysisPipelineResult result, string? methodologyNotes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# GeoMineral Trace — Research Report");
        sb.AppendLine();
        sb.AppendLine($"> **Disclaimer:** Results are ranked probabilistic hypotheses. Solar loci, OCR, audio, and mineral cues can corroborate each other but none alone proves a site. Verify land ownership/claims before any visit.");
        sb.AppendLine();
        sb.AppendLine($"- **Session:** {result.Session.DisplayName} (`{result.Session.Id:N}`)");
        sb.AppendLine($"- **Status:** {result.Session.Status}");
        sb.AppendLine($"- **Media:** {string.Join("; ", result.Session.SourceMediaPaths)}");
        sb.AppendLine($"- **Generated (UTC):** {DateTimeOffset.UtcNow:u}");
        sb.AppendLine();

        sb.AppendLine("## Hypotheses");
        if (result.Hypotheses.Count == 0)
            sb.AppendLine("_No hypotheses produced._");
        foreach (var h in result.Hypotheses.OrderBy(x => x.Rank))
        {
            sb.AppendLine($"### #{h.Rank} — {h.Label}");
            sb.AppendLine($"- Confidence: {h.Confidence}");
            if (h.Center is { } c) sb.AppendLine($"- Center: {c}");
            if (h.RadiusKm is { } r) sb.AppendLine($"- Radius: ~{r:F0} km");
            if (!string.IsNullOrWhiteSpace(h.RegionDescription))
                sb.AppendLine($"- Detail: {h.RegionDescription}");
            sb.AppendLine($"- Supporting evidence IDs: {string.Join(", ", h.SupportingEvidenceIds)}");
            if (h.UncertaintyNotes is { Count: > 0 })
            {
                sb.AppendLine("- Uncertainty:");
                foreach (var u in h.UncertaintyNotes)
                    sb.AppendLine($"  - {u}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("## Evidence Board");
        foreach (var g in result.Evidence.GroupBy(e => e.Type).OrderBy(g => g.Key.ToString()))
        {
            sb.AppendLine($"### {g.Key} ({g.Count()})");
            foreach (var e in g.OrderBy(x => x.MediaTimestamp).ThenBy(x => x.Summary))
            {
                var reject = e.IsRejected ? " **[REJECTED]**" : "";
                sb.AppendLine($"- {e.Confidence} · {e.Summary}{reject}");
                if (!string.IsNullOrWhiteSpace(e.Notes))
                    sb.AppendLine($"  - _{e.Notes}_");
            }

            sb.AppendLine();
        }

        if (result.SolarLocus is { } locus)
        {
            sb.AppendLine("## Solar locus");
            sb.AppendLine($"- Target elevation: {locus.TargetElevationDegrees:F2}°");
            sb.AppendLine($"- Peak cell: {locus.PeakProbabilityCell}");
            sb.AppendLine($"- Confidence: {locus.OverallConfidence}");
            sb.AppendLine("- Assumptions:");
            foreach (var a in locus.Assumptions) sb.AppendLine($"  - {a}");
            sb.AppendLine("- Limitations:");
            foreach (var a in locus.Limitations) sb.AppendLine($"  - {a}");
            sb.AppendLine();
        }

        sb.AppendLine("## Nearby / mineral-linked localities");
        if (result.NearbyLocalities.Count == 0)
            sb.AppendLine("_None linked in this run._");
        foreach (var n in result.NearbyLocalities)
        {
            var dist = n.DistanceKm is { } d ? $" · {d:F1} km" : "";
            sb.AppendLine($"- **{n.Name}** ({n.StateCode}){dist} · {n.AccessStatus} · rating {n.Rating:F1} · minerals: {string.Join(", ", n.Minerals)}");
            if (n.HumanActivityDataMayBeOutdated)
                sb.AppendLine($"  - **OUTDATED ACTIVITY** — {n.SourceDataset ?? "USGS"} vintage {n.SourceVintage ?? "?"}; legal clarity {n.LegalClarity:F1}/10.");
            if (n.AccessStatus is "Closed" or "Restricted")
                sb.AppendLine("  - **FLAGGED — not a collectable recommendation.**");
        }

        sb.AppendLine();
        sb.AppendLine("## Methodology notes");
        sb.AppendLine(methodologyNotes ??
                       "Pipeline stages: metadata → keyframes → OCR → transcription/NER → scene tags → fusion → rockhounding cross-link. See docs/solar-techniques.md and knowledge/techniques/.");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*GeoMineral Trace research export*");
        return sb.ToString();
    }
}

public readonly record struct ResearchReportPaths(string MarkdownPath, string JsonPath);
