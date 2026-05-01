# STING family library bundle

Pre-loaded ISO 19650 tag families, title blocks, annotation markers,
and schedule templates the plugin loads on first project setup so the
author doesn't start with a blank Revit project.

## Layout

```
family-library/
├── manifest.json         # canonical index — read by FamilyLibraryLoaderCommand
├── tags/                 # 7 tag families (asset, room, door, window, MEP)
├── titleblocks/          # 3 title blocks (A1 / A3 ISO, A1 presentation)
├── annotation/           # 4 markers (section, elevation, callout, level)
└── schedules/            # 3 schedule templates (door, room, window)
```

The actual `.rfa` family files are NOT checked into the repo (binary
files at ~150 KB each — bloats git). They live in the
`Families/PlanscapeStandard-v1.0.0.zip` artefact uploaded to:

  https://cdn.planscape.app/families/PlanscapeStandard-v1.0.0.zip

`FamilyLibraryLoaderCommand` (in `StingTools/Temp/`) downloads + verifies
the SHA-256 + extracts to a per-user `%APPDATA%/Planscape/Families/`
cache the first time an author runs it. Subsequent project loads pull
from the cache instantly.

## Versioning

`manifest.json:version` is bumped whenever a family is added / edited.
The plugin re-downloads the bundle when the cached version drifts. Old
versions stay in the cache so older Revit projects that reference an
older family can still open without a re-link round-trip.

## Updating

1. Author the new family in Revit 2025 (lowest supported version).
2. Drop into `family-library/<category>/`.
3. Add an entry to `manifest.json:categories[].items[]`.
4. Bump `manifest.json:version`.
5. Re-build the zip + upload to CDN.
6. Test in a fresh Revit session.

## ISO 19650 alignment

The 7 tag families map to the 8-segment STING tag format and emit
TAG7 narratives (per `Core/TagConfig.cs`). Title blocks expose the
ISO 19650 metadata fields the `TitleBlockParamApplier` (Phase 113
drawing-template manager) populates declaratively from drawing-type
profiles.
