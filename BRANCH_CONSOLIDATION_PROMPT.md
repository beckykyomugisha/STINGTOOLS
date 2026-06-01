# Prompt — Consolidate ALL branches into `main`, then delete them (build-gated, no work lost)

Hand this to a build-capable agent (it has .NET + Node + Python). This is a
**destructive, repo-wide** operation: the end state is `main` containing every
branch's real work, and **only `main` remaining**. The two non-negotiables:
**(1) no committed work is ever lost; (2) `main` builds green** (modulo the
documented pre-existing baselines below).

Repo: `beckykyomugisha/stingtools`. ~64 branches at last count. Treat every
number/name here as a starting picture — **re-enumerate live; trust git, not
this doc.**

---

## Hard rules (read first; violating any is a failure)

1. **Back up before touching anything** (Phase 0). The bundle + tags are the
   ultimate "nothing is lost" guarantee.
2. **Never delete a branch until its tip is provably an ancestor of `main`**
   (`git merge-base --is-ancestor <branch> main` exits 0). No exceptions.
3. **Never `git push --force` to `main`.** Build it up on an integration
   branch, verify, then fast-forward `main` to it.
4. **Never resolve a conflict with blind `-X ours`/`-X theirs` on whole
   merges.** Resolve hunk-by-hunk with understanding; the goal is the *union*
   of real work. If a conflict is a genuine either/or design choice (competing
   implementations), **STOP and ask the user** — don't pick silently.
5. **Build-gate every merge.** After each merge, the build must be green
   (modulo documented baselines). If a merge turns the build red and you can't
   fix it cleanly, **abort that merge** (`git merge --abort` / reset to
   pre-merge), set the branch aside as "blocked — needs review," and continue
   with the others. Don't leave `main` red.
6. **Don't fabricate.** If a branch's work is genuinely obsolete/superseded or
   un-mergeable, **report it, don't silently drop it and don't fake a
   resolution.** A branch left undeleted with a reason is a fine outcome.

---

## Phase 0 — Back up (do this first, always)

```
git fetch --all --prune
git bundle create ../stingtools-ALL-REFS-$(date +%Y%m%d-%H%M).bundle --all
# tag every remote branch tip so the commits are reachable even if the branch is deleted
for b in $(git branch -r | grep -v HEAD | grep -v '/main$' | sed 's#origin/##'); do
  git tag -f "backup/$b" "origin/$b"
done
git push origin --tags
```

The bundle is a full repo backup; the `backup/<branch>` tags keep every tip
reachable. **If anything goes wrong, every commit is recoverable from these.**

---

## Phase 1 — Triage (classify every branch; produce a written plan; STOP for ambiguity)

For each remote branch (except `main`), classify into exactly one bucket:

- **(A) Already merged** — `git merge-base --is-ancestor origin/<b> origin/main`
  exits 0 (zero unique commits). → safe to delete, no merge needed.
- **(B) Unique work, clean** — has commits not in `main`
  (`git log --oneline origin/main..origin/<b>` non-empty), and a trial merge
  into the integration branch applies without conflict.
- **(C) Unique work, conflicting / competing / ambiguous** — has unique commits
  AND either conflicts on trial-merge, or is one of a *competing pair* (two
  branches that change the same thing different ways). These need judgment.

