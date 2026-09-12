# KNP26 — Information Request 001

**Project** Kibale National Park Lodge, Kabaale
**Project code** KNP26
**From** Planscape Consulting Engineers Ltd — BIM modelling and documentation
**To** ACE (architects) · and the land surveyor
**Date** [ ]
**Response needed by** [ ] — see *Why the dates matter* at the end

---

## Part A — to ACE

### A1 · Sections and elevations — **this one blocks the model**

The set issued contains **no vertical information**: no wall heights, no roof pitch, no roof
construction, no floor-to-ceiling dimension, no eaves or ridge level.

A Ø11.59 m round pavilion is a **roof-led** building. It cannot be modelled from a plan. Everything
above the floor slab is currently un-modellable, including the typical cottage — which is the one
building that repeats seven times and therefore the one worth getting right first.

**Requested:** at minimum one section and one elevation through the typical round cottage. If the
twin cottage differs above slab level, one of each.

### A2 · Roof specification

- Covering — makuti thatch, shingle, or sheet? The material name drives both the quantity take-off
  and the embodied-carbon figure, and thatch is labour-dominated in a way sheet roofing is not.
- Pitch, and whether it is constant on the radial geometry
- Structure — purlin size and spacing, rafter arrangement at the apex of a radial plan
- Eaves overhang and gutter arrangement

### A3 · Construction specification

- Wall build-ups — block size and thickness, whether rendered one or both faces, finish
- Floor slab thickness, and whether a separate screed is intended (the finish schedule needs it as
  its own layer, not merged into the slab)
- Foundation type — strip, pad, or raft — and depth
- Ceiling construction, if any, in the round pavilions

### A4 · Per-cottage finished floor level

The site falls **27.75 m** (1471.521 → 1499.268). The seven "typical" cottages are only typical
*above* their slab. Below it, every one is different: different platform, different fill or cut,
different foundation depth.

**Requested:** the intended FFL for each of `COT01`–`COT08`, `STF`, `KDR`, and the reception,
laundry and pool structures. If these have not been set, say so — we can propose them from the
survey, but they must be confirmed before earthwork quantities mean anything.

### A5 · Door and window schedule

The drawings carry type codes (`D1`–`D5`, `W1`–`W3`, suffixed `/pvo`) but no schedule.

**Requested:** for each code — leaf size, frame material, glazing, ironmongery set, fire rating
where applicable, and finish. These become type-level data and are scheduled once per type rather
than once per opening, so the schedule is short.

### A6 · Scale confirmation — **before anyone measures off the drawing**

The sheets are **A0** (3370 × 2384 pt) but the scale note reads **1:300 (A3)**. Those disagree. A
drawing measured at the wrong scale propagates a constant multiplier into every setting-out
dimension and every quantity.

**Requested:** confirm the true issue scale, or confirm the drawing is not to be measured from and
that dimensions govern.

### A7 · Two naming confirmations, one line each

- **Project name spelling.** The park is *Kibale*; the location is *Kabaale*. Our Project
  Information currently reads `KIBALE NATIONAL PARK LODGES`. Confirm the form you want on the
  title block, since it appears on every sheet and every transmittal.
- **Originator code.** Under ISO 19650 the originator identifies who produced the information
  container. Planscape authored the models, so our files are named `…-PLN-…`. If ACE issues them
  as author instead, they should read `…-ACE-…`. One line settles it; we would rather fix it now
  than rename sixty files later.

---

## Part B — to the land surveyor

### B1 · The survey in native format — **do not send another PDF**

We hold the survey as a PDF only. **We will not trace it.** Tracing a 1:300 raster puts a
100–300 mm error into every setting-out dimension, and on a 27.75 m fall that error follows all the
way through to cut-and-fill volumes.

**Requested:** the native `.dwg`, and the levelled point file as `.csv` or `.txt`
(point number, easting, northing, level, description). The survey shows **159 levelled points**;
we need all of them, not a reduced set.

### B2 · Coordinate system and datum

- Which projection and zone — UTM 36N, a national grid, or an arbitrary site grid?
- Which vertical datum do the levels reference?
- If the grid is arbitrary, we need the transformation to a real-world system, or written
  confirmation that arbitrary is acceptable for the life of the project

This is not a formality. It is set once, in the site model, and every building model inherits it.
Changing it later means re-acquiring coordinates in every file.

### B3 · Benchmark

Location, value, and description of the site benchmark used, so levels can be checked on site
against the model rather than against another drawing.

### B4 · Existing features

The survey shows one hatched structure mid-site and a substantial tree survey — `mango1`–`4`,
`ovacado1`–`4`, `tree1`–`4`, `pltn` (plantation), `bd1`–`6`, `garden1`–`9`, `house1`–`4`, and a
road chain of 21 points.

**Requested:**
- The hatched structure — what is it, and is it to be retained or demolished?
- For the trees: species, trunk position, canopy spread and approximate height. In a national park
  buffer these are likely to be constraints on the building positions, not decoration, and they
  belong in the model as such.
- Whether the road chain is existing or proposed

---

## Why the dates matter

| Item | Blocks |
|---|---|
| **A1** sections and elevations | the typical cottage model — the single highest-leverage piece of work on the project, since it repeats seven times |
| **B1 / B2** native survey and datum | shared coordinates, which every other model file inherits and which cannot be changed cheaply once set |
| **A4** per-cottage FFL | all earthwork and foundation quantities |
| **A2 / A3** specification | the bill of quantities — a quantity without a specified material is measured but not priceable |
| **A5** door and window schedule | the door and window schedules, and their contribution to the bill |
| **A6 / A7** scale and naming | best answered now; both get more expensive with every sheet issued |

Items **A1**, **B1** and **B2** are on the critical path. The rest can follow, but the model cannot
progress above slab level without A1, and the site model cannot be set out reliably without B1.
