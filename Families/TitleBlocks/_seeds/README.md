# Title-block seed families (`_seeds/`)

**Why this folder exists.** Revit 2025 **removed `FamilyItemFactory.NewLabel`** from
the API (verified three ways — see `StingTools/Core/Drawing/TitleBlockFactory.cs`
build comment, and the same limitation documented in
`StingTools/Tags/FamilyLabelAuthor.cs` lines 33–41). The title-block factory can
create parameters, lines, filled regions, static text and slots, but it **cannot
create the *label* elements** that display a parameter's value. Without labels,
every title-block **value cell** (project name, SYSTEM, revision, spool, weight,
client, sheet number, …) renders **blank** on the produced sheet.

A label can't be *created* through the API, but a family that **already contains**
labels can be opened, augmented and re-saved. So we pre-author the labels **once**
into a *seed* `.rfa` per family. `TitleBlockFactory.Build` then:

1. looks for a seed at `Families/TitleBlocks/_seeds/<spec.Id>.rfa`
   (project directory first, then the addin directory),
2. copies it to a temp file and opens the **copy** (the seed is never mutated),
3. ensures the full shared-parameter set is present (idempotent — a param the
   seed already carries is reused, never duplicated) and stamps the spec
   defaults (BIM/NONBIM mode, DrawingType/version),
4. adds the **static-text captions** and **viewport slots** on top,
5. `SaveAs` the concrete family (`Families/TitleBlocks/<id>.rfa`).

If **no seed** exists the factory falls back to building from the blank `.rft`
template — the family still builds (25/0) but is **label-less** and logs one
warning. This means seeds can be authored **incrementally, most-used first** —
you do not have to author all 25 before the factory is useful.

---

## What the seed must contain vs. what the factory adds

| Element              | Authored in the **seed** (this checklist) | Added by the **factory** at Build |
|----------------------|:-----------------------------------------:|:---------------------------------:|
| Border / strip lines | ✅ (`lines[]`)                            | — (skipped on the seed path)      |
| Filled regions       | ✅ (`filledRegions[]`)                    | — (skipped on the seed path)      |
| **Labels** (value cells) | ✅ (`labels[]`) — **API can't create these** | — (cannot create on 2025)     |
| Shared parameters    | loaded (so labels can bind)               | ✅ ensured + defaults stamped     |
| Static-text captions | — (do **not** hand-author)                | ✅ (`staticText[]`, TextNotes)    |
| Viewport slots       | — (optional)                              | ✅ (`slots[]`)                    |

> Do **not** hand-author the static-text captions or slots — the factory places
> them every Build, so authoring them in the seed would double them. Author only
> the **border lines, filled regions and labels**.

---

## One-time authoring checklist (per family, in the Family Editor)

Each family's geometry is fully specified in
`StingTools/Data/STING_TITLE_BLOCKS.json`. Use the **resolved** spec — a family
with an `"extends"` parent inherits that parent's `lines[] / filledRegions[] /
labels[]` (run `TitleBlock_Create` once with no seed to dump the resolved
counts to the build `.log`, or read the parent chain in the JSON).

1. **New family** → *Titleblock* → pick the metric template named in the
   family's `templateRft` (e.g. `Metric Title Block - A1.rft`).
2. **Manage → Shared Parameters → load** `StingTools/Data/MR_PARAMETERS.txt`
   (the shared-parameter file the factory binds against). Every param a
   `labels[]` entry references lives here. *(You only need the title-block
   subset present; the factory adds any the seed is missing at Build.)*
3. **Draw the border / strip + filled regions** for this family's resolved
   `lines[]` and `filledRegions[]`. All coordinates are **mm from the family
   bottom-left** (= sheet bottom-left). Use the line `style` given per entry.
4. **Place a Label** (Annotate → *Label*) for **each** resolved `labels[]`
   entry:
   - bind it to the shared parameter named in the entry's `param`,
   - position it at the entry's `anchor` `[x, y]` mm,
   - set text size = `size` mm, and horizontal/vertical alignment = `hAlign` /
     `vAlign`,
   - apply any `prefix` / `suffix`.
   This is the **only** step the API cannot do — it is the whole reason the seed
   exists.
5. **Save as** `Families/TitleBlocks/_seeds/<id>.rfa` — the file name **must be
   the exact spec `id`** (e.g. `STING_TB_A1_BIM_v2.0.rfa`), so the factory finds
   it.
6. **Repeat** for the other concrete families. The bottom-strip labels
   (revision, sheet number, scale, …) are shared across the size variants — copy
   them across rather than re-placing by hand.

After authoring a seed, deploy and run **Build All** (`TitleBlock_CreateAll`) or
**Build** (`TitleBlock_Create`) for that family. The build report / `.log` will
show `labels N (from seed)` with **N > 0**; un-seeded families show
`labels 0 (no seed)`.

---

## The 25 concrete families (author most-used first)

Priority order — the everyday production sheets first:

| Priority | Family id                       | Notes |
|---------:|---------------------------------|-------|
| 1        | `STING_TB_A1_BIM_v2.0`          | Primary A1 landscape production sheet |
| 2        | `STING_TB_A1_NONBIM_v2.0`       | A1 without the BIM data strip |
| 3        | `STING_TB_A0_BIM_v2.0`          | A0 landscape |
| 4        | `STING_TB_A3_BIM_v2.0`          | A3 details |
| 5        | `STING_TB_A0_NONBIM_v2.0` · `STING_TB_A3_NONBIM_v2.0` | non-BIM variants |
| 6        | `STING_TB_A0_PORT_BIM_v2.0` · `STING_TB_A1_PORT_BIM_v2.0` · `STING_TB_A3_PORT_BIM_v2.0` (+ `_NONBIM`) | portrait variants |
| 7        | `STING_TB_ASSEMBLY_PIPE_v1.0` · `..._DUCT_v1.0` · `..._COND_v1.0` · `..._HANGER_v1.0` | fabrication spool sheets |
| 8        | `STING_TB_PRESENT_A1_v1.0` · `..._MONO_v1.0` | client presentation |
| 9        | `STING_TB_COVER_A1_v1.0` · `STING_TB_DIVIDER_A1_v1.0` · `STING_TB_REGISTER_A1_v1.0` | pack front-matter |
| 10       | `STING_TB_SUBMISSION_KCCA_v1.0` · `..._ERA_v1.0` · `..._NEMA_v1.0` | Uganda authority submissions |
| 11       | `STING_TB_CLARIFICATION_A3_v1.0` | RFI / clarification A3 |

(Abstract base specs — `A1_common_v2.0`, `A0_common_v2.0`, … — are **not**
minted and need **no** seed; they only supply inherited geometry to the concrete
families above.)

---

## Notes

- **Idempotent.** Re-running Build always regenerates from a fresh copy of the
  pristine seed, so you can iterate the JSON (params / captions / slots) and
  rebuild without re-touching the seed.
- **Project override.** A seed placed in a project's own
  `Families/TitleBlocks/_seeds/` wins over the one shipped beside the addin, so a
  project can override the corporate seed for one family.
- **Out of scope here.** Auto-generating seeds on Revit 2024 (where `NewLabel`
  still exists) would need a separate .NET Framework 4.8 build and is deferred.
