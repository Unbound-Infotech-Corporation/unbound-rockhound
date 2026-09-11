using FluentAssertions;
using GeoMineralTrace.Core.Rockhounding;
using GeoMineralTrace.Rockhounding.Glossary;
using GeoMineralTrace.Rockhounding.Import;
using GeoMineralTrace.Rockhounding.Storage;
using Microsoft.Data.Sqlite;

namespace GeoMineralTrace.Rockhounding.Tests;

public class MineralGlossaryStoreTests
{
    [Fact]
    public async Task SeedImport_RequiredFieldsPresent()
    {
        await using var ctx = await TempStoreAsync();
        var importer = new MineralGlossaryImporter();
        var n = await importer.ImportFromSeedAsync(ctx.Store);
        n.Should().BeGreaterThan(40);

        var quartz = await ctx.Store.GetByIdOrNameAsync("quartz");
        quartz.Should().NotBeNull();
        quartz!.Name.Should().Be("Quartz");
        quartz.Formula.Should().NotBeNullOrWhiteSpace();
        quartz.Description.Should().NotBeNullOrWhiteSpace();
        quartz.CrystalSystem.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Search_ByNameCrystalMohs()
    {
        await using var ctx = await TempStoreAsync();
        await new MineralGlossaryImporter().ImportFromSeedAsync(ctx.Store);

        var byName = await ctx.Store.SearchAsync(new MineralSpeciesFilter { NameQuery = "amethyst" });
        byName.Should().Contain(s => s.Name.Equals("Amethyst", StringComparison.OrdinalIgnoreCase));

        var trigonal = await ctx.Store.SearchAsync(new MineralSpeciesFilter { CrystalSystem = "Trigonal" });
        trigonal.Should().NotBeEmpty();
        trigonal.Should().OnlyContain(s =>
            s.CrystalSystem != null
            && s.CrystalSystem.Contains("Trigonal", StringComparison.OrdinalIgnoreCase));

        var hard = await ctx.Store.SearchAsync(new MineralSpeciesFilter { MohsMin = 9 });
        hard.Select(s => s.Name).Should().Contain(new[] { "Diamond", "Corundum", "Ruby", "Sapphire" });
    }

    [Fact]
    public async Task ImageRow_RequiresLicenseAndAttribution()
    {
        await using var ctx = await TempStoreAsync();
        await new MineralGlossaryImporter().ImportFromSeedAsync(ctx.Store);

        var species = await ctx.Store.GetByIdOrNameAsync("quartz");
        species.Should().NotBeNull();

        await ctx.Store.ReplaceImagesAsync(species!.Id,
        [
            new MineralImage
            {
                Id = "quartz-01",
                SpeciesId = species.Id,
                FilePath = null,
                SourceUrl = "https://commons.wikimedia.org/wiki/File:example.jpg",
                LicenseType = "CC-BY",
                AttributionText = "Example photographer; CC-BY; commons",
                Photographer = "Example photographer",
                SortOrder = 0
            }
        ]);

        var images = await ctx.Store.ListImagesAsync(species.Id);
        images.Should().ContainSingle();
        images[0].LicenseType.Should().Be("CC-BY");
        images[0].AttributionText.Should().NotBeNullOrWhiteSpace();

        var attrs = await ctx.Store.ListAllAttributionsAsync();
        attrs.Should().Contain(a => a.SpeciesId == species.Id && a.LicenseType == "CC-BY");
    }

    [Fact]
    public void DescriptionBuilder_ContainsFormulaWhenProvided()
    {
        var text = MineralDescriptionBuilder.Build(
            "Quartz", "SiO₂", "Trigonal", 7, 7, "Colorless", "Vitreous", "IMA approved", null);
        text.Should().Contain("SiO₂");
        text.Should().Contain("Quartz");
        text.Should().Contain("Trigonal");
    }

    [Fact]
    public void SeedIntegrity_EverySpeciesHasNameAndDescription()
    {
        PriorityGlossarySeed.All.Should().NotBeEmpty();
        foreach (var s in PriorityGlossarySeed.All)
        {
            s.Name.Should().NotBeNullOrWhiteSpace();
            s.Description.Should().NotBeNullOrWhiteSpace();
            s.Id.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void NormalizeLicense_MapsAllowedTypes()
    {
        MineralGlossaryImporter.NormalizeLicense("CC0", null).Should().Be("CC0");
        MineralGlossaryImporter.NormalizeLicense("CC BY 4.0", "https://creativecommons.org/licenses/by/4.0/")
            .Should().Be("CC-BY");
        MineralGlossaryImporter.NormalizeLicense("CC BY-SA 3.0", null).Should().Be("CC-BY-SA");
        MineralGlossaryImporter.NormalizeLicense("Public domain", null).Should().Be("Public Domain");
        MineralGlossaryImporter.NormalizeLicense("All rights reserved", null).Should().BeNull();
    }

    private static async Task<TempCtx> TempStoreAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"gmt-glossary-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var db = Path.Combine(dir, "glossary.db");
        var images = Path.Combine(dir, "images");
        var store = new MineralGlossaryStore(db, images);
        await store.InitializeAsync();
        return new TempCtx(store, dir);
    }

    private sealed class TempCtx(MineralGlossaryStore store, string dir) : IAsyncDisposable
    {
        public MineralGlossaryStore Store { get; } = store;

        public async ValueTask DisposeAsync()
        {
            await Store.DisposeAsync();
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // best-effort on Windows file locks
            }
        }
    }
}
