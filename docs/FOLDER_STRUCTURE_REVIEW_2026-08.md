# Folder Structure Review — August 2026 (re-measure)

**Date**: 2026-08-05 · **Branch**: `claude/review-folder-structure-audit` · **Type**: review & advice (no code changes)

This re-measures [`ISO19650_DOC_FOLDER_REVIEW.md`](ISO19650_DOC_FOLDER_REVIEW.md) (2026-07-19), which
asked *"why does folder creation produce many disorganised folders, some repeated inside others?"*
Roughly half that review's findings have since been fixed. This one records what was fixed, what was
not, and the two mechanisms that still produce the sprawl the user sees.

**Verdict in one line:** the *design* is now correct and singular — `ProjectFolderEngine` owns the
tree, `StingPaths` is the one legal resolver, a CI gate defends it. The *adoption* never happened:
**124 live call sites still write `_BIM_COORD` as a sibling of the `.rvt`**, outside the root the
consolidation created, and the gate baselines them instead of failing them. Meanwhile the plugin
**eagerly creates ~53–60 empty directories on every document open**. Sprawl is now a migration
problem, not an architecture problem.

---

## Part 1 — What was fixed since July

Verified on this branch. These are real, and they closed the most dangerous items:

| Prior finding | Status now | Evidence |
|---|---|---|
| `MigrateFromLegacy` ran unprompted on every DocumentOpened, moving user files with no record | **Fixed** | Requires `consented: true`; refuses a second run via a breadcrumb; retires folders as `*.migrated_yyyyMMdd` rather than deleting; logs every move ([`ProjectFolderEngine.cs:912`](../StingTools/Core/ProjectFolderEngine.cs)) |
| `_rootPath` was a single static shared across documents (cross-project contamination) | **Fixed** | Per-document `_rootByDoc` / `_setupCache` / `_folderStatsByDoc`, all keyed on `.rvt` path |
| Two builders of the "same" numbered tree disagreed (7 vs 5 disciplines, 14 vs 4 issue subs) | **Fixed** | `CreateFolderStructure` now delegates to the same `ProjectSetup`; the divergent list was deleted ([`ProjectSetup.cs:88`](../StingTools/Core/ProjectSetup.cs)) |
| `AUTO_CREATE_CDE_FOLDERS` was dead config governing nothing | **Fixed** (see §2.1 — arguably over-corrected) | Now read and honoured at DocumentOpened ([`StingToolsApp.cs:1321`](../StingTools/Core/StingToolsApp.cs)) |
| Sibling `_CDE/` tree writers | **Gone** | 0 remaining `Path.Combine(…, "_CDE", …)` sites |
| Project-number rename forked a whole new root tree | **Fixed** | `StingProjectRootSchema` stamps the resolved root onto ProjectInformation and resolves it first |
| No enforcement of path discipline | **Added** | [`tools/check_path_discipline.ps1`](../tools/check_path_discipline.ps1), wired into CI |

Credit where due: the hard correctness bugs are closed. What remains is tidiness and follow-through.

---

## Part 2 — What still produces the sprawl

### 2.1 Mechanism A — eager creation of ~53–60 empty folders on document open

`AutoCreateCdeFolders` defaults to **true** ([`TagConfig.cs:241`](../StingTools/Core/TagConfig.cs)), so
opening *any* saved project runs `CreateFolderStructure`, which materialises the entire tree whether or
not the user ever exports anything:

| Mode | Top-level | Sub-folders | Total dirs created |
|---|---|---|---|
| **CdeFirst** (default for greenfield — `CdeFirstLayout = true`) | 12 | 21 content-type (3 states × 7) + 14 issue types + 3 clash | **~53** |
| **BIM** (existing projects) | 20 | 20 discipline (4 folders × 5) + 14 issue types + 3 clash | **~60** |

