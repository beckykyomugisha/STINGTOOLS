# In-Revit Smoke Test — Symbol / SLD / DWG-MEP (branch `claude/symbol-sld-only`)

One-sitting accuracy test for the symbol library, SLD render, standard switching,
and DWG-to-MEP work (P0-1…P1-3 + F1…F9). Run on the machine that has StingTools
installed. ~30–45 min. Goal is **accuracy**, not stub-hunting.

**Golden rule for logging results:** classify every failure as either
- **BLANK** = a family/symbol didn't load → path/precondition problem (recoverable), or
- **WRONG** = it loaded but the value/shape/behaviour is incorrect → a real defect worth a ticket.
Keep the two separate — they have different fix paths.

---

## 0. Deploy the branch under test

1. Close Revit.
2. Build Release from the worktree and deploy to the live add-in folder:
   ```
   dotnet build C:/Dev/STINGTOOLS-wt/symbol-only/StingTools/StingTools.csproj -c Release -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"
   ```
   Copy `StingTools.dll` + `Newtonsoft.Json.dll` + `ClosedXML.dll` + the `data/` folder
   to `C:\Dev\STING_PLACEMENT_GOLD` (the folder the `.addin` points at). Confirm the
   DLL timestamp updated.
3. **Family template path (the #1 silent precondition):** Revit → Options → File
   Locations → Family Template Files → must point at a folder containing
   `Metric Generic Annotation.rft`, `Metric Detail Item.rft`, `Metric Title Block.rft`.
4. Open a small test project with a few walls, a room, and a handful of MEP elements
   per discipline. Load shared params (CREATE → `LoadSharedParams`). Keep
   `StingTools.log` (next to the DLL) open in a tail viewer throughout.

---

## A. Build the library + the latent-bug gate (MUST PASS FIRST)

> This is the highest-value check. Before the fix, `STING_ELEC_SYMBOLS.json` and
> `STING_FP_SYMBOLS.json` failed C# (Newtonsoft) deserialization on a bad `startDeg`
> value → **0 families** for both, silently. Confirm they now build.

| # | Action | Expected PASS | If FAIL |
|---|---|---|---|
| A1 | TAGS → Symbols → **Preflight** (`Symbols_Preflight`) | Dialog reports template folder + Generic Annotation/Model `.rft` = **PASS**. | If FAILED → fix §0.3 before continuing. |
| A2 | TAGS → Symbols → **★ Create All** (`Symbols_CreateAll`) | Report shows **0 catalogues produced 0 families**. | If any catalogue = 0 → note which; almost always template path (BLANK). |
| A3 | **Electrical + Fire catalogues built** (the latent-bug gate) | Report Created/Existing for Electrical and FireProt are **non-zero** (~ELEC 79, FP 60). | If ELEC or FP = 0 → the arc-repair regressed (WRONG — stop, report). |
| A4 | On disk: `<project>/_BIM_COORD/Families/Symbols/SLD/IEC/` | Contains `.rfa` (e.g. `SLD_MCB.rfa`, `SLD_MOTOR.rfa`, `ELEC_TRANSFORMER.rfa`). | Empty → generation wrote nowhere (BLANK). |

Expected per-catalogue new-symbol counts (added this branch): ELEC +17, FP +11,
Lighting +11, MEP +7, PipeAcc +7, Plumbing +10, SLD +10 (**73 total**).

---

## B. SLD render — the P0-1 fix (symbols load from the right folder)

| # | Action | Expected PASS | If FAIL |
|---|---|---|---|
| B1 | Circuit a small board (a panel + 2–3 circuits). | Circuits assigned. | — |
| B2 | Run **`SLD_Generate`** on that board. | SLD drafting view renders **with symbols placed** (real FamilyInstances, not blank). `StingTools.log` shows `PlaceSymbols: auto-loaded …/Families/Symbols/SLD/IEC/…`. | If symbols blank + log says "family not found — run Symbols_CreateAll": P0-1 regressed or A2 didn't build (BLANK). |
| B3 | Inspect topology. | Breaker→load hierarchy matches the circuits. | Mismatch = WRONG. |

---

## C. Symbol accuracy — F1 params, F2/F7 weights, F3 glyphs, F4 fills

| # | Action | Expected PASS | If FAIL |
|---|---|---|---|
| C1 | Select a regenerated **`SLD_MOTOR`** (from the SLD or the built library) → Edit Family → check parameters. | Has `CIRCUIT_REF`, `RATING_A`, `LABEL`, `HP_KW` — **not** an empty list (F1). Sockets show CIRCUIT_REF/RATING_A; valves SIZE_MM; etc. | Empty params = F1 regressed (WRONG). |
| C2 | Look at **`SLD_MOTOR`** rendered. | A circle with a readable **"M"** inside (F3 — the M is a text element). | Bare circle, no M = WRONG. |
| C3 | Look at **`ELEC_TRANSFORMER`**. | **Two overlapping circles** (F3 arc-repair). | One circle / stray line / parse-fail = WRONG. |
| C4 | Line-weight spot check: put several symbols on one view incl. an **ISO6412** spool symbol and a **Lighting** symbol. | Curves on different subcategories plot at **visibly different weights** (F2/F7: ISO6412→4, Lighting→3, Wire→2, AirTerminal→4). Toggle a subcategory in VG to confirm it exists. | All uniform weight = F2 alias-matching not applied (WRONG). |
| C5 | Edit `data/Symbols/STING_LINE_WEIGHTS.json` (or add `<project>/_BIM_COORD/line_weights.json`), change a weight, re-run `Symbols_CreateAll`. | New weight applied — **no recompile** (F7 flexibility). | No change = registry not reading the file (WRONG). |
| C6 | Confirm fill-only symbols (`LTG_SURFACE_SQ`, `STR_BEAM_SECT`, `STR_COLUMN_SECT`). | Render **solid** (F4). Any outline fallback is logged + listed in the Create All report, never silent. | Blank = FilledRegionType creation failed (BLANK); silent outline = F4 regressed (WRONG). |

---

## D. Standard switching — P2-2 swap + F6 guard

| # | Action | Expected PASS | If FAIL |
|---|---|---|---|
| D1 | With only IEC built, **`Symbols_SwitchProject`** → pick **NFPA** (or any standard whose library you did NOT build). | Prominent dialog **"No 'NFPA' symbol families are built"** offering **"Build the symbol library now"** or Cancel — **not** a silent "0 swapped" (F6). | Silent success/no dialog = F6 regressed (WRONG). |
| D2 | Build IEEE (`Symbols_CreateAll` covers all standards), then `Symbols_SwitchProject` → **IEEE**. | Report: N **tags** updated **and** M **model symbol instances swapped** (P2-2); the model line reads "M swapped" (or a ⚠ line if 0 swapped but skips exist). SLD symbols resolve `IEEE_SLD_*`. | Only tags swap / instances corrupt across categories = WRONG. |
| D3 | Inspect a swapped instance. | Correct IEEE family, same category, not deleted/corrupted. | Wrong/broken element = WRONG (P2-2 category guard failed). |

---

## E. Orientation audit — F5 (honest gap surfacing)

| # | Action | Expected PASS | If FAIL |
|---|---|---|---|
| E1 | TAGS → Symbols → **Orientation Audit** (`Symbols_OrientationAudit`). | Dialog lists: `Concepts declaring orientationStates: N`, `variant families referenced: M`, `referenced-but-MISSING: K` — the gap is now **explicit**, not hidden. (Expect a list of missing variants — that's the point.) | Command missing / errors = WRONG. |

---

## F. Preflight + one-run workflow — F8

| # | Action | Expected PASS | If FAIL |
|---|---|---|---|
| F1 | Temporarily **unset** the family template path (§0.3), run Workflow presets → **"Symbols and SLD"**. | Stops at the **Preflight** step with the fix message (rollback on failure) — does not silently build 0 families. | Proceeds and produces 0 families silently = F8 regressed (WRONG). |
| F2 | Restore the path, re-run the **"Symbols and SLD"** workflow. | Preflight passes → builds all catalogues → generates the SLD in **one run**. | Any step silently no-ops = WRONG. |

---

## G. DWG-to-MEP — P1-3 (stage-1)

| # | Action | Expected PASS | If FAIL |
|---|---|---|---|
| G1 | Import (not link) an MEP DWG with duct + panel/socket blocks on recognised layers (names matching socket/switch/data/emergency/etc.), then MODEL → **DWG to Model**. | Result summary lists "… N MEP fixtures, M MEP runs …" alongside walls/floors; blocks matched by the fixture map place as families, rotated per the block transform, and are auto-tagged (ISO 19650). | Nothing placed on a drawing that clearly has MEP blocks = check block names vs `STING_DWG_FIXTURE_MAP.json` (see G2). |
| G2 | Note any fixtures with **no matching family**. | Reported as a **warning with the precise skip reason** — never silently dropped (P1-3). Unmatched block names are listed (extend the fixture map / load the family). | Silent discard = WRONG. |
| G3 | (Optional) Check the import was read **once** (F9a). | `StingTools.log` shows a single geometry extraction, not two. | Two extractions = F9a regressed. |

---

## Results log

| Step | PASS / FAIL | BLANK or WRONG | Note |
|---|---|---|---|
| A1 Preflight | | | |
| A2 Create All (0 empty) | | | |
| **A3 ELEC+FP build (latent-bug gate)** | | | |
| A4 .rfa on disk | | | |
| B2 SLD symbols render | | | |
| B3 SLD topology | | | |
| C1 new-symbol params | | | |
| C2 motor "M" | | | |
| C3 transformer 2 circles | | | |
| C4 line weights differ | | | |
| C5 weights data-driven | | | |
| C6 fills solid | | | |
| D1 switch guard (unbuilt) | | | |
| D2 instances swap | | | |
| D3 swap not corrupt | | | |
| E1 orientation audit | | | |
| F1 preflight blocks | | | |
| F2 one-run workflow | | | |
| G1 DWG MEP places | | | |
| G2 skip-with-reason | | | |

**Merge gate:** A3 + B2 + D3 must PASS (families build, SLD renders, no instance
corruption). C/E/F/G failures classified WRONG should be ticketed before merge;
BLANK failures are usually §0 preconditions, not code.
