# Implementation Prompt — branch and workspace triage: get unreviewed work landed or laid to rest

> **Audience**: autonomous terminal coding agent on the Windows box that holds
> `C:\Dev\STINGTOOLS`, with the Revit 2025 API and the .NET 8 SDK. You can build.
>
> **Repo**: STINGTOOLS. Read `CLAUDE.md` (root) first. Log finished work in
> `docs/CHANGELOG.md`, new gaps in `docs/ROADMAP.md`.
>
> **Autonomy**: you own every judgement below. Where this says *"Recommended"*,
> that is a considered default — adopt it unless the repo tells you otherwise,
> and if you deviate, say so with the evidence that changed your mind. Do not
> stop to ask. If one work item is blocked, finish the others in full and state
> plainly what you left and why.
>
> **This task is mostly about NOT losing things.** Read §2 before touching a
> single branch. Every rule there was paid for.

---

## 0. Start here

```bash
git -C C:/Dev/STINGTOOLS fetch origin --prune
```

Then take a baseline you can prove you did not regress:

```bash
dotnet build StingTools/StingTools.csproj -c Debug -clp:Summary      # expect 0 errors / 0 warnings
pwsh tools/check_workflow_wiring.ps1                                  # expect OK, Tier 4 = 0
python tools/check_smoke_test.py                                      # expect OK
```

Work in a **git worktree of your own**, cut from `origin/main`. Do not work in
`C:\Dev\STINGTOOLS` itself: it is the shared checkout, other sessions are live in
it, and its branch moves under you.

**Re-measure everything.** The numbers in this prompt were true when it was
written and this repo moves fast — the worktree count went 47 → 29 → 44 inside a
few hours while one session watched. Treat every figure below as a starting
hypothesis, not a fact.

---

## 1. The problem

**23 remote branches carry unique unmerged commits and have never had a PR
opened.** They are not drafts in review; nobody is looking at them at all. The
oldest is 76 days. Together they are the single largest pool of unreviewed work
in the repository, and every day makes each one harder to land as `main` moves
away underneath it.

| Branch | Unique commits | Age |
|---|---|---|
| `claude/kibale-np-bim-modeling-f5e653` | 141 | 7d |
| `claude/kut-lifecycle-integration` | 42 | 60d |
| `claude/tb-w1w5-fold` | 33 | 33d |
| `claude/tb-w1w5-impl` | 30 | 37d |
| `claude/render-deploy-merge-504` | 19 | 22d |
| `claude/symbol-sld-only` | 18 | 50d |
| `claude/boq-accuracy-hardening` | 16 | 61d |
| `claude/kibale-part1-fixes` | 11 | 13d |
| `claude/bonsai-installable` | 5 | 71d |
| `p517-resolve` · `eager-dirac-check` | 4 each | 16d |
| `claude/scope-box-manager` · `claude/repo-review-hardening` · `claude/m-pass-deploy` · `claude/kibale-finish-params` | 3 each | 13–76d |
| `claude/document-manager-iso-review-dbb595` · `claude/datarights-json-fix` · `claude/boq-p4-cost-control` | 2 each | 16–57d |
| `claude/sustainability-laymans-guide` · `claude/sitephotos-d1-guards` · `claude/setdepth-perf` · `claude/export-center-layout-issues-557d8e` · `claude/distgroups-server-canonical` | 1 each | 16–56d |

Alongside that, **4 worktrees hold uncommitted work** — the same failure mode one
step earlier, where the work is not even in git:

| Worktree | Branch | Files |
|---|---|---|
| `wt-viscenter` | `claude/visibility-temp-declarative` | 16 |
| `wt-authz-model` | `claude/one-authz-model` | 4 |
| `.wt/brave-elion-f9aaec` | `main` | 3 |
| `.wt/complete-unwired-controllers-741d58` | `claude/complete-unwired-controllers-741d58` | 1 |

---

## 2. Rules that are not negotiable

Each of these is a mistake that was actually made, or nearly made, in the session
that produced this prompt.

**2.1 — Commit count lies across a rebase. Use `git cherry`.**
A branch reported "3 commits ahead" of its remote. It looked like unbacked work.
The remote had been rebased 80 commits forward and already contained all three
patches under new SHAs. `git cherry -v <upstream> <branch>` marks each commit `-`
(patch already upstream) or `+` (genuinely unique). **`+` count is the only
measure of unique work in this document.** Had that branch been "rescued" by a
forced push, 80 commits would have been destroyed to restore a stale copy.

**2.2 — Never force-push without `--force-with-lease`.**
And never at all to a branch with an open PR or a live worktree unless the human
has said so in those words. `--force-with-lease=<branch>:<expected-sha>` aborts if
the remote moved. Use the explicit form with the SHA, not the bare flag.

**2.3 — A backup branch is only a backup if you check what is in it.**
A "backup" was cut from the wrong tip and carried an entire unrelated feature —
28 files, 4,161 lines — which would have duplicated an open PR. **Always
`git diff --stat origin/main...<branch>` a branch you just created and confirm it
contains what you think.**

**2.4 — Verify at the moment of action, not from a table you built earlier.**
Re-check clean/dirty and PR state immediately before each destructive step. Other
sessions commit, push and create worktrees while you work.

**2.5 — Removing a worktree is safe only when the commits live elsewhere.**
Clean tree **and** (`git branch -a --contains HEAD` non-empty, or the branch is on
the remote, or `git cherry` shows no `+`). Removing a worktree does not delete its
branch — but a detached HEAD has no branch protecting it.

**2.6 — Directory mtime is not a liveness signal.**
Running `git status` in a worktree refreshes the index and touches the directory,
so a survey loop contaminates its own evidence. Use commit dates and dirty state.

