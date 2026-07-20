<!--
  Autonomous-agent runner. Hand this whole file to a fresh Claude Code / terminal
  agent. It finishes the universal-tag pivot: (A) the short+maskable display, (B)
  redundant-code cleanup from the recent Tokens & Depth wiring, (C) the staged
  legacy-tagging cutover, (D) an alignment/consistency sweep. Every task is gated
  on a green build; nothing is deleted without a caller check.
-->

# Universal Tag — Finalize Runner

**Repo:** `C:\Dev\STINGTOOLS` · **Work branch:** create `claude/universal-tag-finalize` off the current `claude/tag-tier-review-94c78a` (do NOT work on `main`; isolate in a git worktree per the repo's worktree convention).
**Build:** `dotnet build StingTools/StingTools.csproj -c Release -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"` — this machine CAN build (net8 = Revit 2025/26). Baseline = **0 errors, 4 pre-existing CS0618 warnings** in `Clash/ClashIssueSyncCommand.cs`. Any NEW warning/error is yours.
**Tests:** `StingTools.Tags.Tests` — baseline 134 pass / 2 pre-existing `CsiMasterFormat` fails (unrelated). Keep that ratio.
**Deploy target:** `C:\Dev\STING_PLACEMENT_GOLD` (the running Revit addin loads from here). Deploy = build Release → robocopy `StingTools/bin/Release` → GOLD, **Revit must be closed** (DLL lock). Do NOT deploy while Revit runs.

---

## 0. Context — what is already TRUE (do not re-litigate)

The tag system pivoted to ONE hand-built **universal label** (65 rows, tiers T1–T10 gated by `TAG_PARA_STATE_1..10_BOOL`) propagated to all 206 families by `PropagateUniversalTagCommand` (recategorise preserves labels — **proven live** on Duct: category flip, all rows, formulas, breaks intact). Depth works (`SetParagraphDepthCommand` sets the type-level STATE bools; empty tiers render blank by design). Status delivery = `Status_Register` (Excel), **not** in-tag badges (tag formulas can't read the tagged element's params). Size = separate SaveAs families (`STING_Tag_Universal_{1mm..5mm}`); `DrawingType.ResolveTagSizeToken` snaps a scale to the nearest built size.

**Authoritative references — READ before touching code:**
- `docs/UNIVERSAL_TAG_LABEL_BUILD_SHEET.md` — the 65-row master spec.
- `docs/UNIVERSAL_TAG_DUCT_SMOKE_TEST.md` — the pass/fail gate.
- `docs/ROADMAP.md` §"Universal Tag pivot — Task 4 legacy cleanup" — the staged cutover map + entanglement table.
- `docs/UNIVERSAL_TAG_TASK4_STEP2_PATCH.md` — the ready-to-apply MigrateTagFamilies trim.

---

## HARD CONSTRAINTS (non-negotiable — violating any = stop and report)

1. **Green gate after every task.** Build Release + run Tags.Tests after each Task below. If a task regresses either baseline, revert that task's edits and report — do not push past red.
2. **No blind deletes.** Before removing ANY method/class/file, run a caller search (`Grep` for the symbol across `StingTools/**/*.cs` AND the XAML `Tag="..."` strings AND `WorkflowEngine.ResolveCommand` AND `StingCommandHandler` switch AND `TagsCommandModule`/module registries). A symbol with ANY live caller is NOT dead — leave it, note it. The ROADMAP is explicit: the legacy tier machinery is **entangled, not orphaned**.
3. **Never break live placement/UI.** `StingAutoTagger`, `SmartTagPlacement`, the dock-panel button dispatch, and the colour-scheme commands are LIVE. Depth-variant creation (`TagTypeVariantWriter`) is shared and stays.
4. **One logical change per commit**, imperative subject, end with the repo's `Co-Authored-By` trailer. Commit after each Task passes its gate.
5. **Manual Revit steps are flagged `[HUMAN-IN-REVIT]`** — you cannot do them (the API can't author label rows). Emit them as a checklist for the user; do not fake them.
6. **Do not touch `main`. Do not force-push. Do not deploy while Revit is open.**

---

## TASK A — Short + maskable display (the on-drawing tag = `ASS_DISPLAY_TXT`, mask-controlled)

**Goal:** line 1 of the tag becomes the compact, `TOKEN SEGMENT VISIBILITY`-controlled `ASS_DISPLAY_TXT` (full-8-then-masked); the redundant short `ASS_TAG_2` line is dropped; `ASS_TAG_1` stays in the data (unique key, ISO traceability). Root causes confirmed this session: the label shows `ASS_TAG_1` (unmasked), the mask targets `ASS_DISPLAY_TXT` (not on the label), and `TagConfig.BuildAndWriteTag` line ~2405 overwrites `ASS_DISPLAY_TXT` with the **full** tag every build, and `DisplayModeDefault=2` is a mode where the mask never applies (`BuildDisplayTag` masks only in modes 0/5).

**A1 — code (`StingTools/Core/ParamRegistry.cs`):** change `DisplayModeDefault` from `2` to `5` so `ASS_DISPLAY_TXT` defaults to full-8-segment (the only base a full 8-char mask can shorten). Update its doc-comment.

**A2 — code (`StingTools/Core/TagConfig.cs` ~line 2405):** today `BuildAndWriteTag` unconditionally does `SetString(DISPLAY_TXT, tag, overwrite:true)` (the full tag), which clobbers any masked value. Change so the masked/mode-resolved display wins: after computing `tag`, call `BuildDisplayTag(el)` (the mode+mask builder, ~line 2721) and write ITS result to `ASS_DISPLAY_TXT`; keep the full-tag write only as the fallback when `BuildDisplayTag` returns empty. Ensure `BuildDisplayTag` runs AFTER tokens + the UI `TokenMask` extra-param are in scope. Verify `ApplySegmentMask` handles a mode-5 full string with a partial mask (it does — it's the existing display path).

**A3 — consistency:** `BuildDisplayTag`'s mask block currently gates on `(mode == 0 || mode == 5)` but its own comment claims "modes 1-5/0". Pick ONE: either make the code match the comment (apply mask across modes, with `ApplySegmentMask` no-op'ing when a segment is already absent) or fix the comment to say "0/5 only". Do NOT leave the lie. Recommended: keep 0/5 (the only modes with all 8 segments present) and correct the comment.

**A4 — `[HUMAN-IN-REVIT]` (emit as checklist, do not attempt):**
- Open master `STING_Tag_Universal` → Edit Label.
- Add `ASS_DISPLAY_TXT` to the field list if absent; make it **row 1** (replacing the `ASS_TAG_1_TXT` row as the visible identity line). Keep `ASS_TAG_1_TXT` bound in the family (data) but off the visible label, or on a deep tier — user's call.
- **Delete** the `ASS_TAG_2_TXT` row ("Show Tier 2 - 2") — the redundant short form.
- Save → **Load into Project** → `Propagate_UniversalTag` → CHOOSE → Duct → verify line 1 = `ASS_DISPLAY_TXT` and unchecking a segment + Build Tags (Overwrite=Yes) shortens it → then ALL 206.

**A5 — gate:** build green + Tags.Tests green. Commit `feat(tags): short+maskable display — ASS_DISPLAY_TXT is the on-drawing tag`.

---

## TASK B — Remove redundant code introduced by this session's Tokens & Depth wiring

The Tokens & Depth wiring now routes format through `ParamRegistry.ApplyTagFormatOverrides` + `TagConfig.SeqPadWidth` (commits `29cbc3ab2`, `c3813e217`). That leaves **dead ExtraParam pushes** in `StingTools/UI/StingDockPanel.xaml.cs` `SetTokenDepthParams()`:

- `SetExtraParam("TagSeparator", sep)` — **no reader** (grep-confirmed). Remove.
- `SetExtraParam("SeqPad", seqPad)` — **no reader**. Remove (the value now drives `TagConfig.SeqPadWidth` directly).
- `SetExtraParam("SegOrder", orderText)` — **no reader**. Remove.

Keep `TokenMask` and `ParaDepth` pushes (both have live readers: `TagConfig` + `RefreshTagDisplayCommand`, and `SetParagraphDepthCommand` + `TagConfig` + `TagStyleEngine`). **Before removing each, re-grep** `GetExtraParam("<key>")` across the repo to confirm zero readers (a later task may have added one). Gate: build green. Commit `refactor(tags): drop dead ExtraParam pushes superseded by ApplyTagFormatOverrides`.

---

## TASK C — Legacy older-tagging cutover (STAGED, GATED — follow ROADMAP §Task-4 exactly)

The pre-pivot machinery authored bespoke per-family tier labels. The Revit API cannot author label rows, so that path is superseded — BUT the ROADMAP proved these are **entangled, not orphaned**. Do the staged cutover; delete only what a caller check proves dead.

**C1 — apply the ready trim.** Apply `docs/UNIVERSAL_TAG_TASK4_STEP2_PATCH.md`: trim `MigrateTagFamiliesCommand`'s `FamilyLabelAuthor.AuthorLabelsMulti(...)` call (the old label-authoring step) while KEEPING its param-injection + the shared `TagTypeVariantWriter` type-variant loop. Build green. Commit.

**C2 — re-run caller checks** on each ROADMAP candidate and delete ONLY those now proven dead:
- `Tags/FamilyLabelAuthor.cs` — after C1, grep every public method. Callers historically: `MigrateTagFamiliesCommand`, `TagFamilyCreatorCommand`, `TagConfigCsvReader`. Delete the file only if ALL are gone; otherwise delete just the now-unreferenced methods.
- `Core/TagConfigPlanResolver.cs` — same discipline.
- `Core/TagConfigCsvReader.cs` + `Data/STING_TAG_CONFIG_v5_0_*.csv` — **HIGH ENTANGLEMENT**: also feeds `LABEL_DEFINITIONS.json` sync, `HandoverModeHelper`, `PresentationModeCommand`, `ParamRegistry`, `TagConfig`, `FamilyParamCreatorCommand`. Per `reference-tag-config-sources`: `LABEL_DEFINITIONS.json` is canonical; the v5.0 CSVs are the richest synced source — **do NOT delete the CSVs unless every reader is gone**. Most likely: leave as-is, note in ROADMAP.

**C3 — do NOT touch** (explicitly out of scope for deletion): `TagStyleEngine.ResolveTagTypeForPlacement` (live in `StingAutoTagger` + `SmartTagPlacement`, 6 sites), the colour-scheme commands (`ApplyColorScheme`/`SwitchTagStyleByDisc`/`BatchApplyColorScheme`/`ColorByVariable` — live buttons), and all depth-variant logic. Colour-scheme deprecation is a separate surgical refactor, not this runner.

**C4** — for anything you leave because it still has callers, add a one-line `// LEGACY(universal-tag): superseded by <new path>; retained — still called by <caller>` marker so the next pass is faster. Gate + commit per deletion group. Move each closed item from ROADMAP §Task-4 to `docs/CHANGELOG.md`.

---

## TASK D — Refinement / alignment / consistency sweep (read-only findings + safe fixes)

Audit the tag subsystem for drift the recent changes may have introduced or exposed. Report a findings list; apply only mechanical, zero-risk fixes (comment/typo/dead-`using`); anything behavioural → propose, don't apply.

Check specifically:
1. **Param datatype consistency** — `TAG_PARA_STATE_*` are `YESNO` in `MR_PARAMETERS.txt` but code comments in `SetParagraphDepthCommand.SetYesNo` / `TagTypeVariantWriter` claim "stored as TEXT so label formulas can reference them". Reconcile: confirm what the master actually carries (YESNO, proven this session) and fix the stale comments. Also verify `FamilyParamCreatorCommand` line ~352 `isInstance = paramName != TAG_POS` — STATE/style params SHOULD be TYPE params; flag if injecting them as instance (it does) is a latent bug for families built via "Inject Params" vs propagation (which adds them as type). Propose a fix; don't silently change binding.
2. **SeqPad precedence** — `TagConfig.SeqPadWidth > 0 ? SeqPadWidth : NumPad` means `NumPad` is dead for SEQ. Confirm `ApplyTagFormatOverrides`'s `NumPad` write is still needed elsewhere (it is: `BuildSeqString` fallback + other consumers). Note the dual-source-of-truth; recommend a single accessor.
3. **Overwrite semantics** — document (in the panel tooltip + `UNIVERSAL_TAG_MANUAL_CONFIG_GUIDE.md`) that format/pad changes only re-pad existing tags under Overwrite=Yes; this cost real confusion.
4. **Dead `using`s / unreachable branches** in the files you touched.
5. **Doc alignment** — `CLAUDE.md` counts, `docs/CHANGELOG.md` phase entries, and any guide that still describes in-tag status badges (abandoned) or the pre-pivot bespoke-tier scheme.

---

## VERIFICATION & REPORTING

After all tasks:
1. `dotnet build ... -c Release` → confirm 0 errors / 4 baseline warnings.
2. Run `StingTools.Tags.Tests` → confirm 134 pass / 2 baseline fails.
3. Produce a report: per-task what changed, every symbol deleted + its proven-zero caller evidence, every symbol RETAINED + why, and the `[HUMAN-IN-REVIT]` checklist (Task A4) for the user.
4. Update `docs/CHANGELOG.md` (new phase entry) and prune closed items from `docs/ROADMAP.md`.
5. **Do NOT deploy** — leave that to the user (Revit must be closed). List the exact deploy command.
6. **Do NOT merge to main** — stop at a clean, green, committed branch and report.

**If any gate goes red, or a "dead" symbol turns out to have a caller, STOP and report rather than forcing the change.**
