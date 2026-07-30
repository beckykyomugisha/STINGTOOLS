# W-1 / W-2 / W-3 — Revit test checklist

**Merged**: PR #498 → `main` @ `c7b10d2c7`. Test from `main`, not a branch.
**Purpose**: verify in Revit what unit tests structurally cannot reach — where files are written,
when they are regenerated, and whether the P0 geometry repairs are actually visible.

Everything below is unverified in Revit. Logic-level checks pass (7/7 W-1, 4/4 W-3), but those
prove the manifest *reports* correctly, not that the builder *acts* on it.

**Log for every step**: `C:\Dev\STINGTOOLS\CompiledPlugin\StingTools.log`
**Shared library**: `C:\ProgramData\STING\ContentLibrary\Symbols`
**Buttons**: dock panel → **SETUP** tab → Symbols section

---

## Step 0 — Deploy

Close Revit first; the DLL is locked while it runs.

```bash
cd "C:/Dev/STINGTOOLS" && git checkout main && git pull && dotnet build StingTools/StingTools.csproj -c Release -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025" -t:Rebuild
```

```bash
cp -r "C:/Dev/STINGTOOLS/StingTools/bin/Release/." "C:/Dev/STINGTOOLS/CompiledPlugin/"
```

✅ Build reports `0 Warning(s), 0 Error(s)` and `CompiledPlugin\StingTools.dll` timestamp is now.
❌ If Revit was open the copy fails silently on the DLL — close it and repeat.

> All three Revit versions point at `CompiledPlugin`, so this deploys to 2025/2026/2027 at once.

---

## Step 1 — W-3, fresh project → shared library

A project with **no** `_BIM_COORD\Families\Symbols` folder.

1. Delete `C:\ProgramData\STING\ContentLibrary` if present, so the run is genuinely cold.
2. Open/save a test project that has never built symbols.
3. SETUP → Symbols → **SLD** (one catalogue — faster than Create All for a first pass).

| | Expect |
|---|---|
| ✅ | `C:\ProgramData\STING\ContentLibrary\Symbols\SLD\IEC\` contains `.rfa` files |
| ✅ | **`...\Symbols\SLD\IEC\.sting_library.json`** exists — the sidecar sits in the *catalogue's own sub-folder*, not at the `Symbols` root, because `RunBatch` builds each catalogue into `<root>\<subFolder>` |
| ✅ | Sidecar shows `"generatorVersion": "2"`, a `catalogues` entry for `STING_SLD_SYMBOLS.json` with a 64-char hash, and an empty `failedSymbols` |
| ❌ | Families landed in the project's `_BIM_COORD` instead → shared root was rejected; check the log for `not writable` |
| ❌ | Families landed in `%TEMP%\STING_Symbols` → both roots failed |

**This is the step most likely to fail on a locked-down machine.** `%PROGRAMDATA%` is writable here,
but the write-probe fallback exists precisely because that varies.

---

## Step 2 — W-1, cache is honoured when fresh

Immediately after Step 1, click **SLD** again.

| | Expect |
|---|---|
| ✅ | Result reports families as existing, **not** rebuilt (`Created` 0 / `Existed` > 0) |
| ✅ | No `rebuilding cached families` line in the log |
| ✅ | `.rfa` timestamps unchanged |
| ❌ | Rebuilds every time → the sidecar isn't being read or saved; check for a `SymbolCacheManifest.Save` warning |

---

## Step 3 — W-1, catalogue edit invalidates

1. Note the modified time of `...\Symbols\SLD\IEC\SLD_MCB.rfa`.
2. Edit `CompiledPlugin\data\Symbols\STING_SLD_SYMBOLS.json` — change any `symbolSize` (e.g. `3.0` → `3.1`). Save.
3. Click **SLD**.

| | Expect |
|---|---|
| ✅ | Log: `rebuilding 'STING_SLD_SYMBOLS.json' — catalogue content changed` |
| ✅ | `SLD_MCB.rfa` timestamp updated |
| ✅ | Sidecar hash for that catalogue changed |
| ✅ | **Other** catalogues untouched — `Lighting\*.rfa` timestamps unchanged |
| ❌ | Everything rebuilt → hashing is per-library not per-catalogue |

> Isolation here is structural as well as hash-based: each catalogue builds into its own sub-folder
> with its own sidecar, so cross-contamination would require a path bug rather than a hashing bug.

Revert the edit afterwards, or leave it — Step 4 rebuilds regardless.

---

## Step 4 — W-2, `Symbols_Rebuild`

SETUP → Symbols → **Rebuild**. Dialog offers *Stale only* / *Force all*.

**4a — Stale only**, with everything already current:

| | Expect |
|---|---|
| ✅ | Report: `Every catalogue was already current — nothing to rebuild.` |

**4b — Force all**:

| | Expect |
|---|---|
| ✅ | Report header `Rebuild mode: FORCE ALL`, `Rebuilt` ≈ every symbol in the library |
| ✅ | Log shows `forced rebuild` as the cause |
| ✅ | All `.rfa` timestamps updated |
| ❌ | Cancel produces no dialog / no action → dispatch tag not wired |

---

## Step 5 — W-3, existing project library is NOT orphaned

**The guard I could not test headlessly** (`ExistingProjectLibrary` takes a `Document`). It protects
every library built before this change, so it matters most.

1. Open a project that **already has** a populated symbol library. Either layout counts, and both
   are probed (consolidated first): `<root>\_data\_BIM_COORD\Families\Symbols\` or the legacy
   sibling `<project>\_BIM_COORD\Families\Symbols\`.
   If none exists, make one: temporarily rename `C:\ProgramData\STING\ContentLibrary`, build SLD
   into the project, then restore the name.
2. Click **SLD**.

| | Expect |
|---|---|
| ✅ | Log: `ResolveOutputRoot: using the project's existing symbol library at '<resolved path>'` — and the path it names is the layout the project actually uses |
| ✅ | Families rebuild **in the project folder**, not in ProgramData |
| ✅ | Because generator version moved 1 → 2, they genuinely rebuild rather than skip |
| ❌ | Build goes to ProgramData while the project copies stay stale → the guard failed, and stale families will keep winning the read path. **Stop and report — this is the regression that matters.** |

