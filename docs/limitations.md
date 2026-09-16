# Limitations

GeoMineral Trace is a **research aid**, not an oracle.

## Geolocation

- Outputs are **ranked hypotheses** with incomplete, noisy, and sometimes contradictory evidence.
- Embedded GPS can be wrong, spoofed, or unrelated to the filmed scene.
- Place names in audio/OCR may refer to remote topics, brands, or media—not the filming site.
- Solar elevation-only loci are **geographic bands**; treating the peak cell as “the location” is incorrect without corroboration.
- Video perspective, rolling shutter, and lens distortion bias shadow ratios.
- Incorrect timestamps/time zones invalidate solar loci entirely.

## Rockhounding database

- Seed data is incomplete and may be outdated the day after import.
- “Open” status can change without notice (fires, claims, seasonal closures).
- Mineral lists do not imply legal collecting rights.
- Ratings are transparent but still subjective summaries of incomplete information.

## Software gaps / upgrade hooks

- Bitmap keyframes require **ffmpeg** on PATH (otherwise a sampling plan is written).
- OCR uses **Windows.Media.Ocr** with sidecar `.ocr.txt` fallback.
- ASR uses sidecar `.srt`/`.vtt`/`.txt`, optional **Whisper CLI** when installed, or `media.whisper.srt`.
- Scene tags are heuristic keywords (+ `.tags.txt`) until an ONNX/vision adapter is registered.
- Map markers work offline; **OSM tiles** require Online Enrichment (Settings) and network. Leaflet JS still loads from CDN when the WebView can reach the network. Do **not** use `tile.openstreetmap.org` (WebView gets “Access blocked”).
- Cooperative National Geologic Map overlays and tap-identify are **opt-in Online Enrichment**. The product is a national synthesis: unit boundaries and GeoMaterial classes are regional, not a collecting map. Prospect Guess bands are honest uncertainty labels, not occurrence proof.
- Place-name gazetteer is an expanded offline seed table — treat place hypotheses as leads, not proof.

## Operator duty

Verify land ownership, claims, permits, and safety **before** any field visit. When uncertain, do not go.