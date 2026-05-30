# UI Cleanup Campaign — Consolidated Tracker

**Period:** May 2026 — single multi-day session
**Scope:** STING Tools Revit plugin UI + Planscape server-side bring-up
**Status:** complete (all originally-planned items closed); follow-ups queued in §"Future work"

This document is the retrospective tracker for the UI cleanup campaign. It
covers every phase that landed on `main`, the verifiable diffs, the
decisions made, the items deliberately deferred, and the new findings
that surfaced during the work.

If you're picking this up fresh: read §1 for the why, §2 for the phase
shape, §3-9 for what each phase shipped, §10 for the Planscape server
revive, §11 for the open follow-ups, and §12 for the merge log.

---

## 1. What we were solving

The STING Tools dock panel had accumulated three years of feature
additions without ever getting a style pass. The user's specific
complaints, in their words:

- "*Most tabs not visually appealing, only [a few] have interactive UI setups*"
- "*Currently the most tabs are wasting space with just coloured big boxes*"
- "*Make them compact to reduce long scrolls, use dropdowns / combobox / checkboxes / circles*"
- "*I don't like the highlighting fill colours on buttons, better an outline colour*"
- "*Things should look corporate, I like the LPS panel layout, simple and compact, not many colours*"
- "*The theme button doesn't change the panels' appearance*"

Plus a parallel symptom on Planscape (the cloud webapp at `localhost:5000`):

- "*The buttons are lifeless, 3D model viewer not opening, the new
   meeting functions not seen, camera and photo functions not seen*"

Two campaigns, one session. The Revit UI side is **Phases A → D**; the
Planscape side is **Phase X**. They are largely independent but were
worked in interleaved fashion as agents finished.

---

## 2. Phase shape

| Phase | What |
|---|---|
| **A**   | Shared style dictionary `StingButtonStyles.xaml` — outline aesthetic, LPS-spec density, theme-cycle wiring |
| **A.5** | Strip inline fills from emphasis cards + 9 state buttons; drop 2 `<Button.Template>` overrides |
| **A.6** | Multi-target theme registry — fix the singleton clobber that kept theme cycling from working |
| **B**   | Per-tab interaction redesign — 8 tabs re-patterned over 4 rounds (R0 patterns doc + R1-R4 tab work) |
| **C**   | Migrate the 5 dedicated panels (HVAC / Electrical / Plumbing / LPS / Material Hub) to shared dictionary |
| **D**   | Triage + targeted fixes: dedupe, mis-wire repairs, command consolidation |
| **X**   | Planscape webapp revive (P0-1+2, P0-3, P0-4, P0-5) — separate campaign, interleaved scheduling |
| **Y**   | Planscape P1 server items — running in the terminal session, not in this Claude session |

Phases A, A.5, A.6, X are sequential single-agent or in-session work.
Phases B, C, D use parallel-agent fanout with sequential rebase + FF
merging.

---

## 3. Phase A — shared style dictionary

**Commit:** `ef55df2f0` (Phase A) · `be787ad4f` (Phase A.5) · `438a82a97` (Phase A.6)

### What landed

