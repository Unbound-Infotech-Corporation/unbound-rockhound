# GeoMaterial → gem/mineral heuristics

These rules are implemented in `GeoMaterialMineralCatalog` (local C#, no cloud LLM).
They are **transparent lithology hints**, not a deposit model and not permission to collect.

Source vocabulary: GeMS GeoMaterial classes on the USGS Cooperative National Geologic Map
(Earth’s surface / Quaternary / Pre-Quaternary / Precambrian). Identify payload also includes
source-map unit name and synthesis description; keywords match that combined text.

| Rule id | Keywords (examples) | Candidate minerals | Geology-only band |
|---------|---------------------|--------------------|-------------------|
| pegmatite-granitic | pegmatite, granite, aplite | tourmaline, beryl, mica, feldspar, quartz, garnet, spodumene | Plausible |
| volcanic-silica | volcanic, basalt, rhyolite, tuff, obsidian | agate, chalcedony, jasper, opal, obsidian, zeolite | Plausible |
| ultramafic | ultramafic, serpentinite, peridotite | jadeite, nephrite, chromite, magnetite, garnierite | Speculative |
| carbonate-karst | limestone, dolomite, karst, marble | calcite, fluorite, aragonite, gypsum | Plausible |
| placer-alluvium | alluvium, gravel, placer, terrace | gold, garnet, sapphire, magnetite, quartz | Speculative |
| glacial | glacial, till, outwash, moraine | agate, jasper, gold, copper | Speculative |
| metamorphic | schist, gneiss, quartzite, amphibolite | garnet, kyanite, staurolite, mica, quartz | Plausible |
| mafic-intrusive | gabbro, mafic, diabase | magnetite, ilmenite, copper | Speculative |
| evaporite | evaporite, playa, gypsum, borate | gypsum, halite, ulexite, borax | Plausible |
| siliceous-sediment | chert, flint, novaculite | chert, jasper, agate | Plausible |
| sandstone-wood | sandstone, arkose, conglomerate | petrified wood, agate, jasper | Speculative |
| hydrothermal-alteration | hydrothermal, vein, gossan, skarn | quartz, pyrite, copper, turquoise | Speculative |
| greenstone | greenstone, metavolcanic | copper, epidote, pumpellyite | Speculative |
| kimberlite | kimberlite, lamproite | diamond | Speculative |
| coal-organic | coal, lignite, peat | jet, amber | Speculative |
| unconsolidated-eolian | eolian, loess, dune | agate, jasper, garnet | Speculative |

## Ranking (Prospect Guess)

- **Stronger** — geologic keyword match **and** a nearby curated/USGS locality (≤ 15 km) lists the same mineral.
- **Plausible** — strong lithology match without a close locality, or a close curated listing without a geology flag.
- **Speculative** — geology-only weak matches, or farther USGS corridor listings.

Geology alone never produces **Stronger**. Water, ice, artificial ground, and unmapped units produce no mineral hints.

## Honesty

National synthesis polygons are regional. Outcrop, alteration, and placers vary inside a unit.
This is a research aid. It is not a collecting permit, land-status opinion, or guarantee of finds.
