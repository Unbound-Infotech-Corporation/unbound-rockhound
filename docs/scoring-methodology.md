# Scoring methodology

## Locality system rating

Factors are scored **0–10**. Overall is a weighted mean:

| Factor | Weight | Intent |
|--------|--------|--------|
| Legal clarity | 0.22 | Closed/illegal sites must not rank highly |
| Accessibility | 0.18 | Road/trail effort, clarity of access |
| Productivity | 0.18 | Typical find quality / frequency reports |
| Recency | 0.12 | Freshness of verification |
| Beginner-friendliness | 0.12 | Difficulty + open access |
| Variety | 0.10 | Diversity of reported minerals |
| Safety | 0.08 | Hazards; defaults to 5 if unknown |

Implementation: `LocalityRating.ComputeOverall` and `LocalityRatingCalculator`.

### Access → legal clarity defaults

| AccessStatus | Legal clarity |
|--------------|---------------|
| Open | 9.0 |
| PermitRequired | 7.0 |
| Seasonal | 6.5 |
| Restricted | 3.0 |
| Closed | 0.5 |
| Unknown | 4.0 |

## Prospect Guess (map tap)

`ProspectGuessScorer` ranks gem/mineral **research hints** from:

1. CNGM GeoMaterial / unit keywords (`GeoMaterialMineralCatalog` — local rules, no LLM)
2. Nearby curated and USGS MRDS localities (default 25 km corridor)

| Band | Meaning |
|------|---------|
| Stronger | Lithology keyword **and** a nearby record (≤ 15 km) lists the same mineral |
| Plausible | Strong lithology match without a close listing, or a close curated listing without a geology flag |
| Speculative | Weak geology-only match, or farther USGS corridor listings |

Geology alone never yields **Stronger**. Water / ice / artificial / unmapped units yield no mineral hints.

This is **not** a legal-access or collecting-permit score. Legal clarity remains the locality-rating factor above.

## Evidence confidence

`Confidence` is clamped to \([0,1]\). User dispositions adjust fusion weight:

| Disposition | Multiplier |
|-------------|------------|
| Rejected | 0 |
| Downweighted | 0.5 × confidence |
| Neutral | 1.0 × confidence |
| Upweighted | 1.5 × confidence |
| Accepted | 2.0 × confidence |

## Hypothesis ranking

Early fusion (`HypothesisFusionEngine`) ranks by confidence descending and attaches supporting evidence IDs plus uncertainty notes. Later milestones will add calibrated score fusion across gazetteer matches, solar probability mass, and mineral co-occurrence.
