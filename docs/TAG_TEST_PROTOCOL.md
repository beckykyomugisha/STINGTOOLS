# Tag / Room Test Protocol — Phases 287–293

Five tests that can only be answered inside Revit, in the order they must be run. Every fix
in Phases 287–293 is a data or source change verified against shipped data; **none has been
seen on a tag in Revit.** That is what this protocol closes.

Each test names its `docs/ROADMAP.md` id. Record the result there, not here — this file is
the method, the ROADMAP is the state.

---

## 0 · Before you start

### 0.1 What must be deployed

| | |
|---|---|
| Live manifest target | `C:\Dev\wt-sting-live\CompiledPlugin\StingTools.dll` |
| Must be built from | `114e38244` (merge of #965) or later |

Confirm the target has not moved — it has moved five times in two days before now, and
building the right code into the wrong folder **succeeds silently**:

```bash
grep -h "<Assembly>" "$APPDATA/Autodesk/Revit/Addins"/*/StingTools.addin | sort -u
```

If that path is not the folder you built into, stop. You will be testing a different DLL.

### 0.2 Prove Revit loaded it

A fresh build proves what reached the disk, not what Revit read. The log filename is
**date-stamped** — `StingTools_yyyyMMdd.log`, not `StingTools.log`; searching for the
undated name finds nothing and reads as "the plugin never logged".

```bash
tail -20 "C:/Dev/wt-sting-live/CompiledPlugin/StingTools_20260916.log"
```

Expect startup lines newer than the DLL's timestamp. **A silent no-op is indistinguishable
from a wrong fix, a bad path, and a command that never ran** — get the log, don't infer.

### 0.3 Where the buttons are

The STING panel's real tabs, in order: **HVAC · SELECT · TAGGING · DOCS · SETUP ·
CREATE TAGS · MODEL · BIM · TAG STUDIO**. (`CLAUDE.md`'s tab list is wrong; these come from
`StingDockPanel.xaml`.) Open the panel from the ribbon: **STING Tools → STING Panel**.

| Need | Tab | Section | Button |
|---|---|---|---|
| Tag the active view | **TAGGING** | `DATA TAGGING (ISO 19650)` (green) | **Auto Tag** |
| Tag whole project | **TAGGING** | same | **Batch Tag** |
| Dry run, write nothing | **TAGGING** | same, 2nd row | **Pre-Audit** |
| Set tier depth | **CREATE TAGS** | `Manual token overrides` | **Depth** |
| See what a tier *should* contain | **CREATE TAGS** | same | **Label Spec** |
| Switch presentation mode | **CREATE TAGS** | same | **Pres Mode** |
| Room renumber / tags | **SETUP** | `ROOM / SPACE` (green) | **Renumber**, **Number Schemes**, **Place Tags** |
| Room audit | **SETUP** | `ROOM / SPACE` → expand `Audits & reports` | **Audit** |

**Depth** prompts twice: first *Depth 1…10*, then *"Apply to which elements?"* with three
scope options. Depth applies to element **types**, not instances.

**Label Spec is the diagnostic tool for T2 and T3.** It reports the rows a tier is
*specified* to carry. If a row is specified but blank on the tag, the label row was never
authored in the `.rfa` — a different problem from a broken mapping, and the two are easy to
confuse.

### 0.4 Two things that make a correct result look like a failure

**"Auto Tag" writes parameters. It does not place annotations.** `AutoTagCommand.cs`
contains zero `IndependentTag.Create` calls. The word "tag" means two different things in
this plugin and nothing in the UI says so. For something visible: **TAGGING → `VISUAL TAG
PLACEMENT` (teal) → Smart Place**, or native *Annotate → Tag by Category*. The label rows
read the parameters either way, so a parameter check does not need a placed tag.

**Mapped writes are SetIfEmpty in every collision mode.** `WriteMapped` passes
`overwrite: false` unconditionally, so a parameter that already holds a stale value is not
refreshed — not even by "Overwrite all". An empty one is written. So test on an element
that has **never been tagged**; on a previously tagged element you cannot tell today's
result from last month's residue. Check `ASS_TAG_MODIFIED_DT` if unsure.

### 0.5 Read the log, not the dialog

The summary line is the honest one:

```
AutoTag: view='...', tagged=54, skipped=278, collisions=0, mode=Skip
```

Before Phase 294 that same run also emitted **278** `BuildAndWriteTag failed` warnings —
one per deliberate skip — around **one** real fault. That is fixed (TAGLOG-1): a skip is no
longer logged as a failure. If you still see a `failed` line, it is real, and it now names
the outcome.


---

## T1 · Unit conversion — `MAPTYPE-1` ⚠️ RUN FIRST

**Why first:** if this is wrong, every numeric row in T2 is wrong *for this reason*, and you
would mis-diagnose T2 entirely.

**Status: the reversal question is answered.** Measured on a door 2026-09-16 — width read
**blank**, not `274320` and not `2.95`, so the arguments are the right way round
(`MAPTYPE-1`, closed). The blank had a different cause: a **type** built-in being read from
the **instance**, fixed in Phase 294 (`MAPTYPE-4`). What remains is the regression check
below — the fix is verified by the compiler and six gates, not yet by a tag (`MAPTYPE-5`).

**What is under test.** `WriteMapped` has to carry two representations of one number:
Revit-internal decimal feet for a Double-storage target, and millimetres for the `_TXT`
mirror the tag label reads. If the arguments are swapped, a 900 mm door stores **900 feet**.
The build is green either way and nothing logs.

### Steps

1. Open a model with a door, a window, a room and a pipe.
2. **TAGGING → Auto Tag** on the active view.
3. Select the door. Read **both** surfaces — the tag row *and* the Properties palette.

### Pass / fail

| Surface | Pass | Fail — and what it means |
|---|---|---|
| Tag row `W:` | `900` | blank → mirror not written · `2.95` → mirror got feet |
| `BLE_DOOR_WIDTH_MM` in Properties | `900 mm` | `274320` → **arguments reversed** (900 ft in mm) · `2.95` → conversion applied twice |

Repeat for: window width, `ASS_ROOM_AREA_SQ_M` on the room, pipe length.

**If it fails:** a one-line argument swap in `StingTools/Core/ParameterHelpers.cs`.
`tools/check_map_target_types.py` already asserts `displayText` still exists, so the gate
survives the fix.

- [ ] door · [ ] window · [ ] room area · [ ] pipe length

---

## T2 · Revived tag rows — `TAGBIND-4`

1,161 tag rows could never display anything; the count is now 0. This checks that against a
real tag.

### Steps

1. **CREATE TAGS → Depth → Depth 3**, scope = whole project (or the selection you tagged).
2. Place a room tag, a door tag and a window tag.
3. Read the rows.

### Expect values where there were blanks

| Element | Rows |
|---|---|
| Room | finishes, headroom, occupancy, ventilation, escape capacity |
| Door | type, operation, glazing, threshold |
| Window | vent area |

### Reading a blank correctly

**Occupancy, escape capacity, door threshold, operation and glazing are design inputs**
(`TAGBIND-5`) — recorded in `PARAM_CONTRACT_BASELINE.json` with role `input`. Blank there
means *nobody typed a value*, not a defect. Everything else blank **is** a defect.

If a row is blank, press **Label Spec** before concluding anything: a row that is specified
but absent from the tag was never authored in the `.rfa`.

Also confirm **no row prints the same value twice** — 291 did, across tier 2/3 which 8 of
the 12 presentation modes show together. Fixed in #964; if you see a doubled value, the
deployed build predates it.

- [ ] room · [ ] door · [ ] window · [ ] no doubled values

---

## T3 · Room name in tier 2 — `ROOM-6`

The room tag rendered a blank where the name belongs for as long as it has existed:
`ASS_DESCRIPTION_TXT` was tier 2 row 3, populated from a loadable-family built-in that a
spatial element does not have.

### Steps

1. Place a `STING - Room Tag`.
2. **CREATE TAGS → Depth → Depth 2**.
3. The room name should appear **between the ISO code and `Area:`**.

**If it is blank:** the likely cause is that tier-2 row 3 was never authored in the `.rfa`,
**not** that the mapping is wrong. Press **Label Spec** to tell the two apart. The label
rows are hand-built; the Revit API cannot author them.

- [ ] name renders

---

## T4 · Cyclic renumber — `ROOM-1`

`RoomNumberPlanner` is Revit-free and has 24 unit tests. The Revit half is confirmed only by
the compiler. `NewRoomTag` is the first room-tag placement code in the plugin, and the
two-pass park/write in `RoomNumberingEngine.Apply` is the first place STING deliberately
writes a throwaway value it intends to overwrite.

### Steps — **SETUP → ROOM / SPACE**

1. **Number Schemes** (read-only) — what is available, what the default would do here.
2. **Renumber** over the active view.
   - [ ] preview lists what you expect
   - [ ] **Cancel writes nothing** — re-check a room's number after cancelling
   - [ ] a **cyclic case** (01→02, 02→03) succeeds rather than tripping Revit's
         duplicate-number rejection
   - [ ] no room left on a `~STING` placeholder
3. **Place Tags**, then **Place Tags again on the same view**
   - [ ] one tag per room, **not two**

The park/write pass is the thing under test. A leftover `~STING` number is the signature of
it failing halfway.

---

## T5 · Refusal path — `TOKPOL-2`

The token policy's refusal path (`fallback: null` → element skipped and counted) is
unit-tested but has **never run in Revit**, because the shipped baseline deliberately cannot
trigger it. Proving an error branch is reachable is the check this codebase most needs:
several defects in the 2026-08 audit were error handling that could never execute.

### Steps

1. Create `<project>/_BIM_COORD/tag_token_policy.json`:

```json
{"tokens":[{"token":"LVL","level":"MANDATORY","fallback":null,"why":"test"}]}
```

2. **TAGGING → Batch Tag** on a model containing a levelless element.
3. Confirm:
   - [ ] the element is **skipped**
   - [ ] it is counted under **REFUSED** in the result dialog
   - [ ] **no tag is written with a doubled separator** (`A--Z01-`)
4. **Delete the file.** Leaving it makes LVL mandatory for that project.

---

## T6 · SEQ reaches the tag — `BINDSCOPE-2` ⭐ the one that started Phase 294

A newly placed door tagged `A-BLD1-Z01-L01-ARC-FIT-DR-` — no sequence number, just a
trailing separator. `ASS_SEQ_NUM_TXT` was **greyed** in Properties (type-scoped), so the
per-element write returned false in silence.

⚠️ **The CSV fix alone will not change your model.** `CATEGORY_BINDINGS.csv` is read by the
binder when it RUNS; a project bound earlier keeps its old scope. Re-bind first.

### Steps

1. Re-run the shared-parameter binder on the test model.
2. Select a door → Properties → confirm **`ASS_SEQ_NUM_TXT` is no longer greyed**.
   Greyed = still type-scoped = the re-bind did not take, and nothing below will work.
3. Place a new door. **TAGGING → Auto Tag**.
4. Read `ASS_TAG_1_TXT`.

### Pass / fail

| Read | Pass | Fail |
|---|---|---|
| `ASS_SEQ_NUM_TXT` | greyed → **not** greyed | still greyed → re-bind did not apply |
| its value | `0215`-style | `-215` → separator glued to the serial, the old artifact |
| `ASS_TAG_1_TXT` | ends `…-DR-0215` | ends `…-DR-` → SEQ still not written |
| `ASS_TAG_6_TXT` | no leading separator | `- BLD1` → same disease, different container |

**Two doors of the same type must get different SEQ values.** That is the whole point of
the Instance binding, and it is the one check that proves the fix rather than the symptom.

- [ ] not greyed · [ ] SEQ in tag · [ ] two doors differ

---

## T7 · Material suffix survives — `TAGPROD-1`

`FSP-CON` and `DR-GLZ` were truncated to `FSP` / `DR` on every write because `-` is the tag
separator. No material suffix has ever reached a tag.

1. Tag an element whose category has a material PROD override (a fire-stop, a glazed door).
2. Read `ASS_PRODCT_COD_TXT`.

| Pass | Fail |
|---|---|
| `FSP_CON` — joined with `_`, not `-` | `FSP` → suffix still being truncated |

Confirm the log carries **no** `SetString: rejecting malformed ASS_PRODCT_COD_TXT` line. It
is the only signal this defect ever gave.

- [ ] suffix present · [ ] no malformed warning

---

## Results

| Test | Id | Result | Notes |
|---|---|---|---|
| T1 units | `MAPTYPE-1` | | |
| T2 revived rows | `TAGBIND-4` | | |
| T3 room name | `ROOM-6` | | |
| T4 renumber | `ROOM-1` | | |
| T5 refusal | `TOKPOL-2` | | |
| T6 SEQ reaches tag | `BINDSCOPE-2` | | |
| T7 material suffix | `TAGPROD-1` | | |

Record outcomes against the ids in `docs/ROADMAP.md`. **Report both numbers** — what was
expected and what appeared — rather than "works". An assertion that passes against an empty
result set is the failure mode this repo produces.

---

## What this protocol does not cover

Manual Family Editor work, because the Revit API cannot author label rows
(`TagFamilyCreatorCommand.cs:1496`) and cross-category label paste is blocked:

- **`UNITAG-2`** — one universal label, ~62 rows, in a single Edit Label session, then the
  Duct smoke test before scaling to 206. The actual critical path.
- **`ROOM-7`** — a name-first room tag, ~6 rows. Everything else about it is automatable.
- The **291 duplicate rows** still present in the shipped `.rfa` files.

⚠️ Params added to the field list are **lost if the Edit Label dialog closes** before the
rows are pushed. One session, rows pushed before OK.

Open decisions — `UNITAG-4` (universal vs per-category), `TAGBIND-3` (where door clear
width comes from), `UNITAG-3` (8 truncated family names), `TOKPOL-1`, `TAGDUP-2` — are
standards questions, not test outcomes. They live in `docs/ROADMAP.md`.
