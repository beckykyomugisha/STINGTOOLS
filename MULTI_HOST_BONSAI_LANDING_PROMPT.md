# Prompt — Land the Bonsai build commit safely, then verify the plugin in Blender

Run this on the **Windows machine** (`C:/Dev/STINGTOOLS`) where the bonsai
`build.py` work was committed locally. Two jobs, in order:

1. **Phase 1 — Land the commit**: get the local `claude/bonsai-installable`
   commit onto `origin/claude/bonsai-installable` without losing the unrelated
   WIP in the working tree and without rewriting any other branch.
2. **Phase 2 — Verify in Blender** (if Blender is installed on this machine):
   build the extension `.zip`, install it, and run the Part-1 acceptance
   click-path from `MULTI_HOST_BUILD_PROMPT.md` §3.4.

This prompt is written defensively because the last manual attempt ran the
rebase **on the wrong branch** (`claude/optimistic-bell-EfjJw`) over a **dirty
working tree** — it failed safely, but only by luck. You will re-derive the
state yourself and refuse to proceed when reality doesn't match.

---

## Context (verify, don't trust)

- `origin/claude/bonsai-installable` carries **documentation commits only**
  (the `MULTI_HOST_*` prompt `.md` files at repo root).
- The **local** `claude/bonsai-installable` branch carries one implementation
  commit: `stingtools-bonsai/{__init__.py, build.py, README.md,
  _vendor/_README.md}` + a `.gitignore`. It has **not** been pushed.
- The working tree (currently on `claude/optimistic-bell-EfjJw`) has unrelated
  uncommitted WIP: `.xlsx` deletions/renames + an untracked `ARCHICAD_DROP/`
  directory. This WIP must end up exactly where it started: **uncommitted, on
  `claude/optimistic-bell-EfjJw`**.
- The two sides of the rebase are expected to touch **disjoint files** (docs at
  root vs. `stingtools-bonsai/*`). You will confirm this before rebasing.

## Hard rules (violating any is a failure)

1. **Never `git push --force` / `--force-with-lease`** to any branch. If a push
   is rejected, stop and report — do not force.
2. **Never let the WIP cross branches.** It must never be committed, and never
   appear in a commit on `claude/bonsai-installable`.
3. **Never drop the stash** until it has been successfully popped back on
   `claude/optimistic-bell-EfjJw`. On a pop conflict, leave the stash intact
   (git keeps it on conflict) and report.
4. **On any rebase conflict: `git rebase --abort`, stop, report.** Disjoint
   files mean conflicts shouldn't happen; if one does, reality differs from the
   assumptions and a human should look.
5. **Do not touch `claude/optimistic-bell-EfjJw`'s history** or any other
   branch. The only branch you rewrite (rebase) is the local, unpushed
   `claude/bonsai-installable`.
6. **Re-derive every count and SHA.** This doc's numbers may be stale (more doc
   commits may have landed on origin). Trust `git`, not this prompt.

---

## Phase 1 — Land the commit

### 1.0 Detect state (read-only; abort if anything surprises you)

```bash
cd /c/Dev/STINGTOOLS
git fetch origin
git status                                  # note current branch + dirty files
git log --oneline -3 claude/bonsai-installable
git log --oneline origin/main..origin/claude/bonsai-installable
# Confirm disjoint file sets (the safety basis for a clean rebase):
git diff --name-only origin/main..origin/claude/bonsai-installable
git diff --name-only origin/main..claude/bonsai-installable
```

**Proceed only if:** (a) the local branch has the bonsai implementation
commit(s) not on origin; (b) the two `--name-only` lists share **no files**.
If they overlap, stop and report the overlapping paths.

### 1.1 Park the WIP

```bash
git stash push -u -m "wip: xlsx + ARCHICAD_DROP (parked by landing agent)"
git status                                  # must now be clean
```

### 1.2 Rebase the bonsai branch onto origin

```bash
git checkout claude/bonsai-installable
git rebase origin/claude/bonsai-installable
```

On conflict: `git rebase --abort` → restore (1.4) → report (rule 4).

### 1.3 Push (must fast-forward)

```bash
git push -u origin claude/bonsai-installable
# Sanity: implementation commit(s) now on top of the doc commits:
git log --oneline origin/main..origin/claude/bonsai-installable
```

If rejected non-fast-forward even after the rebase: someone pushed meanwhile —
`git fetch origin && git rebase origin/claude/bonsai-installable` once more,
then push. Still rejected → stop and report. **No force.**

### 1.4 Restore the WIP where it came from

```bash
git checkout claude/optimistic-bell-EfjJw
git stash pop
git status    # the xlsx/ARCHICAD_DROP WIP is back, uncommitted, on this branch
```

**Phase 1 exit check:** origin has the implementation commit(s); local
`optimistic-bell` is exactly as found (same HEAD, same dirty files); stash list
is empty (or reported).

---

## Phase 2 — Verify the plugin in real Blender (this machine, if Blender exists)

Skip with an explicit "Blender not installed" note if there is no Blender —
do not simulate these checks.

```bash
git checkout claude/bonsai-installable
python stingtools-bonsai/build.py --blender "<path-to-blender.exe>"   # or --manual
blender --command extension validate stingtools-bonsai                # if supported
blender --background --python stingtools-bonsai/tests/verify_blender.py
```

Then install `dist/stingtools_bonsai-*.zip` in Blender
(Preferences → Get Extensions / Add-ons → Install from Disk) **with Bonsai
installed**, and run the acceptance click-path
(`MULTI_HOST_BUILD_PROMPT.md` §3.4):

| # | Check | ✅/❓/❌ |
|---|---|---|
| 1 | `.zip` installs; add-on enables without error | |
| 2 | `STING` tab visible in the 3D-view sidebar | |
| 3 | Bonsai disabled → "Bonsai is required" banner, no ops | |
| 4 | Bonsai enabled + IFC open → "core v… / Bonsai v…" header (NOT "core: NOT LOADED") | |
| 5 | Auto-Tag writes `Pset_StingTags`; **Ctrl-Z reverts it** | |
| 6 | Run IDS Validation produces results; summary strip updates | |
| 7 | A SELECT button selects the expected elements | |
| 8 | Planscape Login + Push succeed (live server; else mock the call and mark ❓ with reason) | |

Fix small breakages you're confident about (e.g. a path bug surfaced only when
installed) directly on `claude/bonsai-installable`, re-run the failing check,
commit + push (normal push, rule 1 applies). Anything architectural → report
instead of improvising.

When done, leave the working tree back on `claude/optimistic-bell-EfjJw` with
the WIP intact.

---

## Reporting

End with:
- Phase 1: the final `git log --oneline origin/main..origin/claude/bonsai-installable`,
  confirmation the WIP is restored untouched, and any deviation taken.
- Phase 2: the click-path table honestly marked, the `.zip` artifact path, any
  fixes committed (SHAs), and anything ❓ with the reason.
- Anything that made you stop under the hard rules, verbatim error included.
