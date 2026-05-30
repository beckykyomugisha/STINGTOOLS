# Phase B Round 2 — SETUP tab redesign plan

Audit-first plan for the SETUP tab redesign per
`docs/UI_PHASE_B_PATTERNS.md`. Round 2 follows the methodology
INTEROP Round 1 used in commit `60fea4493`: read the tab, classify
each section against the decision tree, pick the right pattern,
commit the plan first, then apply XAML + code-behind edits in one
follow-up commit.

## Tab location

`StingTools/UI/StingDockPanel.xaml` lines 1806–2100.
~160 dispatch tags across 16 sections.

## Per-section pattern decisions

Decision tree applied per `docs/UI_PHASE_B_PATTERNS.md`.

| # | Section (current TextBlock label) | Buttons | Pattern | Rationale |
|---|---|---|---|---|
| 1 | ⚙ SETUP | 5 | **Pattern 5 + 6** — primary `Project Setup Wizard` stays proud, secondaries (Master Setup / Retrofit / Create Params / Check Data) into Expander | One starring primary action; Retrofit + diagnostics are power-user follow-ups |
| 2 | MATERIALS | 6 | **Pattern 5 + 6** — primary `Material Manager` stays out, BLE + MEP + PBR ops into Expander | Material Manager covers 95% of the user need; bulk-creation buttons are infrequent setup ops |
| 3 | FAMILY TYPES | 8 | **Pattern 1 + 6** — chip row, short labels already in place | All 8 are independent immediate-action category creators; perfect chip-row fit |
| 4 | SYMBOLS & DEVICES | 24 | **Pattern 5 + 6** — keep `★ Create All Symbols` + `★ Set Profile` + `★ Place MEP Symbols` out; fold the other 21 into three named Expanders (Standards · Augment · Heal/Coverage) | Three clear power-user buckets; 4 wrap-panels currently is the worst space hog on the tab |
| 5 | SLD GENERATOR | 6 | **Pattern 5 + 6** — `★ Generate SLD` stays out, 5 sub-ops (Options/Update/SyncToggle/Validate/MigrateLabels) into Expander | One primary action surrounded by maintenance ops |
| 6 | SCHEDULES | 9 | **Pattern 5 + 6** — `★ Schedule Wizard` + `Full AutoPop` + `Excel Link` stay out; rest (Batch/Mat-Takeoff/AutoPop-Tok/Formulas/Export-CSV/BOQ) into Expander | Three starring entry points; the rest are advanced |
| 7 | MEP SCHEDULES | 7 | **Pattern 3 (ComboBox) + Pattern 6** — single ComboBox `MEP schedule type` (Panels/Lighting/Mech/Plumb/Fire/Elec) + `Create` Run button, with `Batch MEP` as a separate primary button above | 6 discipline-keyed peers fold cleanly into a picker; Batch creates them all |
| 8 | ROOM / SPACE | 9 | **Pattern 5 + 6** — keep `Room Push` + `Connectivity` primary; rest (Audit/DataDrop/Weekly/COBie Import/Zone/Sched/Export) into Expander | Two daily-use actions; rest are periodic |
| 9 | VIEW TEMPLATES | 6 | **Pattern 1 + 6** — chip row, already short labels | All 6 are independent setup actions; perfect chip-row fit |
| 10 | TEMPLATE MANAGER | 15 | **Pattern 5 + 6** — keep `★ Dashboard` + `Wizard` primary; the other 13 (Auto-Assign/Audit/VG Audit/Diff/Comply/AutoFix/Sync VG/Apply VG/Clone/VG Reset/Fam Params/Fam Processor/Tmpl Sched) into Expander | Dashboard subsumes 95% of operations; the 13 are advanced maintenance |
| 11 | STYLES | 5 | **Pattern 1 + 6** — chip row, short labels | All 5 are independent style-table creators |
| 12 | DATA QA | 20 | **Pattern 4 (CheckBox grid) + Pattern 5 + Pattern 6** — split into 2 grids/expanders: **Validators suite** (Val Tmpl / Dyn Bind / Schema / Clash Detect / X-Model Clash / MEP Clearance / Naming Audit / IFC Validate / gbXML Check) as CheckBox grid with `Run selected` runner; **Exchange & reports** (Productivity / Tasks / Notify Prefs / IFC Export / COBie / JSON / Excel Import / Keynotes / Excel → View / Schedule → Excel / Sticky Import) into Expander as chip row | Validators are the canonical Pattern 4 use-case; the second cluster is action chips |
| 13 | COBie EXPORT DASHBOARD | 1 | **Leave as-is** — single full-width primary button, no change needed | Already the right shape |
| 14 | COBie REFERENCE DATA | 12 | **Pattern 5 + 6** — keep `★ Type Map` + `Auto-Match` primary; rest (PickLists/Jobs/Spares/Attributes/Zones/Systems/Doc Types/Doc Type Audit/Zone Type Audit/Summary) into Expander | Two daily-use actions; the rest are reference browsers + audits |
| 15 | WORKFLOW AUTOMATION | 6 | **Pattern 1 + 6** — chip row, shorten labels | 6 independent automation toggles/launchers |
| 16 | QUICK WORKFLOWS | 17 | **Pattern 3 (ComboBox) + Pattern 6** — replace 14 named workflow buttons with a ComboBox `Workflow preset` + `Run` button; keep the 4 clash-related buttons (`Clash Manager` / `Run Clash` / `Clash Live` / `Clash BCF` / `Clash Matrix`) as a separate chip row + `Repeat Last` as standalone | 14 workflow presets are a classic ComboBox fit; clash ops stay visible because they're a different mental model |

