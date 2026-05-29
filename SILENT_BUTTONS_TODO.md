# Silent Buttons — Phase 1 audit + TODO

**Branch:** `fix/silent-buttons-phase1` (off `main`). Build 0 errors / 0 warnings.

## Audit correction (the "141" hypothesis was ~96% false-positive or tab-removable)

The 141 figure counted **every** `Tag="…"` in the main panel XAML against the
`StingCommandHandler` switch only. Re-derived across **all** main-panel dispatch
surfaces (CommandRegistry modules + switch + dynamic prefixes + code-behind
local handlers) and restricted to controls that actually dispatch
(`Click="Cmd_Click"`), the breakdown of the original 141 is:

| Bucket | Count | Verdict |
|---|---|---|
| `Hvac_*` buttons on the redundant main **HVAC** tab | ~21 | genuinely silent → **removed with the tab** |
| `MAT_*` buttons on the redundant main **MAT** tab | ~35 | genuinely silent → **removed with the tab** |
| `<ComboBoxItem>` / `<TabItem>` `Tag` values (facility profiles, gas types, zones, specialists, IoT transports, USP, workflow-name pickers, numerics) | ~68 | **FALSE POSITIVE** — combo/tab options read as *values* by code-behind, not buttons |
| `Cat*` buttons (`CatAll/None/Invert/MEP/Arch/Str/Plb`) | 7 | **FALSE POSITIVE** — handled locally by `CatQuick_Click`, not `Cmd_Click` |
| `CycleTheme` | 1 | **FALSE POSITIVE** — handled locally in `Cmd_Click` (returns before dispatch) |
| `Healthcare_*` action buttons | 5 | **genuinely silent** → see below |

So: **~61 real silent buttons removed for free** by deleting the HVAC + MAT
tabs; **~76 were never silent** (combo options / locally-handled); **5 remain
genuinely silent** on the Healthcare tab.

### Verified vs the hypothesis
- **False positives in the 141:** ~76 (68 combo/tab options + 7 `CatQuick` + 1 `CycleTheme`).
- **True silent (main panel):** 5 (`Healthcare_*`), after the 61 HVAC/MAT removals.
- **Audit missed:** that `Tag` is carried by non-button elements (ComboBoxItem/TabItem)
  and that some buttons use local Click handlers (`CatQuick_Click`) — both must be
  excluded. Also the dispatch order is **CommandRegistry-first, then switch**.

## Tabs removed (Phase 1 §2)
- Main-panel **HVAC** top-level tab — every one of its 21 `Hvac_*` tags is a strict
  subset of the dedicated **STING HVAC** panel's 66 handled tags → nothing lost.
- Main-panel **MAT** tab + 6 sub-tabs (Browse/Layers/Assets/Duplicates/Library/I/O) —
  its `MAT_*` buttons were already silent (dead); the live material UI is the
  dockable **Material Hub** pane + the 7-tab Material Manager dialog → nothing lost.
- Dead code-behind removed: MAT-grid selection-sync (`SubscribeSelectionSync`,
  `OnRevitSelectionChanged`, `HighlightMaterials`) + its `StingCommandHandler` caller,
  and the MAT-tab XAML event handlers (`MatSearch_TextChanged`, `MatFilter_Changed`,
  `MatRegion_Changed`, `MatGrid_*`, `MatBtn_Click`). The `MatActions` API surface
  (`GetCachedMaterialRows`/`SetDuplicateRows`/`GetDuplicateRows`/`GetSelectedAssetKind`/
  `ShowMaterialsTab`) was **kept** — `MatActions.cs` still calls it.

---

## TODO — genuinely-silent buttons (ambiguous; need a dedicated Healthcare round)

