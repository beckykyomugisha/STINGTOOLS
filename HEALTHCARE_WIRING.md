# Healthcare action wiring — DESIGN (no code yet)

**Branch:** `feature/wire-healthcare-actions` (off `main`).
**Status:** design-first — **PAUSED for your review** before implementing.

## Re-verification: my earlier TODO was mostly wrong (don't trust it)

I re-derived from the code, not the TODO. Two "blockers" the TODO flagged are **false**:

1. **"Selection model doesn't persist / SetHealthcareOptions flushes none."** ❌ False.
   `StingDockPanel.HealthcareTab.cs:SetHealthcareOptions()` flushes a rich `Hc.*`
   extra-param set on every `Healthcare_*` dispatch, and `Core/HcOptions.cs` is a
   complete typed reader for it — including `SelectedValidators()` (ticked validator
   rows), `RdsPickedRooms()` (ticked room rows), `RadCalcType`, `MgasStep/Gas/Zone`,
   `SpecialistKind`, and a `CancelRequested`/`ClearCancel()` signal. **No selection-model
   change is needed.**

2. **"Dynamic `Healthcare_<kind>` tags are unhandled."** ❌ False. `StingCommandHandler`
   already has explicit cases for every specialist kind (`Healthcare_HybridOr`,
   `Healthcare_Dialysis`, `Healthcare_Hbo`, … lines 275–282) plus all 16 validators,
   MGAS, RadCalc{Chest,Ct,Linac}, MRI, RDS issue/batch. The dynamic tags from
   `HcSpecialistRun_Click` dispatch to real commands today — **not silent**.

So the only genuinely-silent tags are the **5 static action buttons**, and every one
maps to an **already-existing command** that already reads the selection model.

## Per-tag wiring map (verified)

| Silent tag | Reads (HcOptions) | Target command (exists ✓) | Notes |
|---|---|---|---|
| `Healthcare_RunSelected` | `SelectedValidators()` | `Healthcare.HealthcareRunAllValidatorsCommand` | **Already honors the ticked subset** (line 113–119: picked>0 ⇒ `RunSelectedHealthcareValidators.Validate(doc, picked)`, else run-all). Pure route. |
| `Healthcare_IssueSelectedRds` | `RdsPickedRooms()` | `Healthcare.BatchIssueRoomDataSheetsCommand` | Already reads `Hc.Rds.PickedRooms` (empty ⇒ all candidates). Pure route. |
| `Healthcare_MgasVerifyStep` | `Mgas.Step/Gas/Zone` | `MedGas.MgasVerifyCommand` | Pure route. ⚠️ command does **not** read `Hc.Mgas.Step` yet → runs the full verify, ignoring the selected step. Right action; step-granularity is a later opt-in. |
| `Healthcare_RadCalcInline` | `RadCalcType` ∈ {Chest, CT, LINAC, **Custom**} | Chest→`Radiation.RadCalcChestRoomCommand`, CT→`RadCalcCtRoomCommand`, LINAC→`RadCalcLinacVaultCommand` | **Custom has no dedicated command** → see ambiguity below. |
| `Healthcare_Cancel` | — | (local) | Sets `Hc.CancelRequested=1` + clears the inline result strip. ⚠️ see Cancel note. |

## The one genuine ambiguity — RadCalc "Custom"

`cmbHcRadCalc` offers Chest / CT / LINAC / **Custom (inline inputs)**. Chest/CT/LINAC
route 1:1 to the three existing commands. **"Custom" has no dedicated command.** Per your
"never silently run the wrong command" rule, I will **not** guess — instead show a small
chooser when `RadCalcType == "Custom"` (or any unrecognised value):

> *"Custom radiation calc — apply the inline inputs (kVp/W/U/T/D) as which barrier type?"*
> **[ Chest room ] [ CT room ] [ LINAC vault ] [ Cancel ]**

…then run the chosen command. (The inline inputs are already in `Hc.Rad.*` for any
command that opts to read them.)
**Your call:** is that the behaviour you want, or should "Custom" route straight to one
specific command (e.g. CT)? I'll default to the chooser unless you say otherwise.

## The Cancel note (your call)

`HcOptions.CancelRequested` / `ClearCancel()` exist, but **no command polls them yet**
(verified: zero readers). So a cooperative cancel has no effect today. Proposed
`Healthcare_Cancel` behaviour: **set `Hc.CancelRequested=1`** (forward-compatible — future
long-running validators can poll it) **and** clear the inline result strip + show a
"Cancelled / cleared" status. That gives an immediate visible effect now and the right
signal later. Alternative: make it a pure UI reset (no flag). **Default: flag + UI reset.**

## Dispatcher design

The dynamic `Healthcare_<kind>` and the 30+ validator/specialist tags are already explicit
cases (matched first). I'll add a **`Healthcare_` branch in the switch `default`** (next to
the existing `ZoomToIssue_`/`SelectByDisc_` dynamic-prefix routing) that calls one resolver:

