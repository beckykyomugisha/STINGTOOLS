# Branch Consolidation Log

Goal: end with **only `main`** (plus `archive/*` preservation tags), green on
CI. Work is preserved by *tagging* a branch tip, never lost. CI-green on GitHub
Actions is the only acceptance signal (local builds need network/Revit and are
not the gate).

Invariants honoured:
- `archive/<branch>` tag pushed for every branch tip **before** deletion.
- A branch is deleted only when it is an **ancestor of `origin/main`** *or*
  carried by an `archive/*` tag (so its commits stay reachable forever).
- No `git push --force` to `main`. Every promotion was a normal merge gated on CI.
- Conflicts resolved **explicitly** (no `-X ours/theirs`).

---

## Session — 2026-06-04 (CI-green + final consolidation)

Starting point: `main` at `700111780` (#291 merge). A one-line Docker CI fix sat
uncommitted/local-only on `fix/ci-docker-context` after a WiFi drop.

### CI baseline landed

| PR | Branch | Result |
|----|--------|--------|
| #289 | `claude/upbeat-cori-vdOPA` | already MERGED (cross-host + viewer + Connect) |
| #290 | `fix/ci-green-baseline` | already MERGED (green baseline; replaced `tools/revit-stubs/` hand-written stubs with real NuGet ref assemblies — stub project **deleted**) |
| #291 | `fix/ci-docker-buildx` | already MERGED (Buildx for GHA cache export) |
| **#292** | `fix/ci-docker-context` | **MERGED → `eab5b0b06`**. Docker build `context: Planscape.Server` → `context: .` (the Dockerfile COPYs repo-root paths). Root cause proven from the #291 main run log (`"/Planscape.Server/src/...": not found`). The Docker job runs **only on push to `main`** (`if: github.ref==main && event==push`), so it was validated on the post-merge `main` run → **Docker Build & Push green**. |

### Branch sweep (`git branch -r --no-merged origin/main`)

All five candidate tips were archive-tagged before any action.

| Branch | Disposition | Why |
|--------|-------------|-----|
| `claude/awesome-thompson-QwGKw` | **Retired** (deleted, `archive/` tag) | `git cherry` → all 9 commits already patch-present in `main` (absorbed by #289). `meeting-sync.js` in `main` is the *newer* version (BLK-5 model-ready gate); the branch's copy was older. Nothing to add. |
| `claude/implement-alignment-audit` | **Merged** via PR **#293 → `b7a24ddc7`** | 7 genuinely-new commits (server controllers/services/hubs, mobile api/realtime, plugin `PlanscapeServerClient` + commands, `align-audit.mjs` harness, `docs/PLANSCAPE_ALIGNMENT_AUDIT.md`). Two competing hunks resolved to **main's current** versions: `coordination-viewer.js` (main's streaming GLB loader, newer than the branch's format-guard); `PublishModelCommand.cs` (main's "IFC auto-converted server-side" policy, matching the IFC→GLB converter enabled in #290 — the branch's stale glTF-only messaging dropped). Extra commit `dba786664` synced the canonical `assets/viewer/signalr-shim.js` so the byte-equal **viewer-drift gate passes** while keeping the branch's real fix (in-app notification listener `NotificationCreated`→`Notification`, matching `NotificationService.SendAsync`). **All checks green** (plugin build, server build, mobile typecheck, wire-contract, viewer-drift). |
| `feat/viewer-esm-modern` | **Retired** (deleted, `archive/` tag) | After #293 its 7 alignment commits are in `main`. The 6 remaining are CI/sync commits for the **old** build world — incl. `4006799f7` "make Revit API stubs compile" which would **re-add the stub project #290 deleted** (a regression). Its dup-GUID CI fix is already in `main`; its viewer ESM modernization (vendor/three, es-module-shims) landed earlier via #285/#286. `main` is green without it. |
| `claude/fix-ci-stubs-param-validator` (PR **#288**) | **Retired** (PR closed w/ explanation, branch deleted, `archive/` tag) | `966174c6c` (dup-GUID validator: real collisions not the txt/csv mirror) is **already in `main`** via #290's `stingtools-plugin.yml`. `4de65465c` (fix RevitAPI stub compile) is **obsolete** — the stub project was deleted in #290. |
| `claude/festive-davinci-O18JC` | **Merged** via PR **#294 → `3f9957193`** | One additive-only commit: `docs/client-drawings/KAKUBA_RODERIC_SLD_DATA.csv`. Clean merge, no code paths, no workflows triggered. |

### Merged-PR branch cleanup

`claude/magical-mayer-hLnIk`, `claude/upbeat-cori-vdOPA` (#289),
`fix/ci-docker-buildx` (#291), `fix/ci-green-baseline` (#290) were all
**ancestors of `origin/main`** (0 commits ahead) — deleted; archive tags added
for the three that lacked them.

### BLOCKED

**None.**

---

## Final state

- **Only branch:** `main` (`origin/main` = `fork/main`, same repo).
- **Open PRs:** none.
- **`main` HEAD:** `3f9957193` (docs-only festive merge) on top of the last
  code-bearing commit `b7a24ddc7` (#293).
- **CI on `main`:** all five workflows **green** at `b7a24ddc7` —
  Planscape Server (incl. **Docker Build & Push**), StingTools Plugin,
  Planscape Mobile, Wire-Contract Drift, Viewer drift. `3f9957193` is docs-only
  and triggers no build workflow.
- **Archive tags:** 74 `archive/*` tags present (every retired/merged tip
  preserved, including all sweep candidates).
