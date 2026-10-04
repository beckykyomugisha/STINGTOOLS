# Tagging loop — runner prompt (remaining items, ≥ 3 rounds)

> Paste everything below the line into a new Claude Code session opened on this repo.
> Written 2026-10-04 at the close of session `tag-families-setup-976ed0`. State before
> starting: main `c2c79e054`+, `StingTools.Tags.Tests` 5,626/5,626, in-Revit smoke 11/13.

---

You are continuing the **standing tagging review-and-fix loop** on StingTools (Revit plugin,
repo `beckykyomugisha/STINGTOOLS`). Work unattended. Do not ask me questions unless a decision
genuinely belongs to me; when it does, log it in `docs/WORKLOG_TAGGING.md` under *Decisions
needed* and carry on with another item.

## 0. Read first

1. `docs/WORKLOG_TAGGING.md` — *Resume here*, *NEEDS REVIT CHECK*, *Findings*. It is the
   state of record; keep it current after every merge.
2. `docs/ROADMAP.md` rows **TAGFAM-9**, **DRAW-9**, **TAGACC-12**, and every open `TAGACC-*` /
   `TAGFAM-*` / `DRAW-*` row.
3. `CLAUDE.md` — sections *The failure mode this codebase produces*, *Conventions for AI
   Assistants*, *Tag Style Engine*, *Drawing Template Manager*.
4. Memory: `project-tagging-review-loop`, `reference-headless-revit-pyrevit-run`,
   `feedback-break-the-gate-circularity`, `feedback-json-typecheck-newtonsoft`.

**Verify every item against the code before planning a fix.** Items written from memory have
been stale before (worklog F1/F2).

## 1. Safety rules (verbatim — these override everything else)

- Never force-push, rewrite published history, or delete branches, files or data you did not
  create in this session.
- Never disable, skip or weaken a test or gate to make it pass.
- Never run a renumber or overwrite on a real project model; use test models only.
- If blocked on one item, log why and continue with another.

Also: never deploy over the live add-in (`C:\Dev\STING_KUT_LIVE`, owned by the KUT/ACC
session); never `git stash` bare (shared stash); never stack a branch on an unmerged branch.

## 2. Working rules

- One logical change per commit, message prefixed with its id (`TAGFAM-9b:`, `DRAW-9b:`,
  `TAGACC-28:` …). New gaps get the next free id in `docs/ROADMAP.md`.
- Every branch fresh from `origin/main`, inside this session's own worktree.
- Merge only when the Windows build, `StingTools.Tags.Tests`, every gate and CI are green
  (`gh pr checks N --watch` in a background shell; `gh pr merge N --merge`). On CHANGELOG /
  ROADMAP conflicts merge `origin/main` in and keep both sides, rows in numeric order.
- Data-driven and backward-compatible: existing projects, families and saved configs keep
  working; never renumber tags as a side effect.
- Each fix ships with a test that **fails before and passes after** — prove RED, then GREEN,
  and report both numbers. Prefer fixes that make the failure inexpressible (one shared
  function both paths call; a test that enumerates the whole set).
- Log the fix in `docs/CHANGELOG.md`, update or close the ROADMAP row, update the worklog.

## 3. Tools you have

- **Build / unit tests:** `dotnet build StingTools/StingTools.csproj -c Debug` ·
  `dotnet test StingTools.Tags.Tests/StingTools.Tags.Tests.csproj`.
- **In-Revit smoke (Revit 2025 must be closed):** `tools/run_revit_smoke.ps1 -RevitVersion 2025`.
  First move `%APPDATA%\Autodesk\Revit\Addins\2025\StingTools.addin` aside and restore it in a
  shell `trap` (checksum before and after must match). Results: `TestResults/revit-smoke/<ts>_R2025/`.
- **Headless Revit:** `pyrevit run <script.py> --revit=2025 --purge` — no UI, logs to a file,
  load the plugin with `Assembly.LoadFrom` on the smoke build's `StingTools.dll`. Reusable
  scripts in `tools/pyrevit/headless/`. Use this, not computer-use, for anything in Revit.

## 4. The items

### A. TAGFAM-9b — style writers that never record a style  *(start here)*
Since TAGFAM-9, new tag families carry no `TAG_*_BOOL` switches; a family's style is its type
plus `TAG_STYLE_CODE_TXT`. These writers still set **only** switches, so on a new family they
record nothing and report success:
`ApplyColorSchemeCommand` and `BatchApplyColorSchemeCommand` (`Tags/TagStyleCommands.cs`),
`SwitchTagStyleByDiscCommand` (same file), the scale tiers (`Core/ScaleTiers.cs` + the
`ScaleTier*` commands), `Core/Drawing/TokenProfileApplier.cs`.
- Route every one through the single writer the variant writer and Apply Tag Style already use
  (find it from `TagStyleEngine.cs` / `TagStyleFamilyParams.cs`); write the code, still drive a
  switch where the family has one, and **name** any type that has neither instead of skipping it.
- Gate: a source test that enumerates every caller that sets a `TAG_*_BOOL` and fails if any
  does not also go through the code writer — so a sixth writer cannot slip in.
