using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using GeoMineralTrace.Claims;
using GeoMineralTrace.Core.Claims;
using GeoMineralTrace.Core.Finds;
using GeoMineralTrace.Core.Hydrology;
using GeoMineralTrace.Core.Rockhounding;
using GeoMineralTrace.Hydrology.Storage;

namespace GeoMineralTrace.Reporting.Kml;

/// <summary>
/// Builds multi-folder KML/KMZ for Google Earth from app catalog entities.
/// Trails/Campgrounds folders are reserved (empty until those datasets exist).
/// </summary>
public static class KmlCatalogBuilder
{
    public const string GoogleEarthDownloadUrl = "https://www.google.com/earth/about/versions/";
    public const string GoogleEarthWebUrl = "https://earth.google.com/web/";

    public static KmlDocument BuildVisibleExport(
        string documentName,
        IEnumerable<Locality>? localities = null,
        IEnumerable<MiningClaim>? claims = null,
        IEnumerable<PersonalFind>? finds = null,
        IEnumerable<Watercourse>? rivers = null,
        IEnumerable<(string Name, double Lat, double Lon, string Kind, string? DescriptionHtml)>? sessionMarkers = null)
    {
        var doc = new KmlDocument { Name = documentName };

        var localityFolder = new KmlFolder { Name = "Rockhounding localities", Visible = true };
        foreach (var l in localities ?? [])
        {
            if (l.Coordinates is not { } c || !c.IsValid) continue;
            localityFolder.Placemarks.Add(FromLocality(l));
        }

        doc.Folders.Add(localityFolder);

        var claimsFolder = new KmlFolder { Name = "Mining claims (Active / Expiring)", Visible = true };
        foreach (var claim in claims ?? [])
        {
            if (claim.Coordinates is not { } c || !c.IsValid) continue;
            if (claim.Status is not (ClaimStatus.Active or ClaimStatus.ExpiringSoon
                or ClaimStatus.LapsedReopenable or ClaimStatus.Closed))
                continue;
            claimsFolder.Placemarks.Add(FromClaim(claim));
        }

        doc.Folders.Add(claimsFolder);

        var findsFolder = new KmlFolder { Name = "Personal finds", Visible = true };
        foreach (var find in finds ?? [])
        {
            if (find.Coordinates is not { } c || !c.IsValid) continue;
            findsFolder.Placemarks.Add(FromFind(find));
        }

        doc.Folders.Add(findsFolder);

        var riversFolder = new KmlFolder { Name = "Rivers & waterways", Visible = true };
        foreach (var river in rivers ?? [])
        {
            if (river.Vertices.Count < 2)
                continue;
            riversFolder.Placemarks.Add(FromWatercourse(river));
        }

        doc.Folders.Add(riversFolder);

        // Reserved for future offline campground datasets.
        doc.Folders.Add(new KmlFolder { Name = "Campgrounds", Visible = false });

        if (sessionMarkers is not null)
        {
            var sessionFolder = new KmlFolder { Name = "Analysis session (map)", Visible = true };
            foreach (var m in sessionMarkers)
            {
                sessionFolder.Placemarks.Add(new KmlPlacemark
                {
                    Name = m.Name,
                    Latitude = m.Lat,
                    Longitude = m.Lon,
                    DescriptionHtml = m.DescriptionHtml ?? $"<p>{WebUtility.HtmlEncode(m.Kind)}</p>",
                    StyleId = m.Kind switch
                    {
                        "hypothesis" => "hypothesis",
                        "locus" or "peak" => "locus",
                        "locality" => "locality",
                        _ => "default"
                    }
                });
            }

            doc.Folders.Add(sessionFolder);
        }

        return doc;
    }

    public static KmlDocument SingleLocality(Locality locality) =>
        new()
        {
            Name = locality.Name,
            Folders =
            {
                new KmlFolder
                {
                    Name = "Rockhounding localities",
                    Placemarks = { FromLocality(locality) }
                }
            }
        };

    public static KmlDocument SingleClaim(MiningClaim claim) =>
        new()
        {
            Name = claim.ClaimName,
            Folders =
            {
                new KmlFolder
                {
                    Name = "Mining claims",
                    Placemarks = { FromClaim(claim) }
                }
            }
        };

