# Tag / Room Test Protocol — Phases 287–294

Ten tests that can only be answered inside Revit, in the order they must be run, plus
**section U** — the manual universal-tag build, which is the critical path and the one thing
here that no code can do. Every fix
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

### While you are here — the derivation layer (`TOKPOL-1`, closed Phase 294)

Until this phase the policy governed the tag STRING but not the DERIVATION: `DetectZone` and
`DetectLoc` returned the literals `"Z01"` and `"BLD1"` straight from the source, so a project
that overrode those fallbacks still got the hardcoded pair written onto every element — and
the tag and the parameter it came from could disagree.

Put a LOC fallback in the same policy file and tag an element with no derivable location:

```json
{"tokens":[{"token":"LOC","level":"OPTIONAL","fallback":"SITE","why":"test"}]}
```

- [ ] `ASS_LOC_TXT` on that element reads `SITE`, **not** `BLD1`
- [ ] the log carries one `Token policy: 'LOC' could not be derived; using the policy
      fallback` line — once per session, not once per element

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

## T8 · Spaces derive LOC and ZONE — `LIGHTGRID-5` ⭐ needs a Spaces-based model

`GetRoomAtElement` is Room-only at every step, and both `DetectLoc` and `DetectZone` open by
calling it. On a model that uses **MEP Spaces** rather than Rooms, neither token could ever
be derived — both fell through to the policy fallback, looking exactly like a project that
had not set its location codes. Phase 294 added a Space fallback.

**This is the one thing nothing in CI can check.** A compiler, 1,542 tests and seven gates
cannot see a Revit Space.

### You need a model with MEP Spaces and no Rooms over the same area

If you only have architectural models, run **T9** instead and treat T8 as untested — saying
so is worth more than a green tick from the wrong model.

### Steps

1. Open the Spaces model. **TAGGING → Auto Tag** on a view with MEP elements.
2. Select a tagged element and read the Properties palette.

| Read | Pass | Fail |
|---|---|---|
| `ASS_LOC_TXT` | a derived code | the policy fallback (`BLD1` by default) |
| `ASS_ZONE_TXT` | a derived code | the policy fallback (`Z01` by default) |
| `ASS_ROOM_NAME_TXT` | the Space's name | blank |
| `ASS_ROOM_NUM_TXT` | the Space's number | blank |

### The evidence line — check this before interpreting anything

The run writes one line the first time a Space resolves a token:

```bash
grep 'LIGHTGRID-4 LIVE' "C:/Dev/wt-sting-live/CompiledPlugin/StingTools_<yyyyMMdd>.log"
```

- **Line present** → the Space path ran. Any blank token above is then a real defect.
- **Line absent** → the Space path never ran. The tokens tell you nothing, and the question
  is why: no Spaces in the view, elements already carrying values (these writes are
  SetIfEmpty), or a stale build. **Do not read absent tokens as a failed fix.**

- [ ] evidence line present · [ ] LOC derived · [ ] ZONE derived · [ ] name + number filled

---

## T9 · Architectural models are unchanged — `LIGHTGRID-5` (the regression half)

**This is the half that protects existing projects, and the one to run if you only have
architectural models.**

Rooms are resolved first and still win; a Space is only consulted where a Room produced
nothing. So on any model with Rooms the tokens must be **identical** to before.

### Steps

1. On an architectural model, note `ASS_LOC_TXT` / `ASS_ZONE_TXT` / `ASS_ROOM_NAME_TXT` /
   `ASS_ROOM_NUM_TXT` on a few tagged elements **before** deploying this build.
2. Deploy, re-tag the same elements, compare.

| Expect | Meaning |
|---|---|
| tokens identical | correct — room-first ordering held |
| any token **changed** | **stop.** A Space is overriding a Room, which the ordering is meant to prevent |
| `LIGHTGRID-4 LIVE` in the log | only expected if the model *also* has Spaces covering area no Room does |

A **mixed** model — architectural Rooms plus MEP Spaces over the same floor — is the most
valuable case here, because it is the common real one and the only one where the ordering is
actually load-bearing.

- [ ] tokens unchanged on a Rooms model · [ ] tokens unchanged on a mixed model

---

## T10 · Quick Lux on the Spaces model — `LIGHTGRID-2`

The cheapest single proof that the lighting suite reaches Spaces at all. Eleven commands
were converted; this one exercises the shared path.

1. On the Spaces model: **STING Electrical panel → Quick Lux** (or `QuickLuxEstimate`).
2. It should report **one row per Space**. Before Phase 294 it reported nothing at all and
   still claimed success.

If it lists spaces, `SpatialCompat.Collect` is working, and the other ten commands share it.

- [ ] rows per Space · [ ] fixture counts non-zero

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
| T8 Spaces derive LOC/ZONE | `LIGHTGRID-5` | | |
| T9 Rooms model unchanged | `LIGHTGRID-5` | | |
| T10 Quick Lux on Spaces | `LIGHTGRID-2` | | |

Record outcomes against the ids in `docs/ROADMAP.md`. **Report both numbers** — what was
expected and what appeared — rather than "works". An assertion that passes against an empty
result set is the failure mode this repo produces.

---

## U · The universal tag — the manual build (`UNITAG-2`)

This is the critical path, and it is the one thing in this repo that **cannot be automated**:
the Revit API cannot author label rows (`TagFamilyCreatorCommand.cs:1496`) and cross-category
label paste is blocked. One person builds **one** label by hand; `Propagate_UniversalTag`
then clones it to all 206 families.

### U.0 Where the authority is — do not work from this page alone

