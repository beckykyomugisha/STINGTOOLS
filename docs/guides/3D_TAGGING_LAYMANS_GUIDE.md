# STING 3D Tagging — A Layman's Guide for First-Time Modellers

**Audience.** You know what an ordinary Revit tag is — the little label you
drop on a door in a plan view. Someone has now asked you to "tag the model in
3D" so the coordination view, the walkthrough, or the client fly-through shows
labels floating next to every piece of equipment. You open a 3D view, reach
for the Tag command… and Revit won't let you. This guide explains **what** 3D
tagging is, **why** it works differently from normal tagging, and **how** STING
does it for you in one click.

> **How to read this guide.** Each section has three parts:
> - *What it is* — the concept in plain English.
> - *Why it matters* — what goes wrong if you skip or misunderstand it.
> - *How to do it in STING* — the actual buttons / commands, in order.
>
> If you only have twenty minutes, read sections 1, 2, 4, 5 and 11.

---

## Table of contents

1. [Why you can't just "tag in 3D" the normal way](#1-why-you-cant-just-tag-in-3d-the-normal-way)
2. [What a STING 3D tag actually is](#2-what-a-sting-3d-tag-actually-is)
3. [Where the data comes from — the 8-segment tag](#3-where-the-data-comes-from)
4. [Anatomy: finding the button](#4-anatomy-finding-the-button)
5. [Step-by-step: tagging a 3D view](#5-step-by-step-tagging-a-3d-view)
6. [The tag-bubble family — what STING needs loaded](#6-the-tag-bubble-family)
7. [Where the tag floats — placement anchors and offsets](#7-where-the-tag-floats)
8. [Run it twice, nothing breaks — idempotency](#8-run-it-twice-nothing-breaks)
9. [Worksharing, pinning and tidy models](#9-worksharing-pinning-and-tidy-models)
10. [Tagging linked models](#10-tagging-linked-models)
11. [Plain-language mode (TAG7 narrative)](#11-plain-language-mode)
12. [Automatic 3D tagging on drawings](#12-automatic-3d-tagging-on-drawings)
13. [Reading the result report](#13-reading-the-result-report)
14. [Project configuration (`project_config.json`)](#14-project-configuration)
15. [Common first-timer mistakes](#15-common-first-timer-mistakes)
16. [Glossary](#16-glossary)

---

## 1. Why you can't just "tag in 3D" the normal way

**What it is.** A normal Revit tag (the `IndependentTag` object) is a *2D
annotation*. It lives flat on the sheet/plan, like a sticky note pinned to a
sheet of paper. Revit deliberately refuses to place those tags in a 3D view —
there is no flat page for them to lie on.

**Why it matters.** Coordination reviews, client walkthroughs and clash
sessions increasingly happen in a live 3D view, not on paper. People want to
spin the model and see "that's AHU-03, that's DB-L02-A" floating next to each
unit. The standard tag can't do this, so first-timers get stuck the moment
they switch to a 3D view.

**How STING solves it.** Instead of a flat annotation, STING places a small
**3D "tag bubble"** — a real piece of geometry that floats in space next to
each element and displays the element's tag text. Because it's geometry (not a
2D annotation), it shows up in any 3D view, any orientation, and even in
exports and renders.

---

## 2. What a STING 3D tag actually is

**What it is.** Each 3D tag is a **Generic Model family instance** — think of
it as a tiny billboard you load into the project once. It carries a single
text label parameter, `ASS_TAG_3D_TXT`, and STING writes the element's tag
into that parameter. The bubble is placed in space a short distance from the
element it describes.

So the chain is:

```
  Real element            STING 3D tag (a Generic Model instance)
  ┌──────────┐            ┌─────────────────────────────┐
  │  AHU      │  ◄──────  │ ASS_TAG_3D_TXT =            │
  │ (the host)│  floats   │  "M-BLD1-Z01-L02-HVAC-SUP-  │
  └──────────┘  beside it │   AHU-0003"                 │
                          └─────────────────────────────┘
```

**Why it matters.** Knowing it's a *real family instance*, not a 2D tag,
explains every other behaviour in this guide: it can be pinned, it lives on a
workset, it survives in 3D, it has to be loaded as a family first, and STING
has to remember which instance belongs to which host so re-runs don't create
duplicates.

**How to recognise one.** Select a floating label in a 3D view — it reports as
a *Generic Model* in the Properties palette, and its `ASS_TAG_3D_TXT`
parameter holds the tag string. A hidden instance parameter,
`STING_TAG3D_HOST_ID_TXT`, records which element it was placed for.

---

## 3. Where the data comes from

**What it is.** STING doesn't invent the label. It writes the same **8-segment
ISO 19650 tag** that the rest of STING builds for every element:

```
DISC - LOC  - ZONE - LVL - SYS  - FUNC - PROD - SEQ
  M  - BLD1 - Z01  - L02 - HVAC - SUP  - AHU  - 0003
```

| Segment | Meaning             | Example |
|---------|---------------------|---------|
| DISC    | Discipline          | `M`, `E`, `P`, `A`, `S` |
| LOC     | Location / building | `BLD1`, `EXT` |
| ZONE    | Zone                | `Z01` |
| LVL     | Level               | `L02`, `GF`, `B1` |
| SYS     | System              | `HVAC`, `DCW`, `LV` |
| FUNC    | Function            | `SUP`, `HTG`, `PWR` |
| PROD    | Product class       | `AHU`, `DB`, `SOK` |
| SEQ     | 4-digit sequence    | `0003` |

**Why it matters.** A 3D tag is only as good as the data behind it. If an
element has never been tagged, there's nothing to display. STING handles this
gracefully (see below), but the cleanest result comes from tagging your model
*first* with the normal **CREATE → Tag & Combine** workflow, then dropping 3D
bubbles on top.

**How STING fills gaps.** When you run 3D tagging on an element that has *no*
tag yet, STING quietly runs its full tagging pipeline on that element first —
deriving DISC/LOC/ZONE/LVL/SYS/FUNC/PROD, assigning the next sequence number,
and writing all the tag containers — so the bubble is never labelled with an
empty string. These auto-tagged hosts are counted as **"enriched"** in the
result report.

---

## 4. Anatomy: finding the button

**What it is.** 3D tagging is a single command, surfaced in three places:

| Where | What you click |
|-------|----------------|
| Main dock panel → **TAGS** tab | **"Place 3D tags"** button (tag `Tag3D`) |
| **Tag Center** dialog | **"Tag 3D"** button |
| Natural-language box | type *"tag in 3D"* / *"tag elements in 3D view"* |

**Why it matters.** All three routes run the same command class
(`Tag3DCommand`). There's no difference in behaviour — use whichever surface
you already have open.

**How to do it.** The command only works when the **active view is a 3D view**.
If you're in a plan, section or sheet, it stops and tells you to switch to a 3D
view. View templates are also skipped (there's nothing to place into a
template).

---

## 5. Step-by-step: tagging a 3D view

Do these in order.

### 5.1 Tag the model normally first (recommended)

Run **CREATE → Tag & Combine** so every element already carries a tag. 3D
tagging *can* fill gaps for you, but a pre-tagged model gives the tidiest,
fastest result.

### 5.2 Open a 3D view

Open (or create) an ordinary `{3D}` view. Orbit so the equipment you care
about is visible. Hide the elements you don't want to label (turning off
categories or worksets reduces clutter — STING only tags what the view shows).

### 5.3 (Optional) Select specific elements

If you select some elements before running the command, STING asks whether to
**tag only the selection** or **tag every taggable element in the view**.
Leave nothing selected to tag the whole view.

### 5.4 Click "Place 3D tags"

STING then:

1. Finds the loaded 3D tag-bubble family (§6).
2. Collects every taggable element visible in the view (filtered to STING's
   known categories).
3. For each element: reads its tag — or runs the full pipeline to create one —
   computes where the bubble should float (§7), places a Generic Model
   instance there, and writes the tag into `ASS_TAG_3D_TXT`.
4. Stamps each bubble with its host's id so future runs don't duplicate it.
5. Shows a summary: placed / enriched / skipped / errors (§13).

### 5.5 Review

Orbit the view. Each labelled element now has a floating bubble. Re-run any
time you add equipment — only the *new* elements get bubbles (§8).

---

## 6. The tag-bubble family

**What it is.** The floating label is a **Generic Model family** that you load
into the project. It must carry a text label parameter named exactly
`ASS_TAG_3D_TXT` — that's the box STING writes the tag into.

**Why it matters.** If no suitable family is loaded, STING has nothing to
place and stops with a clear warning. If a family is loaded but lacks the
`ASS_TAG_3D_TXT` label, STING refuses to use it (placing bubbles it can't
label would just be noise) — it tells you to load a proper one and retry.

**How STING finds the family** (in priority order):

1. A loaded Generic Model family whose name contains **"TAG"** or **"3D"**.
2. Any loaded Generic Model family (fallback).
3. A family path set in `project_config.json` under the key
   `tag3DFamilyPath` — STING loads it for you.

Before using a family, STING **probe-places a temporary instance** (rolled
back instantly) to confirm it really carries the `ASS_TAG_3D_TXT` label. The
result is cached, so this check is cheap on repeat runs.

> **Practical tip.** Author one small "tag bubble" Generic Model family with
> the `ASS_TAG_3D_TXT` label, give it a name like `STING - 3D Tag`, and load it
> into your template. Then 3D tagging "just works" on every project.

---

## 7. Where the tag floats

**What it is.** STING computes a point in space for each bubble. By default it
takes the **centre of the element** and lifts the bubble **1 ft (≈ 305 mm)**
above it.

**Why it matters.** On a wall of identical sockets, bubbles that all sit at the
exact element centre overlap and become unreadable. Lifting them — and, on big
plant, lifting them clear of the top of the unit — keeps them legible.

**How to control it** (via `project_config.json`, §14):

| Setting | What it does | Default |
|---------|--------------|---------|
| `anchor` | `Centroid` (centre of element) or `TopOfBbox` (top face of the element's bounding box) | `Centroid` |
| `defaultOffsetMm` | How far above the anchor the bubble floats | 305 mm (1 ft) |
| `perCategoryOffsetMm` | Override the offset for specific categories (e.g. lift big AHUs higher) | — |

For example, set `anchor: "TopOfBbox"` and
`perCategoryOffsetMm: { "Mechanical Equipment": 600 }` so AHU labels sit 600 mm
above the top of each unit, well clear of the casing.

> **Note.** The horizontal position always follows the element centre; only the
> vertical lift is configurable. Mounting height of the *element* is not changed
> — STING never moves your model, only the floating label.

---

## 8. Run it twice, nothing breaks

**What it is.** 3D tagging is **idempotent** — running it repeatedly is safe.
Each placed bubble is stamped with the id of the host element it belongs to.

**Why it matters.** On a live project you add equipment weekly. You want to
re-run 3D tagging to label the new kit *without* doubling up bubbles on
everything that was already done.

**How it works.** Before placing anything, STING scans the view for existing
tag bubbles and reads their host stamps. Any element already carrying a bubble
is **skipped** (counted as `AlreadyTagged`). The stamp lives in:

- `STING_TAG3D_HOST_ID_TXT` — a dedicated instance parameter (preferred), or
- a marker in the bubble's **Comments** (`[STING3D host=…]`) when that
  parameter isn't bound on the project.

Both forms are recognised, so a project that later runs the standard parameter
setup keeps recognising older bubbles.

---

## 9. Worksharing, pinning and tidy models

**What it is.** STING places the bubbles with shared-model etiquette in mind.

**Why it matters.** On a workshared central file, you don't want to grab
elements another user has checked out, you don't want a stray click to drag a
label off its element, and you want all the annotation clutter on one workset
you can switch off.

**How STING handles it:**

- **Ownership check.** Elements owned by another user are skipped (counted as
  `OwnedByOtherUser`) rather than forcing a checkout.
- **Pinning.** Placed bubbles are **pinned** by default so they can't be moved
  by accident. (Turn off via `pinPlacedInstances: false`.)
- **Workset.** If a user workset named **`STING-Annotations`** exists, bubbles
  are moved onto it, so you can isolate or hide all 3D tags at once. If it
  doesn't exist, bubbles land on your active workset — no harm done.

---

## 10. Tagging linked models

**What it is.** STING can optionally place bubbles next to elements that live
in **linked Revit models** (e.g. you're the MEP author tagging architectural
equipment in the linked architecture model).

**Why it matters.** Coordination views often combine your model with several
links. Labels next to linked equipment make the federated view self-explaining.

**How to enable it.** Set `includeLinked: true` in `project_config.json`.
STING then walks each loaded link, transforms the linked element's position
into your model's coordinates, and places a bubble there.

> **Limitation.** Linked elements are **read-only** — STING can't run the
> tagging pipeline on them. So a linked element that has *no* tag is skipped
> (it can't be enriched). Tag the link's own model first if you need full
> coverage. Linked bubbles get their own stamp (`link:<key>`) so they too are
> idempotent.

---

## 11. Plain-language mode

**What it is.** Instead of the cryptic 8-segment tag, STING can label each
bubble with the **TAG7 narrative** — a rich, plain-English description of the
element (the same narrative STING builds for presentation drawings).

**Why it matters.** For a *client* walkthrough, `M-BLD1-Z01-L02-HVAC-SUP-AHU-0003`
means nothing. "Supply Air Handling Unit serving Level 2 offices" means
everything. Narrative mode swaps the technical code for the human sentence.

**How to switch it on.** Narrative mode follows the drawing's **display mode**:
when the view is set to display mode 6, the automatic 3D tagger (§12) labels
with the TAG7 narrative instead of the technical tag. If an element has no
narrative, STING falls back to the technical tag for that one — you never get a
blank bubble.

---

## 12. Automatic 3D tagging on drawings

**What it is.** You don't always have to click the button. STING's drawing
engine can place 3D tags **automatically** when it produces a 3D drawing.

**Why it matters.** Many corporate drawing types (axonometrics, 3D
coordination views, presentation fly-throughs) are *supposed* to carry labels.
Wiring the tagging into the drawing profile means every such sheet is labelled
consistently with no manual step.

**How it works.** A drawing-type profile can carry an **`Auto3DTag`** rule in
its annotation pack. When STING applies that profile to a **3D view**, it
automatically runs the same 3D tagging routine — honouring narrative mode if
the profile's display mode is 6. (On a non-3D view the rule is harmlessly
skipped with a note, because 3D bubbles only make sense in 3D.) Many of the
shipped corporate drawing types already include this rule.

---

## 13. Reading the result report

After every run STING shows a summary. Here's what each line means:

| Line | Meaning | What to do about it |
|------|---------|---------------------|
| **Placed** | Bubbles created this run | The headline number |
| **(incl. N on linked elements)** | How many of those were on linked models | Only if `includeLinked` is on |
| **Enriched** | Hosts that had no tag, so STING auto-tagged them first | Consider running normal tagging earlier next time |
| **Skipped** | Elements deliberately not tagged, with reasons (below) | Usually fine; check the reasons |
| **Errors** | Placements that threw an exception | Check `StingTools.log`; often a degenerate family |
| **Warnings** | Non-fatal notes (e.g. workset move failed) | Read the first few |

**Skip reasons explained:**

| Reason | Meaning |
|--------|---------|
| `AlreadyTagged` | Host already has a bubble (re-run safety) — expected, not a problem |
| `NoLocation` | STING couldn't work out where the element is in space (no geometry/location) |
| `OwnedByOtherUser` | Element checked out by a teammate on a workshared file |
| `NoTagAfterPipeline` | Even after the pipeline, no tag could be built (e.g. read-only linked element) |
| `ExceptionDuringPlacement` | Something failed placing this one — also counts as an Error |

---

## 14. Project configuration

**What it is.** All placement behaviour is tunable per project via a
`tag3DPlacement` block in `project_config.json` (sat next to the `.rvt`).

**Why it matters.** Different projects want different label heights, anchor
styles, and whether links and pinning are on. One config file sets the house
style once.

**How to write it:**

```json
{
  "tag3DFamilyPath": "C:/STING/Families/STING - 3D Tag.rfa",
  "tag3DPlacement": {
    "anchor": "TopOfBbox",
    "defaultOffsetMm": 300,
    "perCategoryOffsetMm": {
      "Mechanical Equipment": 600,
      "Electrical Equipment": 400
    },
    "pinPlacedInstances": true,
    "includeLinked": false
  }
}
```

| Key | Type | Default | Effect |
|-----|------|---------|--------|
| `tag3DFamilyPath` | path | — | Where to load the tag-bubble family from if none is in the project |
| `anchor` | `Centroid` / `TopOfBbox` | `Centroid` | Where the bubble's vertical reference starts |
| `defaultOffsetMm` | number | 305 | Float height above the anchor |
| `perCategoryOffsetMm` | map | — | Per-category override of the float height |
| `pinPlacedInstances` | bool | `true` | Pin bubbles so they can't be dragged |
| `includeLinked` | bool | `false` | Also tag elements in linked models |

> All keys are optional. Omitting the block keeps the historic defaults
> (centroid + 1 ft + pinned), so upgrading an existing project never shifts the
> position of bubbles you already placed.

---

## 15. Common first-timer mistakes

| Mistake | What goes wrong | How STING responds |
|---------|-----------------|--------------------|
| Running the command from a plan or section | Nothing happens | STING tells you the active view must be a 3D view |
| Running it on a 3D *view template* | Nothing placed | STING reports the view is a template |
| No tag-bubble family loaded | No bubbles appear | Clear warning to load a Generic Model family with `ASS_TAG_3D_TXT` |
| Family loaded but missing the `ASS_TAG_3D_TXT` label | Run aborts | STING refuses and names the family to fix |
| Expecting bubbles on a model you never tagged | Lots of "enriched" hosts, slower run | Works, but tag normally first for a clean result |
| Re-running and fearing duplicates | — | None created — already-tagged hosts are skipped |
| Wanting client-friendly labels but seeing codes | Labels show `M-BLD1-…` | Use narrative mode (display mode 6) — §11 |
| Bubbles overlapping on dense equipment | Unreadable | Switch `anchor` to `TopOfBbox` and raise `defaultOffsetMm` |
| Can't find/hide all the 3D tags at once | Clutter | Create a `STING-Annotations` workset before running |
| Bubbles in the wrong place after editing the element | Stale label position | Delete the bubble and re-run, or move the (pinned) bubble manually |
| Tagging a federated view, links unlabelled | Half the view bare | Set `includeLinked: true` (and tag the link's own model for full coverage) |

---

## 16. Glossary

| Term | Meaning |
|------|---------|
| 3D tag / tag bubble | A Generic Model family instance that floats beside an element and displays its tag in 3D views |
| `ASS_TAG_3D_TXT` | The text label parameter on the bubble family that STING writes the tag into |
| `STING_TAG3D_HOST_ID_TXT` | Hidden instance parameter recording which host element a bubble belongs to (idempotency stamp) |
| `IndependentTag` | Revit's standard 2D annotation tag — cannot be placed in 3D views |
| Generic Model | The Revit family category STING uses for the tag bubble |
| 8-segment tag | The ISO 19650 identifier `DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ` |
| Enriched | A host that had no tag, so STING ran the tagging pipeline before placing its bubble |
| Idempotent | Safe to run repeatedly — re-runs don't create duplicates |
| TAG7 narrative | The plain-English description of an element (used in display mode 6) |
| Display mode 6 | The view setting that tells STING to label with narrative text instead of the technical tag |
| `Auto3DTag` | A drawing-type annotation rule that auto-runs 3D tagging when a 3D drawing is produced |
| `STING-Annotations` | The user workset STING puts bubbles on when it exists, so you can hide them all at once |
| Anchor (`Centroid` / `TopOfBbox`) | Whether the bubble floats from the element centre or above its bounding-box top |
| Pinned | Locked in place so a stray click can't move the bubble |
| `project_config.json` | Per-project settings file (beside the `.rvt`) holding the `tag3DPlacement` block |

---

*Document version 1.0 — May 2026. Maintained alongside `CLAUDE.md`. For the
full tagging reference see `StingTools/Data/TAGGING_GUIDE.md` and
`docs/TAGGING_WORKFLOW_GUIDE.md`; the 3D tagging command lives in
`StingTools/Tags/Tag3DCommand.cs`.*
