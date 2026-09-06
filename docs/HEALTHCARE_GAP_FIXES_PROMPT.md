# Healthcare Pack — Gap Remediation (Autonomous Agent Prompt)

You are an autonomous engineering agent working in the **StingTools** monorepo
(`C:\Dev\STINGTOOLS`). Execute all four workstreams below **end to end without
stopping to ask questions**. Make the decisions yourself; where a choice exists,
pick the **most flexible, sustainable, data-driven option** — the one that a
future maintainer can change without recompiling, that reuses existing patterns,
and that will not need re-doing when the next standard revision lands. Do not
gold-plate: solve exactly these four items well.

This work follows a read-only audit of the healthcare pack. The findings below
are verified — you do **not** need to re-discover them, but you **must** read the
cited files before editing them.

## Ground rules (apply to everything)

- **Do not ask for confirmation.** Work autonomously to completion.
- **Branch discipline.** You are likely on `claude/pm-complete`. Do **not** commit
  to `main`. Create/verify a feature branch first (`git rev-parse --abbrev-ref HEAD`).
  If multiple agents share this checkout, prefer a git worktree for isolation and
  verify the branch before any `reset`/`checkout` (see repo memory on worktree
  isolation).
- **Build caveat.** This environment usually has no .NET/Revit API, so a full
  `dotnet build` of the Revit plugin may not be possible. Where you cannot build,
  say so explicitly in the commit message and CHANGELOG entry (matches existing
  repo convention). The **server** project (`Planscape.Server`) and any pure-.NET
  test/tool code **should** build — attempt `dotnet build` there and report results.
- **Conventions.** Follow `CLAUDE.md` → "Conventions for AI Assistants": read
  before edit, targeted edits over rewrites, `StingLog` not silent catches,
  `TaskDialog` not `MessageBox`, transactions prefixed `STING`, one logical change
  per commit.
- **Logging your work.** Append a `#### Completed (Phase N — Healthcare gap fixes)`
  block to `docs/CHANGELOG.md`, and move any newly-tracked deferrals into
  `docs/ROADMAP.md`. Keep `CLAUDE.md` for stable structure only.
- **Commit** each workstream as its own logical commit. End commit messages with
  the repo's required trailer.

---

## Workstream 1 — RDS renderer template (BLOCKING)

**Problem.** `RdsRenderer.Render()` looks for
`StingTools/Docs/_template_sources/healthcare_rds.docx`; only
`healthcare_rds_README.md` exists there. Every Room Data Sheet issue therefore
returns `null` ("template missing"), which silently breaks
`IssueRoomDataSheetCommand`, `BatchIssueRoomDataSheetsCommand`, and the
`RdsIssue`, `HealthcareCommissioning`, and `HTM-04-01-Annual` workflows. The rest
of the chain is real and working: `RdsContextBuilder` populates 41 tokens + 4
loops from `StingTools/Data/HEALTHCARE_RDS_FIELDMAP.json`, and
`MiniWordAdapter.Render` genuinely writes `.docx` via MiniWord
(`MiniWord.SaveAsByTemplate`).