```
default:
   ...existing dynamic prefixes...
   if (tag.StartsWith("Healthcare_")) { ResolveHealthcareAction(app, tag); break; }
   // unknown tag → existing "Unrecognised command" dialog
```

`ResolveHealthcareAction(app, tag)`:
- `Healthcare_RunSelected`      → `RunCommand<HealthcareRunAllValidatorsCommand>`
- `Healthcare_IssueSelectedRds` → `RunCommand<BatchIssueRoomDataSheetsCommand>`
- `Healthcare_MgasVerifyStep`   → `RunCommand<MgasVerifyCommand>`
- `Healthcare_RadCalcInline`    → switch `HcOptions.RadCalcType`: Chest/CT/LINAC → the 3
  commands; Custom/unknown → **chooser** (above)
- `Healthcare_Cancel`           → `SetExtraParam("Hc.CancelRequested","1")` + clear result strip
- anything else `Healthcare_*`  → clear "not yet wired: <tag>" dialog (**no silent fall-through**)

Why a default-branch resolver (not 5 explicit cases): it future-proofs the whole
`Healthcare_` family — any new healthcare button that lacks an explicit case gets a clear
message instead of silently doing nothing (avoids re-creating this exact problem). Explicit
cases still take precedence, so nothing existing changes.

## Summary of what implementation will do
- **Wire 5 tags** (4 pure routes + RadCalcInline with a Custom chooser).
- **Selection model:** no change (already complete) — corrects the TODO.
- **Cancel:** flag + UI reset (pending your confirmation).
- **Ambiguity left to you:** RadCalc "Custom" → chooser vs fixed target.
- **Untouched:** Scale sliders.

**Decisions I need from you:** (1) RadCalc "Custom" → chooser (my default) or a fixed
command? (2) Cancel → flag+reset (my default) or pure reset?

---

## IMPLEMENTED (per your decisions)

All 5 silent buttons wired through a `Healthcare_` resolver in the `StingCommandHandler`
switch `default` (explicit `Healthcare_<X>` cases still take precedence; unknown
`Healthcare_*` now gets a clear "not yet wired" dialog instead of silence).

| Tag | Wired to | Behaviour |
|---|---|---|
| `Healthcare_RunSelected` | `HealthcareRunAllValidatorsCommand` | runs the ticked validator subset (else all) |
| `Healthcare_IssueSelectedRds` | `BatchIssueRoomDataSheetsCommand` | issues RDS for the ticked rooms (else all) |
| `Healthcare_MgasVerifyStep` | `MgasVerifyCommand` | full NFPA 99 verify (see relabel) |
| `Healthcare_RadCalcInline` | RadCalc Chest/CT/LINAC by `RadCalcType`; **Custom/unknown → chooser** | chooser cancel = no-op |
| `Healthcare_Cancel` | (local) | sets `Hc.CancelRequested` + clears result strip |

### (1) RadCalc "Custom" → CHOOSER ✓
`RunHealthcareRadCalcInline` routes Chest/CT/LINAC directly; for "Custom" (or any
unrecognised type) it shows a 3-option TaskDialog (Chest / CT / LINAC) + Cancel.
**Cancelling the chooser is a no-op** — never guesses a command.

### (2) Cancel — path TAKEN: FLAG + RESET + step-boundary poll (+ honest tooltip & TODO)
- `Healthcare_Cancel` sets `Hc.CancelRequested=1` and clears the inline result strip
  (`StingDockPanel.ClearHcResultStrip`).
- **Added the cooperative poll** at the validator-loop step boundary in BOTH
  `RunAllHealthcareValidators` and `RunSelectedHealthcareValidators`: before each
  validator it checks `HcOptions.CancelRequested`, stops cleanly if set, and clears
  the flag.
- **Honest limitation (why the poll isn't a true mid-run stop today):** the dock-panel
  dispatch is **synchronous on the Revit API thread**, so a Cancel click is only
  processed *after* the running command returns — it cannot interrupt an in-flight
  run. The poll is therefore the correct step hook but only fires once runs are
  chunked/async. The Cancel button tooltip says this plainly, and it's a **TODO**.
  (I did NOT force an async/background refactor — that touches the whole dispatch
  pipeline and is out of scope, per your "don't force if invasive" guidance.)

### (3) MGAS Verify relabel ✓
`MgasVerifyCommand` ignores `Hc.Mgas.Step` and runs the FULL verify. The button was
relabelled **"Run step" → "MGAS Verify (Full)"** with a matching tooltip so the label
doesn't lie. **TODO:** make `MgasVerifyCommand` honour `HcOptions.MgasStep` to run a
single NFPA 99 §5.1.12 step.

### Selection model
**No change** — `SetHealthcareOptions` + `HcOptions` already capture everything the
resolver needs (the TODO's "flushes none" blocker was false).

## Outstanding TODOs (tracked here)
1. `MgasVerifyCommand`: honour `HcOptions.MgasStep` for a single-step run.
2. Cancel: true mid-run cancellation needs the Healthcare run pipeline to be chunked /
   async so the Cancel click can be processed while a run is in flight. The
   step-boundary poll is already in place for that day.
