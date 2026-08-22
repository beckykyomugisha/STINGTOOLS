# Healthcare Pack — Accuracy Remediation (Autonomous Agent Prompt)

You are an autonomous engineering agent in the **StingTools** monorepo. Execute
every workstream below **end to end, without stopping to ask**. Make the calls
yourself; at each choice pick the **most flexible, sustainable, data-driven
option** — one canonical source of truth, reused everywhere, changeable without a
recompile, matching patterns already in the repo. Do not gold-plate; fix exactly
what is listed, well.

These findings come from a verified accuracy audit of the healthcare pack. Each
was checked against the actual code — the false positives are called out
explicitly at the end so you **do not** chase them. Read every cited file before
editing it.

## Ground rules

- **Do not ask for confirmation.** Work autonomously to completion.
- **Branch / worktree.** This work continues the healthcare gap-fix line. Prefer
  the existing worktree at `C:\Dev\STINGTOOLS-hc-fixes` on branch
  `claude/healthcare-gap-fixes`; confirm with `git -C C:\Dev\STINGTOOLS-hc-fixes
  rev-parse --abbrev-ref HEAD` first. If it is gone, create a fresh worktree off
  the latest `claude/pm-complete` (`git worktree add ../STINGTOOLS-hc-acc -b
  claude/healthcare-accuracy`). Do ALL work in the worktree. Never touch the
  shared `C:\Dev\STINGTOOLS` checkout; never commit to `main`.
- **Build.** This machine can build the Revit plugin (Revit 2025/2026 + .NET SDK
  present — do NOT reflexively apply the "no build in sandbox" caveat). Build
  `StingTools` after code changes and report `0 errors` or the actual errors.
  `Planscape.sln` is unaffected by this work.
- **Conventions.** Follow `CLAUDE.md` → Conventions: read before edit, targeted
  edits, `StingLog` not silent catches, one logical change per commit, commit
  messages in imperative mood ending with the repo's `Co-Authored-By` trailer.
- **Docs.** Append a `#### Completed (Phase N — …)` block to `docs/CHANGELOG.md`
  (use the next free phase number — check the file, do not reuse an existing one)
  and update `docs/ROADMAP.md` where noted. Correct `CLAUDE.md` only where a
  caveat becomes false.
- Leave the branch for review — **no merge, no PR**. Finish with a summary:
  files changed, the canonical enum decision, build status, verified vs deferred.

---

## WS-1 — RDS noise-field typo (BLOCKING, trivial)

**Verified bug.** `StingTools/Data/HEALTHCARE_RDS_FIELDMAP.json` line ~25 maps
`"room.noise.nr": "CLN_ROOM_NOISE_NR_NR"`, but that parameter does not exist. The
registered parameter is `CLN_NOISE_NR_NR` (see `MR_PARAMETERS.txt` ~line 2804 —
no `ROOM_` segment). Result: the "Noise Rating NR" field is blank in every Room
Data Sheet, silently.

**Task.** Change the field-map value to `CLN_NOISE_NR_NR`. Then re-run the RDS
template generator so the generated `.docx` stays in lockstep with the field map:
`python tools/build_healthcare_rds_docx.py` (it self-verifies token/loop coverage
and exits non-zero on drift). Confirm the token set still matches.

**Acceptance.** Field map references only registered parameters; generator
self-test passes; `room.noise.nr` now resolves to `CLN_NOISE_NR_NR`.

## WS-2 — Canonical room-class vocabulary (SYSTEMIC — the main fix)

**Verified problem.** There is no single canonical value set for
`CLN_ROOM_CLASS_TXT`. Producers and consumers use different spellings for the same
room, so rooms silently fall through lookups:

| Room | `HbnRoomAutoPopulatorCommand` writes/keys | `HTMStandards` keys | Validators expect |
|---|---|---|---|
| Ward | `WARD` | `WARD-INPT` | — |
| Protective env. | `PE_ROOM` | `PE-PROT` | — |
| CT | `CT` | (none) | `IMG-CT` (StructuralLoadValidator) |
| MRI | `MRI` | (none) | `IMG-MRI` |
| Cath lab | (none) | (none) | `CATHLAB` (StructuralLoad, EesBranch) |

