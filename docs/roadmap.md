# Implementation roadmap

Phased delivery so each stage is usable without waiting for the full vision.

## Phase 1 — Foundation (complete)

- Solution modularity + domain models
- Solar position, shadow geometry, inverse locus + tests
- Techniques knowledge base
- Evidence Board shell
- Early hypothesis fusion
- Offline rockhounding SQLite + ratings + legal flags
- WinUI research shell (nav, theme, disclaimers)

## Phase 2 — Evidence extraction (complete)

1. Video ingest (single + batch) with progress/cancel — `AnalysisPipeline` + Analyze page
2. Metadata / EXIF / GPS extractors → Evidence Board — `MediaMetadataExtractor`
3. Keyframe extraction (ffmpeg scene/interval; plan fallback) — `FfmpegKeyframeExtractor`
4. OCR module (pluggable; Windows.Media.Ocr + sidecar) — `CompositeOcrEngine`
5. Persist analysis sessions to disk — `AnalysisSessionStore`

## Phase 3 — Audio & vision cues (complete)

1. Offline ASR via sidecar `.srt`/`.vtt`/`.txt` + optional Whisper CLI — `CompositeTranscriptionEngine`
2. Scene tags (expanded heuristic + `.tags.txt` sidecar; model-ready interface) — `HeuristicSceneTagger`
3. Cross-link mineral mentions ↔ locality DB — pipeline nearby hits

## Phase 4 — Solar UX & fusion hardening (complete)

1. Interactive shadow measurement on keyframes + trajectory list
2. Multi-frame trajectory locus
3. Map visualization (Leaflet / WebView2) with Online Enrichment gate
4. Evidence accept/reject/re-weight wired into fusion weights + session persist
5. Nearby localities panel on Hypotheses page + map markers

## Phase 5 — Knowledge corpus & reporting (complete)

1. CSV + USGS MRDS importers for attributed seeds
2. User ratings and notes — `UserRatingService`
3. Exportable research report (Markdown + JSON) — `ResearchReportExporter`
4. Structured logging via `Microsoft.Extensions.Logging`

## Phase 6 — Polish & upgrade hooks (current)

1. Analyze `MediaPlayerElement` + scrubbable timeline + decorative waveform
2. Expanded offline place gazetteer (~150+ entries)
3. Settings engine status panel (OCR/ASR/ffmpeg/yt-dlp/whisper)
4. Home quick-start actions + enter motion

## Remaining upgrade hooks

- Bundled Whisper / GGML weights (optional; CLI/sidecar path exists)
- ONNX vision landmark/vegetation adapters
- Fully offline Leaflet + MBTiles packs (markers work offline when Online Enrichment is off)
- Full NREL SPA drop-in (current solar math is production-usable)

Each phase keeps limitations documentation current.