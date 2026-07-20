# Token-Depth Live — Enhancements Runner (E1–E5)

**Autonomous-agent runner.** Extends the "Set depth applies live" automation with five
enhancements. **Research as you implement** — before each item, read the surrounding code
and any existing pattern, and pick the most *flexible / convenient / consistent / accurate
/ sustainable* option, not the first that compiles. Everything here stays **display-only
and reversible**: only `ASS_DISPLAY_TXT` changes; the canonical `ASS_TAG_1_TXT` (source of
truth for schedules/BOQ/COBie/collision) is never touched, no tokens re-derived, no SEQ
renumbered.

---

## 0. Ground truth / environment

- **Repo** `C:\Dev\STINGTOOLS` — shared by many agents. Isolate in a git worktree; never
  `reset --hard` a shared checkout; verify branch first.
- **Base branch** `claude/set-depth-live` (tip has the live Set-depth feature + separator
  fix + 2-digit pad, stacked on all MEP work). Branch FROM IT:
  `git worktree add -b claude/token-depth-ext …/token-depth-ext claude/set-depth-live`.
- **Build** `dotnet build StingTools/StingTools.csproj -c Release -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"`. Baseline **0 err / 6 warn**. Verify after every item.
- **Deploy** `robocopy StingTools\bin\Release → C:\Dev\STING_PLACEMENT_GOLD`, only when
  `Get-Process Revit` is empty, md5-verify, be the last writer. **GOLD is repeatedly
  clobbered by concurrent agents — coordinate / be last, or the work won't be live.**
- **Activation blocker:** the plugin showed *not activated* on the test machine
  (`ADD3-E01C-3412-14C8-175E`). In-Revit gates can't run until a license is applied — flag
  to the owner; not a code task.

### What already exists (reuse — do NOT fork the rendering path)

| Piece | Where | Role |
|---|---|---|
| `RefreshDisplayInScope(doc, scope, mask, seqPad)` | `Tags/RefreshTagDisplayCommand.cs` | the single display-refresh core |
| `BuildMaskedDisplayFromTokens(el, mask, seqPad)` | same | **assembles display from the 8 source tokens** with current separator/pad/mask — this is the extension point for E1 |
| `RepadSeqSegment` / `CollectScope` | same | SEQ re-pad; scope = View/Selection/Project |
| Live apply on **Set depth** | `Tags/ParagraphDepthCommand.cs` (`sliderPath` block) | fires the refresh after tier depth |
| `SetTokenDepthParams()` | `UI/StingDockPanel.xaml.cs` (~L944) | reads panel → pushes `TokenMask`/`ParaDepth`/`TokenScope` ExtraParams + drives `ParamRegistry.Separator`/`NumPad`/`TagConfig.SeqPadWidth`; SEQ-pad parse ~L983 |
| Tokens & Depth controls | `UI/StingDockPanel.xaml` | `chkMaskDISC..SEQ`, `rbSep*`, `cmbSeqPad` (~L4284), `cmbSegOrder` (~L4292), `cmbTokenScope` |
| `ParamRegistry.AllTokenParams` (8, SEQ=slot 7), `.SegmentOrder`, `.Separator` | `Core/ParamRegistry.cs` | token identity + order + separator |
| Per-category tier depth | `Core/Drawing/TokenProfileApplier.WriteCategoryDepths` + `DrawingType.CategoryDepths` | **reference pattern for E2** |
| Extensible Storage | `Core/Storage/StingViewPresetSchema`, `StingEsHelpers` | **per-view state for E4** |
| Tag scheme engine (alphanumeric SEQ) | `Core/TagSchemeEngine.cs` (Phase 188) | **SEQ-pad must skip non-numeric SEQ** |

