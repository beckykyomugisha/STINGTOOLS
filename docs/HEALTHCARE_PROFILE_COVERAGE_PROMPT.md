# Healthcare Pack — Profile Coverage + Doc Pointer (Autonomous Agent Prompt)

You are an autonomous engineering agent in the **StingTools** monorepo. Execute
both workstreams below **without stopping to ask**. Small, surgical scope — do not
expand it. Pick the most sustainable option (match the existing file's exact
format; one source of truth). Read every cited file before editing.

These are the two remaining findings from a verified hidden-gap review; the new
code around them is already correct — do not touch anything outside these two
items, and respect the DO-NOT-TOUCH note.

## Ground rules

- **Do not ask for confirmation.** Work to completion.
- **Branch / worktree.** Continue on the existing worktree
  `C:\Dev\STINGTOOLS-hc-fixes`, branch `claude/healthcare-gap-fixes` — confirm with
  `git -C C:\Dev\STINGTOOLS-hc-fixes rev-parse --abbrev-ref HEAD`. Never touch the
  shared `C:\Dev\STINGTOOLS` checkout; never commit to `main`. One logical commit
  (both items are small — a single commit is fine). Commit message imperative,
  ending with the repo's `Co-Authored-By` trailer.
- **Data-only + one doc line** — no plugin build strictly required, but if you
  touch any `.cs` build `StingTools` and report the result. Validate the profiles
  JSON parses after editing.
- Leave the branch for review — no merge, no PR. Finish with a one-paragraph
  summary of what changed.

---

## WS-1 — Add the two newest validators to the pack profiles

**Verified gap.** `RoomClassCodeValidator` and `PharmacyRecertValidator` are in the
gate's hardcoded `_allValidators` set (`Core/Validation/Healthcare/HealthcareValidatorGate.cs`)
and in `RunAllHealthcareValidators`, so under the **FULL** profile they run. But
they were never added to `StingTools/Data/HEALTHCARE_PACK_PROFILES.json`, so
`HealthcareValidatorGate.AllowedValidators` silently excludes them under the five
named sub-profiles (**ACUTE / COMMUNITY / DENTAL / IMAGING-ONLY / MENTAL-HEALTH**).
Result: ACUTE/COMMUNITY facilities lose USP-797/800 recert checking, and every
sub-profile loses room-class drift detection — with no error.

**Task.**
1. **First read `HEALTHCARE_PACK_PROFILES.json`** and the gate to confirm the EXACT
   string form each profile's validator list uses, and that it matches the strings
   `AllowedValidators` compares against (`validator.Name`, i.e. the full class-name
   form like `"PharmacyRecertValidator"` / `"RoomClassCodeValidator"` — verify, do
   not assume). Match that exact format and casing.
2. **Add `RoomClassCodeValidator` to ALL five sub-profiles.** It is facility-
   agnostic data hygiene (flags mis-typed `CLN_ROOM_CLASS_TXT` regardless of
   building type), so it should run everywhere, not just FULL.
3. **Add `PharmacyRecertValidator` to the profiles that can contain a pharmacy
   cleanroom: ACUTE and COMMUNITY.** Do NOT add it to DENTAL, IMAGING-ONLY, or
   MENTAL-HEALTH — those have no USP-797/800 cleanroom, so excluding it there is
   correct (don't create noise). If the existing profile semantics clearly argue a
   different placement, follow the data's own logic and note it.
4. Do not alter the FULL profile (it already includes everything via the gate's
   `_allValidators` / "all" handling) unless FULL is represented as an explicit
   list in the JSON that also needs the two names — check and match reality.
5. Validate the JSON parses.

**Acceptance.** Under ACUTE/COMMUNITY the recert + room-class validators run; under
all five sub-profiles the room-class validator runs; DENTAL/IMAGING-ONLY/MENTAL-
HEALTH still (correctly) skip recert; FULL unchanged; JSON valid.

## WS-2 — Bump the stale CLAUDE.md phase pointer

**Verified.** `CLAUDE.md` line ~19 says "The codebase is currently at **Phase
196**", but `docs/CHANGELOG.md` now has Phase 197 (accuracy remediation) and Phase
198 (completeness remediation). No collision — the pointer just wasn't bumped.

**Task.** Update the "currently at **Phase N**" line in `CLAUDE.md` to **198** (the
highest completed phase in the CHANGELOG on this branch — verify it is 198 and use
the true maximum). Do not touch anything else in that paragraph.

**Acceptance.** The pointer equals the highest CHANGELOG phase; no other edit.

---

## DO-NOT-TOUCH

- **Water-flush migration `maxLength` vs model `text`.** An adversarial review
  flagged the `20260627000000_HealthcareWaterLog.cs` migration for specifying
  `maxLength` on string columns while the model/snapshot uses `text`. This is NOT a
  defect: these healthcare migrations are documentation-DDL that never executes
  (schema is built from the model via `CreateTables()`), so the running column is
  `text` and there is no real DB divergence — and it matches the
  `HealthcarePressureLog` sibling exactly. Leave it as-is; "fixing" it would only
  diverge from the sibling pattern.
- Everything else in the recent healthcare work (regional HTM threading, recert
  validator logic/registration, SignalR broadcasts, offline queue, water-flush
  round-trip, room-class canonicalisation, QE gate, NCRP-147/diversity math) was
  verified correct — do not modify.

## Definition of done

- Both new validators present in the correct profile lists; CLAUDE.md pointer at
  198; JSON valid; DO-NOT-TOUCH respected.
- One commit on `claude/healthcare-gap-fixes`; branch left for review.
- Summary: which profiles gained which validator, and the phase-pointer change.