    public static KmlDocument SingleFind(PersonalFind find) =>
        new()
        {
            Name = find.Name,
            Folders =
            {
                new KmlFolder
                {
                    Name = "Personal finds",
                    Placemarks = { FromFind(find) }
                }
            }
        };

    public static KmlPlacemark FromLocality(Locality l)
    {
        var c = l.Coordinates!.Value;
        var gems = l.ReportedMinerals.Count > 0
            ? string.Join(", ", l.ReportedMinerals)
            : "—";
        var legal = l.SystemRating is { } r
            ? $"{r.LegalClarity:F1}/10 (overall {r.Overall:F1}/10)"
            : "not rated";
        var outdated = l.HumanActivityDataMayBeOutdated
            ? "<p><b>⚠ Human-activity status may be outdated</b> (USGS vintage data).</p>"
            : "";
        var restricted = l.AccessStatus is AccessStatus.Closed or AccessStatus.Restricted
            ? "<p><b>RESTRICTED / CLOSED</b> — not a collecting recommendation.</p>"
            : "";

        var html = $"""
            <h3>{WebUtility.HtmlEncode(l.Name)}</h3>
            {outdated}{restricted}
            <p><b>State / county:</b> {WebUtility.HtmlEncode(l.StateCode)} · {WebUtility.HtmlEncode(l.County ?? "—")}</p>
            <p><b>Gemstones / minerals:</b> {WebUtility.HtmlEncode(gems)}</p>
            <p><b>Access:</b> {WebUtility.HtmlEncode(l.AccessStatus.ToString())} · <b>Land:</b> {WebUtility.HtmlEncode(l.LandType.ToString())}</p>
            <p><b>Legal clarity / confidence:</b> {WebUtility.HtmlEncode(legal)}</p>
            <p><b>Access notes:</b> {WebUtility.HtmlEncode(l.AccessNotes ?? "—")}</p>
            <p><b>Typical finds:</b> {WebUtility.HtmlEncode(l.TypicalFinds ?? "—")}</p>
            <p><b>Collecting limits:</b> {WebUtility.HtmlEncode(l.CollectingLimits ?? "—")}</p>
            <p><b>Hazards:</b> {WebUtility.HtmlEncode(l.HazardNotes ?? "—")}</p>
            <p><b>Source:</b> {WebUtility.HtmlEncode(l.SourceDataset ?? "—")} {WebUtility.HtmlEncode(l.SourceVintage ?? "")}</p>
            <p><i>Verify land status, claims, and collecting rules before visiting. GeoMineral Trace export.</i></p>
            """;

        return new KmlPlacemark
        {
            Name = $"{l.Name} ({l.StateCode})",
            Latitude = c.LatitudeDegrees,
            Longitude = c.LongitudeDegrees,
            DescriptionHtml = html,
            StyleId = "locality"
        };
    }

    public static KmlPlacemark FromClaim(MiningClaim claim)
    {
        var c = claim.Coordinates!.Value;
        var badge = ClaimStatusClassifier.StatusBadge(claim.Status);
        var explainer = claim.LegalNotes ?? ClaimStatusClassifier.LegalExplainer(claim.Status);
        var style = claim.Status switch
        {
            ClaimStatus.ExpiringSoon => "claim-expiring",
            ClaimStatus.Active => "claim-active",
            _ => "claim-lapsed"
        };

        var html = $"""
            <h3>{WebUtility.HtmlEncode(claim.ClaimName)}</h3>
            <p><b>Status:</b> {WebUtility.HtmlEncode(badge)}{(claim.MaintenanceDeadlineApproaching ? " · Sept 1 fee check" : "")}</p>
            <p><b>Legal clarity:</b></p>
            <p>{WebUtility.HtmlEncode(explainer)}</p>
            <p><b>Serial:</b> {WebUtility.HtmlEncode(claim.SerialNumber)} · <b>Type:</b> {claim.ClaimType}</p>
            <p><b>Claimant of record:</b> {WebUtility.HtmlEncode(claim.ClaimantOfRecord ?? "— (check MLRS)")}</p>
            <p><b>State / office:</b> {WebUtility.HtmlEncode(claim.StateCode)} · {WebUtility.HtmlEncode(claim.FieldOffice ?? "—")}</p>
            <p><b>Last maintenance fee:</b> {claim.LastMaintenanceFeePaid?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—"}</p>
            <p><b>Minerals:</b> {WebUtility.HtmlEncode(claim.Minerals.Count > 0 ? string.Join(", ", claim.Minerals) : "—")}</p>
            <p><b>PLSS:</b> {WebUtility.HtmlEncode(claim.LegalDescription ?? "—")}</p>
            <p><b>Source:</b> {WebUtility.HtmlEncode(claim.SourceDataset)}</p>
            <p><i>Claims are not sold by BLM. Not a marketplace listing. GeoMineral Trace export.</i></p>
            """;

        return new KmlPlacemark
        {
            Name = $"{claim.ClaimName} [{badge}]",
            Latitude = c.LatitudeDegrees,
            Longitude = c.LongitudeDegrees,
            DescriptionHtml = html,
            StyleId = style
        };
    }