**2.7 — Do not deploy to Revit.** `deploy.bat` re-points the live add-in manifest
and would hijack the plugin slot from other sessions. Nothing here needs Revit.

**2.8 — Do not delete any branch.** This task opens PRs and rescues work. Closing
things out is a human decision; put your recommendation in the summary instead.

---

## 3. Work items

### WI-1 — Rescue the uncommitted work (do this first; it is the only irreversible loss)

For each of the 4 worktrees above, and any others you find:

1. Check whether a **live session** is plausibly in it — a commit from today plus
   files that look mid-edit. If so, **leave it and report it**; committing under
   an active session is worse than leaving the work uncommitted.
2. Otherwise inspect the files. Decide, per worktree, whether they are:
   - **real work** → commit on the branch that worktree is already on, with a
     message saying what they are and that they were found uncommitted;
   - **generated/scratch output** → check whether a tracked generator produces
     them. If so, do not commit; note it. If they should be ignored, propose a
     `.gitignore` line rather than adding one to a branch that is not yours.
3. **Secret-scan every file before committing** (`api[_-]?key|secret|password|
   token|BEGIN .*PRIVATE KEY|gh[pousr]_|xox[baprs]-`). Env-var *names* are fine;
   *values* are never committed.
4. **Commit with an explicit pathspec** — `git add -- <paths>` and
   `git commit -- <paths>` — so a concurrent stage by another session cannot be
   swept into your commit.
5. Push the branch. If it is not a fast-forward, **stop and report**; do not force.

**Watch for the branch mismatch trap.** Files found untracked in a shared
checkout have merely *followed* whatever branch it was on — they are usually
unrelated to it. If the branch has an open PR, committing there puts unrelated
files in someone's review. In that case commit to a **new branch cut from
`origin/main`** (see 2.3) and say so.

### WI-2 — Triage the 23 PR-less branches

Re-measure the table first (`git cherry origin/main origin/<branch>`); some will
have landed. Then classify each into exactly one bucket, with the evidence:

- **A — Superseded.** `git cherry` shows no `+`, or every patch is present in
  `main` under a different SHA. Nothing to do; recommend deletion in your summary.
- **B — Landable.** Unique work, still applies, and either merges cleanly with
  `origin/main` or conflicts trivially. **Open a PR.** Body must state: what it
  does, why it never got one, how far behind `main` it is, and whether it builds.
- **C — Rotted.** Unique work that no longer applies — the code it touches has
  been rewritten, or it conflicts substantially. **Do not open a PR.** Write it up
  in `docs/ROADMAP.md` with the branch name, the SHA, what it was trying to do,
  and what would have to happen to revive it. A ROADMAP row plus a branch that
  still exists is a fine resting place; a PR nobody can merge is not.
- **D — Needs a human.** Large or strategically ambiguous. `kibale-np-bim-modeling-f5e653`
  (141 commits, unreviewed) is almost certainly here. Do not open a 141-commit PR
  on your own judgement; summarise what is in it and what landing it would mean.

Determine mergeability without mutating anything:

```bash
git merge-tree $(git merge-base origin/main origin/<branch>) origin/main origin/<branch>
```

For bucket B, **build before opening the PR** where the branch touches C#. A PR
that does not compile costs a reviewer more than it saves.

Cap yourself at **6 new PRs**. More than that is not review, it is a queue nobody
reads — and say in your summary which bucket-B branches you left unopened and why.

### WI-3 — ROADMAP SMK-3: declarative read-only claims

`tools/check_smoke_test.py` proves a workflow preset is read-only when it declares
`"readOnly": true`, by checking every step's command carries
`[Transaction(TransactionMode.ReadOnly)]`. Exactly **1** preset declares it. The
checker also emits an advisory naming presets whose *prose* claims read-only
without the declaration; it currently names `WORKFLOW_KUT_MonthlyReport.json` and
`WORKFLOW_PlumbingAudit.json`.

For each advisory preset: verify every step really is `ReadOnly`; if so add
`"readOnly": true` and let CI enforce it, and if not, **reword the description** —
do not declare something false. Prose-matching was tried for this and failed in
both directions within minutes, which is why the field is declarative. Re-run the
checker; the advisory list should shrink.

While there: consider whether any *other* preset should carry the declaration.
Do not add it speculatively — only where you have checked the transaction modes.

---

## 4. Verification

```bash
dotnet build StingTools/StingTools.csproj -c Debug -clp:Summary
pwsh tools/check_workflow_wiring.ps1
pwsh tools/check_path_discipline.ps1
python tools/check_smoke_test.py
dotnet test StingTools.Tags.Tests
```

Report actual output, not a claim. If something fails, say so with the output —
a green claim over a red run is worse than no claim.

For each PR you open, confirm afterwards that its file list is what you intended
(`gh pr view <n> --json files`). That check is what catches 2.3.

---

## 5. Deliverable

A written summary — not a PR of its own unless WI-1 or WI-3 produced code — with:

1. **Rescued work**: per worktree, what was found, what you did, where it now
   lives, and which you deliberately left alone because a session looked live.
2. **The branch table, re-measured**, every branch in exactly one bucket with its
   evidence, and links to the PRs you opened.
3. **Your deletion recommendations** for bucket A — names and SHAs, for a human
   to action. You do not delete them (2.8).
4. **SMK-3**: which presets you declared, which you reworded, and the checker's
   advisory list before and after.
5. **What you could not do and why.**

Do not report a branch as "handled" when what you did was open a PR. Landing is
someone else's act, and saying otherwise is how 23 branches came to be invisible
in the first place.