Build a table: `branch | bucket | unique-commit count | one-line summary |
conflicts-with`. Known competing pairs to scrutinise (verify, don't assume):
`fix/boq-waste-dedup` vs `fix/boq-waste-legacy-fallback`;
`fix/material-lookup-long-format` vs `fix/material-lookup-populate`;
`fix/wire-photo-dbsets-and-api-bugs` vs `fix/wire-photo-services`;
`fix/test-config-using` vs `fix/test-project-build`;
`feature/phase3` vs `feature/phase3-interop`;
`feature/phase-a-restyle` vs `feature/phase-a-theme-fix`.

Also note the likely **integration branches** that already aggregate much of
the work — `claude/upbeat-noether-tg4pn` (contract + cross-host + most
sessions) and `claude/magical-mayer-hLnIk` (the audit + the 9-line mobile
import fix). Merging the most-complete one first will absorb many others and
shrink the conflict surface.

**STOP and surface the plan to the user before any destructive merge** — at
minimum the bucket-C list (the genuine either/or choices). Get the user's call
on each competing pair (keep both? keep newer? union?). Buckets A and B you may
proceed with after the plan is acknowledged.

---

## Phase 2 — Build the integration branch (build-gated merges)

```
git switch -c integration/all-to-main origin/main
```

Merge order (lowest-conflict first):
1. The most-complete integration branch (likely `claude/upbeat-noether-tg4pn`).
2. Then `claude/magical-mayer-hLnIk` (audit + mobile import fix) and the other
   `claude/*`.
3. Then bucket-B branches (clean unique work) — `fix/*`, `feature/*`, `feat/*`,
   `audit/*`, `docs/*`.
4. Then bucket-C, one at a time, applying the user's decision per pair.

After **each** merge:
- Resolve conflicts hunk-by-hunk for the union of real work (STOP + ask on
  genuine either/or).
- **Build-gate** (see Phase 3). Green → keep the merge. Red and not cleanly
  fixable → `git merge --abort` (or reset), mark the branch "blocked," move on.
- Commit the merge with a message naming the branch + what was resolved.

Keep a running log: merged ✔ / blocked ✖ (with reason) per branch.

---

## Phase 3 — The build gate (what "green" means here)

Run after each merge (and once at the end):

- **Server:** `dotnet build Planscape.Server/Planscape.sln -c Debug` → **0 errors.**
- **Plugin:** `dotnet build StingTools/StingTools.csproj -c Debug` → **0 errors.**
- **Contract tests:** the Python conformance scripts (`stingtools-core` +
  `StingBridge` test suites) → pass; `tools/tests/cross_host_round_trip.py`
  → skip-clean (exit 0).
- **Server tests:** `dotnet test Planscape.Server` should build; some tests
  fail pre-existing (see baselines) — a merge must not *increase* the failure
  count or break compilation.

**Documented pre-existing baselines (red here ≠ your merge's fault — do NOT
chase them):**
- Mobile `tsc` (`Planscape/`): ~93 pre-existing errors remain after the 9-line
  import fix (that fix is on `claude/magical-mayer-hLnIk` — make sure it
  survives the merge). Mobile is **not** a build gate for this task.
- `Planscape.Tests`: ~127 pre-existing test failures (DI registration / harness
  gaps) even though the project now builds. Gate on *compilation + no new
  failures*, not a fully-green suite.

A merge passes the gate iff: both C# builds are 0 errors, the contract tests
pass, and the server test count didn't regress.

---

## Phase 4 — Promote to `main` and delete the branches

When the integration branch is fully assembled and green:

```
git switch main
git merge --ff-only integration/all-to-main   # fast-forward; if it refuses, main moved — re-merge main into integration first, re-gate, retry
dotnet build Planscape.Server/Planscape.sln && dotnet build StingTools/StingTools.csproj   # final gate on main
git push origin main
```

Then delete **only** branches proven merged:
```
for b in <every branch that is bucket-A OR was successfully merged>; do
  git merge-base --is-ancestor "origin/$b" origin/main || { echo "SKIP $b — not in main"; continue; }
  git push origin --delete "$b"
  git branch -D "$b" 2>/dev/null || true
done
git branch -D integration/all-to-main
```

Leave undeleted (and report): any branch marked **blocked** in Phase 2, or any
bucket-C pair the user said to keep separate. Do **not** delete a branch the
ancestor check fails for.

---

## Phase 5 — Verify final state + report

- `git branch -r` shows **only `origin/main`** plus any explicitly-retained
  blocked branches (with reasons).
- `main` builds green (both C# targets, contract tests).
- Produce a final report: every branch → merged ✔ (into which merge commit) /
  deleted, or blocked ✖ (why, and that its `backup/<branch>` tag + the bundle
  preserve it). Confirm the mobile 9-line import fix and the contract-drift CI
  gate are present in `main`.

---

## Verify / challenge before you start

- **Re-enumerate live** (`git branch -r`); the ~64 count and the names here may
  have changed. Trust git.
- **Don't octopus-merge.** One branch at a time, build-gated. 60+ branches
  merged in one shot is unreviewable and will bury a real conflict.
- **The competing pairs are the only real risk.** Most `feature/phase-*` and
  `fix/*` are probably disjoint or already-merged; the pairs that touch the
  same file two ways are where you must stop and ask. Don't guess which
  implementation "wins."
- **If `main` itself has the pre-existing API build breakage** (the duplicate
  type defs Prompt 5 fixed), the `claude/upbeat-noether-tg4pn` merge should
  resolve it — confirm the build goes green *after* that merge, not before.
- **Confirm scope with the user if the bucket-C list is large.** "Delete all
  branches" is easy to say; if 15 branches are genuine either/or choices,
  that's 15 decisions the user owns, not you.
- This is the one task where **stopping to ask is cheaper than redoing** — a
  wrong silent conflict resolution on `main` is expensive to unwind. Lean
  toward surfacing.
