namespace GeoMineralTrace.Core.Geology;

/// <summary>
/// Local, reviewable GeoMaterial / lithology → gem/mineral heuristics.
/// Not a deposit model and never a claim of occurrence.
/// </summary>
public static class GeoMaterialMineralCatalog
{
    public static IReadOnlyList<GeoMaterialAssociationRule> Rules { get; } =
    [
        new(
            "pegmatite-granitic",
            ["pegmatite", "granitic", "granite", "aplite", "alaskite", "leucogranite"],
            ["tourmaline", "beryl", "mica", "feldspar", "quartz", "garnet", "spodumene"],
            ProspectGuessBand.Plausible,
            "Granitic / pegmatitic hosts are a classic setting for tourmaline, beryl, mica, and related pegmatite minerals."),
        new(
            "volcanic-silica",
            ["volcanic", "lava", "basalt", "rhyolite", "andesite", "dacite", "tuff", "pyroclastic", "tephra", "extrusive", "obsidian"],
            ["agate", "chalcedony", "jasper", "opal", "obsidian", "zeolite"],
            ProspectGuessBand.Plausible,
            "Volcanic and volcaniclastic rocks often host silica (agate/chalcedony/opal) and volcanic glass."),
        new(
            "ultramafic",
            ["ultramafic", "serpentinite", "peridotite", "dunite", "harzburgite", "pyroxenite"],
            ["jadeite", "nephrite", "chromite", "magnetite", "garnierite"],
            ProspectGuessBand.Speculative,
            "Ultramafic / serpentinized rocks are the usual geologic neighborhood for jade-family minerals and chromite — still rare at any given outcrop."),
        new(
            "carbonate-karst",
            ["limestone", "dolomite", "carbonate", "karst", "marble", "calcareous"],
            ["calcite", "fluorite", "aragonite", "gypsum"],
            ProspectGuessBand.Plausible,
            "Carbonate and karst settings commonly yield calcite (and sometimes fluorite or evaporite sulfate) rather than a guaranteed gem pocket."),
        new(
            "placer-alluvium",
            ["alluvium", "alluvial", "gravel", "placer", "terrace", "fluvial", "stream sediment", "floodplain"],
            ["gold", "garnet", "sapphire", "magnetite", "quartz"],
            ProspectGuessBand.Speculative,
            "Placer-friendly alluvium can concentrate dense minerals where a regional source exists — the unit alone does not prove a pay streak."),
        new(
            "glacial",
            ["glacial", "till", "outwash", "drift", "ice-contact", "moraine"],
            ["agate", "jasper", "gold", "copper"],
            ProspectGuessBand.Speculative,
            "Glacial deposits may carry exotic cobbles and heavy minerals far from bedrock source."),
        new(
            "metamorphic",
            ["schist", "gneiss", "quartzite", "phyllite", "slate", "metamorphic", "meta-", "amphibolite"],
            ["garnet", "kyanite", "staurolite", "mica", "quartz"],
            ProspectGuessBand.Plausible,
            "Pelitic and quartzo-feldspathic metamorphic rocks are a common home for garnet and other index minerals."),
        new(
            "mafic-intrusive",
            ["gabbro", "mafic", "norite", "diabase", "dolerite"],
            ["magnetite", "ilmenite", "copper"],
            ProspectGuessBand.Speculative,
            "Mafic intrusive rocks can host Fe-Ti oxides and, locally, copper minerals."),
        new(
            "evaporite",
            ["evaporite", "playa", "gypsum", "halite", "borate", "alkali"],
            ["gypsum", "halite", "ulexite", "borax"],
            ProspectGuessBand.Plausible,
            "Evaporite and playa settings are the ordinary geologic home of gypsum, salts, and borates."),
        new(
            "siliceous-sediment",
            ["chert", "flint", "novaculite", "siliceous"],
            ["chert", "jasper", "agate"],
            ProspectGuessBand.Plausible,
            "Siliceous sedimentary units are themselves the material (chert/flint) and may include jasper/agate."),
        new(
            "sandstone-wood",
            ["sandstone", "arkose", "conglomerate"],
            ["petrified wood", "agate", "jasper"],
            ProspectGuessBand.Speculative,
            "Coarse clastic units sometimes host silicified wood or silica in voids — occurrence is local, not automatic."),
        new(
            "hydrothermal-alteration",
            ["hydrothermal", "alteration", "vein", "gossan", "skarn"],
            ["quartz", "pyrite", "copper", "turquoise"],
            ProspectGuessBand.Speculative,
            "Hydrothermal alteration and veins can host quartz and ore minerals; most altered ground is barren at a collecting scale."),
        new(
            "greenstone",
            ["greenstone", "metavolcanic", "greenschist"],
            ["copper", "epidote", "pumpellyite"],
            ProspectGuessBand.Speculative,
            "Greenstone / metavolcanic belts are a regional copper and epidote neighborhood, not a site guarantee."),
        new(
            "kimberlite",
            ["kimberlite", "lamproite"],
            ["diamond"],
            ProspectGuessBand.Speculative,
            "Kimberlite or lamproite is the host clan for diamond — economically and recreationally still exceptional."),
        new(
            "coal-organic",
            ["coal", "lignite", "peat"],
            ["jet", "amber"],
            ProspectGuessBand.Speculative,
            "Organic-rich sedimentary units are the usual setting for jet or amber, which remain uncommon finds."),
        new(
            "unconsolidated-eolian",
            ["eolian", "loess", "dune"],
            ["agate", "jasper", "garnet"],
            ProspectGuessBand.Speculative,
            "Windblown or loose sand may concentrate durable silica and garnet grains where a source exists.")
    ];

    public static IReadOnlyList<GeoMaterialAssociationRule> Match(string? searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
            return [];

        var hits = new List<GeoMaterialAssociationRule>();
        foreach (var rule in Rules)
        {
            if (rule.MatchKeywords.Any(k => searchText.Contains(k, StringComparison.OrdinalIgnoreCase)))
                hits.Add(rule);
        }

        return hits;
    }
}