This is the single most visible driver of "many disorganised folders". A user who opens a model to
check one view gets 50+ empty directories next to it. The tree is also *entirely* empty on day one —
`11_ISSUES/CVI/`, `11_ISSUES/PMI/`, `12_CLASHES/Snapshots/` exist before the project has an issue.

The intent was defensive ("so exports never race a missing directory"), but every resolver on the
write path (`GetFolderPath`, `GetMetaPath`, `StingPaths.Cde`, `GetDataPath`) already calls
`Directory.CreateDirectory` itself. The eager pass is redundant with the lazy one.

### 2.2 Mechanism B — 124 writers still write outside the root

This is the one that makes consolidation *un-stick*.

```
Consolidated (intended):   <rvtDir>/<CODE>/_data/_BIM_COORD/…
Still written by 124 sites: <rvtDir>/_BIM_COORD/…          ← sibling of the .rvt
```

Measured on this branch:

| Metric | Count |
|---|---|
| `Path.Combine(…, "_BIM_COORD", …)` lines total | 140 |
| …resolving through `GetMetaPath` / `GetDataPath` / `GetRootPath` / `StingPaths` (correct) | 15 |
| …built on a **raw directory base** — writes the sibling | **124** |
| Files that both derive the model dir *and* create directories | 45 |
| Files still referencing `Path.GetDirectoryName(doc.PathName)` | 109 |

The base expressions confirm these are model-directory variables, not resolver results:
`dir` (45), `parent` (28), `root` (11), `baseDir` (11), explicit `Path.GetDirectoryName(doc.PathName)` (8+),
`modelDir`, `projectFolder`, `projPath`.

Concrete, verified examples:

- [`Core/Electrical/WireProfile.cs:140-150`](../StingTools/Core/Electrical/WireProfile.cs) — `Path.Combine(<modelDir>, "_BIM_COORD", "wire_profiles.json")`, then `CreateDirectory` on its parent at line 223.
- [`Core/Placement/SeedEnsurer.cs:215-219`](../StingTools/Core/Placement/SeedEnsurer.cs) — `Path.Combine(<modelDir>, "_BIM_COORD", "Families", "Seeds")`.
- [`Commands/Symbols/SymbolLibraryCommands.cs:92-100`](../StingTools/Commands/Symbols/SymbolLibraryCommands.cs) — `Path.Combine(<modelDir>, "_BIM_COORD", "Families", "Symbols")`.

**Why this is worse than untidy.** `MigrateFromLegacy` drains `<rvtDir>/_BIM_COORD` into
`<CODE>/_data/_BIM_COORD` and retires the source. The next command that runs re-creates the sibling
and writes into it. The user consolidates, and the folder comes back. Reads are partially masked by
`ResolveProjectOverridePath`, which falls back to the legacy sibling — so the store silently *forks*:
new writes land in one place, the consolidated copy goes stale, and neither is obviously wrong.

### 2.3 Mechanism C — the gate ratchets the problem instead of closing it

[`check_path_discipline.ps1`](../tools/check_path_discipline.ps1) is a genuinely good gate — its own
header documents the three holes it closed in its predecessor. But its two tiers are calibrated
against the wrong risk:

- **Tier 1** (hard zero) bans `STING_BIM_MANAGER` / `_bim_manager` literals. Currently **0** — clean.
- **Tier 2** (ratcheted against a 123-line baseline) covers `_BIM_COORD`, described as
  *"layout-coupled but mostly land in the right place today"*.

That description is not accurate for 124 of the 140 sites: a raw-dir base does **not** land in the
right place, it lands in the pre-consolidation sibling — precisely the failure Tier 1 exists to
prevent. The gate reports **"Path-discipline OK"** today with the whole fork live.

The fix is cheap and needs no refactor: **Tier 2 already knows how to tell the two apart.** The script
computes `$resolverBase` (`GetMetaPath|GetDataPath|GetProjectDataDir|GetRootPath`) and applies it to
Tier 1 only. Applying the same discriminator to Tier 2 splits 140 into 15 acceptable and 124
baselined-but-wrong, and makes the baseline a countdown that means something.