These 5 use `Click="Cmd_Click"` (dispatch to `StingCommandHandler`) but **no
surface handles them**. They are real action buttons (not value-setters, not
dead/dup), so they must be **wired** — but correct wiring needs the Healthcare-tab
**selection model**, which is not yet flushed into extra-params (`SetHealthcareOptions`
sets none for these), and several have multiple candidate targets. Guess-wiring
would risk running the wrong command, so they are parked here.

| Silent tag | Button | Candidate target command(s) | Blocker / decision needed |
|---|---|---|---|
| `Healthcare_RunSelected` | "Run selected" (Validators sub-tab) | `Commands/Healthcare/HealthcareValidatorCommands.cs` (16 validators) | Need a `Healthcare_`-prefix dispatcher that reads the *selected* validator (combo) and runs it. No selection param is flushed today. |
| `Healthcare_MgasVerifyStep` | "Run step" (MGPS sub-tab) | `Commands/MedGas/MgasVerifyCommand.cs` | Confirm "step" semantics (single NFPA 99 §5.1.12 step vs full 12-step run) before wiring. |
| `Healthcare_RadCalcInline` | "Run inline" (Radiation sub-tab) | `Commands/Radiation/RadCalc{ChestRoom,CtRoom,LinacVault}Command.cs` | 3 variants — needs the room-type selection (combo) to pick the right calc. |
| `Healthcare_IssueSelectedRds` | "Issue selected" (Rooms/RDS sub-tab) | RDS issue path (see `WORKFLOW_RdsIssue.json` / RDS engine) | Need the selected-RDS list binding + the issue command. |
| `Healthcare_Cancel` | "Cancel" (Validators sub-tab) | (local) | Likely a local UI cancel/clear — convert to a code-behind handler, not a dispatch. |

### Related (dynamic, not a static button — flagged for the same round)
`HcSpecialistRun_Click` re-tags its Run button to `Healthcare_<kind>` at click time
(e.g. `Healthcare_HybridOr`, `Healthcare_Dialysis`, `Healthcare_LINAC` …). Those
**dynamic** tags are also unhandled by `StingCommandHandler` → silent at runtime.
The same `Healthcare_`-prefix dispatcher should cover them. (Not visible to a static
XAML scan; flagged here so the Healthcare round catches it.)

**Recommendation:** a single Healthcare round that adds a `Healthcare_*` prefix
dispatch (mirroring the `ZoomToIssue_` etc. dynamic-prefix routing), reading the
active Healthcare sub-tab's selection, and routing to the candidate commands above.

---

## Cross-panel findings (dedicated panels — OUT of main-panel Phase-1 scope)

Scanned every panel XAML (not just the main one). Plumbing + LPS are clean. The
following dispatch buttons in **dedicated** panels appear unhandled by their own
handler / the main handler / modules — flag for a follow-up (NOT touched here):

- **STING HVAC panel:** `DocPackage` (RPRT tab) — verify it isn't handled via the
  fall-through to `StingCommandHandler`.
- **STING Electrical panel:** `Circuit_AssignAuto`, `Validation_BS7671` — verify
  against `StingElectricalCommandHandler`.
- **Material Hub:** `HUB_Help`, `HUB_Refresh`, `HUB_Settings` use the panel's **local**
  `HubBtn_Click` handler (not the main dispatch) — confirm that handler covers them;
  not silent if so.

---

## Counts (this PR)
- **Verified-silent vs the 141 hypothesis:** true silent buttons = **5** main-panel
  Healthcare actions (the rest were tab-removable (~61) or false positives (~76)).
- **Removed:** 2 redundant top-level tabs (HVAC, MAT) + 6 MAT sub-tabs + ~61 silent
  buttons + dead code-behind (selection-sync + 7 MAT XAML handlers).
- **Wired:** 0 (none cleanly wireable without the Healthcare selection model).
- **Converted:** 0.
- **TODO (parked):** 5 Healthcare actions (+ dynamic specialist tags) + 3 cross-panel
  candidates.
- **Untouched:** TAGS → Scale sub-tab and all scale sliders.
