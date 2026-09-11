using GeoMineralTrace.Core.Claims;
using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Rockhounding;
using GeoMineralTrace.Reporting.Kml;

namespace GeoMineralTrace.Core.Tests;

public class KmlExportTests
{
    [Fact]
    public void Writer_Emits_Folders_And_Coordinates()
    {
        var doc = new KmlDocument { Name = "Test" };
        doc.Folders.Add(new KmlFolder
        {
            Name = "Rockhounding localities",
            Placemarks =
            {
                new KmlPlacemark
                {
                    Name = "Site A",
                    Latitude = 44.1,
                    Longitude = -120.2,
                    DescriptionHtml = "<p>demo</p>",
                    StyleId = "locality"
                }
            }
        });
        doc.Folders.Add(new KmlFolder { Name = "Trails", Visible = false });

        var xml = KmlWriter.Write(doc);
        Assert.Contains("<Folder>", xml);
        Assert.Contains("Rockhounding localities", xml);
        Assert.Contains("-120.2,44.1,0", xml);
        Assert.Contains("<![CDATA[", xml);
        Assert.Contains("Trails", xml);
    }

    [Fact]
    public void CatalogBuilder_Locality_Includes_Legal_Clarity()
    {
        var locality = new Locality
        {
            Id = Guid.NewGuid(),
            Name = "Thunder Egg Beds",
            StateCode = "OR",
            Coordinates = new GeoCoordinate(43.5, -120.5),
            ReportedMinerals = ["Thunder egg", "Agate"],
            AccessStatus = AccessStatus.Open,
            SystemRating = new LocalityRating
            {
                Accessibility = 7,
                Productivity = 6,
                LegalClarity = 8.5,
                Recency = 5,
                BeginnerFriendliness = 7,
                Variety = 6
            }
        };

        var xml = KmlWriter.Write(KmlCatalogBuilder.SingleLocality(locality));
        Assert.Contains("Legal clarity", xml);
        Assert.Contains("8.5/10", xml);
        Assert.Contains("Thunder egg", xml);
    }

    [Fact]
    public void CatalogBuilder_Claim_Includes_Status_Explainer()
    {
        var claim = new MiningClaim
        {
            Id = Guid.NewGuid(),
            ClaimName = "Demo Lode",
            SerialNumber = "NV105999999",
            StateCode = "NV",
            Status = ClaimStatus.ExpiringSoon,
            Coordinates = new GeoCoordinate(39.5, -119.8),
            LegalNotes = "EXPIRING SOON test note"
        };

        var xml = KmlWriter.Write(KmlCatalogBuilder.SingleClaim(claim));
        Assert.Contains("Expiring Soon", xml);
        Assert.Contains("EXPIRING SOON test note", xml);
        Assert.Contains("not sold by BLM", xml, StringComparison.OrdinalIgnoreCase);
    }
}
