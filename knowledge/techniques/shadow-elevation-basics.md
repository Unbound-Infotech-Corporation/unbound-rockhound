---
title: Shadow elevation from height/length ratio
tags: [shadow, elevation, geometry]
---

# Shadow elevation basics

For a vertical object of height `h` casting a shadow of length `L` on level ground:

    α = arctan(h / L)

where α is the solar elevation angle.

## Assumptions

- Object is vertical (plumb).
- Ground is locally flat (or slope is measured and corrected).
- Shadow tip and object base are correctly identified in the image.
- Lengths share the same scale (pixels are fine if both are in pixels).

## Primary error sources

- Perspective / lens distortion
- Soft shadow penumbra (uncertain tip)
- Non-level terrain
- Incorrect time zone / UTC offset on the observation timestamp
