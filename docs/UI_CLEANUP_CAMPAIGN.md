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

These came up DURING the campaign work but weren't in the audit. Each is its own ticket-sized item. **Status legend:** ✅ closed · 🔄 in flight · ⏸ user-config / external · 📤 terminal-agent-owned · 📥 cloud-agent-owned

**Z-1 — Photo*.cs services break the build (43 CS1061 errors)** ✅ MERGED to main (commit `680029a81`, branch `fix/wire-photo-services`)
**Classification: UNFINISHED, not dead** — the entire Photo feature was authored (entities + controllers + Hangfire jobs + mobile UI) except the DbSet declarations. Evidence: all 8 entities exist in `Planscape.Core/Entities` (+5 more Photo*); controllers exist (PhotoAlbums/PhotoChecklists/PhotoPolicy/PhotoShare/PhotoExport/DistributionGroups + SitePhotos*, using `_db.PhotoAlbums` 14×); mobile UI exists (`Planscape/app/site-photos/{albums,album-detail,checklists,checklist-detail}.tsx` + `src/api/endpoints.ts`). Only the 8 DbSets were missing → neither Infrastructure NOR the API compiled (so the doc's "API builds" was stale; the deployed webapp is an OLDER build). **Fix:** added 8 `DbSet<T>` to PlanscapeDbContext (PhotoAlbums, PhotoAlbumPhotos, PhotoAnnotations, PhotoChecklists, PhotoChecklistItems, PhotoPolicies, DistributionGroups, DistributionGroupMembers); fixed a masked bug behind them (`DailyPhotoDigestJob.cs:143` used undefined `guest.Email` → `email`). **Migration: deliberately none** — heeded the Z-2 trap (55/113 entities unmigrated; `dotnet ef migrations add` would emit a monster). Schema is created via the project's `EnsureCreated()`/`CreateTables()` path (`Program.cs:1289-1310`), same as every other unmigrated entity. `dotnet build Planscape.Infrastructure`: **0 errors / 0 warnings (was 43)**. EF runtime model-validation to be confirmed on first server start (8 entities now join the model; FKs resolve by convention) — compile mandate met.

**Z-1b — API project has 6 pre-existing duplicate-definition errors (server STILL won't build)** 📤 NEW — P0-class (the API doesn't compile)
Clearing Z-1's 43 errors let the compiler report further and surfaced **6 remaining CS0111/CS0101 duplicate-definition errors in the API project** — unrelated merge artifacts, NOT caused by Z-1 (a DbSet addition can't create duplicate methods): `TransmittalsController.BulkCreate` ×2, `DocumentsController.ValidateName` ×2, cross-file duplicate `ReorderRequest` / `AddMemberRequest` records. **Until these are fixed the API project doesn't compile** — which is why the running webapp is a stale older build (confirmed: neither Infrastructure (pre-Z-1) nor the API (still) builds from current source). This is the same class as Z-11 (duplicate `seq:` in docker-compose) and the dashboard.js dupe — a recurring `-X ours` / bad-merge duplication pattern. Fix: dedupe each — keep one definition of each method/record, delete the merge-artifact copy, confirm `dotnet build Planscape.API` → 0 errors. Likely a ~1h cleanup but it's what actually unblocks deploying current source.


