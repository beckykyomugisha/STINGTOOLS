# STINGTOOLS — Real Testing Guide

> How to move from demo/CI builds to actual Revit integration testing.

---

## Testing Tiers at a Glance

| Tier | What it tests | Machine needed | Automated? |
|---|---|---|---|
| **1 — Pure logic** | Clash math, routing algorithms, server API | Any (Linux/Mac/Windows) | ✅ `dotnet test` |
| **2 — Build verification** | Plugin compiles against Revit API | Windows (stubs work) | ✅ CI / `build.bat` |
| **3 — Revit integration** | Full plugin in a live Revit session | Windows + Revit license | 🔴 Manual today |
| **4 — Server stack** | Planscape Server APIs + DB | Any (Docker) | ✅ `docker compose` |

---

## Tier 1 — Pure Logic Tests (run right now)

No Revit required. These test the math engines that back the plugin.

```powershell
# From the repo root — Windows, Mac, or Linux with .NET 8 SDK
dotnet test StingTools.Clash.Tests/
dotnet test StingTools.Routing.Tests/
dotnet test Planscape.Server/tests/Planscape.Tests/
```

**What's covered** (13 tests):
- `MollerSatTests` — triangle/OBB intersection geometry
- `AabbSweepTests` — broad-phase sweep-and-prune
- `ClashGrouperTests`, `ClashHistoryTests`, `ClashRuleEngineTests` — clash logic
- `BcfMarkupBuilderTests` — BCF 2.1 XML export
- `AStarSolverTests`, `AcoRefinerTests`, `VoxelGridTests` — routing algorithms
- `ConduitRouteEngineTests` — conduit auto-route
- Planscape server integration tests (in-memory EF Core DB)

**When to run**: on every commit. The GitHub Actions `stingtools-plugin.yml` runs
these automatically on push to `main`.

---

## Tier 2 — Build Verification (Windows, no Revit license needed)

Proves the C# compiles cleanly. The build uses Revit API **stubs** in
`tools/revit-stubs/` so you don't need a Revit installation.

```powershell
# Install .NET 8 SDK first: winget install Microsoft.DotNet.SDK.8

# Option A — with real Revit installed:
build.bat

# Option B — with stubs only (CI path, no Revit needed):
dotnet build tools/revit-stubs/RevitApiStubs.sln
dotnet build StingTools/StingTools.csproj `
  -c Release `
  -p:RevitApiStubsDir="$PWD/tools/revit-stubs/bin"
```

**What this catches**: missing `using` statements, type errors, ambiguous
overloads, renamed Revit API members (e.g. `IntegerValue` → `Value`).

**What it does NOT catch**: runtime errors, logic bugs, UI layout problems,
performance issues.

---

## Tier 3 — Real Revit Integration Testing (the real test)

### Prerequisites

| Requirement | Where to get |
|---|---|
| Windows 10/11 | — |
| Autodesk Revit 2025, 2026, or 2027 | autodesk.com (Education / Commercial) |
| .NET 8 SDK | `winget install Microsoft.DotNet.SDK.8` |
| Git for Windows | `winget install Git.Git` (includes Git Bash for `build.bat`) |

### Step 1 — Clone and build

```powershell
# Recommended location (addin path already matches):
cd C:\Dev
git clone https://github.com/beckykyomugisha/stingtools STINGTOOLS
cd STINGTOOLS

# Build + deploy in one command:
build.bat
```