**Task.** Produce a real `healthcare_rds.docx` MiniWord template at
`StingTools/Docs/_template_sources/healthcare_rds.docx` that honours the token
contract in `healthcare_rds_README.md` and the field map in
`HEALTHCARE_RDS_FIELDMAP.json` exactly (all `{{token}}` names, all 4 loop tables
`services` / `equipment` / `finishes` / `signatures`, and any `{{#if}}`
conditionals the adapter's `PreProcess` supports).

**Most-sustainable guidance.** A hand-crafted opaque binary is *not* the flexible
option — it can't be diffed or regenerated. Prefer a **reproducible generator**:
add a small script/tool (e.g. Python `python-docx`, or a .NET
`DocumentFormat.OpenXml` one-shot under `tools/`) that emits the `.docx` from the
field map, so the template can be rebuilt when tokens change. Commit both the
generator **and** the generated `.docx`. Verify the output actually opens and that
its `{{tokens}}` match `HEALTHCARE_RDS_FIELDMAP.json` (diff the token set
programmatically — fail loudly if any token in the map is missing from the
template or vice-versa). Match the visual/structural conventions of the existing
16 templates in `_template_sources/` (banded header, footer PAGE/NUMPAGES, loop
tables).

**Acceptance.** `RdsRenderer.Render(doc, room)` would return a written path (not
null) given a template present on disk; token/loop coverage verified against the
field map; generator is re-runnable.

## Workstream 2 — PenetrationSignoffs creating migration (BLOCKING)

**Problem.** `PenetrationSignoff` has an entity, a registered `DbSet`
(`PlanscapeDbContext.cs`), full model config, all 4 controller endpoints, and a
snapshot entry — but **no `CreateTable` migration**. Worse:
`Planscape.Server/src/Planscape.Infrastructure/Data/Migrations/20260601000000_CrossHostIdentityFields.cs`
does `AddColumn`/`CreateIndex` **on `PenetrationSignoffs`** (line ~45) and its own
header comment (line 23) admits the table "still lacks a creating migration." On a
clean `dotnet ef database update`, the `20260601` migration fails because the
table does not exist. It only survives today via any `EnsureCreated()` fallback.

**Task.** Add a proper EF Core migration that **creates the `PenetrationSignoffs`
table**, ordered **before** `20260601000000_CrossHostIdentityFields` (e.g. a
timestamp such as `20260517000010_CreatePenetrationSignoffs` — pick an id that
sorts after `PenetrationSignoff` first appears in the model and before
`20260601`). Schema must match the entity (`Planscape.Core/Entities/PenetrationSignoff.cs`)
and the `PlanscapeDbContextModelSnapshot` definition exactly: all columns, the
tenant/project FKs with cascade behaviour consistent with sibling healthcare
tables (see `20260515000000_HealthcarePack.cs` as the template), and the idempotency
index on `(ProjectId, ControlNumber, PfvUuid)` plus any indexes the model config
declares. **Do not** duplicate the `ElementIfcGlobalId` column/index that
`20260601` adds — leave that to `20260601`.

**Most-sustainable guidance.** Generate via
`dotnet ef migrations add …` if the tooling runs in this environment (preferred —
keeps the snapshot authoritative). If EF tooling is unavailable, hand-author the
migration in the exact style of `20260515000000_HealthcarePack.cs` **and** confirm
the existing `PlanscapeDbContextModelSnapshot.cs` already contains the table (it
does) so no snapshot edit is needed. Then verify by building `Planscape.Server`
and, if a Postgres is reachable, running `dotnet ef database update` on a scratch
DB from empty and confirming it applies cleanly through `20260601` and beyond.

**Acceptance.** A from-empty migration run creates `PenetrationSignoffs` and then
applies `20260601` without error; `Planscape.Server` builds.

## Workstream 3 — Harden the radiation QE sign-off gate

**Problem.** Radiation shielding outputs can be treated as authoritative with no
Qualified Expert named. `RAD_QE_NAME_TXT` is only a *soft warning* in
`RadShieldValidator` (opt-in via `RequireQeSignoff`), `AdvancedRadShieldValidator`
warns only, and the `RadCalc*` commands
(`StingTools/Commands/Radiation/RadCalcChestRoomCommand.cs`, `RadCalcCtRoomCommand.cs`,
`RadCalcLinacVaultCommand.cs`) don't check QE at all — they show a TaskDialog and
exit with no persistence and no draft marking. This is a safety/liability gap, not
just a feature gap.

**Task.**
1. Make every `RadCalc*` command output explicitly labelled as an **unsigned
   draft** unless `RAD_QE_NAME_TXT` (read from ProjectInformation, or the relevant
   room/barrier element per existing param scoping) is populated — e.g. a
   prominent "DRAFT — NOT FOR CONSTRUCTION — no Qualified Expert on record" banner
   in the TaskDialog and log line.
2. If/when these commands persist any result to BIM (see item 3 below), **refuse
   to write authoritative values** (or write them flagged as draft) while QE is
   empty. Never auto-mark anything "verified/approved."
3. Provide an optional write-back path so a calc result can be stamped onto the
   barrier/room element (e.g. `RAD_LEAD_MM_NR` and companions) **with an audit
   trail** — but gated on QE being present, and stamped with a draft/approved flag.
   If write-back is out of scope to do safely now, at minimum implement the draft
   labelling (1) and leave a ROADMAP item for persistence.

**Most-sustainable guidance.** Centralise the QE gate rather than copy-pasting the
check into three commands — add a single helper (e.g. a static
`RadiationSignoffGate.IsSigned(doc, element)` / `.DraftBanner(...)` next to the
radiation core) and call it from all `RadCalc*` commands and both radiation
validators, so the policy has one home. Make the "require QE" behaviour a
project-level setting (reuse the existing `HcOptions`/panel toggle pattern and/or
a `PRJ_ORG_HEALTH_*` parameter) so a project can tighten it to *blocking* without
a code change. Keep the calculators' own disclaimers intact.

**Acceptance.** No `RadCalc*` path presents a number as authoritative without a
named QE; the gate logic exists in exactly one place and is reused; behaviour is
configurable per project.

## Workstream 4 — Maintainability: AcousticValidator + tracked deferrals + doc hygiene

**4a. Refactor `AcousticValidator`.**
`StingTools/Core/Validation/Healthcare/AcousticValidator.cs` hardcodes NR and RT60
targets as dictionary literals, unlike its siblings (e.g.
`PressureRegimeValidator` calls `HTMStandards.GetDesignDeltaPa(...)`). Move the
NR/RT60 targets into the standards library (`StingTools.Standards/HTM/HTMStandards.cs`,
HTM 08-01) exposed via lookup methods (e.g. `GetNrTarget(roomClass)`,
`GetRt60Target(roomClass)`), and have the validator call them. Preserve current
values exactly during the move (no behaviour change) and keep a safe fallback for
unknown room classes. This keeps threshold governance in one place — the flexible,
sustainable pattern the rest of the pack already uses.

**4b. File tracked deferrals in `docs/ROADMAP.md`** so their partial/stub status is
explicit rather than discovered later:
- `RadCalcLinacVaultCommand` is a first-pass NCRP-151 estimate (no occupancy/use/
  distance factors, maze = 40% rule-of-thumb, neutron narrative-only) — flag as
  indicative-only, full calc belongs with the QE.
- `AdvancedRadShield` PET/SPECT/Brachy use single-TVL constants (no build-up/
  scatter); NCRP-147 Archer coefficients are approximate digitisations.
- `AdjacencyValidator` uses centroid distance as a Phase-H-10 placeholder for the
  planned door-graph BFS (can false-flag corridor-connected rooms).
- Twin `BacnetReadback`/`OpcUaReadback` return empty — live BMS read-back is an
  FM/commissioning add-on (hooks only).
- `MgasNetworkAuditCommand` computes diversified zone loads but doesn't display the
  per-gas/per-zone breakdown (minor UX follow-up).

**4c. Fix stale caveats in `CLAUDE.md`.** In the Healthcare Pack "Caveats" list,
**caveat #5 ("EF migration not run yet — `dotnet ef migrations add HealthcarePack`
is required") is now false** — `20260515000000_HealthcarePack.cs` exists and all
four healthcare `DbSet`s are registered. Replace #5 with the real remaining gap
(the `PenetrationSignoffs` creating migration — mark it resolved once Workstream 2
lands). Also reconcile the RDS caveat once Workstream 1 lands (template now ships).
Leaving false caveats erodes trust in the whole list.

**Acceptance.** Acoustic thresholds sourced from `HTMStandards` with no behaviour
change; ROADMAP carries the five deferrals; `CLAUDE.md` caveats reflect reality.

---

## Definition of done

- All four workstreams implemented, each in its own commit on a feature branch
  (not `main`).
- Wherever a build was possible it was run and the result reported; wherever it
  wasn't, the "unverified — no .NET/Revit in sandbox" caveat is stated in the
  commit + CHANGELOG.
- `docs/CHANGELOG.md` has a new phase block; `docs/ROADMAP.md` has the deferrals;
  `CLAUDE.md` caveats corrected.
- A short final summary listing what changed, what was verified vs unverified, and
  any residual follow-ups.
