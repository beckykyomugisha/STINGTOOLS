# Penetration Register Workflow — Plain-English Guide

This guide explains what the STING penetration workflow does, how to run it,
and what to do when something goes wrong. No BIM or coding knowledge required.

---

## What problem does this solve?

Every time a pipe, duct, or cable runs through a **fire-rated wall or floor**
it creates a hole. Building regulations (BS 9999, BS 476-20) require that hole
to be **sealed with an approved firestop product** and **recorded in a
register** — so that inspectors, facilities teams, and insurers can prove the
building's fire compartments haven't been compromised.

Finding every one of those holes by hand in a large Revit model takes days and
is error-prone. This workflow does it in minutes — automatically.

---

## What it does, step by step

| Step | What happens | Time |
|------|-------------|------|
| 1 — Build seed families | Creates placeholder Revit families for firestop collars, fire dampers, smoke dampers, and acoustic seals. One-time setup; skipped on re-runs. | ~30 s |
| 2 — Load shared parameters | Adds the `PEN_*` fields (fire rating, control number, installer, etc.) to the model so each penetration can carry its own data. | ~10 s |
| 3 — Detect & place | Finds every pipe/duct/cable crossing a rated barrier and drops the correct family at that point. | 1–5 min |
| 4 — Auto-tag | Gives each penetration family a unique asset tag (e.g. `M-BLD1-Z01-L02-FLS-FLS-FRP-0042`) so it appears in schedules and mobile sign-off forms. | ~30 s |
| 5 — Coverage audit | Produces a report listing any crossings still without a seal, any "orphan" seals with no matching pipe/duct, and any beam penetrations needing structural review. | ~20 s |
| 6 — Build schedule | Creates (or refreshes) the **Penetration Register** schedule in Revit, one row per penetration. | ~10 s |
| 7 — Place on sheet | Puts the schedule on an A1 sheet ready for printing or PDF export. | ~5 s |
| 8 — Export | Saves the register as a PDF (`FP-PEN-001.pdf`) and a CSV file you can open in Excel. | ~15 s |

**Total run time: under 10 minutes for a typical multi-storey building.**

---

## How to run the workflow

### Option A — One-click workflow preset (recommended)

1. Open your Revit project.
2. In the **STING Tools** dock panel, go to the **BIM** tab.
3. Click **Workflow Presets** → find **"Penetration Register — Full End-to-End"**.
4. Click **Run**. The workflow runs all 8 steps in sequence.
5. When it finishes, a result panel shows what was placed, what was skipped,
   and any findings that need your attention.

### Option B — Step by step (for troubleshooting or partial re-runs)

Run each command individually from the **TEMP** tab (setup commands) and the
**BIM** tab (detect/place/schedule/export). Useful if one step fails and you
want to re-run just that step without repeating the earlier ones.

---

## What each family represents

| Family name | What it is | Where it's used |
|-------------|-----------|-----------------|
| `STING_SEED_SpecialityEquipment` — `FR30` … `FR240` | Intumescent firestop collar / sleeve for pipes, conduit, and cable tray | Fire-rated walls and floors |
| `STING_SEED_FireDamper` — `FD_FR60_RECT_FUSIBLE` | Rectangular fire damper with fusible link | Ducts through FR60 / FR90 barriers |
| `STING_SEED_FireDamper` — `FD_FR60_ROUND_MOTORISED` | Circular motorised fire damper | Round ducts through any fire-rated barrier |
| `STING_SEED_FireDamper` — `FD_FR60_COMBINED_SMOKE` | Combined fire + smoke damper | Barriers rated for both fire AND smoke compartmentation |
| `STING_SEED_FireDamper` — `FSD_SMOKE_ONLY` | Motorised smoke-only damper | Smoke compartment barriers with **no** fire rating |
| `STING_SEED_AcousticSeal` — `ACS_RW45` | Acoustic penetration seal | Acoustic-rated partitions with no fire rating |
| `STING_SEED_SpecialityEquipment` — `SLEEVE_GENERIC` | Plain unlined sleeve | Non-rated walls and floors / beam penetrations |

**How the right family is chosen automatically:**

```
Does the host wall/floor have a fire rating?
  Yes + it's a duct →  Fire damper (round or rectangular, FR rating matched)
  Yes + it's a pipe → Intumescent firestop (FR30 / FR60 / FR90 / FR120 / FR240)
  No  + smoke-rated + duct → Smoke-only damper
  No  + acoustic-rated → Acoustic seal
  No  + nothing rated → Generic sleeve
  Beam host (any) → Generic sleeve + structural review flag
```

---

## How to swap seed families for real manufacturer products

Once the register is finalised and contractor sign-off is complete, replace the
generic seed families with specific manufacturer products using the **Swap to
Manufacturer** command (BIM tab → **SwapToManufacturer**).

Supported vendor mappings (set in `STING_FAMILY_SWAP_REGISTRY.json`):