- Revit check: headless script — build a switch-less tag family, run each writer, read back
  `TAG_STYLE_CODE_TXT`.

### B. DRAW-9b — provenance stamps that still only log
`AnnotationRunner` grid/level chains and match-line frames, `MatchLineEngine` captions and
`MEPDimensioner` still log a refused stamp instead of counting it. Route them through
`ProvenanceStampTally` (as `ElementDimenser` / `DrainageInvertDimensioner` do) so the run
report says how many annotations could not be stamped. Gate: a source test listing every
`Stamp(` call site that bypasses the tally. Non-StingTools callers (Dynamo, pyRevit) stay
reported-not-fixed.

### C. Spot-slope smoke fixture
`SpotSlope_WithNoSlopeType_Blocks_PlacesNothing` (`StingTools.Revit.SmokeTests/MepAnnotatorSmokeTests.cs`)
is inconclusive every run: the metric template refuses deletion of its spot-slope type. Give
it a fixture that reaches a document with no spot-slope type (another template, a family-free
project, or a seam that lets the annotator see "none") **without** weakening the assertion.
Target: smoke 13/13, or a documented reason it cannot be.

### D. Config that loads but does nothing (research seam)
`project_config.json` sections deserialised into POCOs without `JsonProperty` names:
`CATEGORY_VISUAL_POLICY`, `CATEGORY_TOKEN_OVERRIDES`, `SLA_THRESHOLDS`, `SHEET_MARGINS`. For
each: does the shipped / wizard-written JSON's casing reach the POCO fields? Is every parsed
field read by something? Does any writer wipe sibling keys (TAGACC-19 shape)? Fix each real
mismatch with a round-trip test over the shipped file, not a hand-written fixture. Then the
same question for the other tagging data files (`TAG_STYLE_RULES.json`,
`tag_style_catalogue.json`, `TAG_PLACEMENT_PRESETS_DEFAULT.json`, `STING_TAG_SCHEMES.json`).

### E. AnnotationRunner auto-load
When a drawing type names a tag family that is not loaded, the runner warns and falls back to
the category's STING family (F13). Enhancement: load it from `Data/TagFamilies` if shipped,
report it if not. Keep the fallback; never silently place a different family than declared.

### F. Re-time TAGFAM-9
`tools/pyrevit/headless/time_add_shared_params.py` on a quiet machine, three runs, report
median seconds per door tag and per parameter, TYPE vs INSTANCE separated
(`profile_add_params.py`). Record in CHANGELOG; replace the "noisy" caveat only if the runs agree.

### G. Still needs a person — do not fake, keep listed
TAGACC-12 Part B (two users, central model — `docs/TAGGING_ACCURACY_TEST_PROTOCOL.md`); a look
at Room Finish / Fire Compartment tags at 2.5 and 3.5 mm in a project (headless export draws no
room tags). If you find a way to verify either headlessly, do it; otherwise leave them under
*NEEDS REVIT CHECK* with exact steps.

## 5. The loop — at least 3 rounds

Run **Round 1, Round 2, Round 3** at minimum; continue with Round 4+ until a round finds no
new issue. Each round:

1. **Baseline** — fetch, fresh worktree state on `origin/main`; build; run
   `StingTools.Tags.Tests` and the in-Revit smoke. Record pass/fail counts in the worklog.
   Any regression since the last round is the first thing you fix.
2. **Fix** — take the next open items (Round 1: A, B, C; Round 2: D, E, plus whatever
   Round 1 found; Round 3: F and everything found since). One PR per item, merged before the
   next depends on it.
3. **Re-audit what you just touched** — for every merged fix, actively hunt for its siblings:
   other callers of the same pattern, other data files with the same shape, other commands
   that report success with nothing done. Try to break your own gate (circular expectation?
   alternative path that escapes it?). Every new defect becomes a Findings row with severity
   and an id.
4. **Wider sweep** (one per round, rotate): R1 token writers + SEQ (`TagConfig`,
   `TokenAutoPopulator`, `StingAutoTagger`); R2 tag families + registry/bindings
   (`TagFamilyCreatorCommand`, `RESOLVED_BINDINGS.csv`, `PARAMETER_REGISTRY.json`, manifest);
   R3 annotation pass + drawing types + scope boxes (`AnnotationRunner`, `DrawingTypePresentation`,
   `ScopeBoxBinder`). Look especially for: silent `catch {}` around writes, `GetString` on
   non-TEXT params, `SetString` into unit-bearing doubles, Newtonsoft field names that don't
   match the JSON, empty scopes counted as passes.
5. **Close the round** — update *Resume here*, *State (round N)*, *Findings*; commit the
   worklog as its own PR. A round is not done until the worklog says what it found and fixed.

## 6. Finish

Stop when at least three rounds are complete **and** the last round produced no new finding.
Then: all branches merged or explicitly handed off; worklog *Resume here* lists only what is
truly left; final baseline numbers (unit + smoke) recorded; the add-in manifest restored
byte-identical. Report to me in plain language: what changed for a user, what was verified in
Revit and how, what is still open and why, and anything that needs my decision.
