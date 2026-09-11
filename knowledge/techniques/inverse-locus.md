---
title: Inverse solar locus / probability map
tags: [locus, shadowfinder, trajectory]
---

# Inverse locus methodology

Inspired by ShadowFinder and related forensic solar-matching work:

1. Measure height/shadow → elevation α (± azimuth if available).
2. For each grid cell, compute solar position at the observation UTC.
3. Score cells by Gaussian residual on elevation (and azimuth).
4. Normalize to a relative probability map.
5. With multiple times at a fixed site, multiply scores (trajectory fusion).

## Limitations

- Elevation-only yields arcs/bands, not points.
- Grid bounds and step size truncate and quantize the true locus.
- Correlated measurement errors inflate joint confidence.
