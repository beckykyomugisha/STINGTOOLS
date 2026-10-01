# WORKLOG — Tagging review-and-fix loop

Standing task (2026-10-01): continuous review-and-fix of everything tagging touches — tokens, commands,
tag families, registry/bindings, scope-box inputs, project config, the annotation pass, tests, docs.
Rules: one logical change per commit with its TAGACC/ROADMAP id; merge only on green build + tests +
gates + CI; never weaken a test; never renumber/overwrite a real model.

## Resume here

1. **NEEDS REVIT CHECK #1** below: with Fire Door / Accessible Door / Room Finish open in Revit, run
   pyRevit › Reload, then *STING Families › Tag Labels › Size Copies*. Copy the saved `.rfa` files from
   `Documents\STING_TAG_BUILD` into `StingTools/Data/TagFamilies` and commit.
2. Get PR #1020 (specialist tag labels) green and merged.
3. Work the open list under **Findings** top-down (ZONE scope boxes first).

## State (2026-10-01 03:00)

- PR #1019 merged (config-declared tag families, read-only hub buttons, untaggable categories).
- PR #1020 open — branch `claude/specialist-tag-labels`, worktree `.claude/worktrees/tag-families-setup-976ed0`.
- Live Revit build is `C:\Dev\STING_KUT_LIVE` (detached, ACC hardening + an early snapshot of TAGFAM-1). It does
  **not** have #1018/#1019/#1020. Rebuilding it (merge `origin/main` into `claude/kut-combined-acc-tags`)
  is blocked for this agent by the auto-mode classifier ("Modify Shared Resources"); a person must do it.
  Steps are in the TAGFAM-2 ROADMAP row.

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

## Findings (open)

| # | Sev | Where | Finding | Plan |
|---|---|---|---|---|
| F1 | High | scope boxes | ZONE never read from scope boxes; falls back to Z01 | add `STING-ZONE::` boxes mirroring `STING-LOC::` |
| F2 | High | registry | `STING_SCOPE_BOX_TAG_TXT` not registered → `::<zone>` stamp no-op | register + bind, or remove the stamp |
| F3 | Med | Token Conf | fallback-to-default not reported per element with reason | add report |
| F4 | Med | config | Save Config to Project rewrites file — check every tagging key survives | test round-trip |
| F5 | Med | SEQ | multi-user borrow/defer/re-sequence/repair — no Revit run | protocol + smoke |
| F6 | Med | families | text types/sizes consistent; labels' params registered + bound; coverage; orphans | gate |
