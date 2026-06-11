# KUT — Kampala Uganda Temple project overlay (worked example)

Ready-to-copy project overlay files for the Kampala Uganda Temple
project. The BIM Manager copies these into the live project's
`<project>/_BIM_COORD/` folder. They are **documentation artifacts** —
nothing here ships enabled in the corporate baseline.

> The Owner's (LDS Church Special Projects) own BIM standards arrive in
> week 1 of mobilisation and **supersede** these interim conventions.
> Everything here is data-driven exactly so that adopting the Owner's
> volume table / originator code / sequence rules is a field edit, never
> a code change.

## Files

| File | Copy to | Purpose |
|---|---|---|
| `project_config.json` | `<project>/_BIM_COORD/project_config.json` | Six-building `LOC_CODES` + per-building sequence grouping |
| `tag_schemes.json` | `<project>/_BIM_COORD/tag_schemes.json` | Enables the `kut-temple-example` scheme (LOC → BEP volume code) |

The six buildings: **BLD1** Temple · **BLD2** Meetinghouse · **BLD3**
Housing/Ancillary · **BLD4** Grounds · **BLD5** Utility · **BLD6** Guard
House · **EXT** site-wide. `SEQ_INCLUDE_LOC: true` restarts the 4-digit
sequence per building; `SEQ_INCLUDE_ZONE: false` keeps ZONE out of the
sequence key.

## Setup sequence

1. **Seed Project Information** — on the Project Information element set
   `PRJ_ORG_PROJECT_CODE_TXT = KUT` and `PRJ_ORG_ORIGINATOR_CODE_TXT` to
   the appointed party's originator code (e.g. the modelling
   consultant). These drive the scheme's `projectInfo` segments.
2. **Copy the two overlay files** into `<project>/_BIM_COORD/`.
3. **Bind parameters** — run `LoadSharedParams` (Load Params). Verify
   `ASS_TAG_SCHEME_TXT` and the provenance params
   (`ASS_LOC_SOURCE_TXT` / `ASS_ZONE_SOURCE_TXT` /
   `ASS_SYS_DETECT_LAYER_INT`) bind.
4. **Verify the scheme is live** — run **Scheme Inspect**
   (`TagScheme_Inspect`). The `kut-temple-example` scheme should show
   `●` (enabled) and valid.
5. **Tag the model** as usual (Batch Tag / Tag & Combine). New tags get
   their scheme string from the pipeline automatically.
6. **Back-fill existing tags** — run **Render Scheme** (`TagScheme_Render`)
   once to render the scheme onto already-tagged elements.
7. **Audit confidence** — run **Token Confidence** (`TokenConfidenceAudit`)
   before any coordination publish: it surfaces silent `BLD1` defaults
   (elements that read as "Temple" only because nothing detected their
   building) and per-discipline SYS fallback. Fix those before the gate.
8. **Audit scheme consistency** — run **Scheme Audit**
   (`TagScheme_Audit`) to confirm no stored scheme string has drifted
   from the current tokens.

## BEP rules that make detection trustworthy

The Token Confidence Audit only pays off if buildings are detectable.
Pick **one** of:

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
(`STING-LOC::BLDn`, see the Token Confidence Audit `ScopeBox` band).