**`StingTools/UI/Resources/StingButtonStyles.xaml`** (~130 lines, new file)
- Brushes: `PrimaryBtnBrush` (teal #00897B), `DangerBtnBrush` (red #C62828),
  `BtnHoverBrush` / `BtnPressBrush` overlays, `PrimaryTintBrush` / `DangerTintBrush`
- Theme-driven keys: `ButtonBg` / `ButtonFg` / `BorderColor` / `AccentBrush` / `SecondaryBg` (registered by `ThemeManager` at runtime)
- Sizes: `BtnHeight 22` · `BtnMinWidth 70` · `BtnFontSize 10` · `SectionHeaderHeight 16`
- Styles: `StingButton` (default), `StingPrimaryButton` (outline teal), `StingMutedButton`, `StingDangerButton` (outline red), `StingSectionHeader`, `StingDivider`
- Implicit default: every `<Button/>` with no explicit Style picks up `StingButton`

**Key design decision:** `Primary` and `Danger` are outline-only — transparent fill, brand-colour border, brand-colour text, subtle tint on hover. This is the "corporate-flat" look the user said they liked on the LPS panel. Saturated fills were rejected.

### A.5 — strip inline fills

21 buttons in `StingDockPanel.xaml` had inline `Background="#..."` overrides that bypassed the shared styles. State buttons (Untagged / Tagged / EmptyMark / [Pin]Pinned / View / Project / etc) got re-styled via local `OrangeBtn / BlueBtn / PurpleBtn / TealBtn` (all outline). Two `<Button.Template>` overrides (BCC hero, WARNINGS DASHBOARD hero) were dissolved — they now use `StingPrimaryButton` with hero sizing. Section-card wrapper Borders converted from light brand-tint fill to `{DynamicResource SecondaryBg}` + 2px brand-coloured bottom edge (LPS-style accent strip). Color-scheme swatches in TAG STUDIO → Style&Color and result-strip tints kept intentionally (data, not chrome).

### A.6 — multi-target theme registry

**Root cause discovered:** `ThemeManager._targetElement` was a static singleton. 8 panels call `RegisterTarget(this)` in their constructors (StingDockPanel, StingLpsPanel, StingHvacPanel, StingElectricalPanel, StingPlumbingPanel, StingPlacementCenter, CircuitWizardDialog, PhotometricLibraryDialog). Each `RegisterTarget` clobbered the previous one. Whichever panel constructed last became the sole target; clicking the theme button only repainted that panel. The main dock panel was usually orphaned because LPS or HVAC opened more recently.

**Fix:** `_targetElement` → `List<WeakReference<FrameworkElement>> _targets`. `RegisterTarget` adds (deduped by identity, prunes dead refs in the same pass — short-lived dialogs can be GC'd without leaking). `ApplyTheme` iterates every live target and writes brushes to each. `Application.Current.Resources` is still written for child dialogs that don't register a target. Public API unchanged.

After A.6, cycling the theme button repaints every open panel + dialog together for the first time.

---

## 4. Phase B — per-tab interaction redesign

**Round 0 commit:** `b9996d274` (patterns catalogue) — `docs/UI_PHASE_B_PATTERNS.md`

The patterns catalogue is the canonical brief for every Phase B agent. Six patterns + decision tree + locked-from-Phase-A list + do-not-touch list + verification gate ("every dispatch tag still REACHABLE" replaces Phase 3's "button count constant", since the whole point of B is to fold N buttons into 1 picker).

### Patterns

| # | Pattern | Use when |
|---|---|---|
| 1 | Action chip row (Pattern 6 short labels) | 3-8 immediate-action buttons, no mode pick |
| 2 | RadioButton ring + Apply | 2-4 mutually-exclusive modes / scopes |
| 3 | ComboBox + Run | 5+ choices or data-driven |
| 4 | CheckBox grid + suite-Run | Subset of independent flags |
| 5 | `<Expander>` for advanced ops | 1-2 primaries + 4-10 secondaries |
| 6 | Short labels + ToolTip (universal) | Every button on every tab |

### Round 1 (INTEROP) — proof of pattern

**Commit:** `60fea4493` (XAML moves) · `5bb5480df` (audit plan)

18 sections re-patterned in the INTEROP tab. Highlights:
- 3× SPECKLE byte-identical duplicate blocks collapsed to 1 (all 3 dispatch tags reachable from the surviving block)
- Excel Link Bidirectional → CheckBox grid (sync flags)
- Platform Integration → ComboBox picker (publish target)
- ExLink Dynamic Export → RadioButton ring (3 formats)
- 4 new runner tags: `ExcelLink_SyncSuite`, `Platform_PublishTarget`, `ExLinkDynamic_Run`, `ISB_CreateSelected`

Code-behind helpers added (idempotent for Round 2+ reuse):
- `OnRadioRouteChecked` — universal handler for routing RadioButtons tagged with their dispatch string
- `RunInteropRunner(string)` — INTEROP-scoped suite runner

### Round 2 — DOCS + MODEL + SETUP (parallel)

**Commits:** `f47c3d3e2` (DOCS) · `aed3ab7ae` (MODEL) · `c6d37150c` (SETUP)

Three agents in parallel, three different tabs:
- **DOCS** (10 sections re-patterned): 5 Expanders, 3 RadioButton rings, new helper `RunDocsRunner` with cases for `Rev_DeleteClouds`, `Export_PrintScope`, `Heatmap_PaintSelected`
- **MODEL** (17 sections): heavy Expander use (13 nested Expanders for STRUCTURAL AUTOMATION's 60 buttons collapsed under 5 promoted star-primaries), no code-behind needed
- **SETUP** (16 sections): 9 Expanders + Data QA validator CheckBox grid (9 flags) + ComboBoxes for MEP Schedules (6 disciplines) and Quick Workflows (11 presets). New helper `RunSetupRunner` with cases for `Setup_ValidatorSuite`, `Setup_MepScheduleCreate`, `Setup_QuickWorkflowRun`

Merge cascade had one mechanical conflict in `xaml.cs` between DOCS and SETUP (both added their `Run*Runner` method + Cmd_Click guard side-by-side) — resolved by combining both into independent methods.

### Round 3 — CREATE TAGS + BIM + TAG STUDIO sub-tabs (parallel)

**Commits:** `1628b2192` (CREATE TAGS + manual scope-radio fix) · `463cd6f38` (CREATE TAGS XAML) · `e6e78f10a` (CREATE TAGS plan) · `e04c1d9e2` (BIM) · `d97ad09db` (BIM plan) · `29673e969` (TAG STUDIO) · `7d28ff54e` (TAG STUDIO plan)

- **CREATE TAGS**: ISO HEADER scope+overwrite radio rings, SETUP / POPULATE TOKENS / PARAGRAPH&PRESENTATION Expanders, QUALITY ASSURANCE chip row. New helper `RunCreateTagsRunner`. **Required hot-fix**: the agent invented `ScopeSelection` / `ScopeProject` tags that had no handler. Manual fix dropped the Selection radio (no third mode exists in `SelectionScopeHelper`) and rewired the View / Project radios to use the existing `SetScopeView` / `SetScopeProject` tags. Lesson: every new tag must be wired before commit (hard rule added to Round 4 brief).
- **BIM** (24 sections): Document Mgmt, BEP, 4D ops, LPS quick-access, CDE, Issue/RFI, COBie, GAP Analysis, Sticky Notes, Revision Mgmt all → Expanders. MEP Schedules + Deliverable Lifecycle → ComboBoxes. **Preserved** the BCC + WARNINGS hero cards (Phase A.5 outline + bottom-edge strip). New helper `RunBimRunner`.
- **TAG STUDIO sub-tabs** (Automation / Standards / MEP / Fabrication / Routing / Fixtures / Tools): re-patterned per the catalogue. Scale / Placement / Style&Color / Tokens&Depth / Categories / Leader&Elbow explicitly untouched (interactive already OR data-coded OR explicitly forbidden by user).

### Round 4 — TAGGING

**Commit:** `3cbcdb02f` (XAML + RunTaggingRunner) · `14da7e29a` (plan)

14 sections re-patterned. Highlights:
- ANALYSE → 3×4 CheckBox grid (12 audit flags) + suite runner
- ALIGN → 6-way RadioButton ring (top / bottom / left / right / center-H / center-V) + single Apply
- ORIENTATION → 3-way + 3-way text-align radio rings
- TAG POSITION → 4-way radio ring
- ROOM TAG POSITION SYNC → 3-way anchor + 2-way leader picker

6 new runner tags, all consumed locally by `RunTaggingRunner` — none reach `StingCommandHandler`.

---

## 5. Phase C — dedicated-panel restyle migration

**Commits:** `f1d8ef08e` (Electrical) · `2287d1db5` (HVAC) · `b88b3533e` (Plumbing) · `5cbda840e` (LPS) · `f011ac092` (Material Hub)

5 parallel agents, one per panel. Each:
1. Merged the shared `StingButtonStyles.xaml` dictionary into the panel's `<Page.Resources>` MergedDictionaries
2. Rebased generic action / Primary / SectionHeader local styles on shared
3. Preserved brand-semantic colour locally (per panel discipline)
4. Converted hardcoded `Background="#..."` on neutral buttons to theme brushes

**User-question moment**: after Electrical + HVAC landed (both with visible "Primary fill → outline" change), the user asked "*what are you changing to? I like how the panels currently look*". Paused, explained the scope, offered 4 options (A cancel, B LPS only, C land all you Revit-verify per panel, D land all but preserve filled-Primary aesthetic). User picked C — "do all".

### Per-panel result

| Panel | Visible aesthetic shift | Brand colour kept | Notes |
|---|---|---|---|
| Electrical | filled blue Primary → outline blue | BS 7671 phase-C blue (`#1976D2`); `LockBadge` red overlay | 4 local styles migrated; 165 tags identical |
| HVAC | filled teal Primary → outline teal | HvacWarnBtn orange (`#FFEF6C00`) for over-velocity / pressure-class breach | 3 styles migrated; 94 tags identical |
| Plumbing | filled blue Primary → outline | None — BS 1710 service colours are on pipework via view filters, not UI buttons | 3 styles migrated; 90 tags identical |
| LPS (gold standard) | **no change** — only added the shared-dictionary reference; all 5 local styles kept verbatim | All 5 (`LpsActionBtn`, `LpsPrimaryBtn` purple, `LpsWarnBtn` orange, `LpsAccentBtn` blue, `LpsSectionHeader`) | +13/-1 |
| Material Hub | no Primary restyle (no filled Primary existed). **ThemeManager.RegisterTarget added** — was missing entirely, panel was orphaned from the multi-target registry | 6 identity-chrome brushes (HubHeaderBg navy, etc); PBR / texture / lifecycle / EPD freshness swatches | 5 dead resources deleted (2 unused Button styles, KpiCard, CardBorder, CardHeader); 3 tags identical |

Sequential rebase + FF merge — different files, no conflicts.

---

## 6. Phase D — triage + targeted fixes

### Triage sweep (audit only)

**Commit:** `af04d03ae` on `audit/phase-d-triage` (not merged — reference doc)

`docs/PHASE_D_TRIAGE.md` with three buckets:
- **Group 3 TODOs**: 2 SMALL + 2 MEDIUM open
- **Healthcare TODOs**: 1 CLOSED + 2 SMALL + 1 MEDIUM + 1 LARGE open
- **Material surfaces consolidation**: 3 SMALL + 1 BLOCKED (needed user decision)

### Quick-access dedupe

**Commit:** `21d3ab3ce`

Removed 20 main-panel buttons reachable from dedicated panels (18 Lightning → STING LPS panel, 2 SLD → STING Electrical panel). Each removed tag verified reachable via the dedicated panel (LPS uses `Lps_*` lowercase variants pointing to the same `Commands.Lightning.*` classes; no workflow JSON references the removed `LPS_*` strings — no external breakage). Symbols / MEP Schedules clusters preserved (no dedicated-panel equivalents — staying in main).

Net main-panel dispatch surface: 1559 → 1540.

### AssignIssues rewire

**Commit:** `1fe9d86d3`

Three surfaces flagged in triage:
- `StingCommandHandler.cs:3769` — `RaiseIssueCommand` → `UpdateIssueCommand`
- `BimCommandModule.cs:35` — new registry entry routing `AssignIssues` to `UpdateIssueCommand`
- BCC dispatch — already correct (no change needed; `WarningsManager._actionToCommandTag` already mapped `AssignIssues → UpdateIssue`)

### MgasVerify step honouring

**Commit:** `a4843cca3`

`MgasVerifyCommand` now reads `HcOptions.MgasStep` (0 = All / 1..12 = single NFPA 99 §5.1.12 step) and gates the step loop accordingly. ComboBox in StingDockPanel.xaml gained a new first item `All — Full 12-step verify` (`Tag="0"` `IsSelected="True"`). Button relabelled from "MGAS Verify (Full)" → "MGAS Verify". `HcOptions.MgasStep` default flipped 1 → 0 with XML doc explaining the new semantics. 0 dispatch tags added or removed.

### DrawingTypeEditor 6-button collapse

**Commit:** `d023a5ec6`

6 align-direction buttons in the **dialog** (VPAlignTop / MidY / Bot / Left / MidX / Right) → 1 button labelled "Align Viewports…" with tag `VPAlignRight`. The 5 dropped tags couldn't have their handler arms deleted because each tag is ALSO referenced from the main dock panel XAML (lines 1358-1363). Dialog cleaned up; main panel still has the 6-button row (followup item).

### Material consolidation (Option a)

**Commit:** `d823e9c29`

User picked Option (a) Hub-only from the triage. Net effect:
- **Deleted** `StingMaterialManagerCommand` (orphan #1)
- **Deleted** `MaterialManagerCommand` + `MaterialManagerDialog` modal (surface #2) — ~2022 lines net
- **Added** dock-panel `ToggleMaterialHub` button matching the pattern used for HVAC / Electrical / Plumbing / LPS toggle buttons
- `ToggleMaterialHubCommand` was already wired in `StingCommandHandler.cs:2074` — no handler change needed

Material Hub is now the single material-management surface.

### CreateIssuesFromWarnings

**Commit:** `60c30f684`

New 365-line command `StingTools/BIMManager/CreateIssuesFromWarningsCommand.cs` replacing the silent fall-through to `RaiseIssueCommand`. Highlights:
- Reads warnings via `WarningsEngine.ScanWarnings(doc)`
- Scope picker (3-way command-link TaskDialog): Critical only / Critical + High (default) / All classified — with live counts per option
- **Idempotent** via `source_hash` field (SHA-256 first 12 hex chars over `"warn-v1" | Category | Severity | first 100 chars of desc | sorted element-ID list`). Re-running on the same warnings produces 0 duplicates and reports clearly.
- Type mapping: Critical→NCR/CRITICAL, High→SI/HIGH, Medium→RFI/MEDIUM
- Persists via `BIMManagerEngine.CreateIssue(...)` + `SaveJsonFile(issuesPath, …)` — schema-compatible with IssueDashboard / UpdateIssue
- `FromWarnings` rewired in `StingCommandHandler.cs:3772` and `BimCommandModule.cs:41`

---

## 10. Phase X — Planscape webapp revive

The webapp at `localhost:5000` was completely dead at session start — empty body on every tab, stuck "Signing in...", no click handlers binding.

### Deep audit (read-only)

**Commit:** `audit/planscape-deep-review` branch (not merged)

`docs/PLANSCAPE_SERVER_AUDIT.md` + `docs/PLANSCAPE_ENHANCEMENT_BACKLOG.md`. The shocking finding: **one broken static asset** killed everything. `Planscape.Server/src/Planscape.API/wwwroot/js/dashboard.js` had a mis-spliced duplicate block in the IIFE (lines 789-1039 ish), so the whole file failed to parse, so `boot()` never ran. Server-side: healthy. CORS, JWT, ~120 controllers all fine.

### P0-1+2 — dashboard.js revive

**Commit:** `25ad81fe8`

Surgical fix:
- **P0-1**: deleted 82-line orphan block (was duplicate `initProjectsMap` body spliced into `trendSparklineHtml`)
- **P0-2**: added 16-line `loadPublicConfig` function that fetches `/api/public-config` (already wired server-side, `[AllowAnonymous]`), wrapped in try/catch so a missing endpoint doesn't crash boot

Diff: +15 / -81 on a single file. `node --check` PARSE OK.

### P0-3 — Redis harden

**Commit:** `e24275acc`

`AuthController` had 7 Redis call sites that would 500 if Redis was unavailable. Each wrapped in try/catch (`RedisException | TimeoutException | SocketException`):
- Lockout pre-check → **fail-open** (failing-closed = self-DoS during a Redis blip; IP RateLimiter + bcrypt + DB-backed RefreshToken still cap abuse)
- Refresh inactivity check → **fail-open**
- 5 write paths (fail-count increment, success clear, JTI blacklist, etc) → swallow + log

`Program.cs` ConnectionMultiplexer.Connect replaced with explicit `ConfigurationOptions { AbortOnConnectFail=false, ConnectTimeout=5000, ConnectRetry=3 }` + outer try/catch so app boots when Redis is down. Multiplexer auto-reconnects when Redis returns. `/health` already has a Redis check — no follow-up needed.

15 Redis calls preserved (wrapped, not removed).

### P0-4 — tenantId claim mismatch

**Commit:** `4a6a81eb1`

JWT issues claim `"tenant_id"` (snake_case, confirmed at `AuthController.cs:964`). 8 controllers were reading `"tenantId"` (camelCase) — every authenticated call to them 500'd. Fixed:

Dashboard / Boq / Classification / Mfa / ModelChecks / OfflineManifest / Sso / Suitability — `User.FindFirst("tenantId")` → `User.FindFirst("tenant_id")` plus the matching exception-message strings.

Grep verified the 8 above were the only mismatched controllers. 60+ other controllers already read `"tenant_id"` correctly. No other claim-name mismatches detected. Diff: 8 files / +16 / -16.

### P0-5 — empty-tenant friendly state

**Commit:** `8b157e085`

Path A (client-side) — `dashboard.js` line 164-183 (21 net lines added). When `/api/projects` returns `[]`, render a friendly panel:

```
Welcome, {userName}.
No projects yet — create one to start tracking issues, documents,
transmittals, and the rest of the coordination workflow.
[＋ New Project]
```

The button wires to `openNewProjectModal()` (already in dashboard.js since P0-1's audit notes). `<div id="modal-mount">` rendered so the modal can attach. Style classes match the populated dashboard's `greeting-strip` / `summary` / `actions` / `btn-primary`. `node --check` PARSE OK.

---

## 11. Future work — open follow-ups

Items surfaced during the campaign that weren't in the original scope. Each has enough detail to pick up cold.

### Phase Y — Planscape P1 server items (runs in terminal Claude Code session, not this session)

A prompt was written and pasted into the terminal Claude Code session covering 4 sequential PRs:

- **P1-A** `fix/ifc-ingest-migration` — **already landed** on the terminal side (commit `0938b577`). Generated `20260519000000_IfcIngestSubstrate.cs` migration for `ExternalElementMappings` table + 2 partial-unique indexes on `TaggedElements`. Hand-authored (not `dotnet ef migrations add`) because the model snapshot is severely stale (58 entities tracked vs 113 configured in OnModelCreating; 0 of 72 existing migrations carry `[Migration]` attributes — dev workflow uses `CreateTables()` not `Migrate()`). Auto-gen would have emitted a 55-table monster.
- **P1-B** `fix/hvac-publish-rewire` — pending. Reconcile route mismatch between `PlanscapeServerClient.PushHvac*Async` and `HvacController`. ~0.5d.
- **P1-C** `fix/warnings-signalr-wire` — pending. Larger (~1d). dashboard.js has no SignalR client; needs install + connect + subscribe to `WarningsReported`. Reusable subscribe pattern so future events (IssueRaised / ClashDetected / WorkflowStateUpdate) can follow same shape.
- **P1-D** `fix/signalr-live-events` — deferred until P1-C lands. Subscribes to the remaining server-side events.

### Phase Z — newly-surfaced findings

These came up DURING the campaign work but weren't in the audit. Each is its own ticket-sized item.

**Z-1 — Photo*.cs services break the build (43 pre-existing CS1061 errors)**
Discovered during P1-A. `Planscape.Server/src/Planscape.Infrastructure/Services/PhotoChecklistDueJob.cs` and related Photo*.cs files reference missing `PhotoChecklistItems` / `PhotoAlbumPhotos` / `PhotoPolicies` / `PhotoAlbums` DbSets on `PlanscapeDbContext`. Either the feature is dead (delete the orphan services) or unfinished (add the DbSets). Audit needed before deciding. The running webapp must use a different build path because Infrastructure literally doesn't compile right now.

**Z-2 — EF model snapshot is severely stale**
Discovered during P1-A. Snapshot tracks 58 entities; `OnModelCreating` configures 113. The Migrate() pattern is unused. Future migrations face the same trap that P1-A worked around. Either regenerate the snapshot cleanly OR commit to the `CreateTables()`-only workflow and delete the now-half-used EF migration infrastructure.

**Z-3 — MgasVerify ComboBox label drift**
Discovered during Phase D MgasVerify wiring. The XAML ComboBox labels ("Pressure decay", "Cross-connection", etc) **do not match the canonical 12-step checklist order** in `HTMStandards.MgpsVerificationChecklist`. The fix dispatches the right `MgpsVerificationChecklist[N-1]` based on the picker's numeric Tag, so step 7 always runs index `[6]` whatever the label says — but the labels lie to the user. Relabelling required.

**Z-4 — Main dock panel still has the 6-button DOCS align row**
Phase D collapsed the row inside `DrawingTypeEditorDialog`. The main panel (DOCS tab, lines 1358-1363) still has 6 separate `VPAlignTop / MidY / Bot / Left / MidX / Right` buttons all routing to `AlignViewportsCommand`. Could be collapsed to 1 the same way the dialog was, with the same external-ref policy.

### Deferred from triage, never picked up

The triage doc (`docs/PHASE_D_TRIAGE.md`) lists items beyond the top-5 that we worked. Re-read it when starting the next campaign — most are still OPEN.

---

## 12. Merge log

In chronological order. All commits land on `main` via fast-forward (with rebase for parallel-agent rounds).

```
ef55df2f0  Phase A:   shared button/section styling (outline aesthetic, theme-cycle responsive)
be787ad4f  Phase A.5: strip inline fills from emphasis cards + state buttons
438a82a97  Phase A.6: multi-target theme registry — every open panel cycles
b9996d274  Phase B Round 0: per-tab interaction patterns catalogue
5bb5480df  Phase B Round 1 (INTEROP): audit-first redesign plan
60fea4493  Phase B Round 1 (INTEROP): 18 sections re-patterned
ff4c13383  Phase B Round 2 (DOCS): audit plan
f47c3d3e2  Phase B Round 2 (DOCS): 10 sections re-patterned
1f174ff73  Phase B Round 2 (MODEL): audit plan
aed3ab7ae  Phase B Round 2 (MODEL): 17 sections re-patterned
803136a01  Phase B Round 2 (SETUP): audit plan
c6d37150c  Phase B Round 2 (SETUP): 16 sections re-patterned
e6e78f10a  Phase B Round 3 (CREATE TAGS): audit plan
463cd6f38  Phase B Round 3 (CREATE TAGS): 7 sections re-patterned
1628b2192  Phase B Round 3 (CREATE TAGS) fix: align scope radios with 2-state handler
7d28ff54e  Phase B Round 3 (TAG STUDIO): audit plan
29673e969  Phase B Round 3 (TAG STUDIO): 7 sub-tabs re-patterned
d97ad09db  Phase B Round 3 (BIM): audit plan
e04c1d9e2  Phase B Round 3 (BIM): 24 sections re-patterned
14da7e29a  Phase B Round 4 (TAGGING): audit plan
3cbcdb02f  Phase B Round 4 (TAGGING): XAML + RunTaggingRunner
25ad81fe8  Phase X P0-1+2: dashboard.js revive (parse fix + loadPublicConfig)
e24275acc  Phase X P0-3:   AuthController + Program.cs Redis harden
4a6a81eb1  Phase X P0-4:   8 controllers read canonical tenant_id JWT claim
f1d8ef08e  Phase C:   Electrical panel migration
2287d1db5  Phase C:   HVAC panel migration
b88b3533e  Phase C:   Plumbing panel migration
5cbda840e  Phase C:   LPS panel (dictionary-merge only)
8b157e085  Phase X P0-5:   friendly empty-tenant state
f011ac092  Phase C:   Material Hub migration + ThemeManager wiring
21d3ab3ce  Phase D:   dedupe (Lightning/SLD/Symbols/MEP)
1fe9d86d3  Phase D:   AssignIssues rewire (3 surfaces → UpdateIssueCommand)
a4843cca3  Phase D:   MGAS step wiring (HcOptions.MgasStep honoured)
d023a5ec6  Phase D:   DrawingTypeEditor 6-button collapse
d823e9c29  Phase D:   material consolidation (Hub-only — 2022 lines deleted)
60c30f684  Phase D:   CreateIssuesFromWarnings command (idempotent, scope-picker)
```

Plus the unmerged audit branches kept as reference:
- `audit/planscape-deep-review` — `docs/PLANSCAPE_SERVER_AUDIT.md` + `docs/PLANSCAPE_ENHANCEMENT_BACKLOG.md`
- `audit/phase-d-triage` — `docs/PHASE_D_TRIAGE.md`

---

## 13. Net effect

| Metric | Result |
|---|---|
| Lines deleted across the campaign | ~3500 net |
| Tabs re-patterned in main panel | 8 (INTEROP / DOCS / MODEL / SETUP / CREATE TAGS / BIM / TAG STUDIO / TAGGING) |
| Sub-tabs re-patterned | 7 TAG STUDIO inner sub-tabs |
| Dedicated panels migrated to shared dictionary | 5 (HVAC / Electrical / Plumbing / LPS / Material Hub) |
| Buttons removed from main panel | 20 (all reachable from dedicated panels) |
| Material surfaces consolidated | 3 → 1 (Hub-only) |
| Theme registry | Fixed (was singleton, now multi-target with WeakRef GC) |
| Planscape webapp | Revived from dead to fully usable empty-tenant friendly |
| Planscape server controllers fixed | 8 (tenantId → tenant_id claim) |
| Planscape Redis hardening | 7 call sites wrapped in fail-open / swallow-and-log |
| Net new dispatch tags | ~10 (suite runners introduced by Phase B; all consumed locally, not in handler) |
| Net dispatch tags lost | 0 (every one reachable somewhere) |

What the user sees:
- Every panel cycles theme together
- Main panel is dramatically shorter (long button rows replaced with pickers / dropdowns / checkbox grids / expanders)
- Dedicated panels are consistent with the main panel
- Material Hub is the single material surface
- Planscape webapp is alive and friendly to empty tenants

---

*This document was written at campaign close as the last commit of Phase D.
If you're starting the next campaign, the future-work items in §11 are
where to begin.*
