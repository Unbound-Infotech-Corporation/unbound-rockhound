# Mineral & Gem Glossary — sourcing & licensing

Offline reference data lives in `%LocalAppData%\UnboundRockhound\glossary.db`
(same pattern as `localities.db`). Images download into
`%LocalAppData%\UnboundRockhound\glossary-images\` during import — **not**
bundled in the installer (keeps package size small).

## Allowed sources

| Kind | Source | Notes |
|------|--------|--------|
| Canonical species names | IMA-CNMNC list (via Wikidata `P556` / mineral entities) | First pass uses a **priority subset** only |
| Structured facts | Wikidata (CC0) | formula, crystal system, hardness, color, luster |
| Descriptions | **Generated** from structured facts | Never scrape Mindat / Webmineral / similar |
| Images | Wikimedia Commons API | **CC0 / CC-BY / CC-BY-SA / Public Domain only** |

## Required image metadata

Every `mineral_images` row stores `license_type`, `attribution_text`,
`source_url`, and optional `photographer`. The in-app **Glossary Credits**
page lists every attribution (CC-BY / CC-BY-SA compliance).

## Missing images

Many rare species have no usable free image. The UI shows a clean
**“No image available yet”** empty state. Import logs count text-only vs
imaged species in `WORKLOG.md`.

## First-pass scope

Import species that appear in localities/claims/NER lexicons plus common
gemstones — not all ~5,900 IMA species. Expand later with the same importer.
