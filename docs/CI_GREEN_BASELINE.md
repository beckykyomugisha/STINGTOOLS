# CI green baseline

Root cause + fix for the four chronic CI failures that were red on `main`, taken
to green on `fix/ci-green-baseline`. Each was reproduced with the job's EXACT
command locally before and after the fix. No job was disabled and no real data
was deleted to fake green.

A recurring theme: GitHub Actions stops a job at the **first** failing step, so
several jobs had a *second* latent failure that only surfaced once the first was
fixed (the same way fixing the first compile error reveals the next). Those are
called out below.

| # | Job | Workflow | Was failing because | Status |
|---|-----|----------|---------------------|--------|
| 1 | Build StingTools Plugin | `stingtools-plugin.yml` | Revit API stubs didn't compile | ✅ green |
| 2 | Validate data files | `stingtools-plugin.yml` | duplicate-GUID validator bug (+ latent CSV-schema drift) | ✅ green |
| 3 | Type-check + Lint | `planscape-mobile.yml` | `npm ci` lockfile out of sync (+ missing eslint config) | ✅ green |
| 4 | Mobile typecheck (tsc --noEmit) | `contract-drift.yml` | same `npm ci` lockfile | ✅ green |

---

## FIX 1 — Build StingTools Plugin  (real Revit reference assemblies from NuGet)

**Exact command (CI):** `dotnet restore StingTools/StingTools.csproj` then
`dotnet build StingTools/StingTools.csproj -c Release --no-restore` on
`windows-latest`, with **no** `RevitApiPath` set.

**Root cause.** CI compiled the plugin against hand-written, type-only Revit API
stubs in `tools/revit-stubs/`. Even after the stubs themselves were made to
compile, they covered only a fraction of the API — the plugin references ~140 real
types the stubs lacked (`FamilyElementVisibilityType`, `IFamilyLoadOptions`,
`FamilySource`, `ConnectorProfileType`, `ACADVersion`, `AddInId`,
`DocumentSavingAsEventArgs`, `ViewDuplicateOption`, `DWGExportOptions`,
`IFCVersion`, `ImageResolution`, `RoutingPreferenceManager`, …). Hand-stubbing the
whole Revit API is unwinnable, so the **plugin** never compiled in CI even when the
stub projects did.

**Fix — replace stubs with the REAL Revit 2025 reference assemblies from NuGet.**
- `StingTools.csproj` resolves Revit references conditionally via a computed
  `UseRevitNuget` property (true unless a local `$(RevitApiPath)\RevitAPI.dll`
  exists):
  - **Developer machine with Revit installed** → references the real local
    `RevitAPI.dll`/`RevitAPIUI.dll` by `HintPath`, `Private=false`. *Unchanged
    developer behaviour.*
  - **CI / no local Revit** → restores **`Nice3point.Revit.Api.RevitAPI`** +
    **`Nice3point.Revit.Api.RevitAPIUI`**, pinned to **`2025.4.50`**, from
    nuget.org. These ship `ref/net8.0-windows7.0/*.dll` — the **full Revit 2025
    API reference surface**, matching the plugin's `net8.0-windows` TFM.
- Both paths keep Revit **compile-time only** (`HintPath` `Private=false`; NuGet
  `ExcludeAssets=runtime` + `PrivateAssets=all`) so the shipped `StingTools.dll`
  never carries a Revit assembly — at runtime Revit loads its own.
- Workflow (`stingtools-plugin.yml`): removed the "Build Revit API stubs" step and
  the `-p:RevitApiStubsDir=…` params; dropped `tools/revit-stubs/**` from the path
  triggers.
- **Stub project retired:** `tools/revit-stubs/` (13 tracked files incl.
  `RevitApiStubs.sln`) deleted entirely so it can't rot, and removed from the
  csproj/workflow build graph. The repo no longer contains a hand-maintained Revit
  API surface.

**Why a local build is NOT the proof.** The dev machine running this has Revit 2025
installed, so `dotnet build` of the plugin passes locally regardless of this fix
(it had already produced false "0 errors" claims against the incomplete stubs).
The **only** acceptance criterion is the GitHub Actions check.

