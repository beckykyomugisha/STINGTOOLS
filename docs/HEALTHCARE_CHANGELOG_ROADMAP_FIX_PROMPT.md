# Healthcare Pack — CHANGELOG / ROADMAP Coherence Fix (Autonomous Agent Prompt)

You are an autonomous engineering agent in the **StingTools** monorepo. Execute the
workstreams below **without stopping to ask**. This is a **docs-only** pass — do NOT
touch any code or data files. Small, surgical scope; match the existing document
style exactly. Read the cited files before editing.

A coherence review found the CHANGELOG has one undocumented change plus a subtle
over-claim; the ROADMAP is already clean and is verify-only.

## Ground rules

- **Do not ask for confirmation.** Work to completion.
- **Branch / worktree.** Continue on the existing worktree
  `C:\Dev\STINGTOOLS-hc-fixes`, branch `claude/healthcare-gap-fixes` — confirm with
  `git -C C:\Dev\STINGTOOLS-hc-fixes rev-parse --abbrev-ref HEAD`. Never touch the
  shared `C:\Dev\STINGTOOLS` checkout; never commit to `main`. One commit; message
  imperative, ending with the repo's `Co-Authored-By` trailer.
- **Docs only** — no `.cs`, `.json` data, or migration edits. No build needed; just
  confirm `git diff --stat` shows only `docs/CHANGELOG.md` (and, only if a genuine
  inconsistency is found, `docs/ROADMAP.md`).
- Leave the branch for review — no merge, no PR. Finish with a one-paragraph
  summary.

---

## WS-1 — Document the profile-coverage follow-up in the CHANGELOG

**Verified gap.** Commit `44b888634` ("add new validators to pack sub-profiles;
bump phase pointer") edited `StingTools/Data/HEALTHCARE_PACK_PROFILES.json` and
`CLAUDE.md` but added **no CHANGELOG entry** — the last change on the branch is
unlogged (grep for "sub-profile" in `docs/CHANGELOG.md` returns nothing).

**Task.** Add a short follow-up note to the **Phase 198** block in
`docs/CHANGELOG.md` (append it inside that block — a "Follow-up" paragraph or a
"WS-8" line, matching the block's existing WS-n / prose style). It must record,
factually and briefly:
- `RoomClassCodeValidator` (added Phase 197) and `PharmacyRecertValidator` (added
  Phase 198 WS-5) were present in the gate's `_allValidators` set (so they ran under
  the **FULL** profile) but were **missing from `HEALTHCARE_PACK_PROFILES.json`**, so
  the five named sub-profiles silently excluded them.
- Fix: `RoomClassCodeValidator` added to **all five** sub-profiles (ACUTE,
  COMMUNITY, DENTAL, IMAGING-ONLY, MENTAL-HEALTH) as facility-agnostic room-class
  drift hygiene; `PharmacyRecertValidator` added to **ACUTE + COMMUNITY** only (the
  pharmacy-cleanroom-bearing profiles) — DENTAL/IMAGING-ONLY/MENTAL-HEALTH correctly
  still skip it. **FULL** is unchanged (`["all"]`). `CLAUDE.md` phase pointer bumped
  196 → 198.

## WS-2 — Correct the WS-5 "DONE" over-claim (tie to WS-1)

**Verified.** The Phase 198 block's summary table marks **WS-5** (USP 797/800 recert)
"DONE," but as originally logged the validator only ran under the **FULL** profile —
it did not actually fire under ACUTE/COMMUNITY until the `44b888634` follow-up. The
same applies to the Phase 197 `RoomClassCodeValidator`.

**Task.** Adjust the wording so "DONE" is not misleading — e.g. keep WS-5 "DONE" but
add a brief clause (or footnote) that sub-profile reachability was completed in the
WS-1 follow-up above. Do not overhaul the table; the minimal honest clarification is
enough. Keep the Phase 197 block accurate too if it implies `RoomClassCodeValidator`
was reachable everywhere on landing.

## WS-3 — Verify the ROADMAP (leave clean; do not churn)

The ROADMAP (`docs/ROADMAP.md`) was reviewed and is coherent: `HC-DEF-01..10` are
sequential and non-colliding, `HC-DEF-06` is correctly struck through as
"CLOSED (Phase 197)", each open item accurately states shipped-vs-deferred, and the
orphaned-param list points to its blocking feature (HC-DEF-09/10).

**Task.** Re-verify quickly. **Do NOT edit it unless you find a genuine
inconsistency** (e.g. an item closed by later work still listed as open, an ID
collision, or a claim now false). If you do edit, describe exactly what and why in
your summary. Specifically **do not** renumber `HC-DEF-01b` — the sub-number
intentionally groups the two radiation-distance items.

---

## DO-NOT-TOUCH

- **Pre-existing repeated phase numbers** in `docs/CHANGELOG.md` (e.g. Phase 184
  appearing 10×, 188 5×, the 192B/192E sub-parts) predate this branch and are
  intentional multi-part phase blocks. Do NOT "deduplicate" or renumber them.
- No code, data, migration, or JSON edits — this pass is CHANGELOG (and only-if-
  needed ROADMAP) prose.
- Do not alter the Phase 196/197 block scopes beyond the minimal WS-2 accuracy
  clarification.

## Definition of done

- Phase 198 CHANGELOG block documents the profile-coverage + pointer follow-up; the
  WS-5 "DONE" is honestly qualified; ROADMAP left clean (or a described genuine fix).
- One docs-only commit on `claude/healthcare-gap-fixes`; `git diff --stat` shows only
  doc files. Branch left for review.
- Summary: what was added to the CHANGELOG, the WS-5 wording change, and whether the
  ROADMAP needed anything.
