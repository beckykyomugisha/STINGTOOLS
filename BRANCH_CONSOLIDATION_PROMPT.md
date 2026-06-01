# Prompt — Tidy the repo to a single `main` (promote + archive, build-gated, no work lost)

Hand this to a build-capable agent (.NET + Node + Python). Goal: end with
**only `main`** (plus preservation tags), where `main` contains the work worth
keeping and builds green. The two non-negotiables: **(1) no committed work is
ever lost; (2) `main` builds green** (modulo the documented baselines below).

**Strategy — do NOT merge all 63 branches.** Most are debris (already-merged
or superseded). Instead: **assemble one integration branch from the *few*
branches that hold work worth keeping, promote it to `main`, then archive-tag
and delete everything else.** You preserve a branch's work by *tagging* it, not
by merging it — so retiring a branch costs nothing and risks nothing.

Repo: `beckykyomugisha/stingtools`. ~64 branches at last count. Re-enumerate
live; trust git, not this doc.

---

## Hard rules (violating any is a failure)

1. **Tag every branch tip before deleting anything** (Phase 0). Once a tip is
   tagged, its commits are reachable forever — deletion can't lose it.
2. **Never delete a branch whose tip is not preserved** — either an ancestor of
   `main` (`git merge-base --is-ancestor origin/<b> origin/main` exits 0) **or**
   carried by an `archive/<b>` tag. One of those must be true for every delete.
3. **Never `git push --force` to `main`.** Build the integration branch, verify,
   then fast-forward `main` to it.
4. **Merge only the handful of branches with work worth keeping**, hunk-by-hunk
   for the union of real work. On a genuine either/or (competing pair), **STOP
   and ask the user** — don't pick silently.
5. **Build-gate every merge and the final promotion.** Red and not cleanly
   fixable → abort that merge, set the branch aside as "blocked," continue.
6. **Don't fabricate, don't silently drop.** A retired branch is preserved by
   its `archive/` tag and reported — that's a clean outcome, not a loss.

---

## Phase 0 — Preserve everything (bundle + archive tags)

```
git fetch --all --prune
git bundle create ../stingtools-ALL-REFS-$(date +%Y%m%d-%H%M).bundle --all   # disaster-recovery backup
# Tag EVERY branch tip. These tags both back up and (for retired branches) become the permanent preservation marker.
for b in $(git branch -r | grep -v HEAD | grep -v '/main$' | sed 's#origin/##'); do
  git tag -f "archive/$b" "origin/$b"
done
git push origin --tags
```

After this, **no deletion can lose work** — every tip is tagged, and the bundle
is a full backup. This is the foundation that makes the rest safe.

---

## Phase 1 — Dry run (READ-ONLY triage; mutate nothing; STOP for authorization)

Produce the full picture without changing any ref/commit/working-tree file.
Use `git merge-tree` for conflict prediction — it computes in memory and never
touches the index or working tree. (The Phase-0 tags are the only writes.)

For each remote branch `<b>` (except `main`):
```
git merge-base --is-ancestor origin/<b> origin/main && echo "MERGED"     # already in main
git rev-list --count origin/main..origin/<b>                              # unique commits
git log --oneline origin/main..origin/<b> | head -5                       # what's unique
git diff --name-only origin/main...origin/<b>                             # files it touches (overlap detection)
git merge-tree --write-tree origin/main origin/<b> >/dev/null 2>&1; echo "merge-tree exit=$?"   # would-conflict?
```

Classify each into:
- **MERGED** — tip is an ancestor of `main`. Pure debris → archive-tag (done in
  Phase 0) + delete. No merge, no decision.
- **KEEP** — has unique commits you want in `main` (real, current work). These
  are the *only* branches you'll merge. Expect a small set (see "likely KEEP").
- **RETIRE** — has unique commits but they're superseded/abandoned/experimental
  and not wanted. → archive-tag (done) + delete. No merge.
- **FORK (ask)** — competing pair or conflicting branch where it's a genuine
  either/or. → needs a user decision (KEEP one / union / RETIRE).