Consequence: a room tagged `IMG-CT` is matched by `StructuralLoadValidator` but
not by `HbnRoomAutoPopulatorCommand` (keys on `CT`), so its ACH / pressure design
params never populate → downstream pressure/ACH/acoustic validators silently skip
it. The full fragmentation matrix spans `HTMStandards`, `ASHRAE170Standards`,
`FGIStandards`, `USPStandards`, `IhfgStandards`, the validators
(`PressureRegimeValidator`, `AcousticValidator`, `StructuralLoadValidator`,
`WasteFlowValidator`, `AntiLigatureValidator`, `AdvancedRadShieldValidator`,
`EesBranchValidator`, `EndoscopeTraceValidator`, `RtlsCoverageValidator`), the
specialist audit commands under `Commands/Healthcare/Specialist/`,
`Core/Adjacency/CleanDirtyFlowSolver.cs`, and the data files
`HEALTHCARE_ACOUSTIC_NR_TARGETS.csv` / `HEALTHCARE_ADJACENCY_HBN.csv`.

**Most-sustainable approach (do this, not per-file patching):**

1. **Establish ONE canonical code list.** Create a single source of truth for the
   `CLN_ROOM_CLASS_TXT` vocabulary — e.g. `StingTools.Standards/RoomClassCodes.cs`
   (a static registry) plus a data companion
   `StingTools/Data/HEALTHCARE_ROOM_CLASSES.json` so the list is editable without
   a recompile (mirror how other healthcare data packs load: corporate baseline +
   optional `<project>/_BIM_COORD/…` override). Each entry carries: canonical code,
   human label, discipline/department, and cross-reference fields (HTM / ASHRAE /
   FGI / iHFG names) so existing standards tables can be reached by mapping, not
   by renaming their keys. **Decide the canonical spelling once** and document the
   rule (recommended: the codes the validators + RDS + tag config already imply —
   hyphenated `IMG-CT`, `WARD-INPT`, `PE-PROT`, `OR-ULTRA`, etc. — but you choose;
   just make everything agree with the choice).

2. **Add a resolver** so lookups never depend on exact spelling: a
   `RoomClassCodes.Canonicalize(raw)` that maps any known alias (`WARD`,
   `PE_ROOM`, `CT`, `CT_SCANNER_ROOM`, …) to the canonical code. Route the
   standards-table lookups and validators through it, OR re-key/alias the tables —
   whichever keeps a single mapping rather than N ad-hoc dictionaries.

3. **Align every producer and consumer** to the canonical set via the resolver:
   - `HbnRoomAutoPopulatorCommand` — its `RoomDesignTable` keys and any codes it
     writes.
   - All validators listed above that compare `CLN_ROOM_CLASS_TXT` (or read it via
     `GetRoomClassCached`).
   - `CleanDirtyFlowSolver` and the specialist audit commands.
   - Data files `HEALTHCARE_ACOUSTIC_NR_TARGETS.csv`, `HEALTHCARE_ADJACENCY_HBN.csv`
     (and any room-class column in tag-config CSVs).
   - `HEALTHCARE_RDS_FIELDMAP.json` room-class token if applicable.
   Preserve every existing threshold/value during the move — this is a
   spelling/lookup unification, **not** a re-tuning. Codes with genuinely no
   backing anywhere (`CATHLAB`, `IMG-BRACHY`, `ENDO-DECON`, `HSDU-S`, `IMG-LIN`)
   must either get a canonical entry (with its cross-refs, even if a standards
   design row is a future item) or be reconciled to an existing code — do not
   leave a validator comparing against a code that the canonical list omits.

4. **Add a load-time / on-demand validator** `RoomClassCodeValidator` (wired into
   the healthcare validator set + gate, same pattern as the others) that flags any
   `CLN_ROOM_CLASS_TXT` value not in the canonical list, so future drift surfaces
   as a warning instead of a silent skip. Gate it through
   `HealthcareValidatorGate` and register it in `RunAll/RunSelected` +
   `HealthcareValidatorCommands` + `WorkflowEngine.ResolveCommand` +
   `StingCommandHandler` like its siblings.

**Acceptance.** One canonical room-class list exists; every producer, consumer,
and data file resolves through it (no room type has two live spellings across
producer↔consumer); thresholds unchanged; unknown codes are flagged, not skipped;
`StingTools` builds clean.

## WS-3 — TEXT-typed numeric params: guard the silent-parse-fail (LOW/MEDIUM)

**Verified nuance.** `HealthcareValidatorBase.GetParamDouble` (line ~51) already
parses TEXT via `double.TryParse(AsString())`, so the earlier "always null" claim
is **false** for clean numeric strings. The real residual risk: several params are
registered TEXT — `HVC_AIR_CHANGES_PER_HR`, `PER_ACOUSTICS_BACKGROUND_NOISE_DB`,
`PER_ACOUSTICS_RT60_S`, `PLM_HOTWTR_TEMP_C` — so a value like `"12 ACH"` or
`"45 dB"` fails the parse and the check silently skips.