    public static KmlPlacemark FromWatercourse(Watercourse river)
    {
        var centroid = river.Centroid ?? RiverStore.ComputeCentroid(river.Vertices);
        var curated = river.Minerals
            .Where(m => m.AssociationKind == MineralAssociationKind.Curated)
            .Select(m => m.MineralName)
            .ToList();
        var inferred = river.Minerals
            .Where(m => m.AssociationKind == MineralAssociationKind.ProximityInferred)
            .Select(m => m.MineralName)
            .Take(10)
            .ToList();

        var html = $"""
            <h3>{WebUtility.HtmlEncode(river.Name)}</h3>
            <p><b>State:</b> {WebUtility.HtmlEncode(river.StateCode)} · <b>Kind:</b> {river.Kind}</p>
            <p><b>Documented minerals:</b> {WebUtility.HtmlEncode(curated.Count > 0 ? string.Join(", ", curated) : "—")}</p>
            <p><b>Nearby reported (inferred):</b> {WebUtility.HtmlEncode(inferred.Count > 0 ? string.Join(", ", inferred) : "—")}</p>
            <p><i>Verify land status and collecting rules. GeoMineral Trace / UnboundRockhound export.</i></p>
            """;

        return new KmlPlacemark
        {
            Name = $"{river.Name} ({river.StateCode})",
            Latitude = centroid?.LatitudeDegrees ?? river.Vertices[0].LatitudeDegrees,
            Longitude = centroid?.LongitudeDegrees ?? river.Vertices[0].LongitudeDegrees,
            LineCoordinates = river.Vertices
                .Select(v => (v.LongitudeDegrees, v.LatitudeDegrees))
                .ToList(),
            DescriptionHtml = html,
            StyleId = "river"
        };
    }

    public static KmlPlacemark FromFind(PersonalFind find)
    {
        var c = find.Coordinates!.Value;
        var minerals = find.Minerals.Count > 0 ? string.Join(", ", find.Minerals) : "—";
        var html = $"""
            <h3>{WebUtility.HtmlEncode(find.Name)}</h3>
            <p><b>Personal find</b> (user field note)</p>
            <p><b>State:</b> {WebUtility.HtmlEncode(find.StateCode ?? "—")}</p>
            <p><b>Minerals:</b> {WebUtility.HtmlEncode(minerals)}</p>
            <p><b>Found on:</b> {find.FoundOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—"}</p>
            <p><b>Linked locality:</b> {WebUtility.HtmlEncode(find.LinkedLocalityName ?? "—")}</p>
            <p><b>Notes:</b> {WebUtility.HtmlEncode(find.Notes ?? "—")}</p>
            """;

        return new KmlPlacemark
        {
            Name = find.Name,
            Latitude = c.LatitudeDegrees,
            Longitude = c.LongitudeDegrees,
            DescriptionHtml = html,
            StyleId = "find"
        };
    }

    public static async Task WriteKmzAsync(
        KmlDocument document,
        string kmzPath,
        CancellationToken cancellationToken = default)
    {
        var kml = KmlWriter.Write(document);
        await using var fs = new FileStream(kmzPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("doc.kml", CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(kml);
        await entryStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }
}