**Likely KEEP** (verify against the dry-run; don't assume):
- `claude/upbeat-noether-tg4pn` — the integration branch with the contract
  guardrail + CI gate, cross-host identity, the six drift fixes, test-project
  build fix. **Use this as the integration base.**
- `claude/magical-mayer-hLnIk` — the audit docs (`CONTRACT_ALIGNMENT*.md`,
  `BRANCH_CONSOLIDATION_PROMPT.md`) **+ the 9-line mobile import fix** (not on
  upbeat).
- `claude/eager-dirac-lNOfB` — the Bonsai MVP operators (~1,665 lines, 16
  operators) — likely not on upbeat.
- Anything the dry-run shows as recent, unique, and real.

**Known competing pairs to scrutinise** (verify): `fix/boq-waste-dedup` vs
`fix/boq-waste-legacy-fallback`; `fix/material-lookup-long-format` vs
`-populate`; `fix/wire-photo-dbsets-and-api-bugs` vs `fix/wire-photo-services`;
`fix/test-config-using` vs `fix/test-project-build`; `feature/phase3` vs
`phase3-interop`; `feature/phase-a-restyle` vs `phase-a-theme-fix`.

**Write `branch-triage-report.md`** (don't commit unless asked):

| branch | class (MERGED/KEEP/RETIRE/FORK) | unique commits | conflicts vs main? | overlaps with | one-line summary |

Plus roll-ups: the MERGED+RETIRE list (delete-only, count), the KEEP list (the
few to merge), the FORK list (each with files-in-contention + a recommendation).

**Then STOP.** Present the report and get the user to confirm: (1) the
delete-only set (MERGED+RETIRE), (2) each FORK decision, (3) the final KEEP
list. Nothing destructive has happened — repo is unchanged except Phase-0 tags.

---

## Phase 2 — Assemble the integration branch (build-gated; only the KEEP set)

```
git switch -c integration/to-main origin/claude/upbeat-noether-tg4pn   # the most-complete base
```
Then merge in **only** the other KEEP branches (e.g. `magical-mayer-hLnIk`,
`eager-dirac-lNOfB`, plus any FORK the user chose to keep), one at a time:
- Resolve conflicts hunk-by-hunk for the union of real work (STOP + ask on a
  genuine either/or).
- **Build-gate after each** (Phase 3). Green → keep; red and not cleanly
  fixable → `git merge --abort`, mark "blocked," continue.
- Commit each merge naming the branch + what was resolved.

This is a *small* number of deliberate merges (the KEEP set), not 60.

---

## Phase 3 — The build gate (what "green" means)

After each merge and once on the final branch:
- **Server:** `dotnet build Planscape.Server/Planscape.sln -c Debug` → **0 errors.**
- **Plugin:** `dotnet build StingTools/StingTools.csproj -c Debug` → **0 errors.**
- **Contract tests:** `stingtools-core` + `StingBridge` Python suites pass;
  `tools/tests/cross_host_round_trip.py` skip-clean (exit 0).
- **Server tests:** `Planscape.Tests` must *compile*; don't *increase* the
  failure count.

**Pre-existing baselines (red here ≠ your fault; do NOT chase):**
- Mobile `tsc` (`Planscape/`): ~93 pre-existing errors remain after the 9-line
  import fix (ensure that fix survives the `magical-mayer` merge). Mobile is
  **not** a gate for this task.
- `Planscape.Tests`: ~127 pre-existing failures (DI/harness gaps) though it
  compiles. Gate on compilation + no new failures, not a green suite.

Passes iff: both C# builds 0 errors, contract tests pass, server tests didn't
regress.

---

## Phase 4 — Promote to `main`, then archive-delete everything else

```
git switch main
git merge --ff-only integration/to-main    # if it refuses, main moved: merge main into integration, re-gate, retry
dotnet build Planscape.Server/Planscape.sln && dotnet build StingTools/StingTools.csproj   # final gate ON main
git push origin main
```

Now delete **every** other branch — safe because each is either an ancestor of
`main` (KEEP, now merged) or preserved by its Phase-0 `archive/` tag (MERGED /
RETIRE / blocked):
```
for b in $(git branch -r | grep -v HEAD | grep -v '/main$' | sed 's#origin/##'); do
  if git merge-base --is-ancestor "origin/$b" origin/main 2>/dev/null || git rev-parse -q --verify "refs/tags/archive/$b" >/dev/null; then
    git push origin --delete "$b"
    git branch -D "$b" 2>/dev/null || true
  else
    echo "SKIP $b — neither in main nor archived (should not happen after Phase 0)"
  fi
done
git branch -D integration/to-main
```

Every retired branch's work lives on in `archive/<name>` (cherry-pick from it
later if ever needed). The branch *list* is now clean; nothing is lost.

---

## Phase 5 — Verify + report

- `git branch -r` shows **only `origin/main`**.
- `main` builds green (both C# targets + contract tests); the mobile 9-line
  import fix and `contract-drift.yml` CI gate are present in `main`.
- Final report: KEEP branches → merged ✔ (into which commit); MERGED/RETIRE →
  deleted (preserved by `archive/<name>`); blocked → why + that the tag/bundle
  preserve it.

---

## Phase 6 — Lock in tidiness going forward (so you don't end up at 64 again)

Recommend (or apply, if the user has admin) on the GitHub repo:
- **Branch protection on `main`** + required status check = the
  `contract-drift.yml` build gate, so `main` stays green.
- **"Automatically delete head branches"** (repo setting) → merged PRs self-clean.
- **PR → squash-merge → auto-delete.** Short-lived branches, gone on merge.

A one-time cleanup without this just defers the next pile-up.

---

## Verify / challenge before you start

- **Re-enumerate live.** The ~64 count/names may have changed.
- **The KEEP set is small — find it, ignore the rest.** Don't merge MERGED or
  RETIRE branches; archive-tag + delete is their whole lifecycle. Merging them
  back is wasted effort and added risk.
- **Confirm the integration base is actually the most complete** before
  building on it. If `upbeat-noether` is missing wanted work that lives on
  another branch, that branch joins the KEEP set (merged in Phase 2) — it does
  not change the strategy.
- **The FORK pairs are the only real decisions** — surface them; don't guess
  which implementation wins.
- **If `main` had the pre-existing API build breakage**, the `upbeat-noether`
  base resolves it — confirm the build goes green after Phase 2, not before.
- **Stopping to ask is cheaper than redoing.** A wrong silent resolution on
  `main` is expensive to unwind; lean toward surfacing.
