# WORKLOG — Tagging review-and-fix loop

Standing task (2026-10-01): continuous review-and-fix of everything tagging touches — tokens, commands,
tag families, registry/bindings, scope-box inputs, project config, the annotation pass, tests, docs.
Rules: one logical change per commit with its TAGACC/ROADMAP id; merge only on green build + tests +
gates + CI; never weaken a test; never renumber/overwrite a real model.

## Resume here

Session `tag-families-setup-976ed0` closed 2026-10-04 with nothing open: every branch it made is merged
(through #1050) and the TAGACC-25 / TAGFAM-7 / -9 / -10 decisions are implemented. Start a new pass from
`origin/main` with:

1. **TAGFAM-9 follow-up writers** — colour schemes, *Switch Tag Style by Discipline*, the scale tiers
   (`ScaleTierCommands`) and `TokenProfileApplier` still set only the `TAG_*_BOOL` switches, never
   `TAG_STYLE_CODE_TXT`, so a new family (no switches) gets no style from them (ROADMAP TAGFAM-9).
2. **DRAW-9 remaining provenance stamp sites** — AnnotationRunner grid/level chains, match-line frames,
   `MatchLineEngine` captions, `MEPDimensioner` (ROADMAP DRAW-9).
3. **Spot-slope smoke test** — `SpotSlope_WithNoSlopeType_Blocks_PlacesNothing` is inconclusive on every run:
   the metric template refuses deletion of its spot-slope type. Needs a fixture that builds a project with
   no slope type, not a code fix.
4. Next research seams (config that loads but does nothing): `CATEGORY_VISUAL_POLICY`,
   `CATEGORY_TOKEN_OVERRIDES`, `SLA_THRESHOLDS`, `SHEET_MARGINS` POCOs without `JsonProperty` names; then the
   AnnotationRunner auto-load enhancement.
5. Re-time TAGFAM-9 on a quiet machine before quoting seconds per family.

## State (2026-10-04, pass 4 — session close)

- Merged this session: #1019, #1020, #1022–#1039, #1041–#1047, #1049, #1050.
- **Full tag test on main `c2c79e054`:** `StingTools.Tags.Tests` 5,626 passed / 0 failed; in-Revit smoke
  (`tools/run_revit_smoke.ps1`, Revit 2025) 11/13 — all 4 tagging-accuracy tests pass, harness, invert and
  flow-arrow pass; the 2 failures are the one spot-slope case above (reported twice), inconclusive setup.
- Headless Revit (`pyrevit run`, scripts in `tools/pyrevit/headless/`) replaces computer-use for family work.
- Live Revit build `C:\Dev\STING_KUT_LIVE` belongs to the KUT/ACC session; the smoke harness moves its
  add-in manifest aside for the run and restores it.

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

1. ~~Size copies + door boxes~~ **Done 2026-10-01 headlessly** (`pyrevit run tools/pyrevit/headless/run_size_copies.py
   --revit=2025 --purge`, #1041). Left: one look at a Room Finish and a Fire Compartment tag of each size in a project —
   the headless export drew no room tags at all (stock tag included).
2. **TAGFAM-2 second run** — *Create Tag Fams* in a throwaway project on the KUT build (now main through #1028).
3. **TAGACC-12** — Part A **ran 2026-10-01** on origin/main `a7b7cff50` (`tools/run_revit_smoke.ps1`, add-in manifest
   moved aside for the run and restored): **all 4 tagging-accuracy tests pass**, harness integrity passes. 4 annotation
   smoke tests did not; the two real ones (drainage invert, wall-length) were fixed in #1043 (DRAW-9) and the re-run on
   `f39abffa1` passed **11/13** — only the 2 spot-slope tests remain, inconclusive because the metric template's slope type
   cannot be applied / deleted. Part B (two users on a central model) still needs two people. Re-run 2026-10-04 on `c2c79e054`: same 11/13.
4. ~~TAGFAM-6 timing~~ **Measured 2026-10-01**: 124 s per door tag, ~0.78 s per parameter in Revit's `AddParameter`;
   see TAGFAM-6 / TAGFAM-9.

## Decisions

- *Style switches (TAGFAM-9)* — `tools/pyrevit/headless/audit_style_switches.py` (2026-10-01): all 211 shipped tag
  families carry the 128 `TAG_*_BOOL` switches and in **none** is any switch associated with a family element; they cannot
  change what a tag shows. New builds stop adding them (architect decision, delegated by Sting).

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
| F7 | Low | families | three tie-in tags built as `… Tag` (doubled) — the tier map and config declare the name without it | **Fixed** — TAGFAM-7 DONE: renamed to the declared names, legacy alias table, in-place rename on load, gated; the same drift on Specialty Equipment fixed as TAGFAM-10 |
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
