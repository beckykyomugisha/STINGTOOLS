# Tagging Workflow Analysis — T1–T10 Tier System Audit

**Date:** 2026-04-28
**Branch:** `claude/tagging-workflow-analysis-XigAY`
**Scope:** Full tagging pipeline, T1–T10 tiers, button dispatch, Planscape sync

---

## Executive Summary

The T1–T6 tagging system (TAG7 sections A–F) is functionally wired. The T4–T10 extension is partially scaffolded but never wired into any working pipeline. Parameter constants are declared, GUID slots exist, and paragraph state toggles are defined — but no code ever reads element data, builds tier content, writes tier parameters, or transmits tier data to Planscape. Additionally, one button that controls depth selection will crash Revit because its handler class does not exist. The overall result is that tiers 4–10 are invisible to the system at runtime despite appearing structurally defined.

---

## Issue Register — Priority Order

### CRITICAL — Will crash or silently fail

**Issue 1 — `SetParagraphDepthExtCommand` does not exist**
`StingCommandHandler` maps `case "SetParagraphDepthExt"` to a class named `SetParagraphDepthExtCommand`. That class does not exist in the codebase — only `SetParagraphDepthCommand` exists. The button labelled "Set Depth (1-10)" in the XAML at line 2000 will throw a null reference or instantiation exception every time it is pressed. This is the primary button a user would reach for to access tiers 4–10.

**Issue 2 — `WriteTag7All()` stops at tier 6, always**
`TagConfig.WriteTag7All()` loops over `ParamRegistry.TAG7Sections`, a fixed 6-item array (A–F). No amount of T4–T10 constant declarations changes this. The pipeline never writes `COMM_STATE_TXT`, `CST_UG_PRICE_UGX`, `CBN_A1_A3_KG_CO2E`, or any other T4–T10 parameter. These parameters are permanently empty after every tagging operation.

**Issue 3 — T4–T10 parameters absent from `MR_PARAMETERS.csv` and `MR_PARAMETERS.txt`**
Revit binds shared parameters by reading the shared parameter file. The 21+ T4–T10 constants declared in `ParamRegistry.cs` (lines 2601–2678) do not exist in either `MR_PARAMETERS.csv` or `MR_PARAMETERS.txt`. Families cannot bind to parameters that are not declared in the shared parameter file. Tag families will silently fail to host T4–T10 data rows.

### HIGH — Tier system non-functional end-to-end

**Issue 4 — `BuildTag7Sections()` assembles only 6 sections**
The method that constructs tier content from element data returns a `Tag7Result` with exactly 6 populated sections. There is no assembly logic for commissioning data, cost data, carbon data, fabrication/QC data, clash triage data, as-built data, or compliance data. The extension points exist in `ParamRegistry` but have no corresponding content-building code.

**Issue 5 — No T4–T10 data hydration anywhere in the pipeline**
`RunFullPipeline()` in `ParameterHelpers.cs` calls nine steps. None of them reads element phase dates, cost parameters, carbon values, commissioning records, or clash data and writes them to the T4–T10 parameters. Even if `WriteTag7All()` were extended to write those parameters, there would be nothing to write — they are never populated.

**Issue 6 — Paragraph pattern selectors have no UI and are never set**
`ParamRegistry` defines `HANDOVER_MODE_HANDOVER_BOOL`, `HANDOVER_MODE_DC_BOOL`, and `HANDOVER_MODE_CUSTOM_BOOL` to control which T4–T10 payload is active per element. No UI controls exist to toggle these. No pipeline step sets them. Tag families that bind their T4–T10 row visibility to these parameters will always show nothing.

**Issue 7 — Planscape sync transmits TAG1 and TAG7 only**
`TagElementPayload` in `PlanscapeServerClient.cs` has fields for `tag1`, `tag7`, `categoryName`, `isComplete`, and `lastModifiedUtc`. No fields exist for TAG7A–TAG7F, any T4–T10 parameter, any `PARA_STATE_N` value, or any pattern selector flag. T4–T10 data is invisible to the server, the mobile app, and any downstream reporting.