**Z-2 — EF model snapshot is severely stale → PATH A (formalise CreateTables-only)** ✅ MERGED to main (commit `146283729`→rebased, branch `fix/ef-formalise-createtables`, doc `EF_SCHEMA_WORKFLOW.md`)
Read-only audit confirmed: snapshot tracks 58, `OnModelCreating` configures **113** (55 stale). Boot path (`Program.cs:1284`): `useEnsureCreated = IsDevelopment() || PLANSCAPE_USE_ENSURE_CREATED=="true"` → if true `creator.CreateTables()` (1310, materialised from OnModelCreating); else `db.Database.Migrate()` (1315). Both branches then run idempotent patchers (`PatchDevSchemaAsync` + `PlatformSchemaPatcher`, ADD COLUMN / CREATE TABLE IF NOT EXISTS). Migration state: **73 migration classes but 0 `.Designer.cs` files + 0 `[Migration]` attributes** → EF discovers ZERO migrations → `Migrate()` is a **no-op**. No env deploys via migrations; all schema comes from CreateTables() + patchers; the migration folder + snapshot are inert. **Decision: PATH A** — regenerating (B) buys nothing (nobody uses migrations) and a naive `dotnet ef migrations add` would diff the 55-stale snapshot into a monster (the P1-A trap). `EF_SCHEMA_WORKFLOW.md` records the audit + the canonical "add an entity" procedure (DbSet + OnModelCreating + idempotent patcher; **never** `dotnet ef migrations add`). No code / migration files / snapshot touched; migration files left in place (inert) — deletion deferred as irreversible. Consistent with Z-1 (Photo DbSets added with no migration).

**Z-2b — Production fresh-DB boot is potentially broken (Migrate() is a no-op)** 📤 NEW — P0-class production risk (flagged by Z-2)
The non-dev (production) boot branch calls `db.Database.Migrate()`, which Z-2 proved is currently a **no-op** (0 recognised migrations). So a **fresh production database would get NO tables from `Migrate()`** — the app would start against an empty/partial schema and fail, UNLESS `PLANSCAPE_USE_ENSURE_CREATED=true` is set in prod (routing it through CreateTables) OR the schema already exists from a prior provision + the idempotent patchers fill gaps. **Production's env-var / provisioning MUST be verified before any boot-path change.** The safe fix (separate, sign-off-gated): route the non-dev branch through `CreateTables()` too so the boot is honest about where schema comes from — but ONLY after confirming how prod is currently provisioned (if prod has a long-lived DB that was EnsureCreated once, flipping the branch is harmless; if something external runs migrations, this changes behaviour). Z-2 deliberately did NOT change boot behaviour — this is the follow-up.


**Z-3 — MgasVerify ComboBox label drift** ✅ closed (cloud commit `e3959fee3`)
The XAML ComboBox labels did not match the canonical 12-step checklist order in `HTMStandards.MgpsVerificationChecklist`. Relabelled all 12 ComboBoxItems to match the canonical order; Tag values (the step indices) unchanged.

**Z-4 — Main dock panel still has the 6-button DOCS align row** 📤
Phase D collapsed the row inside `DrawingTypeEditorDialog`. The main panel (DOCS tab, lines 1358-1363) still has 6 separate `VPAlignTop / MidY / Bot / Left / MidX / Right` buttons all routing to `AlignViewportsCommand`. Could be collapsed to 1 the same way the dialog was, with the same external-ref policy. **Terminal agent Task 2.**

**Z-5 — Deletion briefs need dependency-graph audit** ✅ closed (process change)
Lesson from the Material Hub regression: when an agent is briefed to delete a "dialog" file, the brief must require a class-by-class consumer audit BEFORE delete. Bake this into future deletion briefs.

**Z-6 — `MergeRecoveryStubs.cs` dead-returning methods** 📤
16+ methods in `StingTools/Core/MergeRecoveryStubs.cs` return `Task.FromResult(false/null/0)` without making HTTP calls. P1-B fixed one cluster (HVAC); the other 16 cover Photo NDA, Photo Albums, Photo Export, Photo Bulk Ops, Distribution Groups, Model dedup/delete. Each is a UI surface silently reporting fake success. Audit + per-resource PR plan in `docs/PHASE_Z_AUDITS.md`. **Terminal agent Task 3 (sequential PRs).**