**CI proof (the one that counts).** On PR #290, head commit `2111f2684`, workflow
run **`26944487480`**:

| Check (job) | Result | Run/Job |
|---|---|---|
| **Build StingTools Plugin** | **pass (3m 29s)** | run `26944487480`, job `79493917221` |
| Validate data files | pass | run `26944487480`, job `79493917226` |
| Viewer JS syntax | pass | run `26944487480`, job `79493917222` |

i.e. the plugin **compiled in CI against the real reference assemblies** — the
first time the "Build StingTools Plugin" check has ever been green.

**Local pre-flight (signal only, not proof):** forced the CI path with
`-p:UseRevitNuget=true` → 0 errors; and the dev path `-p:RevitApiPath="C:\Program
Files\Autodesk\Revit 2025"` → 0 errors (developer path preserved).

> Historical note: the earlier attempt got the `tools/revit-stubs/` projects to
> *compile* (deduping `Geometry`/`OST_Rooms`/`FacingOrientation`, adding `MEPModel`
> + `using`s, `UseWPF` on the UI stub, etc.). That made the stub DLLs build but the
> **plugin** still failed in CI against the incomplete surface — which is why the
> stubs are now retired in favour of real reference assemblies.

---

## FIX 2 — Validate data files

**Exact command:** the three `validate-data` steps in `stingtools-plugin.yml`
(JSON validate / duplicate-GUID / CSV-structure).

**Root cause (validator bugs — no data is wrong).**
- *Duplicate-GUID (the reported failure):* `MR_PARAMETERS.csv` is, by design, a
  1:1 mirror of `MR_PARAMETERS.txt`. The check pooled GUIDs from **both** files
  into one dict, so every one of the 3286 GUIDs looked "duplicated" (once in the
  `.txt`, once in its `.csv` mirror). Diagnosed: **0** within-file duplicates;
  all 3286 are cross-mirror only — a pure false positive that had failed on every
  parameter since the mirror was introduced.
- *CSV-structure (latent — never ran on CI because the GUID step aborted the job
  first):* `REQUIRED_COLS` was stale. It wanted `ScheduleName` but the real column
  is `Schedule_Name`; it wanted `StingCategory`/`COBieType` for `COBIE_TYPE_MAP.csv`
  which has never had them (that file uses the COBie Type-sheet schema
  `TypeCode/TypeName/Category/…`). Several STING CSVs also open with a `#` comment
  banner that was read as the header row.

**Fix.**
- Duplicate-GUID check now scopes detection **per file** (a real within-file
  duplicate still fails) and adds a **mirror-integrity** check that `.txt` and
  `.csv` carry the same GUID set.
- CSV-structure check now skips `#`-comment / blank / BOM lines before reading the
  header, and asserts the columns the files **actually** carry. This is correcting
  a check that asserted a false contract and had never executed — not masking.

**Local proof:** all three steps pass (exit 0); `.txt`/`.csv` mirror in sync at
3286 GUIDs; required columns present in both CSVs.

---

## FIX 3 — Type-check + Lint  &  FIX 4 — Mobile typecheck

Both jobs `cd Planscape` and run `npm ci` first.

**Exact commands:**
- FIX 4 (`contract-drift.yml` → *Mobile typecheck*): `npm ci` → `npm run typecheck`
  (= `tsc --noEmit`).
- FIX 3 (`planscape-mobile.yml` → *Type-check + Lint*): `npm ci --no-audit --no-fund`
  → `npx tsc --noEmit` → `npm run lint --if-present`.

**Root cause.** `npm ci` failed: `package-lock.json` was out of sync with
`package.json` — *Missing: react-native-signature-canvas@4.7.4 from lock file*.
With `npm ci` failing, neither tsc nor lint ever ran, hiding two further problems.

**Fix (FIX 3 lockfile):** `npm install` regenerated `package-lock.json` in sync
(adds the missing resolution). `npm ci` now succeeds.

