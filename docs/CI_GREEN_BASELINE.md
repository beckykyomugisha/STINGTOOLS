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

## FIX 1 — Build StingTools Plugin

**Exact command:** build `tools/revit-stubs/RevitAPI` + `RevitAPIUI` (Release), then
`dotnet build StingTools/StingTools.csproj -c Release -p:RevitApiStubsDir=…\tools\revit-stubs\bin`.

**Root cause.** CI compiles the plugin against type-only Revit API stubs (the real
Revit DLLs aren't redistributable). The stub projects didn't compile — 10 errors
in `RevitAPI` + WPF-reference errors in `RevitAPIUI` — so the plugin build never
started.

**Fix (stub surface only — never executed at runtime; no plugin logic touched).**
- `Stubs_DB_Geometry.cs` — removed an empty `public static class Geometry {}` that
  collided with the `Autodesk.Revit.DB.Geometry` sub-namespace (CS0101).
- `Stubs_DB_Enums.cs` — removed a duplicate `BuiltInCategory.OST_Rooms` (CS0102).
- `Stubs_DB_Elements.cs` — removed a duplicate `FamilyInstance.FacingOrientation`
  (kept the real `XYZ`-returning one, CS0102); added `using
  Autodesk.Revit.DB.Electrical/.Structure` so `ElectricalSystem`/`StructuralType`
  resolve (CS0246); added the missing `MEPModel` type (`ConnectorManager` +
  `GetElectricalSystems()`); dropped a bogus `Geometry.` prefix on `Solid`.
- `Stubs_DB_Core.cs` — dropped a bogus `Geometry.` prefix on `GeometryElement`
  (both types live in `Autodesk.Revit.DB`). *(These two `Geometry.`-prefix errors
  were latent — masked by the CS0101 until it was fixed.)*
- `Stubs_ApplicationServices.cs` — `OpenSharedParameterFile()` now returns
  `DefinitionFile` (there is no `SharedParameterFile` type, CS0246); removed
  `ControlledApplication` ribbon methods that referenced `Autodesk.Revit.UI`
  (wrong assembly + not on the real `ControlledApplication`, CS0234).
- `Stubs_DB_Document.cs` — removed a malformed member that used
  `IEnumerable.GetEnumerator` as a return *type* (CS0426).
- `RevitAPIUI.csproj` — enabled `<UseWPF>true</UseWPF>` so the UI stubs' WPF types
  (`System.Windows.Media`, `FrameworkElement`, `BitmapImage`) resolve. The job
  runs on `windows-latest` where the WPF refs exist.

**Local proof:** both stubs and the plugin build with **0 errors**.

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

## Verification matrix

| Job command | Result |
|-------------|--------|
| RevitAPI + RevitAPIUI stub build (Release) | 0 errors |
| `dotnet build StingTools/StingTools.csproj -c Release -p:RevitApiStubsDir=…` | 0 errors |
| Validate data files — JSON / duplicate-GUID / CSV-structure | all pass |
| `Planscape$ npm ci --no-audit --no-fund` | exit 0 (1081 pkgs) |
| `Planscape$ npx tsc --noEmit` / `npm run typecheck` | exit 0 |
| `Planscape$ npm run lint` | exit 0 (0 errors, 102 warnings) |
| `dotnet build Planscape.Server` (sanity) | 0 errors |