### 2.4 Mechanism D — `_data/` reproduces the sprawl one level down

The consolidation moved the legacy sibling folders *inside* `_data/` but kept their legacy names as
separate buckets. Call-site inventory:

| Bucket under `<CODE>/_data/` | Call sites |
|---|---|
| `_BIM_COORD` | 53 |
| `STING_BIM_MANAGER` | 26 |
| `.bimmanager` | 1 |
| `staging` | 1 |
| `recycle` | 1 |

`_bim_manager` is additionally created by the migration path. So a consolidated project holds **three
or four sibling directories under `_data/` that mean the same thing** — "coordination JSON" — split by
which subsystem historically owned the file, which is not a distinction any user can see.
[`CoordStores.cs:44-46`](../StingTools/Core/CoordStores.cs) makes the split explicit and deliberate:

```csharp
private const string CoordBucket    = "STING_BIM_MANAGER";  // issues, meetings, register, revisions
private const string TemplateBucket = "_BIM_COORD";         // transmittals, deliverables, workflow
```

This is the literal answer to *"duplicate folders inside folders"*. It was a defensible call at the
time — pointing `Transmittals` elsewhere would have re-forked the store WP2 had just unified — but it
should be a migration waypoint, not the destination. `STING_BIM_MANAGER` carries 26 call sites and
exactly one named subfolder (`qr`); everything else is loose JSON alongside loose JSON in the bucket
next door.

### 2.5 Smaller residue (each verified present)

| Item | Sites | Note |
|---|---|---|
| `STING_Exports` writers outside the resolver | 4 | Legacy export dump the consolidation was meant to retire |
| `_acc_mirror_tmp` | 3 | Transient staging created beside published files; `StingPaths.Staging` exists for exactly this |
| `_DATA/sharepoint_queue` | 1 | Case-inconsistent with `_data` — on a case-sensitive share this is a *second* directory |
| `_RECYCLE` | 6 | `StingPaths.Recycle` exists; these bypass it |
| `WithCodeSuffix` on every folder | — | `01_WIP` → `01_WIP_FIRESTONE`. Intent (identify a folder when zipped out) is sound, but it doubles every name's length in the very tree meant to look tidy |

---

## Part 3 — Documentation findings

1. **The on-disk output contract is undocumented in `CLAUDE.md`.** `ProjectFolderEngine` gets one
   truncated line in the directory tree; `StingPaths` — declared in its own header as *"THE single
   legal entry point for every StingTools project path"* — is not mentioned at all. A contributor
   reading `CLAUDE.md` has no way to learn the rule they are about to break. This is the direct cause
   of Mechanism B: 124 sites were written by people who did not know the resolver existed.

2. **Stale counts in the layout source itself.** `ProjectSetup.cs:71` says *"BIM folder defaults (16
   numbered folders)"*; there are **20**. `ProjectFolderEngine.cs:19` documents the root as
   `{ProjectDir}/STING_Project/`; it is `{ProjectDir}/<CODE>/`.

3. **`docs/INDEX.md` is still absent** — 132 files in `docs/` + 23 at root, no table of contents. This
   was P0 recommendation #3 in `CLAUDE.md`'s own review section and is still open. Three overlapping
   ISO-19650 folder documents already exist (`ISO19650_DOC_FOLDER_REVIEW.md`,
   `ISO19650_INREVIT_VERIFICATION.md`, `AGENT_FIX_PROMPT_ISO19650_CONSOLIDATION.md`) with no marker
   for which is current — this file makes four.

4. **Repo layout**: 40 top-level directories. Defensible for a multi-product workspace (plugin +
   server + web + mobile + 9 test projects), and not the sprawl the user is seeing. No action urged.

---

## Part 4 — Advice, in priority order

The architecture is right. Do not redesign it. Finish the migration and stop the bleeding.

