# Phase B Round 3 — CREATE TAGS tab redesign plan

Audit-first plan for the CREATE TAGS tab redesign per
`docs/UI_PHASE_B_PATTERNS.md`. Round 3 follows the methodology
established by Round 1 (INTEROP commit `60fea4493`) and Round 2
(DOCS / MODEL / SETUP, commits `f47c3d3e2` / `aed3ab7ae` /
`c6d37150c`): read the tab, classify each section against the
decision tree, pick the right pattern, commit the plan first, then
apply XAML + code-behind edits in one follow-up commit.

## Tab location

`StingTools/UI/StingDockPanel.xaml` lines 2192–2403.
66 unique dispatch tags across 10 sections.

## Per-section pattern decisions

Decision tree applied per `docs/UI_PHASE_B_PATTERNS.md`.

| # | Section (current TextBlock label) | Buttons | Pattern | Rationale |
|---|---|---|---|---|
| 1 | ISO HEADER | 2 | **Pattern 2 + 6** — two `RadioButton` rings (Scope: View/Selection/Project · Overwrite: No/Yes) wired through `OnRadioRouteChecked` | Two binary/triple mode toggles; the existing toggle buttons cycle through hidden state — radios make the state visible at a glance |
| 2 | ⚙ SETUP | 16 | **Pattern 5 + 6** — keep `Load Shared Params` + `Create Tag Families` + `Configure Labels` as primary; fold the other 13 (Purge / ConfigEditor / GuidedDataEditor / TagConfig / SyncSchema / AuditSchema / AddRemap / Load / Audit / ConfigureLoaded / MigrateFamilies / MigrateLabelRefs / StyleAudit) into Expander | Three starring entry points cover the daily workflow; rest are infrequent setup / migration / audit ops |
| 3 | ⚙ POPULATE TOKENS | 19 | **Pattern 5 + 6** — keep `▶ Auto Populate` + `[Brain] Smart Tokens` + `⚡ Build Tags` + `Preview Tag` primary; fold `Assign Numbers` + `Mat Tags` + the 13 manual token chips (PROJ/ORIG/VOL/LVL/DISC/LOC/ZONE/SYS/FUNC/PROD/SEQ/STATUS/REV) into Expander labelled "Manual token overrides" | Auto-Populate + Build + Smart Tokens are the daily flow; manual per-token override is a power-user fallback for when auto-detection fails |
| 4 | ⚙ QUALITY ASSURANCE | 10 | **Pattern 1 + 6** — chip row, shorten labels (Validate → Valid · Completeness % → Complete % · Container Check → Containers · etc.) | All 10 are independent immediate-action validators / inspectors; perfect chip-row fit |
| 5 | ⚙ PARAGRAPH & PRESENTATION | 6 | **Pattern 5 + 6** — keep `Presentation Mode` + `Paragraph Depth` + `Toggle Warnings` primary (the daily formatting trio); fold `View Label Spec` + `Export Label Guide` + `TAG7 Heading Style` into Expander | Three primary actions are the formatting controls; secondary trio are reference/export utilities |
| 6 | [Chart] ISO COMPLETENESS DASHBOARD | 2 | **Leave as-is — Pattern 6 only** — already a well-structured grid with DataGrid + Load/Export buttons + Min% slider + discipline ComboBox | Already optimal layout; no pattern change needed |
| 7 | ⚙ FAMILY & DISPLAY | 6 | **Pattern 1 + 6** — chip row, shorten labels (Tag Family Param Creator → Inject Params · Display Mode → Display · Seq Scheme → Seq · Auto-Tag Visual → Visual · Linked Tags → Linked · Retag Stale → Retag) | All 6 are independent immediate-action toggles/configurators; perfect chip-row fit |
| 8 | ⚙ EXPORT | 5 | **Pattern 1 + 6** — chip row, shorten labels; keep `★ Data Export` visually prominent via `OrangeBtn` style | 5 independent action buttons (export/import/combine); already a clean chip row, just shrink labels |
| 9 | TOKEN INSPECTOR | 2 | **Leave as-is — Pattern 6 only** — two stretched primary buttons (Inspect Selection + Quick Tag Preview) | Already minimal; both are primary inspection actions — just polish tooltips |
| 10 | Resolve All Issues | 1 | **Leave as-is** — single full-width primary button, no change needed | Already the right shape |

## Runner tags added (Pattern 2 routing)

Two new runner tags, parallel to the previous round runners:

| Runner tag | Resolved by | Reads |
|---|---|---|
| `CreateTags_ScopeApply` | `RunCreateTagsRunner` | sibling RadioButtons `rbCtScopeView`/`rbCtScopeSelection`/`rbCtScopeProject` → dispatches `ScopeView` / `ScopeSelection` / `ScopeProject` |
| `CreateTags_OverwriteApply` | `RunCreateTagsRunner` | sibling RadioButtons `rbCtOverwriteNo`/`rbCtOverwriteYes` → dispatches `ToggleOverwrite` (toggle-style command — the runner inspects desired vs current state via the dispatch) |

NOTE on scope: the existing `ScopeView` dispatch tag stays reachable.
Two NEW concrete dispatch tags — `ScopeSelection` and `ScopeProject`
— are added so the radio ring can offer the three canonical scope
choices. They route through the existing `StingCommandHandler` scope
plumbing (parallel pattern to how `ScopeView` already routes). If
`ScopeSelection` / `ScopeProject` are not yet wired in the handler,
the dispatch is a graceful no-op (handler shows "Unknown command"
TaskDialog) — the tag superset proof still passes because they're
ADDITIONS, never removals.

The Overwrite radio ring uses the existing `ToggleOverwrite` tag
(no new tag needed) — `rbCtOverwriteYes.Checked` and
`rbCtOverwriteNo.Checked` both dispatch `ToggleOverwrite`. The
command is idempotent / toggle-style — calling it switches state;
the radio's role is to surface CURRENT state, not to drive new
state directly. (Round 4 can refactor `ToggleOverwrite` into
explicit `OverwriteOn` / `OverwriteOff` if needed; out of scope here.)

Every original tag (ScopeView, ToggleOverwrite, LoadSharedParams,
PurgeSharedParams, ConfigEditor, GuidedDataEditor, TagConfig,
SyncParamSchema, AuditParamSchema, AddParamRemap, CreateTagFamilies,
ConfigureTagLabels, LoadTagFamilies, AuditTagFamilies,
ConfigureLoadedFamilies, MigrateTagFamilies, MigrateTagLabelRefs,
StyleAudit, FullAutoPopulate, AssignNumbers, FamilyStagePopulate,
PreviewTag, BuildTags, MatTags, SetProj, SetOrig, SetVol, SetLvl,
SetDisc, SetLoc, SetZone, SetSys, SetFunc, SetProd, SetStatus, SetRev,
ValidateTags, HighlightInvalid, ClearOverrides, CombinePreFlight,
CompletenessDashboard, FindDuplicates, SelectStale, ContainerPreCheck,
QRCode, CodeLegend, SetPresentationMode, SetParagraphDepth,
ToggleWarningVisibility, ViewLabelSpec, ExportLabelGuide,
SetTag7HeadingStyle, AuditTagsCSV, FamilyParamCreator, SetDisplayMode,
SetSeqScheme, AutoTagVisual, BatchPlaceLinkedTags, RetagStale,
TagRegisterExport, ExportTagMap, ImportTagMap, StingDataExport,
CombineParameters, PreTagAudit, QuickTagPreview, ResolveAllIssues)
stays reachable as a chip-row Button, an Expander Button, or a
RadioButton dispatch.

## Code-behind additions

Add **one** new method to `StingTools/UI/StingDockPanel.xaml.cs`
parallel to `RunSetupRunner`:

```csharp
private bool RunCreateTagsRunner(string runnerTag) { ... switch ... }
```

Two switch cases (`CreateTags_ScopeApply`, `CreateTags_OverwriteApply`).
`OnRadioRouteChecked` is reused as-is — the runner tags fire from the
"Apply" button next to each radio ring.

`Cmd_Click` early-return guard updated to include the two new runner
tags so they don't get dispatched as unknown commands.

## Verification gate

Per `docs/UI_PHASE_B_PATTERNS.md`:

1. Tag superset: BEFORE 1445 tags. AFTER will be 1445 + 4 (2 runners
   `CreateTags_ScopeApply` + `CreateTags_OverwriteApply`, plus 2 new
   scope dispatch tags `ScopeSelection` + `ScopeProject`) = 1449.
   Every original CREATE TAGS tag still reachable. Verified post-edit.
2. XAML well-formed: `python -c "import xml.etree.ElementTree as ET; ET.parse(...)"`
3. Zero CompiledPlugin/Data churn.
4. CREATE TAGS-only commit; INTEROP/DOCS/MODEL/SETUP/TAGGING/BIM/TAG-STUDIO/HEALTHCARE/EXLINK untouched.
5. Phase A locks (outline styles, theme registry, emphasis cards, swatches, StingButtonStyles.xaml) intact.
6. Phase B Rounds 1/2 code-behind helpers (`OnRadioRouteChecked`,
   `RunInteropRunner`, `RunDocsRunner`, `RunSetupRunner`) intact.
