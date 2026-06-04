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

---

## Session — 2026-06-04 (branch protection: prepared, blocked on admin)

Goal: make `main`'s rules **enforced** by GitHub, not relied on by discipline.

### Required-check strategy — option B (single always-run aggregator)

Every real CI workflow is path-filtered on `pull_request`, so **no build job
runs on every PR**. Requiring a path-filtered job directly would deadlock any PR
that doesn't touch its path (a required check that never runs blocks merge
forever). Therefore the required set is **exactly one context: `CI Gate`**.

`CI Gate` (`.github/workflows/ci-gate.yml`, merged to `main` in #296) runs on
**every** PR (no path filter), uses `dorny/paths-filter` to detect changed
areas, and waits **only** for the checks the triggered workflows actually
produce for those areas — passing immediately when nothing build-relevant
changed. Mapping (mirrors each workflow's `pull_request.paths` exactly):

| Area (changed paths) | Checks the gate waits for |
|---|---|
| `StingTools/**`, `StingTools.Standards/**`, `…/stingtools-plugin.yml` | Validate data files · Viewer JS syntax · Build StingTools Plugin |
| `Planscape.Server/**` | Build & Test |
| server DTOs/Entities/Controllers, `stingtools-core/python/**`, `StingBridge/**`, `Planscape/**`, `…/contract-drift.yml` | Server build (DTOs compile) · Client ↔ server wire-contract tests · Mobile typecheck (tsc --noEmit) |
| `Planscape/**` | Type-check + Lint |
| `Planscape/assets/viewer/**`, `…/wwwroot/**`, `…/coordination-viewer-drift.yml` | Source ↔ wwwroot byte-equal |

Never waited on (would deadlock): **Docker Build & Push** / **Deploy to
Render.com** (push-to-main only), **EAS build** (workflow_dispatch only),
**Auto-label PR** (cosmetic).

### BLOCKED — needs repo admin

The token on this machine is **`StingD85`** (write collaborator,
`"admin": false`). The repo settings PATCH and the
`branches/main/protection` PUT both require **admin** on
`beckykyomugisha/STINGTOOLS` → they return **HTTP 404** for a non-admin. No
`beckykyomugisha`/admin credential exists on the machine (checked env, `gh`,
GCM, Windows Credential Manager). Per the owner's decision, protection is left
**unapplied for now**; the `CI Gate` workflow is already on `main` and will be
the single required check the moment an admin runs the two commands below.

### Ready-to-run (owner / admin token) — apply + prove

```bash
# 1) repo merge settings
gh api -X PATCH repos/beckykyomugisha/STINGTOOLS \
  -F delete_branch_on_merge=true -F allow_squash_merge=true \
  -F allow_rebase_merge=true -F allow_merge_commit=false -F allow_auto_merge=true

# 2) branch protection on main (single required check: "CI Gate")
gh api -X PUT repos/beckykyomugisha/STINGTOOLS/branches/main/protection --input - <<'JSON'
{
  "required_status_checks": { "strict": true, "contexts": ["CI Gate"] },
  "enforce_admins": true,
  "required_pull_request_reviews": {
    "required_approving_review_count": 0,
    "dismiss_stale_reviews": true,
    "require_code_owner_reviews": false
  },
  "required_linear_history": true,
  "allow_force_pushes": false,
  "allow_deletions": false,
  "required_conversation_resolution": true,
  "restrictions": null
}
JSON

# 3) prove it
gh api repos/beckykyomugisha/STINGTOOLS/branches/main/protection            # read-back: settings stuck
git push origin HEAD:main                                                   # MUST be rejected
# open a server-only PR (touch a non-DTO file under Planscape.Server/**) →
#   CI Gate waits only for "Build & Test" (no frontend check) → no deadlock
# open a server+frontend PR → CI Gate aggregates both areas' checks → mergeable
# close test PR(s) unmerged; git push origin --delete <test-branch>
```

`required_approving_review_count: 0` because the project is solo + AI (an agent
can't approve its own PR); raise to `1` only when a second human reviewer
exists. `required_linear_history: true` pairs with `allow_merge_commit: false`
(squash/rebase only) — note this changes the merge style from the merge-commit
history used during consolidation.

### Status

- **Enforced now:** nothing additional yet (admin call pending). `CI Gate`
  runs on every PR and is green-tested (#296).
- **Prepared:** required-check strategy decided (`["CI Gate"]`), exact
  apply + proof commands above.
- **Action owner:** repo admin (`beckykyomugisha`) runs the block above, or
  grants `StingD85` admin / supplies an admin token for the agent to apply +
  prove.
