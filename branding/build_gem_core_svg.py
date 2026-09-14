#!/usr/bin/env python3
"""Write branding/gem-core.svg from a watertight Gem Core mesh."""

from __future__ import annotations

from pathlib import Path

OUT = Path(__file__).resolve().parent / "gem-core.svg"

# Unbound cyan family (highlight -> deep). No off-hue colors.
CYAN = {
    "u": "#00FFFF",
    "ice": "#C8FFFF",
    "hi": "#8AFFFF",
    "light": "#4AFFFF",
    "mid": "#00E8F0",
    "base": "#00C4CC",
    "shade": "#009AA2",
    "deep": "#007880",
    "abyss": "#005C64",
}


def fmt_pts(points: list[tuple[float, float]]) -> str:
    return " ".join(f"{x:.1f},{y:.1f}" for x, y in points)


def polygon(points: list[tuple[float, float]], fill: str) -> str:
    return f'    <polygon fill="{fill}" points="{fmt_pts(points)}"/>'


def main() -> None:
    # Three-bar U -------------------------------------------------------
    # Left / right uprights: 45-degree inner tops (outer corner is high).
    # Bottom rail meets the uprights so the silhouette is a single U.
    left = "M 196 240 L 328 372 L 328 820 L 196 820 Z"
    right = "M 828 240 L 828 820 L 696 820 L 696 372 Z"
    rail = '<rect x="196" y="692" width="632" height="128"/>'

    # Gem: hexagon silhouette + inner table. Front-view brilliant. ------
    # Sized to fill the U bowl the way the locked Gem Core mark does.
    o = [
        (512.0, 286.0),  # 0 top (in the U opening)
        (628.0, 360.0),  # 1 upper right
        (646.0, 458.0),  # 2 lower right
        (512.0, 598.0),  # 3 bottom (clear of the rail)
        (378.0, 458.0),  # 4 lower left
        (396.0, 360.0),  # 5 upper left
    ]
    cx, cy = 512.0, 430.0
    inner_scale = 0.34
    inner = [((x - cx) * inner_scale + cx, (y - cy) * inner_scale + cy) for x, y in o]
    mid_scale = 0.66
    mid = [((x - cx) * mid_scale + cx, (y - cy) * mid_scale + cy) for x, y in o]

    # Facet colors around the ring: light comes from upper-right.
    ring_colors = [
        ("ice", "hi"),  # top -> UR
        ("hi", "light"),  # UR -> LR
        ("base", "shade"),  # LR -> bottom
        ("deep", "abyss"),  # bottom -> LL
        ("shade", "base"),  # LL -> UL
        ("light", "mid"),  # UL -> top
    ]
    mid_colors = ["hi", "light", "base", "shade", "mid", "light"]
    table_colors = ["ice", "hi", "mid", "base", "shade", "light"]

    facets: list[str] = []

    # Outer ring (outer hex -> mid hex)
    for i, (c_a, c_b) in enumerate(ring_colors):
        j = (i + 1) % 6
        facets.append(polygon([o[i], o[j], mid[j]], CYAN[c_a]))
        facets.append(polygon([o[i], mid[j], mid[i]], CYAN[c_b]))

    # Mid ring (mid hex -> inner table)
    for i, name in enumerate(mid_colors):
        j = (i + 1) % 6
        facets.append(polygon([mid[i], mid[j], inner[j]], CYAN[name]))
        facets.append(polygon([mid[i], inner[j], inner[i]], CYAN["mid" if i % 2 else "base"]))

    # Table (inner hex fan)
    table_c = (cx, cy)
    for i, name in enumerate(table_colors):
        j = (i + 1) % 6
        facets.append(polygon([inner[i], inner[j], table_c], CYAN[name]))

    svg = f"""<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024" role="img" aria-label="Unbound Rockhound Gem Core">
  <title>Unbound Rockhound Gem Core</title>
  <desc>Unbound Infotech three-bar cyan U with a faceted crystal gem in the bowl. Cyan #00FFFF on black.</desc>

  <g id="u-bars" fill="{CYAN["u"]}">
    <path d="{left}"/>
    <path d="{right}"/>
    {rail}
  </g>

  <g id="gem" stroke="none">
{chr(10).join(facets)}
  </g>
</svg>
"""
    OUT.write_text(svg, encoding="utf-8")
    print(f"wrote {OUT}")


if __name__ == "__main__":
    main()
