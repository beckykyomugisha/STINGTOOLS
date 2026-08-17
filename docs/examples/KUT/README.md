# KUT — Kampala Uganda Temple worked example

This folder holds the **smoke-test source and its generated outputs** for the
Kampala Uganda Temple engagement, plus one credential stub. It is
documentation, not a deployable pack.

## The deployable overlay pack lives elsewhere

> **The project overlay files moved.** `project_config.json`,
> `tag_schemes.json` and `climate_data.json` used to be duplicated here.
> There is now **one** pack:
> **[`project-templates/KUT/_BIM_COORD/`](../../../project-templates/KUT/_BIM_COORD/)**,
> described by its own
> [`manifest.json`](../../../project-templates/KUT/_BIM_COORD/manifest.json).
>
> Deploy by copying that whole `_BIM_COORD/` folder into the temple project
> folder. The deployment sequence is in
> [`project-templates/KUT/README.md` §5](../../../project-templates/KUT/README.md)
> and is the only copy of it — do not restate it here.
>
> The split was the cause of a real defect: the smoke test copied only the
> three files that used to live here, so `owner_standards.json`,
> `lod_matrix.json` and `fohlio_map.json` never reached the project and
> steps that claimed to prove the KUT Owner profile were exercising the
> corporate baseline.

## What is in this folder

| File | What it is |
|---|---|
| `smoke_test.json` | **The source.** Every smoke-test step, machine-readable. Edit this. |
| `REVIT_SMOKE_TEST.md` | **Generated.** `python tools/build_smoke_test.py` |
| `KUT_Revit_Smoke_Test_Checklist.docx` | **Generated.** Same generator — the printable session sheet |
| `fohlio_connection.json.example` | Credential stub for the (optional, stubbed) Fohlio REST tier. Copy to `<project>/_BIM_COORD/fohlio_connection.json` and fill in. The real file is gitignored; the CSV Fohlio path needs no connection file. |

**Do not hand-edit `REVIT_SMOKE_TEST.md` or the `.docx`.** They are outputs.
`tools/check_smoke_test.py` fails CI if the markdown is not a fresh
regeneration of `smoke_test.json`, and it validates every command tag,
panel section, fixture path and parameter name the source declares.

See [`docs/examples/_smoke_test_schema.md`](../_smoke_test_schema.md) for
what a step may declare.

## Background the checklist assumes

The six buildings: **BLD1** Temple · **BLD2** Meetinghouse · **BLD3**
Housing/Ancillary · **BLD4** Grounds · **BLD5** Utility · **BLD6** Guard
House · **EXT** site-wide. `SEQ_INCLUDE_LOC: true` restarts the 4-digit
sequence per building; `SEQ_INCLUDE_ZONE: false` keeps ZONE out of the
sequence key.

> The Owner's (LDS Church Special Projects) own BIM standards arrive in
> week 1 of mobilisation and **supersede** these interim conventions.
> Everything is data-driven exactly so that adopting the Owner's volume
> table / originator code / sequence rules is a field edit, never a code
> change.

### BEP rules that make token detection trustworthy

The Token Confidence Audit (step 8) only pays off if buildings are
detectable. Pick **one** of:

- **Per-building worksets** — name worksets `BLD2_Mechanical`,
  `BLD3_Architecture`, etc. STING's LOC fallback extracts the `BLDn`
  prefix and records `LOC_SOURCE = Workset` (High confidence).
- **One model per building** — set the Project Information LOC on each
  building model so `LOC_SOURCE = ProjectInfo` (Medium confidence; still
  better than a silent default).

Either way, **place rooms before the first coordination publish** — room
boundaries give `LOC_SOURCE = Room` / `ZONE_SOURCE = Room` (High
confidence) and are the strongest signal STING has. Site elements with
no rooms or worksets can use the optional scope-box convention
(`STING-LOC::BLDn`). **Draw STING-LOC scope boxes UNROTATED** — STING
stores each box's axis-aligned plan extents, so a rotated box is treated
as its (larger) axis-aligned envelope. When boxes overlap or nest, the
**smallest** containing box wins, so a campus-wide box plus per-building
boxes resolve each element to its specific building.