**Z-7 — SignalR subscribers gap** ✅ closed (cloud commit `1ac9a5c13`)
P1-D wired 9 events, missed 4. Cloud added `CommentAdded` → issues view, `ApprovalDecided` → documents view, `TagsUpdated` → overview view. Dropped `NotificationCreated` (no server raise). Deferred: LpsRecordPushed, SitePhoto*, PhotoAlbumChanged, Deliverable* (no UI surface yet — wire when views land).

**Z-8 — `ClashLive` button misleading label** ✅ closed (cloud commit `7d621dcf8`)
Two "Live" buttons renamed to "Refresh" with corrected ToolTip explaining the in-Revit clash kernel architecture. No behaviour change.

**Z-9 — Agent brief discipline: always grep server raises, never invent names** ✅ closed (process change)
Lesson from P1-D: my brief invented `WorkflowStateUpdate` (real name: `WorkflowRunCompleted`) and `ClashNotification` (doesn't exist). Agent caught both. Apply to every future "subscribe to event X" brief.

**Z-10 — Mapbox token literal placeholder reaches production HTML** ⏸ user-config
`PLANSCAPE_MAPBOX_TOKEN` placeholder appears in `index.html`. No code fix needed — P0-2's `loadPublicConfig` already plumbs from `/api/public-config`. User obtains a token at mapbox.com, sets env var, restarts server.

**Z-11 — docker-compose.yml duplicate `seq:` key** ✅ closed (cloud commit `d51269c05`)
Lines 264-308 were a byte-identical duplicate of the observability section at lines 184-228. Deleted. YAML now parses cleanly.

**Z-12 — wwwroot bind-mount for dev iteration** ✅ closed (cloud commit `b8c0b5a45`)
Added `../src/Planscape.API/wwwroot/js:/app/wwwroot/js` + same for `css/` to the api service. Dev workflow before: edit → `docker compose down` → `up --build` (~3 min) → reload. After: edit → reload. No image rebuild needed for JS/CSS changes.

**Z-13 — Team handbook: `--remove-orphans` after compose edits** ⏸ doc-only
Add to onboarding: `docker compose down --remove-orphans` before `up --build` to avoid stale containers holding ports (the 2-day-old container that served stale `dashboard.js` for hours).

**Z-14 — `.dockerignore` for wwwroot** ⏸ superseded by Z-12
The bind-mount achieves the same effect (live JS changes without rebuild). Skip unless image-size becomes a concern.

**Z-15 — Forensic: when did the duplicate `seq:` enter the repo?** ✅ documented
Introduced by merge commit `41ae11234` on April 17, 2026 — a `-X ours` consolidation where both parents had ONE seq block each at different line positions. `-X ours` preserved both without YAML semantic dedup. Multiple downstream `-X ours` merges propagated.

**Z-16 — CI check: `docker compose config -q`** 📤
Add to `.github/workflows/` so `docker-compose.yml` parse failures are caught in CI. The Z-11 duplicate ran for weeks because nobody locally was running `compose up` from main; CI would have caught it on the introducing merge.

**Z-17 — `git push --delete` loops need squash-merge-aware check** 📤 (lesson)
The terminal agent's branch-deletion loop's `git branch -r --contains` check returns false for squash-merged branches (squash creates a new SHA that's not a textual descendant of the source). Future deletion scripts should fall back to `git log --oneline --grep="<source branch's commit subject>" origin/main` OR `git log --cherry-pick` for content-equivalence detection.

### Phase Z numerics — deep formula/cost/material audit findings

Audit branch `audit/numerics-deep-review` commit `8774be49` — `docs/PHASE_Z_NUMERIC_AUDIT.md`. 0 P0 / 14 P1 / 11 P2. Three P1s reach delivered BOQs/carbon reports via common export paths and are "fix-next":

**Z-18 — VAT missing from headline `GrandTotalUGX`** 🔄 in flight (terminal agent)
`BOQModels.cs:162-163` computes the headline total WITHOUT VAT. Only `BOQProfessionalExportCommand.cs:1537` adds it in the Word export. Standard XLSX, budget-variance, dashboard, BCC totals are **~18% short** of true contract sum. Fix: centralize VAT into the model (don't sprinkle into each export path). Also verify `VatPct` default = 18 (Uganda VAT).

**Z-19 — Sand bulking DRY=1.15 non-physical** 📤
`MATERIAL_LOOKUP.csv:219-222` has DRY=1.15 (dry sand doesn't bulk — should be ≈1.00), DAMP=1.25 ✓, WET=1.10 (saturated sand collapses back near dry — should be ≈1.05). Reference: CIRIA / IS 2386. Fix: pure CSV one-line edit. Direct quantity error on every sand line.

**Z-20 — Embodied-carbon undercount for metals & glass (~4–9× low)** 📤
`MEP_MATERIALS.csv` steel/copper/glass carbon factors 4-9× below ICE v3.0 reference values. Plus **Z-20b**: concrete-carbon cross-file drift — `BLE_MATERIALS.csv` says 150 kgCO₂/m³ vs `MATERIAL_LOOKUP.csv` 345. Any delivered carbon report or RIBA-stage carbon number is materially wrong-low. Fix scope: not just edit CSVs — DECIDE which file is canonical (MATERIAL_LOOKUP recommended per Phase 76+ work) and route consumers there.

**Z-21 — Waste% skipped on BOQ legacy-fallback path** ✅ MERGED to main (PR #283, commit `2b00a28e3`)
Branch `fix/boq-waste-legacy-fallback` commit `2b00a28e3`. **The code is NOT on `origin/main` yet — PR #283 is still open.** Do not assume Z-21 is mergeable into a branch's base until #283 lands. Fixed in `BOQCostManager.DeriveQuantity` — fallback paths (m², m, m³, kg/tonne) now route through new pure-helper `WasteFactor.Apply(qty, unit, wastePct)`. "Each / item / couldn't-measure" placeholders deliberately stay at 0%. Waste% source: `COST_DEFAULT_WASTE_PCT` config knob (5% default, project-overridable). New `StingTools.Boq.Tests` project added — 20 tests pass, first tests-with-the-fix in the Phase Z numeric series.

**Z-21b — Rate-override × quantity-fallback double-count** ✅ MERGED to main (commit `026c862af`, merge `8796f337c`)
Resolved via Option A (quantity is the single waste surface). `RateProviders.cs:~88` — removed `loadedRate *= 1 + ovr.WastePercent/100` (kept Overhead + Profit); the rate no longer carries waste. `BOQCostManager.DeriveQuantity` legacy fallback — `wastePct = WasteFactor.ResolveWastePercent(overrideWaste, COST_DEFAULT_WASTE_PCT)` so the explicit `StingCostRateOverrideSchema.WastePercent` governs the quantity side and applies exactly once. 6 new tests (26 total): `LineTotal_RateOverrideElement_WastesExactlyOnce` asserts line == 1080 AND `NotEqual(1166.4)` (codifies the old double-count's absence); `LineTotal_NonOverrideElement_UnchangedFromZ21` == 1050 (common path not regressed). Build 0/0, 26/26 pass. **Branch is STACKED on the Z-21 branch — merge order: PR #283 (Z-21) FIRST, then this. If merged alone it carries both fixes.**

**Z-22 — formula "cycles" — Stage 1 found ZERO real cycles; Stage 2 cleaned the noise** ✅ FULLY CLOSED (Stage 1 audit `20b7ea6aa` on `audit/formula-cycles`; Stage 2 fix `1f1a20440` on `fix/formula-self-ref-cleanup`, MERGED to main)
The numeric audit's "63 formulas in cycles" was WRONG — it counted self-reference noise. Stage-1 deep audit replicated the engine's actual detector (`FormulaEvaluatorCommand.cs:485` already does `if (token == ParameterName) continue;` — self-skips) and found: **0 genuine cycles, 0 multi-node cycles, 0 fixed-point cycles**. Kahn orders all **278/278**; "cycle detected" never fires on shipped data. Cross-checked with independent Tarjan SCC + Kahn (both agree). The "63" = 19 single-node self-loops + 44 non-cyclic downstream dependents. 18 of the 19 self-loops are mis-keyed validation formulas (`if(SELF op threshold, "[!WARN]", "")` listing their own output as an input in the metadata column); 1 is `BLE_STRUCT_CONCRETE_GRADE_TXT` (BOQ-critical root of the concrete take-off, but its self-entry is metadata noise, the 17 `CST_*` downstream are non-cyclic). **Stage 2 (SHIPPED, `1f1a20440`):** removed the 19 self-entries from `Input_Parameters` only — `Revit_Formula` untouched (verified field-by-field vs HEAD: 0 formula changes, 19 input-only changes). Used a **row-keyed edit matched by Parameter_Name** — critical because `HVC_VEL_MPS` appears in 4 rows and a naive whole-file replace would have stripped it from the 3 rows that legitimately reference it as an input; the agent caught the multi-match and aborted the naive attempt before writing. Dependency_Level needed no regeneration (engine recomputes by topo sort at load, `FormulaEvaluatorCommand.cs:509`; CSV column is a provisional hint only). 2 new tests lock the regression (0 self-loops + 278/278 Kahn ordering). **Delivered numbers: none moved** — runtime ordering was already correct (engine self-skips); pure metadata hygiene. The whole "63-cycle" finding collapsed from a feared 1-2 week algebraic project to two small PRs because the audit overstated it 30× and the engine already handled it. Deep-audit-before-fix paid off.

**Z-23 — Smaller numeric findings (10 P1 + 11 P2)** ✅ MERGED to main (commit `540b2382c`, branch `fix/numeric-batch-cleanup`)
Batch-fixed with each value verified against its reference, build 0/0, 36/36 tests. Density: steel ceiling tile 2300→7850 (ICE/CIBSE), fiberglass tile 2300→10-100 mineral wool, cement plaster 2400→2000 (BS 5492), softwood skirting 720→480 (was hardwood). Standards: CIBSE supply-duct main velocity 10→9 m/s; BS 7671 cooker comment-only (clipped-correct, marginal). BOQ 6.6: kept `Math.Abs` for closeness ranking, ADDED `BOQReconcileMatch.SignedDeltaUGX` + direction in Reason (+ overrun / − credit-back) — better than the audit's literal "replace Abs" suggestion. Cosmetic 1.4: areaMm2 comment fixed (Ø35.7 mm).
**Delivered numbers: NONE moved** — the 4 density fixes feed mass-display + thermal-U-value only; BOQ m² uses HOST_AREA and carbon uses Tier-1 per-m³, so density is off the delivered-quantity path. 8.1 changes a validation threshold (flag), not a cost/carbon number.
**Skipped (both correctly):** timber 2.8 (blocked on Z-25 Path C); rebar-waste 4.5 + concrete-+5% 4.6 → deferred to Z-23b (they'd double-count with Z-21's COST_DEFAULT_WASTE_PCT which rebar/concrete already inherit).

**Z-23b — Rebar cutting/lapping waste + concrete over-order as opt-in knob** 📤 NEW (deferred from Z-23)
Audit items 4.5 (rebar NRM2 5-7% cutting/lapping waste) + 4.6 (concrete +5% over-order) are NOT single-value fixes — they'd wire a new factor into the FormulaEngine concrete/rebar takeoff. Z-23 correctly deferred because BOQ-priced concrete (m³) and rebar (kg) ALREADY inherit Z-21's `COST_DEFAULT_WASTE_PCT` (5%); a second blanket factor would double-count (the Z-21b bug class). Z-23b must be an **opt-in project-config knob** applying ONLY to the rebar-tonnage / concrete-volume takeoff path where the 5% isn't already applied — AND the QS must confirm their workflow doesn't already pad at order time before it's enabled. Gated on QS sign-off.


**Z-24 — `MATERIAL_LOOKUP.csv` Tier-1 resolver is dead code at runtime** ✅ MERGED to main (commit `c0337a3d7`, branch `fix/material-lookup-long-format`)
Discovered DURING Z-20. `MaterialLookupCsv.EnsureLoaded()` did `iName = Idx("Material","Name","MaterialName")` against the long-format header `Category,TypeKey,Property,Value` → `iName = -1` → empty cache → `GetCarbon`/`GetCost`/`GetDensity` all returned 0. The "Phase 76+ canonical Tier-1" lookup was non-functional. Real BOQ values came entirely from per-row `PROP_CARBON_KG_M3` material params in BLE/MEP CSVs (which Z-20 fixed) — the invisible bug was masked because the fallback chain transparently degraded to the next tier.

**Fix (Option A — long-format parse + pivot):** new `StingTools/UI/MaterialLookupParser.cs` (212 lines) pivots long rows into `MaterialLookupRow` keyed by `(Category, TypeKey)`, indexed under both `"Category TypeKey"` and bare `TypeKey` (when globally unique) + a `Category=DEFAULT` row. Legacy wide-format parser retained for backwards compat. `GetCarbon("C30")` was 0, now **345**. 10 new tests in `StingTools.Boq.Tests` (36 total): bare-TypeKey, composite keys, category-DEFAULT, ambiguous-DEFAULT-not-bare-keyed, multi-property pivot, unknown-material-0, header-only-empty, legacy-wide-still-parses. Build 0/0.

**Resolver reorder: DEFERRED (correctly) → see Z-24b.** The agent did NOT reorder the carbon/cost resolver, so **no delivered BOQ number changed by reorder**. The chain stays material-param-first. The ONLY theoretical delta: an element named exactly like a bare-unique LOOKUP key (e.g. type "C30") with NO carbon material-param — it now resolves via LOOKUP (345) instead of the keyword fallback (was 0 / generic). That's a fix, not a regression — those elements were under-reporting carbon. Flagged for sign-off; couldn't enumerate without the live model.

**Z-24b — Populate LOOKUP + reorder resolver** ✅ STEP 1+2 MERGED (commit `1e1c16afd`, branch `fix/material-lookup-populate`); STEP 3 reorder BLOCKED → Z-24c
**STEP 1+2 done:** MATERIAL_LOOKUP populated with metals/glass/timber carbon+density (matched the SHIPPED Z-20 values, not the prompt's nominal figures — the agent reconciled against actual data: structural steel 12090, galv 22372, copper 31360, glass 3600; timber carries the Z-25 fossil/biogenic split). Cost not populated (no ICE cost values to match). Build 0/0, **+7 tests → 51 total, all pass**. Also **repaired the test project** that a prior merge broke (a phantom `CarbonModels.cs` reference, CS2001, + a dropped `MaterialLookupParser.cs` link — introduced by the cloud session's Z-25 rebase-conflict resolution; see Z-5b). **Resolver UNCHANGED → zero delivered numbers moved** (material-params still win).
**STEP 3 (reorder) BLOCKED → Z-24c.** STEP 2 reconciliation found the per-row BLE/MEP columns are **NOT single-valued per material** — Z-20 fixed galv ducts (→22372) but left trays at the old 12090, leftover 2500s scattered, a timber density bug (ρ720), and mislabels. So flipping the resolver to prefer LOOKUP is NOT a clean no-op — it would change delivered numbers for every element whose per-row value disagrees with the single LOOKUP value. STEP 3 needs the per-row cleanup (Z-24c) first, OR explicit sign-off on the deltas.

**Z-24c — Per-row BLE/MEP carbon consistency cleanup (prerequisite for Z-24b STEP 3)** 📤 NEW (found by Z-24b reconciliation)
Z-20 corrected carbon per material-TYPE but the per-row CSVs have the same material at MULTIPLE values: STRUCTURAL steel 12090×42 + 2500×3; GALVANISED 22372×23 + 12090×36 + 2500×5; STAINLESS 12090×7 + 2500×2 + 3250×1; COPPER 31360×8 + cable composites; GLASS 3600×3 + 12090×1; TIMBER −661×2(ρ480) + −992×39(ρ720). The 2500s are pre-Z-20 leftovers; the 12090-on-galv rows are un-migrated trays; the timber ρ720 is a density bug. Cleanup: make each material single-valued (align to the LOOKUP value), fix the timber density, clear the stale 2500s. AFTER this, Z-24b STEP 3's reorder becomes the clean no-op it was meant to be. Each value change must be flagged for sign-off (these DO move delivered numbers — they correct rows that were under/over-reporting).

**Z-5b — Cloud-session conflict-resolution bug (process lesson)** ✅ fixed by Z-24b
When the cloud session resolved the Z-25 rebase conflict in `StingTools.Boq.Tests.csproj` it hand-wrote a csproj referencing a phantom `CarbonModels.cs` (doesn't exist → CS2001) and dropped the real `MaterialLookupParser.cs` link — breaking the test project on main for 2 commits. The Z-24b agent (which builds) caught + fixed it. Lesson: the cloud session can't `dotnet build`, so hand-resolved conflicts in build-affecting files (csproj, code) are unverified — prefer rebasing the branch on the terminal side OR have the next build-capable agent verify. Pure-data/doc conflicts remain cloud-safe.


**Z-25 — Timber biogenic carbon: Path C SHIPPED (split fossil + biogenic columns)** ✅ MERGED to main (commit `abad8d2da`, branch `fix/timber-biogenic-split`)
Two new columns appended (no index shift): `PROP_CARBON_FOSSIL_KG_M3` (≥0) + `PROP_CARBON_BIOGENIC_KG_M3` (≤0); legacy net `PROP_CARBON_KG_M3` = fossil + biogenic (kept, recomputed for timber). ICE v3.0 timber values: fossil +0.263 kgCO₂e/kg (sawn softwood A1-A3, FSC/PEFC), biogenic −1.64 kgCO₂e/kg × row density. Resolver API added: `GetCarbonFossil`/`GetCarbonBiogenic` + `CarbonFactorResolver.GetCarbonFossilPerM3`/`GetCarbonBiogenicPerM3` (material-param first, then LOOKUP; fossil gated ≥0, biogenic allows negatives). Net `Resolve()`/`GetCarbon` untouched. +6 tests (42 total): net==fossil+biogenic on every BLE row, fossil≥0 & biogenic≤0, non-timber biogenic==0 & fossil==net (no regression), ≥41 timber rows sequester, ρ720 row matches ICE split (189/−1181/−992).
**Delivered numbers: NONE moved.** Both `CarbonFactorResolver:51` and `CarbonTrackingEngine:108` gate Tier-1 on `v > 0`, so the negative timber net param was ALREADY rejected — timber currently resolves via the Tier-4 wood keyword (+0.31/kg, gross). The net moved −900 → −992/−661 (now computed from real ICE components) but it's gated out anyway. Path C's value is **exposing the split**; wiring it into delivered totals is the report follow-up (Z-25b). Rebase conflict in `StingTools.Boq.Tests.csproj` (Z-22 + Z-25 both added test-CSV `<None>` entries) resolved by keeping both ItemGroups. Resolver conflict with Z-24b noted: MATERIAL_LOOKUP has no timber rows yet, so nothing to split there now, but the parser already reads CARBON_FOSSIL/BIOGENIC so Z-24b's timber LOOKUP rows must carry the split.

**Z-25b — Surface fossil / biogenic / net in the carbon report** ✅ MERGED to main (commit `8104ea9dc`→rebased `fd767f60c`, branch `feat/carbon-report-fossil-biogenic`) — done by a CARBON-ENGINEER specialist agent
**Professional decision (cited):** headline = **A1-A3 Fossil (gross upfront carbon)**, biogenic reported separately, net for whole-life context only. Standard: **RICS Whole Life Carbon Assessment 2nd ed. (2023)** — biogenic is NOT netted into the headline upfront figure (uptake must be balanced by a +biogenic Module C release unless permanent storage is evidenced; leading with net would flatter a timber scheme). Corroborated by RIBA 2030 (upfront targets are gross kgCO₂e/m²) + LETI (report sequestration separately). The agent owned the call rather than deferring.
**Implementation (1 number → 3):** new pure helper `StingTools/BOQ/BiogenicCarbon.cs`; `CarbonTrackingCommands.cs` — Dashboard `CarbonCalculatorCommand:~333` one "Total Carbon" line → A1-A3 Fossil (headline) / A1-A3 Biogenic (separate) / Net + methodology note; CSV `CarbonExportCommand:~383` Summary,Total → `A1A3_Fossil_Headline` / `A1A3_Biogenic_Separate` / `Net_Fossil_Plus_Biogenic`; `CalculateProjectCarbon:~228` accumulates via the helper; `CarbonResult` gains Fossil/Biogenic/Net (`TotalCarbonKg`=fossil alias, legacy-safe). Embedded methodology note quotes RICS WLCA 2023 so any assessor reading the report knows the convention.
**Delivered-number impact:** headline timber carbon moves for timber-bearing schemes — sample ρ480 timber: headline 148.8 (old Tier-4 keyword gross +0.31/kg) → **126.2** (ICE v3.0 sawn-softwood A1-A3 fossil +0.263/kg); biogenic −787.2 + net −661.0 now shown separately. **Non-timber: headline unchanged, no regression** (fossil = gross, biogenic = 0). This is the intended correction, standards-grounded + disclosed. Legacy `Resolve`/`GetCarbon` untouched. +12 tests → 63 total, build 0/0.
Note: Z-25b being on a pre-Z-2 base meant a rebase, but its commit touched neither `EF_SCHEMA_WORKFLOW.md` (Z-2) nor PlanscapeDbContext (Z-1) — both preserved; csproj already carried the Z-24b-repaired `MaterialLookupParser` link (no phantom `CarbonModels`).




Three resolution paths:

| Path | Approach | Trade-off |
|---|---|---|
| **A — Leave timber at −900, document the asymmetry** | Disclose in release notes that timber is biogenic-inclusive while steel/concrete (post-Z-20) are A1-A3 gross. | Quickest. Numbers correct per their own scope; reports remain biased low; QS must know to compensate. |
| **B — Strip biogenic from timber (≈+100-200 manufacturing-only)** | Everything in BLE/MEP becomes gross A1-A3, internally comparable. | Lose the timber sequestration story unless captured elsewhere. Industry-conservative. |
| **C — Split timber into 2 columns: fossil + biogenic** | Reports can show both, sum either way. Matches RIBA 2030 / LETI / RICS WLCA modern guidance. | Best long-term. ~1d agent work; schema change touching MATERIAL_LOOKUP / BLE / MEP CSVs + the resolver. |

**Industry direction:** modern frameworks (RIBA 2030 Challenge, LETI Climate Emergency Design Guide, RICS Whole Life Carbon Assessment) want **separated** reporting (path C) — show A1-A3 fossil + A1-A3 biogenic + A4-A5 + B1-B7 + C1-C4 as separate line items, sum at user's discretion.

**Recommendation:** path A for now (zero further code change, just disclose in release notes); path C as a future PR when a carbon engineer / QS can confirm the report format the client expects.

Blocks: any change to timber `PROP_CARBON_KG_M3` values until decided. Z-21 (waste% fallback) and Z-23 (smaller findings) are independent and unblocked.

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