---

## Step 6 — P0 geometry is actually visible

The counts said 206 filled regions and 115 arcs were repaired. Confirm on real geometry rather than
trusting the numbers.

Place these on a drafting view (SETUP → Symbols → **Place View**, or load the `.rfa` directly):

| Symbol | Was | Should now be |
|---|---|---|
| `BS_TX_2W` (SLD/BS) | two **full circles** | two **semicircles** — spec declares 2 arcs, both 0→180° |
| `IEEE_TX_2W` (SLD/IEEE) | two full circles | two semicircles — same 2 × 0→180° |
| `ELEC_C_POL` (Electrical) | full circle | one 90→270° arc — polarised capacitor |
| `EARTH_LPS_TERMINAL` (Earth) | **no solid fill** | 1 solid filled region |
| `DRN_GREASE_TRAP` (DrainAbove) | no solid fill | 1 solid filled region |

All five are GenericAnnotation, so they carry no `realSizeMm` — that is correct, not a defect.

❌ Full circles or missing fills means the alias fix didn't take — check the deployed DLL is current.

---

## Step 7 — Nothing else regressed

| | Expect |
|---|---|
| ✅ | SETUP → Symbols → **Validate** reports 880 symbols, 0 empty-geometry, 0 extent violations |
| ✅ | 142 param-less annotations still reported — expected, that's P3 scope |
| ✅ | **Reload** still works and is distinct from Rebuild (loads, never regenerates) |
| ✅ | Place `ELEC_SOCKET_SINGLE` — **85 mm**, not 4 mm (P1) |
| ✅ | `ELEC_SOCKET_DOUBLE` **150 mm** · `ELEC_SWITCH_2G` **150 mm** · `HVAC_SAD_SQ` **595 mm** · `FP_MCP` **80 mm** |

> These are the values after realignment to each symbol's authored `solid3D` footprint, which
> superseded the standards-book estimates first committed. 85/150/595 are the numbers on `main`.

---

## Reporting back

For each failing step: the step number, the log lines around it, and where files actually landed vs
expected.

This is already on `main`, so a failure is a fix-forward, not a merge veto. Severity order:

- **Step 5** — highest. If the guard fails, existing project libraries are orphaned and stale
  families keep winning the read path. It is also the newest code in the set: CI's path-discipline
  gate forced it to be rewritten to resolve through `StingPaths.Meta`, so it has had the least
  exposure of anything here.
- **Steps 1 and 6** — a wrong output root, or geometry that did not actually repair, undermine the
  whole point of the change.
- **Step 3** — isolation failing means mass regeneration: slow, not wrong.

If all seven pass, W-1/W-2/W-3 are verified and W-4 (converging the four resolvers) becomes safe to
start — it changes three live lookup paths at once, so it needs this baseline proven first.