**Task (sustainable, minimal):** Do NOT re-type the shared parameters (schema
churn, migration risk). Instead harden the reader: make `GetParamDouble` tolerant
of a trailing unit/annotation — strip to the leading numeric token before
`TryParse` (e.g. parse the first number in the string) using
`InvariantCulture`. Add a `StingLog.Warn` when a non-empty TEXT value fails to
yield a number, so a malformed cell is visible instead of silent. Keep behaviour
identical for clean numeric strings. Add/adjust a tiny unit-style check only if a
test harness already exists in the repo; otherwise verify by reasoning + build.

**Acceptance.** `"12"`, `"12 ACH"`, `"0.6 s"`, `"45 dB"` all parse to their number;
empty stays null; malformed non-empty logs a warning; no behaviour change for
existing clean values.

## WS-4 — RadShield non-conservative default distance (LOW, safety-relevant)

**Verified.** `RadShieldValidator` uses a hardcoded 2.0 m default barrier distance
when none is known. Because required shielding scales with `d²` in `B =
P·d²/(W·U·T)`, an optimistic distance under-estimates required lead → a too-thin
barrier can pass the audit. It is audit-only and now QE-gated, but the default
biases the wrong way.

**Task.** Make the default **conservative** (bias toward MORE shielding when
distance is unknown) — e.g. a smaller default distance (such as 1.0 m) or an
explicit "distance unknown" WARNING finding attached to the result, so the audit
never silently blesses a barrier computed from an optimistic guess. Make the
default a named constant / project-tunable value rather than a bare literal, and
note the reasoning in a comment. Record any remaining depth work (true 3D
barrier-distance geometry) in `docs/ROADMAP.md` (there is already an HC-DEF item
for radiation write-back — add alongside).

**Acceptance.** Unknown-distance path no longer biases toward under-shielding; the
default is a documented constant; a warning is surfaced when distance is assumed.

## WS-5 — Medical-gas diversity fallback visibility (LOW, safe already)

**Verified, low risk.** `MgasFlowSolver` applies diversity correctly (NFPA 99
§5.1.13, multiplier after per-zone-per-gas summation). Gases `N2`, `CO2`, `HE`,
`DENT` have no entry in `NFPA99Standards` diversity and fall back to `1.0` — which
over-sizes (safe), but silently.

**Task.** No math change. Just make the fallback **visible**: when a gas has no
tabulated diversity and defaults to 1.0, `StingLog.Info`/`Warn` it and surface it
in the audit output (the `MgasNetworkAuditCommand` breakdown, which currently
computes zone loads but does not display them — show the per-gas/per-zone
diversified load table while you are there). Optionally add the missing gases'
diversity factors to `NFPA99Standards` **only** if you can cite HTM 02-01 Table 8 /
NFPA 99 Table 5.1.13.3.4 values with confidence; otherwise leave 1.0 and log it.

**Acceptance.** No silent diversity=1.0; audit shows the diversified per-gas/zone
loads.

---

## DO-NOT-TOUCH — verified false positives (do not "fix" these)

An audit flagged these; each was checked and is NOT a bug. Changing them would
introduce regressions:

1. **`PressureRegimeValidator` "PE" vs "PE-PROT".** Line ~88 reads
   `CLN_INFECT_CLASS_TXT` (infection class), a **different enum** from room class.
   `"PE"` (Protective Environment) and `"AIIR"` are the correct infection-class
   codes here. Leave as-is. (If WS-2's canonical work also standardises the
   *infection-class* vocabulary, do that as a separate, clearly-scoped step — but
   do not conflate `"PE"` with the room-class `"PE-PROT"`.)
2. **"TEXT params always return null in `GetParamDouble`."** False — the reader
   already `TryParse`s TEXT. WS-3 only hardens the annotated-string edge; it is not
   a wholesale "these are broken" fix.
3. **NCRP 147 Archer math / `MgasFlowSolver` diversity math / `AdvancedRadShield`
   TVL constants / `AdjacencyValidator` ft→m (`* 0.3048`).** All verified correct.
   Do not "correct" them.

## Definition of done

- WS-1..WS-5 implemented, each its own commit; false positives untouched.
- `StingTools` build run and result reported (expect 0 errors).
- One canonical room-class list, reached by every producer/consumer/data file.
- `docs/CHANGELOG.md` phase block added (next free number); `docs/ROADMAP.md`
  updated (WS-4 geometry, any deferred canonical-standards rows); `CLAUDE.md`
  caveats corrected only where now false.
- Final summary: files changed, canonical spelling chosen + resolver location,
  build status, and anything deliberately deferred.