| Seed variant | Hilti | Promat | STI | 3M |
|---|---|---|---|---|
| FR60 pipe/conduit | CP 601S collar | PROMASTOP collar | SpecSeal SSB | FireDam 150+ |
| FR120 pipe/conduit | CP 678 collar | PROMASTOP-II | SpecSeal SSBSR | FireDam 200+ |
| Rect fire damper | — | PROMASEAL FD | — | — |
| Round fire damper | — | PROMASEAL FD-R | — | — |

The swap is idempotent — running it again updates any families whose type
variant has changed without touching families already on manufacturer products.

---

## On-site sign-off (mobile app)

Once a penetration is physically installed and sealed:

1. Open the **Planscape mobile app** on site.
2. Scan the QR label (or search by asset tag) to find the penetration record.
3. Tap **Sign Off** and fill in:
   - `PEN_INSTALL_DATE` — date of installation
   - `PEN_INSTALLER_TXT` — installer name / company
   - `PEN_INSPECTION_DATE` — date of third-party inspection
4. Save. The data syncs back to the Revit model automatically.

Penetrations without an install date appear **red** in the register schedule —
easy to spot before practical completion.

---

## Reading the Penetration Register schedule

The schedule (STING - Penetration Register) has one row per penetration.
Key columns:

| Column | What it means |
|--------|--------------|
| `PEN_CONTROL_NR` | Unique sequential reference number (e.g. FP-0042) |
| `PEN_HOST_REF` | Which wall or floor the penetration is in |
| `PEN_FIRE_RATING` | Required fire rating of that wall/floor (FR60, FR120, etc.) |
| `PEN_PRODUCT_FAMILY` | Seed family used (SpecialityEquipment / FireDamper / AcousticSeal) |
| `PEN_TYPE_VARIANT` | Specific type (FD_FR60_RECT_FUSIBLE, FR60, ACS_RW45, …) |
| `PEN_OD_MM` | Outer diameter (or largest dimension) of the penetrating pipe/duct in mm |
| `PEN_SLAB_THICK_MM` | Thickness of the host wall or floor in mm |
| `ASS_TAG_1` | ISO 19650 asset tag (links to the tagging system) |
| `PEN_INSTALL_DATE` | Date the seal was installed on site |
| `PEN_STRUCTURAL_FLAG` | STRUCT_OK / STRUCT_REVIEW / STRUCT_FAIL for beam penetrations |

---

## Common problems and fixes

### "No penetrations were detected"

- Make sure the pipes/ducts are **modelled as Revit MEP elements** (not CAD
  imports, not generic model families). The detector only reads `MEPCurve`
  elements.
- The host wall or floor must be a **Revit floor/wall element**, not a mesh or
  linked file element (linked file penetrations are not detected automatically).
- The pipe/duct must physically cross the floor/wall bounding box. Very short
  runs that just touch the edge may be missed.

### "Wrong fire damper type was placed"

Check the host element's `STING_FIRE_RATING_TXT` parameter. If it is blank or
set to a non-standard value (e.g. "30 minutes" instead of "FR30"), the
selector falls back to FR60. Correct the parameter value on the host, delete
the wrong family instance, and re-run **Detect & Place**.

### "Beam penetrations show STRUCT_FAIL"

A `STRUCT_FAIL` flag means the penetration is too close to the beam support or
too large relative to beam depth (AISC DG2 / BS EN 1992 limits). Options:
- Re-route the pipe or duct to avoid the beam.
- Move the penetration to the neutral zone (middle third of beam depth,
  between 0.25L and 0.75L from support).
- Get structural engineer sign-off and manually set `PEN_STRUCTURAL_FLAG_TXT`
  to `STRUCT_OK_SE_APPROVED`.

### "The same penetration appears twice in the register"

Each penetration is identified by a UUID derived from the host element ID and
the MEP member ID. A duplicate entry means either:
- The same pipe crosses the same floor twice (check for doubled geometry).
- The pipe was deleted and recreated, giving it a new element ID. Delete the
  orphan entry: run the **Coverage Audit** and use the **Remove Orphans** button.

### "I can't see the Penetration Register schedule"

The schedule is called **"STING - Penetration Register"** and lives in the
Schedules/Quantities browser. If it does not exist, run step 6 (**Build
Register Schedule**) manually from the TEMP tab → Schedules → **Batch Create**.

---

## File outputs

After a successful run you will find:

```
<project folder>/
  _BIM_COORD/
    exports/
      PenetrationRegister_YYYYMMDD.csv   ← Excel-ready register
      FP-PEN-001.pdf                     ← Printed A1 register sheet
```

The CSV can be imported directly into a COBie handover package using the
**COBie Export** command (BIM tab → FM Handover → COBie Export), where
penetration products appear in the `Type` sheet under categories FD, FRP,
ACS, SD, and FD-C.

---

*Guide covers Phase 179. For changes, see `docs/CHANGELOG.md`.*