### MEDIUM — Functional gaps reducing reliability

**Issue 8 — `SetParagraphDepth` legacy dialog hides tiers 4–10**
When `SetParagraphDepthCommand` runs without a slider parameter (direct button press rather than slider path), it presents a three-option dialog: Compact / Standard / Comprehensive — depths 1, 2, and 3 only. Tiers 4–10 are unreachable from this dialog. The slider path via `SetParagraphDepthExt` crashes (Issue 1). Net result: no user path to tiers 4–10 is currently operational.

**Issue 9 — `RichTagNoteCommand` and display commands ignore T4–T10**
`RichTagDisplayCommands.cs` builds formatted text annotations from TAG7 sections A–F only. Colour assignments (blue for A, green for B, orange for C, red for D, purple for E, grey for F) are hardcoded for six sections. T4–T10 content, even if written to element parameters, would never appear in rich tag display or the formatted note overlay.

**Issue 10 — Paragraph state parameters 4–10 toggle correctly but are undocumented**
`SetParagraphDepthCommand` correctly enables `PARA_STATE_1` through `PARA_STATE_N` cumulatively for depth N. The toggle logic is correct. However, there is no documented specification of which tag family label rows must bind to which `PARA_STATE_N` parameter, nor which states correspond to which T4–T10 payload tier. Without this specification, family authors cannot build families that honour the depth system.

**Issue 11 — Duplicate tag style BOOL risk — no enforcement of mutual exclusivity**
`TagStyleEngine.ApplyTagStyle()` sets one `TAG_*_BOOL` to Yes and 127 to No for a given element type. However, no validation sweep confirms this invariant is maintained after bulk operations, family swaps, or partial writes. An element type with two `TAG_*_BOOL` parameters set to Yes simultaneously will show unpredictable label visibility.

**Issue 12 — T4–T10 placeholder GUIDs not yet replaced**
`ParamRegistry` comments (lines 2591–2598) state that T4–T10 GUIDs follow a deterministic placeholder pattern and must be replaced with stable GUIDs during family library authoring. These placeholders are valid hex but will collide if any other firm's shared parameter file uses the same pattern. No ticket or tracking exists to ensure replacement before deployment.

### LOW — Completeness and integration

**Issue 13 — No NLP intents for tier/depth operations**
`NLPCommandProcessor.cs` has no patterns for tier selection, depth-setting, or T4–T10 data queries. A user who types "set tier 5" or "enable commissioning data" in the NLP field will get no result.

**Issue 14 — No completeness audit for T4–T10 parameters**
`ValidateTagsCommand` checks T1–T3 token completeness (DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, SEQ). It has no checks for T4–T10 parameter population. A fully tagged element that has empty `COMM_STATE_TXT` or `CST_UG_PRICE_UGX` is reported as compliant. Post-tier-4 compliance is invisible to the audit system.

---

## Root Cause

The T4–T10 system was architected (parameter constants, GUID slots, state toggles, pipeline step ordering) but implementation was deferred. The parameter definitions are a skeleton — the bones are there but no muscle connects them. The one button intended to expose this system to users crashes because its handler was never created. The pipeline ignores T4–T10 entirely. Planscape never sees it. The system presents as complete from a code-structure perspective while being entirely non-functional at runtime.

---

## What Is Working Correctly

- T1–T6 (TAG7A–TAG7F) write pipeline is complete and functional
- `SetParagraphDepthCommand` correctly toggles `PARA_STATE_1`–3 cumulatively (up to depth 3, reachable)
- `TagStyleEngine` 128-combination BOOL matrix is correctly defined and enforced
- `WriteContainers` correctly distributes TAG1 across all 36 discipline-specific containers
- AutoTag / BatchTag / `RunFullPipeline` correctly execute T1–T6 end-to-end
- Planscape sync correctly transmits TAG1, TAG7, compliance score, and element identity

---

## Implementation Prompt

A self-contained Claude Code prompt for executing the repair is included below.

### Tasks (execute in order)

