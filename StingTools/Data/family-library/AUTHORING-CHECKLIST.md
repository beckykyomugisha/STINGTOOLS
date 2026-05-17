# Family library — authoring checklist

> The 17 entries in `manifest.json` need real `.rfa` files. This is the
> walkthrough for the family author. Goal: a `PlanscapeStandard-v1.0.0.zip`
> that drops on `cdn.planscape.app/families/` and the `FamilyLibraryLoaderCommand`
> picks up automatically.

## What ships

```
PlanscapeStandard-v1.0.0/
├── manifest.json                  ← copied verbatim from this repo
├── tags/                          ← 7 tag families
│   ├── STING-Asset-Tag-Uniform-EN.rfa
│   ├── STING-Asset-Tag-Compact-EN.rfa
│   ├── STING-Asset-Tag-Narrative.rfa
│   ├── STING-Room-Tag.rfa
│   ├── STING-Door-Tag.rfa
│   ├── STING-Window-Tag.rfa
│   └── STING-MEP-Equipment-Tag.rfa
├── titleblocks/                   ← 3 title blocks
│   ├── STING-A1-ISO19650.rfa
│   ├── STING-A3-ISO19650.rfa
│   └── STING-A1-Presentation.rfa
├── annotation/                    ← 4 markers
│   ├── STING-Section-Marker-ISO.rfa
│   ├── STING-Elevation-Marker-ISO.rfa
│   ├── STING-Callout-Marker-ISO.rfa
│   └── STING-Level-Head-ISO.rfa
└── schedules/                     ← 3 schedule templates
    ├── STING-Door-Schedule.rfa
    ├── STING-Room-Schedule.rfa
    └── STING-Window-Schedule.rfa
```

## Authoring rules (apply to every family)

| Rule | Why |
|---|---|
| Author in **Revit 2025**, save with **save-as** down-revisioning OFF | 2025 is the lowest version we support; saving down to 2024 would block Revit 2027 from loading parameter additions |
| Family **category** must match the manifest entry (e.g. `Generic Annotations` for tags) | Wrong category prevents `Document.LoadFamily` |
| Use the **STING shared parameter file** (`Data/MR_PARAMETERS.txt`) for every parameter | Parameters bound by SharedParameter GUID survive any rename + are visible to `ParamRegistry` |
| **No nested families** with paid fonts / textures | Bundle stays free to redistribute |
| **No project-specific Mark / SEQ values** baked in | These are written at instance-placement time |
| File size **≤ 250 KB** per .rfa | Bundle stays under 5 MB total — fits one CDN cache hit on a 3G phone |

## Per-family acceptance

### Tag families (7)

Each tag family must include label rows bound to:

- `ASS_TAG_1_TXT` — full 8-segment ISO 19650 tag
- `ASS_TAG_7_TXT` — narrative
- The 6 paragraph containers `ASS_TAG_7A..F_TXT` (per `Core/TagConfig.cs`)
- `STING_TAG_DISCIPLINE_TXT` for the discipline filter

128 size+style+color combinations defined in `STING_TAG_CONFIG_v5_0_*.csv` —
the `TagStyleEngine` switches between them via `TAG_{SIZE}{STYLE}_{COLOR}_BOOL`
parameters. Tag family must have one label row per combination it supports
(or wildcard rows that read the active BOOL).

### Title blocks (3)

Bind these instance parameters (via SharedParameter GUIDs) so
`TitleBlockParamApplier` from the Drawing Template Manager (Phase 113) can
populate them declaratively:

- `PRJ_PROJECT_COD_TXT`
- `PRJ_ORG_CLIENT_NAME`
- `PRJ_ORG_LEAD_APPOINTED_PARTY`
- `STING_DRAWING_TYPE_ID_TXT` (for browser-organiser routing)
- Plus the 6 ISO 19650 metadata cells: revision, suitability, sheet number,
  sheet name, scale, drawn-by

A1 / A3 paper sizes set in **Family Types**; presentation variant adds an
ISO-9660-friendly bleed area for client-facing prints.

### Annotation markers (4)

Section / elevation / callout markers bind to `STING_VIEW_TAG_STYLE_TXT`
so the `ViewStylePackApplier` can switch styling per drawing type.

### Schedule templates (3)

Schedule families source fields from the bound parameters. Each
schedule's Field Manager order matters — that's what `ScheduleHelper`
reads.

## Build steps

```bash
# 1. Author + verify each family in Revit 2025; save to disk.
# 2. Copy to staging directory.
mkdir -p PlanscapeStandard-v1.0.0/{tags,titleblocks,annotation,schedules}
# … move .rfa files into the right subdir …

# 3. Copy the manifest verbatim.
cp StingTools/Data/family-library/manifest.json PlanscapeStandard-v1.0.0/

# 4. Sanity check: every entry resolves to a real file.
python3 - <<'PY'
import json, os
m = json.load(open("PlanscapeStandard-v1.0.0/manifest.json"))
missing = []
for cat in m["categories"]:
    for it in cat["items"]:
        p = os.path.join("PlanscapeStandard-v1.0.0", it["file"])
        if not os.path.exists(p):
            missing.append(it["file"])
print("MISSING:" if missing else "OK")
for f in missing: print("  -", f)
PY

# 5. Compute SHA-256 for the plugin's verification step.
cd PlanscapeStandard-v1.0.0
zip -r ../PlanscapeStandard-v1.0.0.zip .
cd ..
sha256sum PlanscapeStandard-v1.0.0.zip > PlanscapeStandard-v1.0.0.sha256

# 6. Upload.
aws s3 cp PlanscapeStandard-v1.0.0.zip      s3://cdn-planscape/families/
aws s3 cp PlanscapeStandard-v1.0.0.sha256   s3://cdn-planscape/families/
```

## Smoke test

1. In a clean Revit 2025 install, run `Tags tab → BIM → Load Family Library`.
2. Confirm the toast reports `loaded: 17 · skipped: 0 · failed: 0`.
3. Confirm `%APPDATA%/Planscape/Families/v1.0.0/` exists with all four
   subdirs.
4. Confirm a fresh sheet picks up the A1 ISO 19650 title block from the
   Type Selector dropdown.
5. Run `Tags tab → Auto Tag` on a wall — ASS_TAG_7 narrative renders with
   the sub-section paragraph styling.

## Versioning

- Patch (`v1.0.1`): single-family fix without parameter changes. Bump
  `manifest.json:version` to `1.0.1`, replace the affected .rfa, re-zip,
  re-upload alongside the v1.0.0 zip (don't delete v1.0.0 — older Revit
  projects may still reference families from it).
- Minor (`v1.1.0`): new family added or non-breaking parameter add.
  Authors get a `LoadFamilyLibrary` nag on next plugin start.
- Major (`v2.0.0`): breaking parameter rename / category change. Coordinate
  with the parameter-registry maintainer (`ParamRegistry.cs`) so old +
  new live side by side until projects migrate.

## Cost-of-change

The whole bundle ships in one zip. There is no migration path for
**individual** families — if a tag family changes shape, every project
referencing it must re-link. That's why parameter additions go through
SharedParameter GUIDs (which survive renames) rather than family
parameters (which don't).