**Fix (FIX 4 tsc):** once deps install, `tsc --noEmit` passes with **0 errors** —
the mobile typecheck failure was purely the lockfile.

**Fix (lint — the `+ Lint` half, newly reachable once `npm ci` passes):** the repo
had **no eslint config at all**, so `eslint . --ext .ts,.tsx` errored "couldn't
find a configuration file", and files using `// eslint-disable …
react-hooks/exhaustive-deps` directives errored "rule definition not found"
because `eslint-plugin-react-hooks` was never installed. Added:
- `eslint-plugin-react-hooks@^4.6.2` devDependency (the code already relies on its
  directives — a genuine missing dependency).
- `.eslintrc.json` — `eslint:recommended` + `@typescript-eslint/recommended` +
  `react-hooks/recommended`.
- `eslint --fix` applied the only auto-fixable genuine errors: 2× `prefer-const`
  (`let`→`const` on never-reassigned `token`/`queue`).

**Local proof:** `npm ci` exit 0; `tsc --noEmit` exit 0; `npm run typecheck` exit
0; `npm run lint` exit 0 (0 errors, 102 warnings — eslint exits 0 on warnings).

---

## What was downgraded to warning (and why) — the only "suppression"

The mobile app **had never been linted** (no config existed), so turning on the
full recommended rule sets at `error` would have flooded the first run with ~92
pre-existing findings unrelated to the four CI failures. To establish a green
baseline without masking genuine bugs, these **preference / non-bug** rules are
set to **warn** (they still print in CI logs; eslint only fails on errors):

| Rule | Count | Why warn, not error |
|------|-------|---------------------|
| `@typescript-eslint/no-explicit-any` | 79 | `any` is legal TypeScript, not a bug. Tightening is a separate typing pass. |
| `react-hooks/exhaustive-deps` | (warn) | This is React's **own default** severity; prone to false positives. |
| `no-empty` | 4 | Empty blocks (mostly intentional catches in `realtimeClient.ts`); not a correctness failure. |
| `@typescript-eslint/no-var-requires` | 1 | A `require()` for a React-Native asset in `ModelViewer.tsx` — legitimate in RN. |
| `no-inner-declarations` | 2 | Obsolete for ES modules; off. |

Genuine-bug rules remain **error**: `react-hooks/rules-of-hooks` plus everything
in `eslint:recommended` / `@typescript-eslint/recommended` (e.g. `no-dupe-keys`,
`no-unreachable`, `no-redeclare`). `no-unused-vars` is off in eslint and TS
(TypeScript's own `noUnusedLocals`/compiler already covers it; leaving it on would
flood without adding signal).

**Follow-up (not blocking the green baseline):** ratchet `no-explicit-any` and the
other warns up to error file-by-file, and consider adopting `eslint-config-expo`
for full React-Native rule coverage.

## Verification matrix — CI checks on PR #290 (head `2111f2684`)

GitHub Actions is the acceptance criterion (a local build does not count — this
dev machine has Revit 2025 installed). All checks green, no regressions:

| Check | Result |
|-------|--------|
| **Build StingTools Plugin** (real Revit 2025 ref assemblies, NuGet) | ✅ pass (3m 29s) |
| Validate data files | ✅ pass |
| Type-check + Lint | ✅ pass |
| Mobile typecheck (tsc --noEmit) | ✅ pass |
| Server build (DTOs compile) | ✅ pass |
| Client ↔ server wire-contract tests | ✅ pass |
| Viewer JS syntax | ✅ pass |
| Auto-label PR | ✅ pass |
| EAS build | skipped (manual `workflow_dispatch` only — expected) |

Local pre-flight (signal only):

| Command | Result |
|---------|--------|
| `dotnet build StingTools -c Release -p:UseRevitNuget=true` (CI path) | 0 errors |
| `dotnet build StingTools -c Release -p:RevitApiPath="…\Revit 2025"` (dev path) | 0 errors |
| `dotnet build Planscape.Server` (sanity) | 0 errors |
