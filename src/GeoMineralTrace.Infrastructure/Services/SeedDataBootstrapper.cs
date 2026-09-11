using GeoMineralTrace.Core.App;
using GeoMineralTrace.Claims.Import;
using GeoMineralTrace.Claims.Storage;
using GeoMineralTrace.Hydrology.Import;
using GeoMineralTrace.Hydrology.Storage;
using GeoMineralTrace.Rockhounding.Import;
using GeoMineralTrace.Rockhounding.Storage;
using Microsoft.Extensions.Logging;

namespace GeoMineralTrace.Infrastructure.Services;

/// <summary>
/// Imports bundled seed files once into local SQLite stores (localities + claims + rivers + trails).
/// </summary>
public sealed class SeedDataBootstrapper
{
    private readonly LocalityStore _store;
    private readonly ClaimStore _claims;
    private readonly PersonalFindStore _finds;
    private readonly RumouredSiteStore _rumoured;
    private readonly RiverStore _rivers;
    private readonly TrailStore _trails;
    private readonly MineralGlossaryStore _glossary;
    private readonly MineralGlossaryImporter _glossaryImporter;
    private readonly LocalityCsvImporter _csv;
    private readonly UsgsMrdsImporter _usgs;
    private readonly RumouredSiteCsvImporter _rumouredCsv;
    private readonly BlmMlrsClaimImporter _claimsImporter;
    private readonly NhdWatercourseImporter _nhdImporter;
    private readonly RiverFindsCsvImporter _riverFindsImporter;
    private readonly TrailsGeoJsonImporter _trailsImporter;
    private readonly ILogger<SeedDataBootstrapper>? _logger;

    public SeedDataBootstrapper(
        LocalityStore store,
        ClaimStore claims,
        PersonalFindStore finds,
        RumouredSiteStore rumoured,
        RiverStore rivers,
        TrailStore trails,
        MineralGlossaryStore glossary,
        MineralGlossaryImporter glossaryImporter,
        LocalityCsvImporter csv,
        UsgsMrdsImporter usgs,
        RumouredSiteCsvImporter rumouredCsv,
        BlmMlrsClaimImporter claimsImporter,
        NhdWatercourseImporter nhdImporter,
        RiverFindsCsvImporter riverFindsImporter,
        TrailsGeoJsonImporter trailsImporter,
        ILogger<SeedDataBootstrapper>? logger = null)
    {
        _store = store;
        _claims = claims;
        _finds = finds;
        _rumoured = rumoured;
        _rivers = rivers;
        _trails = trails;
        _glossary = glossary;
        _glossaryImporter = glossaryImporter;
        _csv = csv;
        _usgs = usgs;
        _rumouredCsv = rumouredCsv;
        _claimsImporter = claimsImporter;
        _nhdImporter = nhdImporter;
        _riverFindsImporter = riverFindsImporter;
        _trailsImporter = trailsImporter;
        _logger = logger;
    }

    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _store.SeedDemoDataAsync(cancellationToken).ConfigureAwait(false);

        await _claims.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _claims.SeedDemoAsync(cancellationToken).ConfigureAwait(false);

        await _finds.InitializeAsync(cancellationToken).ConfigureAwait(false);

        await _rumoured.InitializeAsync(cancellationToken).ConfigureAwait(false);

        await _rivers.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _rivers.SeedDemoAsync(cancellationToken).ConfigureAwait(false);

        await _trails.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _trails.SeedDemoAsync(cancellationToken).ConfigureAwait(false);