| Document | What it holds |
|---|---|
| [`UNIVERSAL_TAG_LABEL_BUILD_SHEET.md`](UNIVERSAL_TAG_LABEL_BUILD_SHEET.md) | **THE row list — all 65 rows, exact Calculated-Value names, formulas, prefix, suffix, break.** Work from this table, cell by cell. |
| [`UNIVERSAL_TAG_FIELDLIST_ADD_ORDER.md`](UNIVERSAL_TAG_FIELDLIST_ADD_ORDER.md) | The order to add parameters to the field list |
| [`UNIVERSAL_TAG_DUCT_SMOKE_TEST.md`](UNIVERSAL_TAG_DUCT_SMOKE_TEST.md) | The one-family proof to run **before** scaling to 206 |
| [`UNIVERSAL_TAG_MANUAL_CONFIG_GUIDE.md`](UNIVERSAL_TAG_MANUAL_CONFIG_GUIDE.md) | Family-editor configuration around the label |
| [`UNIVERSAL_TAG_CONFORMANCE.md`](UNIVERSAL_TAG_CONFORMANCE.md) | What "done" means for a propagated family |

The row list is **not duplicated here on purpose.** Two copies of 65 formulas drift, and the
copy someone reads is then the wrong one. This section covers only the mechanics that cost
you the session if you get them wrong.

### U.1 The shape of the label

**65 rows, one label.** Tier visibility is driven by `TAG_PARA_STATE_n_BOOL`, so every
non-T1 row is a Calculated Value wrapping its parameter in an `if()`:

```
if(TAG_PARA_STATE_2_BOOL, ASS_DESCRIPTION_TXT, "")
```

Rows per tier — use this to check you have not lost one:

| T1 | T2 | T4 | T5 | T6 | T7 | T8 | T9 | T10 | total |
|---|---|---|---|---|---|---|---|---|---|
| 1 | 6 | 6 | **21** | 6 | 6 | 6 | 6 | 7 | **65** |

T5 is the big one at 21 rows. There is no T3 — it was dropped deliberately.

### U.2 Four mechanics that bite

⚠️ **ONE Edit Label session.** Parameters added to the field list are **lost if the dialog
closes** before the rows are pushed into the label. Budget the time in one sitting; do not
"just check something" in another dialog halfway through.

⚠️ **Set Spaces = 0 BEFORE ticking Break.** Spaces is only editable while the row *above*
has no Break. Tick Break first and you cannot go back without unpicking the row above.

⚠️ **Every row: Spaces = 0, Type = Text.** Type=Text for every calculated value, including
the numeric ones — the label renders text, which is the whole reason the `_TXT` mirrors
exist.

⚠️ **A YESNO parameter is written BARE in a formula.** `and(TAG_WARN_VISIBLE_BOOL, …)`, never
`TAG_WARN_VISIBLE_BOOL = "Yes"` — comparing a YESNO to a string fails with Revit's
*Inconsistent Units*. (`LABEL_DEFINITIONS.json` claimed the opposite until `614aba59b`.)

### U.3 Order of work

1. **REMOVE the 9 rows** listed in the build sheet's Step 1 — `HVC_DCT_FLW_CFM`,
   `HVC_VEL_MPS`, `MNT_HGT_MM` and all six T3 rows. Select the row, click the left arrow.
2. **Build / verify all 65 rows in order** from the Step 2 table. For each non-T1 row:
   Name → Type = Text → paste Formula → Prefix / Suffix → Spaces = 0 → Break.
3. **Badges (optional, Step 4)** — 6 glyphs on subcategory `STING_TagStatus`, 6 family
   Yes/No params driving their `Visible` property. The plugin already stamps
   `STING_GATE_DATA_STATUS_INT` / `STING_GATE_QA_STATUS_INT` (**CREATE → Stamp Gates**), so
   the badges need no per-family logic. Turning the subcategory off in a print view template
   is how they stay screen-only.
4. **Duct smoke test** — one family, per `UNIVERSAL_TAG_DUCT_SMOKE_TEST.md`. Confirm the
   nested badge symbols survive SaveAs + recategorise; that is the item most likely to break.
5. **Only then** `Propagate_UniversalTag` to the remaining families.

### U.4 What propagation cannot fix

The 291 duplicate rows and the misleading door `Clear:` row were removed from
`LABEL_DEFINITIONS.json`, **not** from the shipped `.rfa` files. Families that are *replaced*
by the universal label inherit the clean one. Any family you keep bespoke still carries the
old rows, and no gate can see inside an `.rfa`.

- [ ] 9 rows removed · [ ] 65 rows built · [ ] badges (optional) · [ ] duct smoke test ·
  [ ] propagated

---

## What this protocol does not cover

Manual Family Editor work is now covered in **section U above** — `UNITAG-2` was previously
listed here as out of scope and is not any more. What remains outside this protocol:
- **`ROOM-7`** — a name-first room tag, ~6 rows. Everything else about it is automatable.
- The **291 duplicate rows** still present in the shipped `.rfa` files.

⚠️ Params added to the field list are **lost if the Edit Label dialog closes** before the
rows are pushed. One session, rows pushed before OK.

Open decisions — `UNITAG-4` (universal vs per-category), `TAGBIND-3` (where door clear width
comes from), `UNITAG-3` (8 truncated family names), `ISO19650DISC-2` / `-4` (Uniclass cover
and whether DISC means role or system), `TAGDUP-2` — are standards questions, not test
outcomes. They live in `docs/ROADMAP.md`. (`TOKPOL-1` was on this list and is now closed.)
