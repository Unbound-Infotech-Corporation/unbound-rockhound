using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Pipeline.Abstractions;

namespace GeoMineralTrace.Pipeline.Vision;

/// <summary>
/// Lightweight heuristic scene tagger from filename/sidecar keywords.
/// Production builds can swap in an ONNX/vision model adapter implementing <see cref="ISceneTagger"/>.
/// </summary>
public sealed class HeuristicSceneTagger : ISceneTagger
{
    private static readonly (string Keyword, EvidenceType Type, string Label)[] Rules =
    [
        ("forest", EvidenceType.Vegetation, "Vegetation: forest/woodland cue"),
        ("woodland", EvidenceType.Vegetation, "Vegetation: woodland cue"),
        ("sage", EvidenceType.Vegetation, "Vegetation: sagebrush cue"),
        ("juniper", EvidenceType.Vegetation, "Vegetation: juniper cue"),
        ("pine", EvidenceType.Vegetation, "Vegetation: pine cue"),
        ("cactus", EvidenceType.Vegetation, "Vegetation: cactus/desert flora"),
        ("yucca", EvidenceType.Vegetation, "Vegetation: yucca cue"),
        ("desert", EvidenceType.Terrain, "Terrain: arid/desert cue"),
        ("canyon", EvidenceType.Terrain, "Terrain: canyon cue"),
        ("mesa", EvidenceType.Terrain, "Terrain: mesa cue"),
        ("butte", EvidenceType.Terrain, "Terrain: butte cue"),
        ("mountain", EvidenceType.Terrain, "Terrain: mountain cue"),
        ("ridge", EvidenceType.Terrain, "Terrain: ridge cue"),
        ("beach", EvidenceType.Terrain, "Terrain: coastal/beach cue"),
        ("dune", EvidenceType.Terrain, "Terrain: sand dune cue"),
        ("snow", EvidenceType.Terrain, "Terrain: snow/alpine cue"),
        ("lava", EvidenceType.Terrain, "Terrain: lava field cue"),
        ("badland", EvidenceType.Terrain, "Terrain: badlands cue"),
        ("mine", EvidenceType.MiningCue, "Mining-related cue"),
        ("quarry", EvidenceType.MiningCue, "Quarry/mining cue"),
        ("claim", EvidenceType.MiningCue, "Claim/mining cue"),
        ("tailing", EvidenceType.MiningCue, "Mine tailings cue"),
        ("adit", EvidenceType.MiningCue, "Mine adit cue"),
        ("shaft", EvidenceType.MiningCue, "Mine shaft cue"),
        ("church", EvidenceType.Architecture, "Architecture: church/steeple cue"),
        ("barn", EvidenceType.Architecture, "Architecture: rural barn cue"),
        ("silo", EvidenceType.Architecture, "Architecture: grain silo cue"),
        ("skyscraper", EvidenceType.Architecture, "Architecture: high-rise cue"),
        ("bridge", EvidenceType.Architecture, "Architecture: bridge cue"),
        ("tower", EvidenceType.Architecture, "Architecture: tower cue"),
        ("highway", EvidenceType.Cultural, "Infrastructure: highway cue"),
        ("interstate", EvidenceType.Cultural, "Infrastructure: interstate cue"),
        ("railroad", EvidenceType.Cultural, "Infrastructure: railroad cue"),
        ("dirt", EvidenceType.SoilRock, "Soil/rock: exposed dirt/soil"),
        ("obsidian", EvidenceType.SoilRock, "Rock appearance: obsidian mention"),
        ("basalt", EvidenceType.SoilRock, "Rock appearance: basalt mention"),
        ("granite", EvidenceType.SoilRock, "Rock appearance: granite mention"),
        ("sandstone", EvidenceType.SoilRock, "Rock appearance: sandstone mention"),
        ("limestone", EvidenceType.SoilRock, "Rock appearance: limestone mention"),
        ("agate", EvidenceType.SoilRock, "Rock appearance: agate mention"),
        ("jasper", EvidenceType.SoilRock, "Rock appearance: jasper mention")
    ];

    public Task<IReadOnlyList<SceneTag>> TagAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var haystack = imagePath.ToLowerInvariant();

        // Prefer media-adjacent source sidecar (title/description from yt-dlp).
        foreach (var candidate in new[]
                 {
                     imagePath,
                     Path.ChangeExtension(imagePath, ".mp4"),
                     Path.ChangeExtension(imagePath, ".webm"),
                     Path.GetDirectoryName(imagePath) is { } dir
                         ? Directory.GetFiles(dir, "*.gmt-source.json").FirstOrDefault()
                         : null
                 })
        {
            if (candidate is null) continue;
            var sidecarPath = candidate.EndsWith(".gmt-source.json", StringComparison.OrdinalIgnoreCase)
                ? candidate
                : Path.ChangeExtension(candidate, ".gmt-source.json");
            if (sidecarPath is null || !File.Exists(sidecarPath)) continue;
            try
            {
                var json = File.ReadAllText(sidecarPath).ToLowerInvariant();
                haystack += " " + json;
                break;
            }
            catch { /* ignore */ }
        }

        foreach (var sidecar in new[]
                 {
                     Path.ChangeExtension(imagePath, ".tags.txt"),
                     Path.ChangeExtension(imagePath, ".tags.json"),
                     imagePath + ".tags.txt"
                 })
        {
            if (!File.Exists(sidecar)) continue;
            try { haystack += " " + File.ReadAllText(sidecar).ToLowerInvariant(); }
            catch { /* ignore unreadable sidecar */ }
        }

        var tags = Rules
            .Where(r => haystack.Contains(r.Keyword))
            .Select(r => new SceneTag(r.Type, r.Label, Confidence.Low,
                "Heuristic keyword tag — replace with vision model for production confidence."))
            .GroupBy(t => t.Label)
            .Select(g => g.First())
            .ToList();

        return Task.FromResult<IReadOnlyList<SceneTag>>(tags);
    }
}