        await _glossary.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (await _glossary.CountSpeciesAsync(cancellationToken).ConfigureAwait(false) == 0)
        {
            var n = await _glossaryImporter.ImportFromSeedAsync(_glossary, cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation("Seeded {Count} mineral glossary species (offline, no images)", n);
        }

        var markers = AppDataPaths.Sub("seed-imported.flag");

        if (File.Exists(markers))
            return;

        var roots = CandidateSeedRoots();
        var imported = 0;

        foreach (var root in roots)
        {
            var localities = Path.Combine(root, "localities-sample.csv");
            if (File.Exists(localities))
            {
                var n = await _csv.ImportFileAsync(localities, _store, cancellationToken).ConfigureAwait(false);
                imported += n;
                _logger?.LogInformation("Imported {Count} localities from {Path}", n, localities);
            }

            var knownMines = Path.Combine(root, "known-mines-public-private.csv");
            if (File.Exists(knownMines))
            {
                var n = await _csv.ImportFileAsync(knownMines, _store, cancellationToken).ConfigureAwait(false);
                imported += n;
                _logger?.LogInformation("Imported {Count} curated public/private mines from {Path}", n, knownMines);
            }

            var usgs = Path.Combine(root, "usgs-mrds-sample.csv");
            if (File.Exists(usgs))
            {
                var n = await _usgs.ImportFileAsync(usgs, _store, cancellationToken).ConfigureAwait(false);
                imported += n;
                _logger?.LogInformation("Imported {Count} USGS rows from {Path}", n, usgs);
            }

            var claimsCsv = Path.Combine(root, "blm-claims-sample.csv");
            if (File.Exists(claimsCsv))
            {
                var result = await _claimsImporter.ImportFileAsync(claimsCsv, _claims, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                imported += result.Upserted;
                _logger?.LogInformation("Imported {Count} claims from {Path}", result.Upserted, claimsCsv);
            }

            var rumouredCsv = Path.Combine(root, "rumoured-sites-sample.csv");
            if (File.Exists(rumouredCsv))
            {
                var n = await _rumouredCsv.ImportFileAsync(rumouredCsv, _rumoured, cancellationToken).ConfigureAwait(false);
                imported += n;
                _logger?.LogInformation("Imported {Count} rumoured sites from {Path}", n, rumouredCsv);
            }

            var riversGeo = Path.Combine(root, "rivers-sample.geojson");
            if (File.Exists(riversGeo))
            {
                var result = await _nhdImporter.ImportFileAsync(riversGeo, _rivers, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                imported += result.Imported;
                _logger?.LogInformation("Imported {Count} watercourses from {Path}", result.Imported, riversGeo);
            }

            var riverFinds = Path.Combine(root, "river-finds.csv");
            if (File.Exists(riverFinds))
            {
                var result = await _riverFindsImporter.ImportFileAsync(riverFinds, _rivers, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                imported += result.AssociationsUpserted;
                _logger?.LogInformation("Imported {Count} river mineral associations from {Path}", result.AssociationsUpserted, riverFinds);
            }

            var trailsGeo = Path.Combine(root, "trails-sample.geojson");
            if (File.Exists(trailsGeo))
            {
                var result = await _trailsImporter.ImportFileAsync(trailsGeo, _trails, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                imported += result.Imported;
                _logger?.LogInformation("Imported {Count} trails from {Path}", result.Imported, trailsGeo);
            }
        }

        if (imported > 0 || roots.Count > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(markers)!);
            await File.WriteAllTextAsync(markers, DateTimeOffset.UtcNow.ToString("O"), cancellationToken)
                .ConfigureAwait(false);
        }

        await EnsureRumouredSeededAsync(roots, cancellationToken).ConfigureAwait(false);
        await EnsureRiversSeededAsync(roots, cancellationToken).ConfigureAwait(false);
        await EnsureTrailsSeededAsync(roots, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureTrailsSeededAsync(IReadOnlyList<string> roots, CancellationToken cancellationToken)
    {
        const int demoOnlyCount = 8;
        if (await _trails.CountAsync(cancellationToken).ConfigureAwait(false) > demoOnlyCount)
            return;

        foreach (var root in roots)
        {
            var trailsGeo = Path.Combine(root, "trails-sample.geojson");
            if (!File.Exists(trailsGeo))
                continue;

            var result = await _trailsImporter.ImportFileAsync(trailsGeo, _trails, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            _logger?.LogInformation("Imported {Count} trails (upgrade) from {Path}", result.Imported, trailsGeo);
            return;
        }
    }

    private async Task EnsureRiversSeededAsync(IReadOnlyList<string> roots, CancellationToken cancellationToken)
    {
        const int demoOnlyCount = 3;
        if (await _rivers.CountAsync(cancellationToken).ConfigureAwait(false) > demoOnlyCount)
            return;

        foreach (var root in roots)
        {
            var riversGeo = Path.Combine(root, "rivers-sample.geojson");
            var riverFinds = Path.Combine(root, "river-finds.csv");
            if (!File.Exists(riversGeo) && !File.Exists(riverFinds))
                continue;

            if (File.Exists(riversGeo))
            {
                var result = await _nhdImporter.ImportFileAsync(riversGeo, _rivers, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                _logger?.LogInformation("Imported {Count} watercourses (rivers upgrade) from {Path}", result.Imported, riversGeo);
            }

            if (File.Exists(riverFinds))
            {
                var result = await _riverFindsImporter.ImportFileAsync(riverFinds, _rivers, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                _logger?.LogInformation(
                    "Imported {Count} river mineral associations (rivers upgrade) from {Path}",
                    result.AssociationsUpserted,
                    riverFinds);
            }

            return;
        }
    }

    private async Task EnsureRumouredSeededAsync(IReadOnlyList<string> roots, CancellationToken cancellationToken)
    {
        if (await _rumoured.CountAsync(cancellationToken).ConfigureAwait(false) > 0)
            return;

        foreach (var root in roots)
        {
            var rumouredCsv = Path.Combine(root, "rumoured-sites-sample.csv");
            if (!File.Exists(rumouredCsv))
                continue;

            var n = await _rumouredCsv.ImportFileAsync(rumouredCsv, _rumoured, cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation("Imported {Count} rumoured sites (first-run) from {Path}", n, rumouredCsv);
            if (n > 0)
                return;
        }
    }

    private static List<string> CandidateSeedRoots()
    {
        var list = new List<string>();
        var baseDir = AppContext.BaseDirectory;
        list.Add(Path.Combine(baseDir, "data", "seed"));
        list.Add(Path.Combine(baseDir, "..", "..", "..", "..", "..", "data", "seed"));

        var dir = new DirectoryInfo(baseDir);
        for (var i = 0; i < 6 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "data", "seed");
            if (Directory.Exists(candidate))
                list.Add(candidate);
            dir = dir.Parent;
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .ToList();
    }
}