### P0 — stop the fork from re-opening (small, high leverage)

1. **Tighten the gate to discriminate by base, not by bucket name.** Apply the existing
   `$resolverBase` test to Tier 2. Re-baseline: 124 wrong / 15 fine. No code moves; the gate simply
   starts telling the truth, and the baseline becomes a burn-down.
2. **Default `AutoCreateCdeFolders` to `false`.** Every write path already creates its own directory.
   Keep the flag for teams who want the tree pre-seeded; stop imposing 53–60 empty folders on a user
   who opened a model to look at it. This is the change with the largest visible effect for the
   smallest diff.

### P1 — burn down the 124 (mechanical, batchable)

3. Replace `Path.Combine(<modelDir>, "_BIM_COORD", …)` with `StingPaths.Meta(doc, "_BIM_COORD", …)`.
   Nearly all are one-line substitutions; the bases are already local variables. Batch by directory
   (`BOQ/` is ~15, `Commands/Symbols/` ~4, `Core/Placement/` ~6) and lower the baseline in the same PR
   — the gate is built for exactly this.
4. Route the residue in §2.5 through the resolvers that already exist: `StingPaths.Staging` for
   `_acc_mirror_tmp` and `sharepoint_queue`, `StingPaths.Recycle` for `_RECYCLE`.

### P2 — collapse `_data/` to one coordination bucket

5. Merge `_BIM_COORD` / `STING_BIM_MANAGER` / `_bim_manager` / `.bimmanager` into a single
   `_data/coord/`. Do this **only after** P1 — merging while 124 writers still target the sibling
   would fork the store again. `CoordStores` is the right place to land it: it already owns the
   read-side legacy merge, so the migration is one more alias in a class built to hold them.
6. Reconsider `WithCodeSuffix`. If the goal is identifying a folder extracted from the root, a
   `FOLDER_INDEX.txt` at the root (which `WriteFolderIndex` already produces) does that without
   putting the project code in all 60 names.

### P3 — documentation

7. Add a **Project Output Folder Layout** section to `CLAUDE.md`: the resolver rule
   (`StingPaths` or `ProjectFolderEngine`, never hand-built), the tree for each of the three modes,
   and a pointer to the gate. One screen; it is what prevents Mechanism B recurring.
8. Fix the two stale comments in §3.2. Add `docs/INDEX.md` and mark the three superseded ISO-19650
   folder documents as historical, pointing at whichever is current.

---

## Appendix — reproducing every number above

```bash
cd StingTools

# eager-creation defaults
grep -n 'AutoCreateCdeFolders\|CdeFirstLayout' Core/TagConfig.cs | grep '= '

# _BIM_COORD: total / correct / sibling-writing
grep -rhE 'Path\.Combine\([^;]*"_BIM_COORD"' --include=*.cs . | wc -l
grep -rhE 'Path\.Combine\([^;]*"_BIM_COORD"' --include=*.cs . | grep -cE 'GetMetaPath|GetDataPath|GetRootPath|StingPaths\.'
grep -rnE 'Path\.Combine\([^;]*"_BIM_COORD"' --include=*.cs . | grep -vE 'GetMetaPath|GetDataPath|GetRootPath|StingPaths\.' | wc -l

# _data bucket inventory
grep -rhoE '(StingPaths\.Meta|GetMetaPath)\s*\(\s*doc[^,]*,\s*"[^"]+"' --include=*.cs . \
  | grep -oE '"[^"]+"$' | sort | uniq -c | sort -rn

# resolver adoption
for p in 'StingPaths\.' 'ProjectFolderEngine\.' 'OutputLocationHelper\.' 'CoordStores\.'; do
  printf "%-24s %s files\n" "$p" "$(grep -rlE "$p" --include=*.cs . | wc -l)"; done

# the gate's current verdict
cd .. && powershell -NoProfile -File tools/check_path_discipline.ps1
```
