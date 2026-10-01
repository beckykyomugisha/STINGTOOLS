# WORKLOG — Tagging review-and-fix loop

Standing task (2026-10-01): continuous review-and-fix of everything tagging touches — tokens, commands,
tag families, registry/bindings, scope-box inputs, project config, the annotation pass, tests, docs.
Rules: one logical change per commit with its TAGACC/ROADMAP id; merge only on green build + tests +
gates + CI; never weaken a test; never renumber/overwrite a real model.

## Resume here

1. Merge #1038 (TAGACC-26, project_config key registry) when CI is green; merge `origin/main` in first if
   CHANGELOG / ROADMAP conflict (keep both, rows in numeric order).
2. **Decision for Sting — TAGACC-25:** six `DISCIPLINE_PROFILES` settings are parsed and applied by nothing
   (`CollisionMode`, `SeqScheme`, `DefaultZone`, `DefaultLoc`, `SeqIncludeZone`, `SeqPadWidth`). Implement
   (changes token/SEQ building) or remove. Interim warning is live.
3. **NEEDS REVIT CHECK** below — the live KUT build carries main through at least #1028; #1 (pyRevit Size
   Copies) unblocks the last three specialist `.rfa` files (then re-stamp the manifest); TAGACC-12 Part A/B
   can run on the KUT build.
4. Next research seams (same shape as TAGACC-23/24/26 — config that loads but does nothing): other
   `TryDeserialize` sections of `project_config.json` whose POCOs have no `JsonProperty` names
   (`CATEGORY_VISUAL_POLICY`, `CATEGORY_TOKEN_OVERRIDES`, `SLA_THRESHOLDS`, `SHEET_MARGINS`); then the
   AnnotationRunner auto-load enhancement; then TAGFAM-7.

## State (2026-10-01, pass 3)

- Merged today: #1019, #1020, #1022–#1037 (TAGACC-18…25-interim, TAGFAM-5…8, protocol, worklogs).
- Open: #1038 TAGACC-26.
- Spun off (outside tagging): `cost_rates_5d.csv` fails `tools/validate_data_schemas.py` on main, and no CI
  job runs that validator — offered as a separate task.
- Live Revit build `C:\Dev\STING_KUT_LIVE` is maintained by the ACC-review session (redeployed at `57c3b236e`
  with main through #1028; it picks up later PRs as they merge).
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
| F14 | Med | config | Tag Rule Engine / Tag Format replaced an unparseable `project_config.json` with a one-key file | **TAGACC-22** (#1034) |
| F15 | **High** | config | Tag Format saved TAG_FORMAT as PascalCase; loader reads snake_case — never applied, and erased the wizard's section | **TAGACC-23** (#1035) |
| F16 | **High** | SEQ | `SeqPadWidth` only set by the dock panel — project `num_pad` never reached the SEQ; panel pad lost on restart | **TAGACC-24** (#1036) |
| F17 | Med | config | six DISCIPLINE_PROFILES fields parsed, never read | **TAGACC-25** interim (#1037); decision open |
| F18 | Med | config | known-key list called ~70 real keys typos, listed 4 dead keys, case-insensitive vs case-sensitive readers | **TAGACC-26** (#1038) |

Lesson from F1/F2: the standing brief's "known items" were written from memory; check the code
before planning a fix.