The script:
1. Finds your Revit installation automatically (2025 → 2026 → 2027)
2. Builds `StingTools.dll` in Release mode
3. Copies everything to `CompiledPlugin\`
4. **Stamps `StingTools.addin` with the actual `CompiledPlugin\` path** (no hardcoded paths)
5. Auto-copies the `.addin` file to `%AppData%\Autodesk\Revit\Addins\<year>\`

> **If you clone to a different path** (not `C:\Dev\STINGTOOLS`), the build script
> still works — it stamps the real path into the generated `.addin`. Do NOT
> manually copy the checked-in `StingTools.addin` — always use the one from
> `CompiledPlugin\` after a build.

### Step 2 — Get a test BIM project

You need a `.rvt` file with:
- At least 2 floor levels
- Rooms or spaces placed on a level
- Some MEP elements: pipes, ducts, conduits, cable trays, or light fixtures
- Preferably also walls, doors, windows (for structural/arch commands)

**Sources**:
- **Autodesk sample files**: installed with Revit at
  `C:\Program Files\Autodesk\Revit 2025\Samples\`
  (e.g., `rac_advanced_sample_project.rvt`, `rme_advanced_sample_project.rvt`)
- **BIMobject / Autodesk community** — free sample projects
- Create a minimal one: 2 rooms + 1 pipe + 1 duct + 1 conduit takes ~5 minutes

### Step 3 — Load the plugin and verify

1. Open Revit → open your test `.rvt`
2. Check the **STING Tools** ribbon tab exists
3. Click the **STING Panel** button — the dock panel should open on the right
4. Confirm: no error dialogs, no "Failed to load addin" messages in the
   `%AppData%\Autodesk\Revit\Journals\` log

**If the plugin doesn't load**, check:
```
%AppData%\Autodesk\Revit\Journals\journal.YYYY-MM-DD.NNNN.txt
```
Look for `"STING"` — any `LoadException` or `FileNotFound` near it.

### Step 4 — Run the smoke tests

Follow the 20-step checklist in `Tests/v4_smoke_test.md`.  
Estimated time: 30–45 minutes for a full pass.

**Priority order for first run**:

1. **Plugin loads** — dock panel opens, 3 panels register (Main, Electrical, Plumbing)
2. **Shared params bind** — TEMP tab → Setup → `Load Params` — expect "Loaded N params, 0 errors"
3. **Auto-tag** — SELECT tab → pick a room → TAGS tab → `Tag & Combine` → check params are populated
4. **Validate** — TAGS tab → QA → `Validate` — expect a compliance report
5. **Sheet ops** — DOCS tab → `Sheet Manager` → should open the WPF dialog
6. **BOQ export** — BIM tab → `BOQ Export` → should produce an XLSX

### Step 5 — What to log

For each command tested, note:

| Column | What to record |
|---|---|
| Command tag | e.g. `TagAndCombine` |
| Result | ✅ Works / ⚠️ Works with caveat / ❌ Crashes |
| Error message | (if any) — from TaskDialog or journal |
| Revit version | 2025 / 2026 / 2027 |
| Notes | Anything unexpected |

---

## Tier 4 — Planscape Server (Docker)

```bash
cd Planscape.Server/docker
docker compose up -d
# API:     http://localhost:5000
# Swagger: http://localhost:5000/swagger
# Login:   admin@planscape.demo / admin123
```

Run server tests:
```bash
dotnet test Planscape.Server/tests/Planscape.Tests/
```

---

## Common Issues

### "Failed to initialize STING: FileNotFoundException — Newtonsoft.Json"

The dependency DLLs are missing from `CompiledPlugin\`. Re-run `build.bat` —
`extract_plugin.sh` copies all DLLs from the Release build output.

### "Dockable pane already registered" crash on Revit load

You have **two copies of `StingTools.addin`** — one in `%AppData%` and one in
`%ProgramData%`. Delete the one in `%ProgramData%`.

### "Parameter binding failed — 0 of N parameters loaded"

The shared parameter file `MR_PARAMETERS.txt` wasn't found. Check that the
`data\` folder is next to `StingTools.dll` in `CompiledPlugin\` and contains
`MR_PARAMETERS.txt` (294 KB). If it's empty, re-run `build.bat`.

### BOQ / Drawing Template commands crash

These need a project open (not a family document or a blank project). Open
the Autodesk sample `.rvt` first.

---

## What's NOT Yet Automated (honest list)

These exist only as manual smoke tests today:

| Feature | Test coverage | Path to automation |
|---|---|---|
| Tag commands (AutoTag, Combine) | Manual checklist | In-Revit test harness (see below) |
| Placement engine (PlaceFixtures) | Manual checklist | Journal file replay |
| Panel schedule generation | Manual | Journal file |
| Drawing Template Manager | Manual | Journal file |
| Healthcare validators | Manual | Journal file |
| Plumbing sizing engines | Manual | Unit test (pure logic exists, needs linking) |
| Electrical calculations | Manual | Unit test (pure logic — cable sizing, fault current) |

### Path to in-Revit automation

Revit supports **journal file replay** — a text record of every API call Revit
makes. Record a session, replay it as a regression test. This is the standard
approach for Revit add-in integration testing.

Tools:
- **Revit.TestRunner** (open source, GitHub: `geberit/Revit.TestRunner`) —
  runs xUnit tests inside the Revit process. This is the recommended path
  for growing the automated test suite.
- **Journal files** — simpler but fragile (sensitive to UI state).

---

## First Week Testing Plan

| Day | Task |
|---|---|
| **1** | Clone repo, install .NET 8, run `build.bat`, confirm plugin loads |
| **1** | Run `dotnet test` on all 3 pure-logic test projects |
| **2** | Open Autodesk sample project, run smoke test steps 1–7 (core tagging) |
| **3** | Run smoke test steps 8–14 (routing, fabrication, BOQ) |
| **4** | Run smoke test steps 15–20 (sheets, drawing types, panel schedules) |
| **5** | Log all failures, open issues, prioritise fixes |

---

## Key Files Reference

| File | Purpose |
|---|---|
| `Tests/v4_smoke_test.md` | 20-step manual smoke test |
| `build.bat` | One-command build + deploy |
| `extract_plugin.sh` | Stamps `.addin` with real path, copies to Revit |
| `StingTools.Clash.Tests/` | 9 pure-logic clash tests |
| `StingTools.Routing.Tests/` | 4 pure-logic routing tests |
| `Planscape.Server/docker/docker-compose.yml` | Server stack |
| `.github/workflows/stingtools-plugin.yml` | CI pipeline |