**Prerequisite that gates everything:** the mask/separator/pad only *show* if the tag
family Label reads `ASS_DISPLAY_TXT`. Keep the existing one-time hint. Research whether the
label binding can be **inspected** programmatically (report families still on `ASS_TAG_1`);
do NOT auto-author labels (Revit API can't reliably author tag-family label rows — see the
project's tag-family-label-tier constraint).

---

## E1 — Segment order live (do first — cheap, completes the format set)

The **Segment order** combo (`cmbSegOrder`) is read into `segOrderText` in
`SetTokenDepthParams` but only feeds `ParamRegistry.ApplyTagFormatOverrides` (affects newly
built tags, not the live display). Make the live display honor it.

- **Research first:** the 8-char mask is positional in **canonical** order (DISC LOC ZONE
  LVL SYS FUNC PROD SEQ). When the order changes you must apply the mask by **token
  identity (slot), not display position** — otherwise masking the wrong segment. Confirm
  what `ParamRegistry.SegmentOrder` returns (token keys like "DISC"/"SEQ") and how to map a
  key → slot index (there's a slot/key table in `SourceTokens`).
- **Implement:** in `BuildMaskedDisplayFromTokens`, build `(slot,value)` for slots where
  `mask[slot]=='1'`, then **emit in `SegmentOrder` order**, join with current separator.
  Pass the order in (or read `ParamRegistry.SegmentOrder`). Keep canonical as the default.
- **Gate:** reorder the combo + Set depth → display reorders live; a masked-out token stays
  hidden regardless of order; round-trip back to canonical restores.

## E2 — Per-category / per-discipline overrides (flagship flexibility — the "doors" case)

Different categories want different display treatment: *doors → 2-digit + compact depth*,
*equipment → 4-digit + full depth*. Apply per-category on Set depth, falling back to the
panel's global settings.

- **Research first:** read `TokenProfileApplier.WriteCategoryDepths` + `DrawingType.CategoryDepths`
  — per-category **tier depth** already exists; extend the same idea to **pad + mask (+
  separator?)**. Decide the most sustainable carrier: prefer a **data-driven JSON**
  (`Data/STING_TOKEN_DEPTH_OVERRIDES.json`, corporate baseline) + **project override**
  (`<project>/_BIM_COORD/token_depth_overrides.json`) — mirror how other STING configs layer
  (e.g. `MepSizingRegistry`, view style packs). Category key = Revit category name via
  `ParameterHelpers.GetCategoryName` (be consistent with the tagFamilies key convention —
  spaced Revit names). Optionally surface as a small editable grid later; JSON first.
- **Implement:** on the Set-depth live pass, resolve each element's category → override
  `{seqPad, mask, depth}`; where absent, use the panel globals. Drive the display builder
  per element with the resolved values, and the per-category tier depth via the existing
  `WriteCategoryDepths` path so the two agree (consistency — one resolution, both surfaces).
- **Gate:** one Set-depth press → doors render 2-digit/compact, equipment 4-digit/full,
  everything else the global default.

## E3 — Named presets (high convenience)

Save/recall a whole Tokens & Depth configuration (mask, separator, pad, order, depth,
handover mode, scope) as a named preset.

- **Research first:** reuse an existing preset pattern — `Data/TAG_PLACEMENT_PRESETS_DEFAULT.json`,
  the `Presets/` engine, or `ProductionPresetRegistry` — don't invent a new persistence shape.
- **Implement:** store presets in `<project>/_BIM_COORD/token_depth_presets.json` + a
  corporate default set. UI: a combo + **Save / Save As / Delete** in the Tokens & Depth
  tab. Selecting a preset **populates all controls**, then optionally auto-applies (Set
  depth). Ship defaults: **Coordination**, **Presentation**, **FM Handover**, **Tight-space
  (2-digit compact)**, **Full (8-seg / 4-digit / T10)**.
- **Gate:** pick a preset → controls populate → Set depth → tags match; Save a custom one →
  reappears next session.

## E4 — Per-view persistence of the full config (sustainable, matches Revit)

The mask already persists to the view (`VIEW_TOKEN_MASK`). Extend so **each view remembers
its own separator + pad + depth + order**, and re-applies on activation.

- **Research first:** compare **Extensible Storage** (`StingViewPresetSchema` +
  `StingEsHelpers` — no shared-param bloat, per-view entity) vs. extra view params. ES is the
  sustainable choice. Check how STING subscribes to Revit events (`StingToolsApp` —
  `DocumentOpened` etc.); a `ViewActivated` hook must be **perf-guarded** (cheap read, only
  act when a stored config exists).
- **Implement:** write the per-view `TokenDepthConfig` to ES on Set depth; on
  `ViewActivated`, read it and either re-apply to the view's tags or (lighter) repopulate the
  panel controls so the next Set depth reproduces it. Prefer repopulate-panel to avoid
  auto-mutating on every view switch — research which feels right and is safe.
- **Gate:** configure view A, switch to view B (different config), back to A → A shows A's
  style; no cross-view bleed.

## E5 — Reactive auto-apply (optional — the "no button" ideal)

Apply on control change (debounced, active-view scope) so tags update as you tune, without
pressing Set depth.

- **Research first:** WPF change events on the Tokens & Depth controls; a debounce timer
  (~300 ms); the `ExternalEvent` path (Revit API must run on its thread — reuse
  `StingCommandHandler`). Performance: only active view; consider a threshold or make it a
  **"Live" toggle** (off by default) so 400+ tag views don't churn on every click.
- **Implement:** a "Live preview" checkbox; when on, debounced control changes raise the
  scoped refresh; when off, today's button behavior. Never auto-apply Project scope.
- **Gate:** toggle Live → change a checkbox/combo → tags update within ~1 s, no button; toggle
  off → back to manual.

---

## Cross-cutting requirements (apply to every item)

- **One rendering path.** All display assembly goes through `BuildMaskedDisplayFromTokens` /
  `RefreshDisplayInScope`. Do not duplicate masking/pad/order logic — extend the core so E1–E5
  stay consistent and a future fix lands once.
- **Display-only + reversible.** Never write `ASS_TAG_1`, never renumber SEQ. Re-check-all +
  Set depth must fully restore the canonical tag.
- **Accuracy.** Mask maps by **token identity** (not position) once order/per-category are in.
  SEQ re-pad applies **only to a numeric SEQ** — a scheme/alphanumeric SEQ (TagSchemeEngine,
  Phase 188) must be left untouched; confirm and guard.
- **Sustainable + data-driven.** Corporate-baseline JSON + project override for overrides and
  presets; ES for per-view state; no hardcoded category lists or magic numbers.
- **Convenient + performant.** Default scope = active view; presets/per-view memory to cut
  repetitive dialing; debounce anything reactive.
- **Keep the ASS_DISPLAY_TXT-label prerequisite visible** (one-time hint + an inspect-and-warn
  of families still on `ASS_TAG_1`; do not auto-author labels).

## Verification gates
- Build 0-err/6-warn after each item.
- Per-item gate above (in-Revit where activation allows; otherwise scripted JSON/param checks
  + a logic walkthrough).
- Regression: with all features **off/default**, behavior === today's Set-depth (canonical
  full-8, hyphen, 4-digit).

## Build + deploy + reporting
- Deploy only Revit-closed, md5-verified, last writer.
- Report each item DONE / PARTIAL / BLOCKED with evidence (file:line, gate result). No "should
  work" — anything that only *renders* in Revit must say it's unverified if the machine is
  unactivated.
- Any design fork you hit (JSON vs UI grid, re-apply vs repopulate on ViewActivated, Live
  default) → pick the more sustainable option and **state the trade-off**; STOP and ask only
  if it's a real product decision.
- Don't merge to `main`; log in `docs/CHANGELOG.md` with the no-merge note. Do E1→E5 in order,
  committing each so partial progress is safe against a GOLD clobber.
