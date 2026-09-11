# Solar & shadow techniques

GeoMineral Trace treats solar geometry as a **priority physical signal**. Implementation lives in `GeoMineralTrace.Solar`; narrative articles also ship in `knowledge/techniques/` and the in-app Techniques KB.

## Forward solar position

`SolarPositionCalculator` implements a **NOAA Solar Calculator–class** algorithm (Astronomical Algorithms / Meeus lineage):

- Julian day → centuries
- Geometric mean longitude & anomaly
- Equation of center, apparent longitude
- Obliquity, declination, equation of time
- Hour angle → zenith / elevation
- Azimuth clockwise from true north
- Optional atmospheric refraction near the horizon

**Typical accuracy:** well under 0.1° for modern civil dates under clear assumptions. For sub-arcminute or specialized historical work, prefer full **NREL SPA** (Reda & Andreas, *Solar Position Algorithm for Solar Radiation Applications*) as a drop-in upgrade path.

## Shadow → elevation

On level ground, for vertical object height \(h\) and shadow length \(L\):

\[
\alpha = \arctan(h / L)
\]

Optional terrain slope in the shadow direction is subtracted from \(\alpha\). Lengths may be pixels; only the ratio matters.

Forward verification: given a candidate lat/lon and time, recompute elevation/azimuth and compare residuals (`SolarLocusEngine.Verify`).

## Inverse locus

ShadowFinder-style workflow:

1. Measure one or more shadows at known UTC times.
2. Score a geographic grid with a Gaussian residual on elevation (± azimuth).
3. Normalize relative probabilities to the peak cell.
4. Multi-time **trajectory fusion** multiplies per-observation scores (naive independence — documented limitation).

Elevation-only constraints produce **bands/arcs**, not points. Azimuth and multi-frame trajectories tighten the fix.

### Shadow azimuth convention

Measured **shadow azimuth** (direction the shadow points, clockwise from north) implies sun azimuth ≈ shadow + 180°.

## Primary error sources

| Source | Effect |
|--------|--------|
| Wrong UTC / timezone | Shifts entire locus |
| Soft penumbra / uncertain tip | Elevation bias |
| Perspective & lens distortion | Ratio error |
| Non-level ground | Elevation bias |
| Moving camera between frames | Breaks trajectory assumption |
| Refraction near horizon | Elevation bias |
| Coarse grid / clipped bounds | Truncated / quantized map |

## References (starting set)

- Reda, I., Andreas, A. — NREL Solar Position Algorithm (SPA)
- NOAA Global Monitoring Laboratory — Solar Calculator documentation
- Jean Meeus — *Astronomical Algorithms*
- ShadowFinder methodology write-ups (elevation matching / trajectory ideas)
- SunCalc-style azimuth/elevation principles used in web mapping tools

Keep `knowledge/techniques/*.md` updated when formulas or assumptions change.
