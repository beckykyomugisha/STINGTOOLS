# WORKLOG — Tagging review-and-fix loop

Standing task (2026-10-01): continuous review-and-fix of everything tagging touches — tokens, commands,
tag families, registry/bindings, scope-box inputs, project config, the annotation pass, tests, docs.
Rules: one logical change per commit with its TAGACC/ROADMAP id; merge only on green build + tests +
gates + CI; never weaken a test; never renumber/overwrite a real model.

## Resume here

1. Merge PR #1022 (TAGACC-18) and PR #1023 (TAGACC-19) when CI is green; #1023 will need main merged
   in (ROADMAP/CHANGELOG rows sit side by side — keep both).
2. **NEEDS REVIT CHECK #1** below (pyRevit Size Copies on Fire Door / Accessible Door / Room Finish),
   then copy the saved `.rfa` files from `Documents\STING_TAG_BUILD` into `StingTools/Data/TagFamilies`.
3. Next findings: F6 (tag-family consistency gate), then F5 (multi-user SEQ protocol), then a
   deeper pass over other whole-file writers of shared JSON (same shape as TAGACC-19).

## State (2026-10-01, pass 1)

- PR #1019 merged; PR #1020 merged (`51b14eb81`) — the four specialist labels are on main.
- PR #1022 open — TAGACC-18 Token Confidence Audit (branch `claude/token-confidence-sources`).
- PR #1023 open — TAGACC-19 config save keeps keys (branch `claude/config-save-keeps-keys`).
- Live Revit build is `C:\Dev\STING_KUT_LIVE` (detached, ACC hardening + an early snapshot of TAGFAM-1). It does
  **not** have #1018 onward. Rebuilding it (merge `origin/main` into `claude/kut-combined-acc-tags`)
  is blocked for this agent by the auto-mode classifier ("Modify Shared Resources"); a person must do it.
  Steps are in the TAGFAM-2 ROADMAP row.
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

Lesson from F1/F2: the standing brief's "known items" were written from memory; check the code
before planning a fix.
