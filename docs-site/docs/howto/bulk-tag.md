# Bulk-tag elements

When you bring an existing Revit model into Planscape, the elements have no ISO 19650 tags. This guide walks you through the five-step bulk-tag workflow that takes a model from zero to fully-tagged in a few minutes.

!!! tip "When to use this"
    Use bulk tagging on a fresh project, after a major model import, or after migrating from a non-tagged BIM workflow. For day-to-day work the auto-tagger handles new elements as you place them — see the **STING → BIM → Auto-Tagger** toggle.

## Step 1 — Load shared parameters

The 8 ISO 19650 tokens live in shared parameters that need to be bound to the project before anything else.

1. Open the `.rvt` you want to tag.
2. **STING Tools → TEMP → Load Params**.
3. The plugin runs a 2-pass binding: pass 1 binds the universal parameters (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD/SEQ + ASS_TAG_1 through ASS_TAG_6 + TAG7 narrative parameters); pass 2 binds discipline-specific containers (HVC_*, ELC_*, PLM_*, FLS_*, COM_*, MAT_*).

Confirm with the report that all 36 tag containers and the 7 source tokens are bound to the right categories. Re-running this command is a no-op if everything's already bound.

## Step 2 — Master Setup

**TEMP → Master Setup** runs a 15-step pipeline that creates the supporting infrastructure:

- 28 view filters keyed off DISC/SYS for discipline-coloured views
- 35 worksets named per ISO 19650 conventions
- 23 view templates for plans, RCPs, sections, elevations
- 10 line patterns matching ISO line styles
- 6 phases (Existing, New, Demolition, …) wired to STATUS auto-detection

This step is idempotent — re-running it skips anything that already exists. Expect 30–60 seconds on a typical project.

## Step 3 — Auto-populate tokens

This is where the magic happens. **TAGS → Family-Stage Populate** walks every taggable element in the project and derives all 7 tokens (DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, plus auto-detected STATUS) from the Revit context:

- Element category → DISC + SYS + PROD lookup
- Hosted room name/department → LOC + ZONE
- Associated level → LVL
- Connected MEP system → SYS + FUNC
- Family name → PROD code (35+ specific codes; falls back to category default)
- Element phase → STATUS

For a 30k-element project this takes 5–15 seconds. It does not yet assemble the 8-segment tag string — that's step 4.

## Step 4 — Batch tag

**TAGS → Batch Tag** assembles the 8-segment tag for every populated element, assigns SEQ numbers (incrementing the highest existing SEQ within each `(DISC, SYS, LVL)` group), and writes to all 36 tag containers.

A dialog asks how to handle collisions (when two elements would get the same tag):

- **Skip** — leave the second element untagged for now (review later)
- **Overwrite** — the second element gets the tag, the first loses it (rarely what you want)
- **Auto-increment** — assign the next free SEQ to the second element (recommended)

Pick auto-increment. Click OK. Watch the progress bar.

## Step 5 — Validate

**TAGS → Validate** produces a completeness report. The headline is per-discipline compliance percentage (Red < 50%, Amber 50–80%, Green > 80%). Below that, a breakdown by token: how many elements are missing LOC, missing SYS, missing PROD, etc.

Common issues:

| Problem | Fix |
|---|---|
| Missing LOC | The elements aren't inside a Room. Place rooms in the architectural model. Re-run **Family-Stage Populate**. |
| Missing SYS | MEP elements aren't connected to a system. In Revit, select the elements, run **Connect All** in the systems browser, re-run populate. |
| Duplicate SEQ | Two elements ended up with the same SEQ — usually because they were placed before tagging and given the same SEQ manually. Run **ORGANISE → Fix Duplicates** to auto-resolve. |
| Wrong DISC for category | A doors element with `DISC=M`. Run **TAGS → Re-Tag** on the offending elements to force re-derivation. |

After fixing, re-run **Batch Tag** in Skip mode (it skips already-complete elements, only re-tags the ones you fixed).

## Day-2 maintenance

Once your model is fully tagged, the **Auto-Tagger** IUpdater (toggle in the dock panel) tags new elements as you place them. The **Stale Marker** flags elements that have moved levels or rooms — run **TAGS → Re-Tag Stale** weekly to keep the model clean.

For elements that change category (rare but happens — e.g. a door becomes a window), use **ORGANISE → Re-Tag** to force a full re-derivation.

## Next steps

- [ISO 19650 tag format](../concepts/iso19650-tag.md) — understand what each segment means
- [Issue an RFI](rfi.md) — start using the tagged model for coordination
