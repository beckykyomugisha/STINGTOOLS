# Healthcare Pack — Phase 199 Fixes + Fresh Gap Hunt (Autonomous Agent Prompt)

You are an autonomous engineering agent in the **StingTools** monorepo. Execute the
two parts below **without stopping to ask**: (A) implement the verified findings,
then (B) hunt for further gaps and fix the real ones. Pick the most flexible,
sustainable, data-driven option; match existing patterns; do not gold-plate. Read
every cited file before editing.

Each finding below was **verified against the actual code** (the CSV evidence and
line numbers are real). The DO-NOT-TOUCH section lists areas already verified
correct — do not re-chase or "fix" them, and do not trust a fresh grep over a
verified conclusion.

## Ground rules

- **Do not ask for confirmation.** Work to completion; one logical commit per fix.
- **Branch / worktree.** Continue on the existing worktree `C:\Dev\STINGTOOLS-hc-def`,
  branch `claude/healthcare-deferred` — confirm with
  `git -C C:\Dev\STINGTOOLS-hc-def rev-parse --abbrev-ref HEAD`. If absent, create a
  worktree off the latest `claude/healthcare-deferred`. Never touch the shared
  `C:\Dev\STINGTOOLS` checkout; never commit to `main`. Commit messages imperative,
  ending with the repo's `Co-Authored-By` trailer.
- **Build.** This machine builds `StingTools` and `Planscape.Server` — build what you
  touch and report `0 errors` or the actual errors.
- **Any new shared parameter** registered in ALL of `MR_PARAMETERS.txt`,
  `MR_PARAMETERS.csv`, `PARAMETER_REGISTRY.json`, `CATEGORY_BINDINGS.csv` with a
  deterministic sibling GUID.
