# Symbol Library — Build Once, Reuse Everywhere (scope)

**Branch**: `claude/iso-symbols-p0p1`
**Date**: 2026-07-29
**Status**: Scope. No implementation.
**Trigger**: [PR #498](https://github.com/beckykyomugisha/STINGTOOLS/pull/498) changes the built output
of ~360 symbols, and **no existing project will pick the fixes up** — see R-1.

---

## 1. The finding that reframes this

This is **not** a "build a caching system" job. A three-tier content system already exists and is
largely unused:

| File | Lines | What it does | Adoption |
|---|---:|---|---|
| `Core/Content/ContentRoots.cs` | 118 | project / shared / baseline root precedence, driven by `ContentManifest.RootPrecedence`; already honours legacy `STING_SYMBOL_LIB` | 1 consumer |
| `Core/Content/ContentResolver.cs` | 263 | `ContentRequest` → `ContentResolution` with miss tracking | **1 consumer** — `Core/Cad/Mep/MepFixtureBuilder.cs:79` |
| `Core/Content/ContentManifest.cs` | 426 | `libraryVersion`, `rootPrecedence`, coverage, corporate + project override layering | read by `ContentCoverageCommand` (display only) |

Meanwhile the paths that actually run symbol lookup each roll their own:

| Path | Resolution logic |
|---|---|
| `SymbolBatchHelper.ResolveOutputRoot` (`SymbolLibraryCommands.cs:55`) | shared root → else `<project>/_BIM_COORD/Families/Symbols` → else `%TEMP%/STING_Symbols` |
| `EquipmentSymbolCommands.ResolveFamilySymbol:132` | loaded → `_BIM_COORD/Families/Symbols/<sub>` → `Families/<disc>/` |
| `MepSymbolEngine.ResolveFamilySymbol:681` | loaded → `Families/{MEP,SLD,ISO6412}` → `_BIM_COORD/...` → recursive scan |
| `MepSymbolEngine.ResolveSharedLibraryRoot:658` | `STING_SYMBOL_LIB` → `%APPDATA%/STING/sting_symbols.json` → **null** |

**Four parallel implementations of the same idea.** The work is convergence plus one genuinely new
capability (version invalidation), not greenfield.

---

## 2. Defects to close

### R-1 — Cache never invalidates (urgent, blocks PR #498 reaching users)

`SymbolLibraryCreator.cs:229`:

```csharp
var rfaPath = Path.Combine(outputFolder, def.Id + ".rfa");
if (File.Exists(rfaPath)) { result.Existed++; continue; }   // ← only staleness test
```

Existence is the entire test. There is no version or content check, so:

- PR #498 repairs 206 filled regions, 115 arc sweeps and 154 device sizes. **Any project that has
  already generated symbols keeps the broken geometry permanently.**
- The same is true of every future catalogue edit. Shipping symbol fixes is unreliable *by
  construction*.
- `libraryVersion` (`STING_CONTENT_MANIFEST.json`, currently `2026.6.1`) exists but is only ever
  *displayed* by `ContentCoverageCommand` — never compared against anything.

There is a second copy of the same skip at `SymbolLibraryCreator.cs:303` (the variant/emit path).
Both need the guard or the fix is half-applied.

`Symbols_Reload` does **not** cover this — it re-loads existing `.rfa` into the document and
flushes the JSON shapes cache. It never rebuilds a stale family.

### R-2 — Shared root is opt-in and undiscoverable

Both `ResolveSharedLibraryRoot` and `ContentRoots.ResolveSharedRoot` return **null** when
unconfigured, so the default experience is a full regenerate of 880 families **per project**.
"Build once" is currently "build once per project", and only for users who happen to know the env
var exists.

### R-3 — Nothing is prebuilt

`Families/` contains **zero `.rfa`**. Every install starts cold. This is also the delivery mechanism
P2 needs for the label seeds, so the two converge.

---

## 3. Work items

### W-1 — Version-stamped cache invalidation  ▸ ~1 day  ▸ **do first**

Write a `.sting_library.json` sidecar into the output folder:

```jsonc
{
  "libraryVersion": "2026.6.1",
  "builtUtc": "2026-07-29T18:00:00Z",
  "catalogues": { "STING_SLD_SYMBOLS.json": "<sha256>", "…": "…" }
}
```

At build, hash each catalogue and compare. Rebuild only what changed; leave the rest as `Existed`.
Both skip sites (`:229`, `:303`) route through one `IsCacheStale(def, outputFolder)` helper.

Reuse the SHA-256 pattern already proven in `DrawingTypeRegistry.ComputeChecksums:476` rather than
inventing a second hashing convention.

**Exit**: edit one catalogue, rebuild, and only that catalogue's families are regenerated.

### W-2 — Force-rebuild command  ▸ ~2 hours

`Symbols_RebuildStale` (rebuild what the sidecar says changed) and a `--force` variant (wipe and
rebuild all). Needed as the escape hatch for anyone already holding stale families today, including
everyone affected by PR #498. Registers in `StingCommandHandler` alongside the existing
`Symbols_*` tags.

**Exit**: a user with a stale library gets current geometry in one click.

### W-3 — Default the shared root  ▸ ~half day

`ContentRoots.ResolveSharedRoot` gains a final fallback of `%PROGRAMDATA%\STING\ContentLibrary`
before returning null. Shared becomes the default; per-project becomes the fallback it was designed
to be.

Watch the interaction with `RootPrecedence`: the manifest defaults to `projectFirst` specifically so
a frozen project ignores firm-level changes. Defaulting the shared *root* must not silently flip
that *precedence* — a delivered project must stay reproducible.

**Exit**: two projects on one machine build the library once between them.

### W-4 — Converge the four resolvers onto `ContentRoots`  ▸ ~2 days

Point `SymbolBatchHelper.ResolveOutputRoot`, `EquipmentSymbolCommands.ResolveFamilySymbol` and
`MepSymbolEngine.ResolveFamilySymbol` at `ContentRoots` / `ContentResolver`. Keep
`ResolveSharedLibraryRoot` as a thin deprecated shim so existing `STING_SYMBOL_LIB` setups keep
working.

Highest regression risk in this scope: three live lookup paths change at once, each with its own
fallback ordering that some project on disk depends on. Do it after W-1/W-2 are proven, and keep the
legacy ordering in `ContentRoots` rather than "tidying" it.

**Exit**: one resolution implementation; `ContentResolver` adoption goes from 1 consumer to 4.

### W-5 — Ship prebuilt `.rfa`  ▸ ~2 days + CI

Generate the library at release time, publish as a versioned bundle, extract into
`%PROGRAMDATA%\STING\ContentLibrary` on first run. Users never cold-generate; the sidecar from W-1
makes the shipped bundle self-describing.

Depends on W-1 and W-3. Also the delivery mechanism for the P2 label seeds — worth building once for
both.

**Exit**: fresh install places a symbol without ever running a generator.

---

## 4. Order and dependencies

```
W-1 ──▶ W-2 ──▶ W-4
  └────▶ W-3 ──▶ W-5
```

W-1 first and alone if nothing else happens: without it, every later improvement ships into caches
that ignore it. W-2 immediately after, because it is the remedy for users already stale today.

Rough total ≈ 5–6 days. W-1 + W-2 alone (~1.5 days) close the correctness hole; W-3 → W-5 are the
"build once, reuse everywhere" outcome.

---

## 5. Risks

| Risk | Mitigation |
|---|---|
| W-4 changes three live lookup paths with subtly different fallback orders | Land after W-1/W-2; preserve legacy ordering inside `ContentRoots`; do not "tidy" precedence while converging |
| Defaulting the shared root changes behaviour for existing installs | Default the *root* only; leave `RootPrecedence` at `projectFirst` so delivered projects stay reproducible |
| A shared root on a network path may be read-only or slow | `ResolveOutputRoot` already falls back on write failure (`SymbolLibraryCommands.cs:66-72`); keep that guard |
| Rebuild invalidation could mass-regenerate on an unrelated edit | Hash per catalogue, not per library, so a one-file edit rebuilds one file's symbols |
| None of this is verifiable outside Revit | Same limit as PR #498 — the family build path needs a real model run |

---

## 6. Recommendation

Do **W-1 + W-2 now**, before PR #498 merges or immediately after. Everything else can be scheduled.

Rationale: PR #498 is a correctness fix that, as things stand, cannot reach any project that has
already generated its symbols. Shipping it without invalidation means believing the library is fixed
when it is not — which is worse than the original bug, because the failure is now silent *and*
assumed solved.

The P4 annotation catalogue should not start until W-1 is in. That content will hit exactly the same
wall on its first revision.
