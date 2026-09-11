---
title: Solar position (NOAA / Meeus-class)
tags: [spa, noaa, solar-position]
---

# Solar position algorithm

GeoMineral Trace uses a NOAA Solar Calculator–class algorithm (Astronomical
Algorithms lineage) for forward solar elevation and azimuth.

For sub-arcminute work or historical edge cases, prefer NREL SPA
(Reda & Andreas, Solar Position Algorithm for Solar Radiation Applications).

## Inputs

- WGS84 latitude / longitude
- UTC timestamp
- Optional refraction correction near horizon

## Outputs

- Elevation (degrees)
- Azimuth (degrees clockwise from true north)
- Declination, equation of time (diagnostics)
