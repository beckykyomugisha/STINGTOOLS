<!--
  Autonomous-agent runner — PHASE 2 (aggressive). Hand this whole file to a fresh
  Claude Code / terminal agent. It APPLIES the behavioural findings the phase-1
  finalize run only reported, then RETIRES the "Create Tag Fams" label path and
  DELETES the now-orphaned legacy tagging stack. Deletion is real this time — but
  still gated: cut callers first, prove zero references, build green, then delete.
-->

# Universal Tag — Legacy Teardown Runner (Phase 2)

**Repo:** `C:\Dev\STINGTOOLS` · **Branch:** create `claude/universal-tag-teardown` **off `claude/universal-tag-finalize`** (phase-1 tasks A–D live there: `735ea7aaa`, `aa67afc25`, `561e9b052`). If that branch isn't present, STOP and report — do not start from an older base.
**Build:** `dotnet build StingTools/StingTools.csproj -c Release -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"` — 0 errors / **4** baseline CS0618 warnings (`Clash/ClashIssueSyncCommand.cs`). Any new warning is yours.
**Tests:** `StingTools.Tags.Tests` — 134 pass / 2 baseline `CsiMasterFormat` fails.
**Deploy target:** `C:\Dev\STING_PLACEMENT_GOLD` (Revit must be CLOSED). You do NOT deploy — you leave a clean branch and list the command.

---

## 0. Mandate (this run is different — you APPLY and DELETE)

Phase 1 was conservative: it reported the two behavioural findings and deleted nothing because the legacy tier machinery is entangled. Phase 2 removes the entanglement and completes the teardown. **You WILL change parameter binding, WILL gut a live command's legacy path, and WILL delete files** — but every deletion is still preceded by a caller check and followed by a green build. Nothing is force-deleted while a caller exists; you remove the caller first (Task 2), then the callee (Task 3).

**Read first:** `docs/UNIVERSAL_TAG_FINALIZE_RUNNER.md` (phase-1 map + entanglement table), `docs/ROADMAP.md` §Task-4, `reference-tag-config-sources` guidance (LABEL_DEFINITIONS.json is canonical; v5.0 CSVs are the synced source).

---

## HARD CONSTRAINTS (violating any = stop + report)

1. **Green gate after every task** (build + Tags.Tests). Regress either baseline → revert that task, report, do not push past red.
2. **Order is load-bearing:** Task 2 (remove callers) BEFORE Task 3 (delete callees). Never delete a symbol with a live caller — if a "should-be-orphan" still has one after Task 2, STOP and report which caller survived.
3. **Caller check before every delete:** grep the symbol across `StingTools/**/*.cs`, XAML `Tag="..."`, `StingCommandHandler` switch, `WorkflowEngine.ResolveCommand`, `UI/Modules/*CommandModule.cs`, and the MCP `run_command` registry. Zero hits everywhere = safe.
4. **Do NOT touch depth-variant logic** (`TagTypeVariantWriter`), `TagStyleEngine.ResolveTagTypeForPlacement` (live in `StingAutoTagger` + `SmartTagPlacement`), or the colour-scheme commands. Those are live and out of scope.
5. One commit per task, imperative subject + the repo `Co-Authored-By` trailer. No `main`, no force-push, no deploy while Revit runs.

---

## TASK 1 — Apply the deferred behavioural findings (phase-1 reported these; now APPLY)

**1a — Fix parameter binding in `FamilyParamCreatorCommand.InjectSharedParams` (~line 352).**
Today: `bool isInstance = paramName != ParamRegistry.TAG_POS;` — binds *everything* except `TAG_POS` as **instance**, including `TAG_PARA_STATE_*` and the style BOOLs. That's inverted vs the depth/variant system, which sets those at **type** level (`SetParagraphDepthCommand`, `TagTypeVariantWriter` both operate on the type). It's a latent bug: families built via "Inject Params" get STATE as instance, so `SetParagraphDepth` (type-targeting) silently no-ops on them.
**Do:** bind as **TYPE** every param the type-level machinery owns — precisely `TagFamilyConfig.VisibilityParams` ∪ `TagFamilyConfig.StyleParams` ∪ `{ ParamRegistry.TAG_DEPTH_TIER, TAG_BOX_* , TAG_LEADER_* }` (the type-graphic params). Bind as **INSTANCE** the per-element value/container params (`ASS_TAG_*`, tokens, and anything whose value comes from the tagged element). Implement as a small predicate, e.g.:
```
static bool IsTypeParam(string p) =>
    TagFamilyConfig.VisibilityParams.Contains(p) ||
    TagFamilyConfig.StyleParams.Contains(p) ||
    p == ParamRegistry.TAG_DEPTH_TIER ||
    p.StartsWith("TAG_BOX_") || p.StartsWith("TAG_LEADER_");
...
bool isInstance = !IsTypeParam(paramName);
```
Do NOT just flip to `StartsWith("ASS_TAG")` — verify against the actual Visibility/Style sets so nothing that must be instance (container values) becomes type. Add a comment citing why STATE/style are type. Gate + commit.

**1b — Collapse the SEQ-pad dual source of truth.**
`TagConfig.BuildAndWriteTag` (~2261) and `~453` both compute `SeqPadWidth > 0 ? SeqPadWidth : NumPad`. Introduce one accessor `TagConfig.EffectiveSeqPad => SeqPadWidth > 0 ? SeqPadWidth : ParamRegistry.NumPad;` and route both sites through it. Keep the panel writing `SeqPadWidth` (from the combo) — that's the live driver — and keep the `NumPad` write in `ApplyTagFormatOverrides` (still used by `BuildSeqString` fallback + `num_pad` export). No behaviour change; single source. Gate + commit.

