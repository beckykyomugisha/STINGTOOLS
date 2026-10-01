# WORKLOG — Tagging review-and-fix loop

Standing task (2026-10-01): continuous review-and-fix of everything tagging touches — tokens, commands,
tag families, registry/bindings, scope-box inputs, project config, the annotation pass, tests, docs.
Rules: one logical change per commit with its TAGACC/ROADMAP id; merge only on green build + tests +
gates + CI; never weaken a test; never renumber/overwrite a real model.

## Resume here

1. Merge, in this order, when CI is green: #1030 (TAGACC-20) → #1031 (protocol; cites #1030's tests)
   → #1032 (TAGACC-21; merge `origin/main` in first — its ROADMAP row sits beside TAGACC-20's, keep
   both, numeric order). Then edit the protocol's "Covered elsewhere" TAGACC-11 row: covered by
   `ProximityRuleTests` (#1032).
2. **NEEDS REVIT CHECK** below — the KUT build was redeployed by another session at `c73ded891`
   (includes #1019/#1021, not #1020 onward), so TAGFAM-2's second run can be done on it; #1 (pyRevit
   Size Copies) unblocks the three remaining specialist `.rfa` files; #4 times TAGFAM-6. After #1, re-stamp
   the manifest: `python tools/restamp_content_manifest.py --apply StingTools/Data/TagFamilies`
   (TAGFAM-8 gate fails until then — that is intended).
3. Next research: an enhancement, not a bug — `AnnotationRunner` warns and falls back when a drawing
   type names a tag family that is not loaded (now likely for the TAGFAM-3 tags); offer to load it from
   `Data/TagFamilies`. Then TAGFAM-7 (decide on the doubled-Tag tie-in names).

## State (2026-10-01, pass 2)

- Merged: #1019, #1020, #1022 TAGACC-18, #1023 TAGACC-19, #1024/#1025 worklog + TAGFAM-7 log,
  #1026 TAGFAM-5, #1027 TAGFAM-6, #1028 TAGFAM-8, #1029 TAGFAM-6 follow-up.
- Open: #1030 TAGACC-20, #1031 protocol, #1032 TAGACC-21.
- Spun off (outside tagging): `cost_rates_5d.csv` fails `tools/validate_data_schemas.py` on main (a
  `PROD` column), and no CI job runs that validator — offered as a separate task.
- Live Revit build `C:\Dev\STING_KUT_LIVE` was redeployed by the ACC-review session at `c73ded891`
  (ACC integration + main through #1019/#1021). It does not have #1020 onward.
- All work happens in worktree `.claude/worktrees/tag-families-setup-976ed0` (the session hook refuses
  writes to other worktrees); switch branches there, one branch per change from `origin/main`.

## Hand configuration done in the Revit UI (reproducible record)

All four specialist families started from the shells Create Tag Fams builds (Door Tags / Room Tags category,
two size types `2.5_NOM_BLACK_Open30_T2` / `3.5_NOM_BLACK_Open30_T2`, `TXT_2_5` / `TXT_3_5` switches set per
type, `_build\<family>.params.txt`). For each family:

1. *Create › Label*, click the origin (Revit often needs two clicks before *Edit Label* opens).
2. *Edit Label*: add the built-in (Mark / Number) with →. Then *Add parameter › Select… › Edit… › Browse*,
   pick the family's `.params.txt`, OK; for each parameter pick its group and parameter, OK, OK. Add the
   direct rows with →; add calculated values with *fx* (Name, **Type = Text**, Formula). New rows insert
   after the selected row; reorder with the ↑/↓ buttons. Set Spaces (0) before ticking Break; prefixes and
   suffixes per `docs/SPECIALIST_TAG_BUILD_SHEET.md`.
3. Label *Properties*: **Horizontal Align = Left, Vertical Align = Middle** (user's ISO rule).
4. *Edit Type › Duplicate* `2.5mm`, Text Size 2.5 mm, Background Transparent.
5. Fire Compartment only (so far): *Visible* → associate `TXT_2_5`; *Copy*, *Paste › Aligned to Same Place*;
   on the copy *Edit Type › Duplicate* `3.5mm` (Text Size 3.5 mm) and *Visible* → `TXT_3_5`.
6. Restore Revit's shared parameter file to `C:\Dev\STINGTOOLS\CompiledPlugin\data\MR_PARAMETERS.txt`
   (*Manage › Shared Parameters › Browse*) — done 2026-10-01.

Automation: steps 5 and the door box are automated by
`tools/pyrevit/STINGFamilyTools.extension/.../Size Copies.pushbutton/script.py` (Revit API: copy label,
duplicate `TextElementType`, `AssociateElementParameterToFamilyParameter(IS_VISIBLE_PARAM)`,
`NewDetailCurve` on a *Tag Box* subcategory). Steps 1-2 (label rows) cannot be automated — the Revit API
cannot author label rows.

Pitfalls met: a calculated value cannot be named *Operation* on a door tag (built-in name); typing in a
Revit canvas fires shortcuts (`TX` = Text); Revit's Open/Browse dialogs refuse typed paths from
computer-use (copy files to `Documents\STING_TAG_BUILD` and double-click); `open_application` spawns a
second Revit; the Claude desktop window can sit over Revit and swallow clicks.

## NEEDS REVIT CHECK

1. **Size copies + door boxes** — open `STING - Fire Door Tag.rfa`, `STING - Accessible Door Tag.rfa`,
   `STING - Room Finish Tag.rfa` (and Fire Compartment, harmless — it is idempotent); pyRevit tab › Reload;
   *STING Families › Tag Labels › Size Copies*. Read the output window: each family should report
   "3.5 mm copy created", "-> TXT_3_5", and for doors "box 2.5 mm drawn" / "box 3.5 mm drawn", then
   "saved". In *Family Types*, switch `2.5_…` / `3.5_…`: only that size's label (and box) shows.
2. **TAGFAM-2 second run** (needs the redeployed KUT build): *Create Tag Fams* in a throwaway project —
   families load (hub button now `Manual`), Temporary Structure / MEP Ancillary Framing tags are accepted
   or reported.
3. **TAGACC-12** — `pwsh tools/run_revit_smoke.ps1`, then protocol Part B with two users.
4. **TAGFAM-6 timing** — on the next *Create Tag Fams* run, note seconds per family from the STING log
   (`AddSharedParameters: added …` lines are one per family) and compare with ~300 s on 2026-09-30.

## Decisions

- *Size copies via pyRevit now, plugin command later* — the live add-in slot belongs to the KUT build and
  this agent cannot redeploy it; pyRevit runs against the open family with no deploy. The same logic goes
  into the plugin so it ships.
- *Left-aligned labels* — user instruction (ISO); recorded in the build sheet and memory.
- *Proximity is Medium, not Low* (TAGACC-18) — the value was copied from a tagged neighbour, so it is
  plausible but not detected; Low is kept for "nothing located it, the policy fallback was written".
- *An unreadable project_config.json is refused, not overwritten* (TAGACC-19) — the old writers would
  have replaced it; now `SaveToFile` logs and returns false so the user's other keys are not lost.
- *Save Config to Project no longer adds LEADER_CLEARANCE_MARGIN_FT when absent* — one writer; the
  reader defaults it to 0.5 and an existing value is kept.

## Findings

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F1 | — | scope boxes | "ZONE never read from scope boxes" | **Stale** — `STING-ZONE::` boxes shipped in `1ace7ed81` (`BuildScopeBoxZoneIndex`, `DetectZoneFromScopeBox`, precedence after room/department) |
| F2 | — | registry | "`STING_SCOPE_BOX_TAG_TXT` not registered" | **Stale** — in `MR_PARAMETERS.txt`, `PARAMETER_REGISTRY.json`, `RESOLVED_BINDINGS.csv` (`<ALL>`) and `ParamRegistry.STING_SCOPE_BOX_TAG` |
| F3 | Med | Token Conf | audit called scope-box zones and proximity fills "Low / fallback"; no reason per element | **TAGACC-18**, PR #1022 |
| F4 | **High** | config | both whole-file writers of `project_config.json` reset every key they did not list (SEQ_*, folder layout, COST_*) — wizard, auto-tagger toggle, Save Config | **TAGACC-19**, PR #1023 |
| F5 | Med | SEQ | multi-user borrow/defer/re-sequence/repair — no Revit run | open — TAGACC-12 protocol + smoke (NEEDS REVIT) |
| F6 | Med | families | text types/sizes consistent; labels' params registered + bound; coverage; orphans | **part done** — manifest coverage + checksums gated (TAGFAM-8); label params gated earlier (`DeclaredTagFamiliesTests`); text types inside `.rfa` need Revit |
| F7 | Low | families | three tie-in tags built as `… Tag` (doubled) — the tier map and config declare the name without it | logged **TAGFAM-7**; no effect today, renaming risky |
| F8 | — | config | do any UI setters change in-memory TagConfig keys that `SaveToFile` does not write? | **Checked** — SEQ setters are the G2 in-memory restore; `AUTO_TAGGER_DISC_FILTER` uses `SetConfigValue`, which a later `SaveToFile` used to wipe — fixed by TAGACC-19 |
| F9 | Med | coverage | which TAGACC fixes have a test or protocol step? 7/8, 11, 13, 14, 16, 17 had none by id | TAGACC-20 (#1030) and -21 (#1032) make 7/8 and 11 executable; #1031 names 2/13/17 in the protocol and says 14/16 call sites are untested |
| F10 | Low | content | four specialist `.rfa` files not in `STING_CONTENT_MANIFEST.json` | **TAGFAM-8** (#1028), gated |
| F11 | Low | perf | four private per-name walks of the shared-parameter file | **TAGFAM-6** (#1027, #1029), gated |
| F12 | — | SEQ | can a stale user lower the shared SEQ counters? | **Checked** — `StingSeqLockStore.Save` max-merges; renumbers leave gaps, never duplicates |
| F13 | — | annotation | missing drawing-type tag family silent? | **Checked** — warned, then falls back to the STING family of the category; auto-load is an enhancement |

Lesson from F1/F2: the standing brief's "known items" were written from memory; check the code
before planning a fix.
