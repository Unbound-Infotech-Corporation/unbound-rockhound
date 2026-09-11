using FluentAssertions;
using GeoMineralTrace.Pipeline.Ner;

namespace GeoMineralTrace.Pipeline.Tests;

public class SourceNerScopingTests
{
    private const string DallmydDescription = """
In this video I team up with friends and search for Amethyst crystals! 
Subscribe if you want to watch more crystal hunting videos like this! http://bit.ly/2LgG7m5

Found Rare Gem While Digging at Diamond Hill Mine! (Unbelievable Find) https://www.youtube.com/watch?v=MhJ_4ekYNjQ
Found Rare Amethyst Crystal While Digging at a Mine! (Unbelievable Find) https://youtu.be/GEzeVbGK2hU
Scuba Diving One of Hawaii's Most Dangerous Cliff Side for Sunken Treasure! (Spitting Caves) https://youtu.be/0dDZ1dLDfyg

My PO Box
DALLMYD
P.O. Box 211
Phenix City, Alabama 36868-0211

#RiverTreasure #Amethyst #TreasureHunting #Crystals 
""";

    [Fact]
    public void Sanitize_StripsRelatedVideoTitles_KeepsOwnProse()
    {
        var cleaned = RemoteDescriptionSanitizer.SanitizeDescription(DallmydDescription);
        cleaned.Should().Contain("Amethyst crystals");
        cleaned.Should().NotContain("Diamond Hill Mine");
        cleaned.Should().NotContain("Hawaii");
        cleaned.Should().NotContain("youtube.com");
        cleaned.Should().NotContain("youtu.be");
        cleaned.Should().NotContain("Phenix City");
    }

    [Fact]
    public void BuildNerCorpus_TwoVideos_ProduceDistinctPlaceLeads()
    {
        var ner = new PlaceAndMineralExtractor();
        var session = Guid.NewGuid();

        var mining = RemoteDescriptionSanitizer.BuildNerCorpus(
            "Found Rare Amethyst Crystal While Digging at a Private Mine!",
            DallmydDescription,
            tags: ["amethyst", "crystal mining"],
            locationLabel: "GEORGIA");

        var cooking = RemoteDescriptionSanitizer.BuildNerCorpus(
            "Easy Homemade Pasta in Rome Kitchen",
            """
            Today I'm making fresh pasta in my small kitchen near the Tiber.
            Subscribe for more recipes! https://youtube.com/watch?v=OTHER123
            Found Rare Amethyst Crystal While Digging at a Private Mine! https://youtu.be/MwAMiddqytE
            """,
            tags: ["pasta", "italian cooking"],
            locationLabel: null);

        var miningItems = ner.ExtractFromText(session, "a.mp4", mining);
        var cookingItems = ner.ExtractFromText(session, "b.mp4", cooking);

        var miningPlaces = miningItems
            .Where(e => e.Type == GeoMineralTrace.Core.Evidence.EvidenceType.PlaceNameMention)
            .Select(e => e.RawContent ?? e.Summary)
            .ToList();
        var cookingPlaces = cookingItems
            .Where(e => e.Type == GeoMineralTrace.Core.Evidence.EvidenceType.PlaceNameMention)
            .Select(e => e.RawContent ?? e.Summary)
            .ToList();

        miningPlaces.Should().Contain(p => p!.Contains("georgia", StringComparison.OrdinalIgnoreCase));
        miningPlaces.Should().NotContain(p => p!.Contains("hawaii", StringComparison.OrdinalIgnoreCase));
        miningPlaces.Should().NotContain(p => p!.Contains("Diamond Hill", StringComparison.OrdinalIgnoreCase));
        miningPlaces.Should().NotContain(p => p!.Contains("Unbelievable Find", StringComparison.OrdinalIgnoreCase));

        cookingPlaces.Should().NotContain(p => p!.Contains("georgia", StringComparison.OrdinalIgnoreCase));
        cookingPlaces.Should().NotContain(p => p!.Contains("Private Mine", StringComparison.OrdinalIgnoreCase));
        cookingPlaces.Should().NotContain(p => p!.Contains("Amethyst", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Jackson Crossroads Mine", true)]
    [InlineData("Maury Mountain", true)]
    [InlineData("Private Mine", true)]
    [InlineData("Unbelievable Find", false)]
    [InlineData("Found Rare Amethyst", false)]
    [InlineData("Navy Bomb Squad", false)]
    public void PlacePhrase_RequiresGeoCue(string phrase, bool expected) =>
        PlaceAndMineralExtractor.IsPlausiblePlacePhrase(phrase).Should().Be(expected);
}