1. **Read all referenced files** before editing — `ParamRegistry.cs`, `TagConfig.cs`, `ParameterHelpers.cs`, `StingCommandHandler.cs`, `StingDockPanel.xaml`, `ParagraphDepthCommand.cs`, `RichTagDisplayCommands.cs`, `TagStyleEngine.cs`, `TagStyleCommands.cs`, `PlanscapeServerClient.cs`, `MR_PARAMETERS.txt`. Confirm which T4–T10 parameter names are present vs absent.

2. **Fix the crashing button.** Either repoint `case "SetParagraphDepthExt"` in `StingCommandHandler` at the existing `SetParagraphDepthCommand` (passing a `ParaDepth` extra param), or add a new `SetParagraphDepthExtCommand` to `ParagraphDepthCommand.cs` that reads the extra param, falls back to a 10-option picker, and applies cumulative state toggling. Match whichever pattern is structurally consistent. Also extend the legacy depth dialog from 3 options to 10 (Compact / Standard / Comprehensive / + Commissioning / + Cost / + Carbon / + Fabrication / + Clash Triage / + As-Built / Full Specification).

3. **Add T4–T10 parameters to `MR_PARAMETERS.txt`.** Match the file's existing format precisely. Use GUIDs from `ParamRegistry`. Use TEXT for `_TXT`, INTEGER for `_NR`, NUMBER for measurement units (`_MM`, `_KG_CO2E`, `_HRS`, `_SCORE`). Group under "STING T4-T10 TIERS".

4. **Extend `BuildTag7Sections()`** to assemble T4 (commissioning), T5 (cost), T6 (carbon), T7 (fabrication/QC), T8 (clash triage), T9 (as-built), T10 (compliance) summary strings from the relevant element parameters. Add T4–T10 properties to `Tag7Result`. Wrap each tier in try/catch.

5. **Extend `WriteTag7All()`** to write T4–T10 after the existing 6-section loop. Read the active pattern mode from `HANDOVER_MODE_*_BOOL`, default to DC. For each tier 4–10, check the corresponding `PARA_STATE_N_BOOL` and write tier data when enabled. Skip missing parameters with a `StingLog.Warn`. Stay inside the existing transaction.

6. **Add pattern selector UI controls** to `StingDockPanel.xaml` near the depth section: three toggle buttons tagged `SetPatternMode_Handover`, `SetPatternMode_DC`, `SetPatternMode_Custom`. Wire matching cases in `StingCommandHandler` that flip the three `HANDOVER_MODE_*_BOOL` values mutually exclusively under a Manual transaction.

7. **Extend Planscape sync.** Add `tag7a`–`tag7f`, `t4Commissioning`–`t10Compliance`, `paraDepth`, and `patternMode` fields to `TagElementPayload`. Populate from element reads in the existing payload-building method. Do not alter existing fields.

8. **Extend `RichTagDisplayCommands`.** After the 6-section block, append T4–T10 with distinct colours (Teal / Gold / LightGreen / SteelBlue / OrangeRed / SlateGray / DarkViolet), conditional on the matching `PARA_STATE_N_BOOL`. Skip empty tiers silently.

9. **Extend `ValidateTagsCommand`.** After T1–T3 token checks, read the active `paraDepth` and warn (not error) when required tier-N parameters are empty. Codes `T4-EMPTY` through `T10-EMPTY`.

10. **Final read-only verification:** confirm `WriteTag7All` covers both T1–T6 and the new T4–T10; all 10 depths are reachable from UI; no `StingCommandHandler` case references a missing class; every declared T4–T10 parameter exists in `MR_PARAMETERS.txt`; `TagElementPayload` carries all new fields. Report any remaining gaps.

### Constraints

- No `dotnet build` verification (Linux sandbox / Revit-only). Trust the existing patterns.
- Follow conventions: "STING"-prefixed transaction names, `[Transaction(TransactionMode.Manual)]`, `StingLog.Info/Warn/Error`, `TaskDialog` for user messaging, no silent catches.
- Read every file before editing. Targeted edits only — no whole-file rewrites.
- Search for ambiguous file paths via grep before assuming.
- Do not create new files unless the task explicitly requires it.
- After each task, state what changed, in which file, and which class or method was affected.