## Runner tags added (Pattern 4 + Pattern 3 routing)

Three new runner tags, parallel to the four INTEROP runners in Round 1:

| Runner tag | Resolved by | Reads |
|---|---|---|
| `Setup_ValidatorSuite` | `RunSetupRunner` | sibling CheckBoxes `chkValVTmpl`/`chkValDynBind`/`chkValSchema`/`chkValClash`/`chkValXClash`/`chkValMep`/`chkValNaming`/`chkValIfc`/`chkValGbxml` → dispatches each ticked concrete tag |
| `Setup_MepScheduleCreate` | `RunSetupRunner` | `cmbMepScheduleType` selected ComboBoxItem `Tag` → dispatches it |
| `Setup_QuickWorkflowRun` | `RunSetupRunner` | `cmbQuickWorkflow` selected ComboBoxItem `Tag` → dispatches it |

Every original tag (Validate*, ClashDetect, CrossModelClash, MEPClearance, NamingAudit, IFCPropertyValidation, GbXMLEnrichment, PanelSchedule, LightingFixtureSchedule, MechEquipSchedule, PlumbingFixtureSchedule, FireDeviceSchedule, ElectricalDeviceSchedule, RunWorkflow_*) stays reachable as a ComboBoxItem `Tag` or CheckBox `Name`. Tag superset proof confirms 0 lost.

## Code-behind additions

Add **one** new method to `StingTools/UI/StingDockPanel.xaml.cs`
parallel to `RunInteropRunner`:

```csharp
private bool RunSetupRunner(string runnerTag) { ... switch ... }
```

Three switch cases (`Setup_ValidatorSuite`, `Setup_MepScheduleCreate`,
`Setup_QuickWorkflowRun`). `OnRadioRouteChecked` is reused as-is —
SETUP doesn't introduce any new radio rings; ComboBoxes do all the
mode-picker work this round.

`Cmd_Click` early-return guard updated to include the three new
runner tags so they don't get dispatched as unknown commands.

## Verification gate

Per `docs/UI_PHASE_B_PATTERNS.md`:

1. Tag superset: BEFORE 1439 tags. AFTER will be 1439 + 3 (runners) = 1442. Every original SETUP tag still reachable. To be verified post-edit.
2. XAML well-formed: `python -c "import xml.etree.ElementTree as ET; ET.parse(...)"`
3. Zero CompiledPlugin/Data churn.
4. SETUP-only commit; INTEROP/MODEL/DOCS/CREATE-TAGS/BIM/TAG-STUDIO/HEALTHCARE/EXLINK untouched.
5. Phase A locks (outline styles, theme registry, emphasis cards, swatches, StingButtonStyles.xaml) intact.