- **Verify before you report or fix.** This codebase has repeatedly produced
  plausible-but-false audit claims (a "catastrophic" binding failure that was a
  false positive; a migration "defect" that wasn't). For every new gap you find in
  Part B, confirm it against the real code path before acting — quote the exact line,
  and for "unread param / unreachable command" claims, check the JSON-driven
  consumers (RDS fieldmap, COBie CSVs, tag-container registry) and the dynamic
  dispatch paths, not just a literal grep. Report a finding as CONFIRMED only after
  that check.
- **Docs.** Update `docs/CHANGELOG.md` (append to the Phase 199 block or add a small
  Phase 199 follow-up sub-section — do not mint a colliding phase number) and
  `docs/ROADMAP.md` where a note is warranted. Leave the branch for review — no merge,
  no PR. Final summary: each finding FIXED / NOT-A-BUG (with why) + any new gaps found
  and their disposition + build status.

---

## PART A — implement the verified findings

### A-1 (MEDIUM, real bug) — Spare-parts dedup drops distinct spares
`StingTools/Docs/ClinicalCobieBridge.cs:145` dedups spare rows by `s.SpareName`
**alone, globally** (`sparesSeen.Add(s.SpareName)`), while the jobs dedup directly
above (line ~129) correctly uses a compound `{tag1}|{JobName}` key. `COBIE_SPARE_PARTS.csv`
legitimately carries the same spare name under different type codes with **distinct
part numbers** (verified: `CEQ-AUTOCLAVE,"Door Seal",…SEAL-AUTO` and
`CEQ-MORT-FRG,"Door Seal",…SEAL-MORT-FRG`). Because the emitted Spare row includes the
TypeName (line ~149), those are two valid, distinct COBie rows — but name-only dedup
silently drops the second, losing that equipment type's spare linkage in the FM handover.

**Task.** Change the spare dedup key to be per-type (and ideally per-part) so distinct
spares survive — e.g. `sparesSeen.Add($"{typeCode}|{s.SpareName}")`, or key on
`s.PartNumber` if that is reliably unique in the CSV. Keep it consistent with how the
jobs dedup is scoped. Verify a element/type pair that shares a spare name with another
type now emits both rows.
**Acceptance.** Two clinical types sharing a spare name each emit their own Spare row
(distinct TypeName/PartNumber); genuine duplicates within one type are still deduped.

### A-2 (LOW, discoverability) — Standalone clinical-COBie command has no button
`Healthcare_CobieClinical` (`HealthcareCobieClinicalExportCommand`) is dispatchable via
`StingCommandHandler` + `WorkflowEngine` but is **not** registered in
`StingTools/UI/Modules/HealthcareCommandModule.cs` nor surfaced as a button in the BCC
Healthcare tab (`UI/BIMCoordinationCenter.cs`). Note the clinical rows already ship
unconditionally via the main COBie handover export (`HandoverExportCommands.cs:406`), so
this is discoverability of a redundant convenience command, **not** unreachable
functionality.

**Task.** Add the command to `HealthcareCommandModule` (and/or a BCC Healthcare-tab
button) following exactly how the sibling healthcare commands are registered there, so
a user can invoke the standalone clinical export from the Healthcare UI. Match the
existing registration signature — do not invent a new pattern.
**Acceptance.** The command is reachable from the Healthcare UI surface like its siblings.

### A-3 (note, not a bug) — FGI escalation opt-in scope
`FgiAdoptionContext.Escalate` is currently called only from `AntiLigatureValidator`
(lines ~50, ~65). This is acceptable seam scope (validators opt in; the mechanism is
one-way), **not** a defect — do NOT bulk-wire it into every validator.

**Task.** Add a one-line note to the `HC-DEF-09` ROADMAP entry stating that only
`AntiLigatureValidator` currently opts into FGI escalation and that extending it to
other validators is a follow-up. No code change unless you also choose to opt in one or
two clearly FGI-governed findings (e.g. a behavioural-health rule) — if so, keep it
minimal and inert-by-default (empty clause map = no change).

---

## PART B — fresh gap hunt (find more, fix the real ones)

Sweep the healthcare pack for gaps NOT yet covered, across integration / alignment /
correctness / accuracy / flexibility / information / automation. Prioritise the
Phase-199 surface (least-audited) but do not stop there. Confirm each before acting.
Specific things worth probing:

- **COBie clinical export edge cases:** does `PatternMatches` (prefix/exact) over-match
  or miss type codes? Are Job rows deduped consistently with the (now-fixed) spare key?
  Does an element with `CEQ_CLINICAL_BOOL` false but a CEQ category still get emitted (or
  correctly skipped)? Are `Esc()`/CSV-escaping applied to every emitted field (comma /
  quote injection from param values)?
- **Adjacency BFS:** does `RoomGraphBuilder.Build` handle multi-storey / linked models,
  and does the centroid fallback use consistent units (ft→m) with the BFS path's
  thresholds? Is `graphUsable` computed once and reused (perf)?
- **Radiation write-back:** does it write to element **types** vs **instances**
  correctly (params are Instance-bound)? Does re-running it on an already-APPROVED
  barrier downgrade it to DRAFT if the QE param was cleared, or does it preserve
  provenance sensibly?
- **Registries (adjacency / diversity / twin / FGI):** do they all fail safe on malformed
  JSON/CSV (log + empty, not throw)? Is the per-doc cache keyed on a path that is stable
  for unsaved documents?
- **Param registration integrity (recurring theme):** re-run the check that every
  parameter the Phase-199 code *reads or writes* is registered in all four data files
  and bound to the categories it is read from (remember: binding is group-driven via
  `coreCats` unless the group is narrowed — CATEGORY_BINDINGS.csv is a reference, not the
  authority; confirm the actual group binding, don't chase CSV gaps).
- **Information/orphans:** any Phase-199-introduced param defined but genuinely unread
  (check code + RDS fieldmap + COBie CSVs + tag-container registry before declaring it
  orphaned).
- **Doc/code alignment:** any other stale comment or ROADMAP/CHANGELOG claim that
  contradicts the shipped code (like the AdjacencyValidator comments just fixed).

For each confirmed gap, fix it if it is a clear, low-risk correctness/accuracy/
integration defect; if it needs a design decision or authoritative data, implement the
seam and ROADMAP it rather than guessing. Report anything you investigated and
concluded was NOT a bug, with the reason (so it isn't re-audited later).

---

## DO-NOT-TOUCH — verified correct; do not re-chase

- **The binding model.** Params bind via group-driven `coreCats` in
  `LoadSharedParamsCommand`; the healthcare groups are not narrowed, so reads resolve.
  `CATEGORY_BINDINGS.csv` is a validation/reference artifact, not the binding authority.
  Do NOT "re-bind" healthcare params chasing CSV gaps.
- **CEQ_EQP_TAG / CEQ_TAG_7_PARA_TXT** are tag/narrative **containers** (registered in
  `TAG_CONFIG_v5_0_CONTAINERS.csv`), populated by the tagging pipeline — NOT orphaned
  data params. Do not "wire" them into the COBie export.
- **Already verified correct (Phase 199):** adjacency BFS hop logic + cap + self/dedup,
  radiation write-back QE gate + empty-selection + non-destructive seeding, diversity
  precedence (project→NFPA99→flagged 1.0 fallback), Twin null-safe resolve, FGI
  jurisdiction/freeze-date parsing, all three registries' cache+override. Re-verify only
  the specific edge cases named in Part B; don't rewrite these.
- **The empty seam data files** (`HEALTHCARE_MGAS_DIVERSITY.json`,
  `HEALTHCARE_FGI_CLAUSE_MAP.json`) are empty **on purpose** — do not populate them with
  guessed values.
- Do not fake domain physics (HC-DEF-02/03) or pull a 3rd-party BMS stack (HC-DEF-05).

## Definition of done

- A-1 fixed (spare dedup), A-2 fixed (command discoverability), A-3 ROADMAP note added.
- Part B: real gaps fixed or seam+ROADMAP'd; investigated-but-not-a-bug items reported
  with reasons; DO-NOT-TOUCH respected.
- `StingTools` (+ `Planscape.Server` if touched) build clean; any new param registered in
  all four files with a GUID.
- CHANGELOG updated (no colliding phase number); ROADMAP notes added.
- Final summary: per-finding disposition, new gaps + disposition, new params + GUIDs,
  build status.