---

## TASK 2 — Retire the "Create Tag Fams" legacy label path (the unlock)

`TagFamilyCreatorCommand` (button "Create Tag Fams") is the ONLY thing keeping the legacy stack alive. In the universal world, new tag families get their label by **propagation** (recategorise the universal master), not per-family authoring — and the Revit API cannot author label rows anyway (`FamilyLabelAuthor` was always a partial dead-end).

**Do — GUT, don't delete the command:** remove the label-authoring path while KEEPING the still-useful shell creation + param injection + `TagTypeVariantWriter` type-variant loop, so the button becomes "mint the family + inject params (then Propagate_UniversalTag to clone the label)". Concretely in `TagFamilyCreatorCommand.cs`:
- Delete the calls at ~1160–1164 (`TagConfigPlanResolver.LoadAllPerMode` / `LoadAll` / `ReadPreserveHandEdits` / `HandoverModeHelper.GetActiveMode`) and the `plansByFamily` / `preserveHandEdits` / `activeMode` locals they feed.
- Delete the `modePlans` / `FamilyLabelAuthor.ModePlan` / `FamilyLabelAuthor.Options` / `AuthorLabelsMulti(...)` block (~2136–2200) and any now-unused helper methods it alone used.
- Keep: family-document creation, `InjectSharedParams` (now with the Task-1a binding), and the type-variant creation.
- Update the button tooltip + the command's summary/XML-doc to describe the universal flow ("creates the family + params; run Propagate Universal to apply the universal label"). Add a `TaskDialog`/log line pointing the user to Propagate.
- Update `UNIVERSAL_TAG_MANUAL_CONFIG_GUIDE.md`: Create Tag Fams → then Propagate.

Gate: build green + Tags.Tests green. Commit `refactor(tags): gut Create Tag Fams legacy label path — universal flow is mint+propagate`.

---

## TASK 3 — Delete the now-orphaned legacy stack (caller-verified)

After Task 2, re-run the caller check on each. **Delete only those with zero callers anywhere; for any that still has one, STOP and report the caller.**

Expected-orphan candidates (verify each — do not assume):
- `Tags/FamilyLabelAuthor.cs` — its only remaining caller was `TagFamilyCreatorCommand` (removed in Task 2) and possibly `MigrateTagFamiliesCommand` (already trimmed in phase-1 `aa67afc25`). If both gone → delete the file.
- `Core/TagConfigPlanResolver.cs` — same; delete if `LoadAll*`/`ReadPreserveHandEdits` have no callers.
- `HandoverModeHelper` — check: `ApplyParagraphPresetCommand` may still call `GetActiveMode`/`GetSelectorBool`. If a live caller remains, RETAIN the whole helper (it's a mode resolver, not pure-legacy) and note it. Delete only truly-dead methods.
- `TierPlan` / tier-plan types — used by `PerFamilyTierMap` + `TagConfigCsvReader`. Almost certainly RETAINED. Verify; don't delete if referenced.

For each deletion: remove the file, remove now-dead `using`s in files that referenced it, build green. Group related deletions into one commit `refactor(tags): delete orphaned legacy tier-authoring (FamilyLabelAuthor, …)`. In the commit body, list each deleted symbol + "0 callers verified".

---

## TASK 4 — v5.0 CSV + data + docs disposition

- `Core/TagConfigCsvReader.cs` + `Data/STING_TAG_CONFIG_v5_0_*.csv`: run the full reader check. These feed `LABEL_DEFINITIONS.json` sync, `ParamRegistry`, `TagConfig`, `PresentationModeCommand`, `FamilyParamCreatorCommand`. If ANY live reader remains → **RETAIN**, add a `// LEGACY(universal-tag): retained — still read by <caller>` marker, and record in ROADMAP why. Do NOT delete data files that are still parsed.
- Re-run a dead-`using` / unreachable-branch sweep across every file touched in Tasks 1–3.
- Docs: move every closed ROADMAP §Task-4 line to `docs/CHANGELOG.md` (new phase entry summarising the teardown); update `CLAUDE.md` only if a command/file it enumerates was deleted or renamed (Create Tag Fams still exists — just gutted — so likely a one-line description tweak).

Gate + commit `docs(tags): teardown changelog + retained-legacy markers`.

---

## VERIFICATION & REPORTING

1. Build Release → 0 errors / 4 baseline warnings. Tags.Tests → 134/2.
2. Report, per task: what changed; **the binding predicate diff (Task 1a)**; every symbol DELETED with its "0 callers" evidence; every symbol RETAINED with the surviving caller; and confirmation that Create Tag Fams still builds a family + injects params (just no legacy label authoring).
3. Regression note: because Task 1a changes STATE/style to type on "Inject Params"-built families, and Task 2 changes the Create Tag Fams workflow, call out the **new user flow**: Create Tag Fams → Propagate Universal (for the label) → Set depth. Add it to the `[HUMAN-IN-REVIT]` notes.
4. Leave a clean, green, committed `claude/universal-tag-teardown`. Do NOT deploy (list the command). Do NOT merge to main.
5. **If any expected-orphan still has a caller after Task 2, or any gate goes red — STOP and report. Do not force the delete.**
