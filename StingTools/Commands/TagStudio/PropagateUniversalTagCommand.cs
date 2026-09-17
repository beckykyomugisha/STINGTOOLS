// ============================================================================
// PropagateUniversalTagCommand.cs — Universal-tag propagation conveyor.
//
//   *** Phase 195 — Universal Tag pivot ***
//
// The original goal was "build one master, auto-propagate a BESPOKE tiered label
// to all 206 tag families". That is IMPOSSIBLE (proven by data + live Revit):
//   • the Revit API cannot author label rows (Family Editor UI only);
//   • cross-category label paste is blocked ("Can't paste Labels across Families
//     of different Categories");
//   • all 62 MEP family sheets have unique row structures, so no single master
//     + ReplaceParameter swap works and a superset master is unbuildable.
//
// The settled design: a human builds ONE UNIVERSAL, discipline-agnostic label
// (see UNIVERSAL_TAG_LABEL_BUILD_SHEET.md — 62 identical rows, tiers T1/T2/T4-T10)
// once, by hand. This command then CLONES that master to every target STING tag
// family via:
//     doc.EditFamily(master) → famDoc
//     famDoc.OwnerFamily.FamilyCategory = <target tag category>   (net-new API call)
//     TagTypeVariantWriter.CreateStandardVariants(...)            (re-create the
//                                                                   data-driven
//                                                                   depth/style
//                                                                   type variants)
//     SaveAs(<tempdir>\<target>.rfa) → LoadFamily(overwrite) → File.Move(→ canonical)
//
// NAMING (hard Revit rule): a loaded family's project name IS its .rfa FILE
// name — renaming famDoc.OwnerFamily does NOT survive SaveAs. The clone must
// therefore be saved under the target's EXACT file name, inside a throwaway
// temp SUBFOLDER for atomicity. Saving under a temp-suffixed file name (the
// pre-fix behaviour) made LoadFamily mint a junk duplicate named
// "<target>.rfa.sting-propagate-<guid>" and left the real target untouched.
// Execute() purges any such leftovers from earlier runs before propagating.
//
// Recategorising a family PRESERVES its label rows (proven live: Air Terminal →
// Duct Tags, every row survived). The label is IDENTICAL for all families, so no
// per-family row swapping is needed. Discipline-specific engineering data lives in
// per-category schedules instead (Schedule_DisciplineTagExpander, Task 3).
//
// Re-sync mode: re-running this command re-pushes master edits to all targets —
// that is the maintainability win, and it is the SAME code path (overwrite is
// idempotent), so re-sync needs no separate mode flag.
//
// SMOKE-TEST FIRST: the scope picker lets you target ONE family (e.g. Duct). Run
// it on Duct, eyeball the result in Revit (rows present, tier toggle works, type
// variants correct, nested badges survive) BEFORE scaling to all 206.
//
// Atomic save-then-publish is reused verbatim from
// MigrateTagLabelReferencesCommand: SaveAs to a temp .rfa, LoadFamily the temp,
// only then File.Move it over the canonical .rfa. Any failure leaves the target's
// existing family untouched.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Select;
using StingTools.Tags;
using StingTools.UI;

namespace StingTools.Commands.TagStudio
{
    /// <summary>
    /// What to do about a target whose own category disagrees with the one declared
    /// for it in STING_TAG_CONFIG_v5_0_*.csv.
    ///
    /// This is not a preference - it decides whether the load can work at all.
    /// Revit matches a reloaded family by NAME, and will not change a loaded
    /// family's category on reload. Every propagation run recorded on this machine
    /// (19:58, 21:02, 21:23 on 2026-09-17) recategorised the clone and then had its
    /// load refused with no failure message, which is exactly that rule's signature.
    /// </summary>
    internal enum RecategoriseMode
    {
        /// <summary>Set the clone to the DECLARED category. Correct, and refused by
        /// Revit when a family of that name is already loaded under another one.</summary>
        EnforceDeclared,

        /// <summary>Keep whatever category the target already carries, so the load is
        /// a same-name-same-category overwrite - the path the conveyor was built for.
        /// The label propagates; the category stays wrong until the family is
        /// reloaded from disk or deleted and re-loaded.</summary>
        KeepExisting
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PropagateUniversalTagCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            var app = ctx.App.Application;

            string sharedParamFile = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
            if (string.IsNullOrEmpty(sharedParamFile) || !File.Exists(sharedParamFile))
            {
                TaskDialog.Show("Propagate Universal Tag",
                    "MR_PARAMETERS.txt not found in data directory. Run 'Check Data' first.");
                return Result.Failed;
            }

            // ── Collect loaded STING-prefixed annotation (tag) families ──
            var stingFamilies = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.Name != null &&
                            f.Name.StartsWith(TagFamilyConfig.FamilyPrefix, StringComparison.OrdinalIgnoreCase) &&
                            f.FamilyCategory != null &&
                            f.FamilyCategory.CategoryType == CategoryType.Annotation)
                .OrderBy(f => f.Name)
                .ToList();

            // ── Identify duplicates minted by pre-fix runs ──
            // Before the temp-subfolder fix, the clone loaded under its temp FILE
            // name ("<target>.rfa.sting-propagate-<guid>"), creating a junk
            // duplicate and leaving the real target untouched. Partition them out
            // of the master/target pickers now, but DEFER the destructive delete
            // until AFTER the confirmation gate — deleting here means a user who
            // cancels still loses the families (and their placed tag instances).
            // Partition reads f.Name while every reference is still valid: a
            // deleted Element throws InvalidObjectException on any property access.
            var junk = new List<Family>();
            var keep = new List<Family>();
            foreach (Family f in stingFamilies)
            {
                if (IsTempNamed(f.Name)) junk.Add(f); else keep.Add(f);
            }
            stingFamilies = keep;
            int junkDeleted = 0;

            if (stingFamilies.Count < 2)
            {
                TaskDialog.Show("Propagate Universal Tag",
                    "Need the universal master plus at least one target family loaded.\n" +
                    "Load the universal master (built from UNIVERSAL_TAG_LABEL_BUILD_SHEET.md)\n" +
                    "and the STING tag families you want to propagate to, then re-run.");
                StingLog.Info($"PropagateUniversalTag: only {stingFamilies.Count} STING annotation " +
                              "family(ies) loaded, need 2 - nothing done");
                return Result.Cancelled;
            }

            // ── 1. Pick the universal master from the loaded families ──
            Family master = PickMaster(stingFamilies);
            if (master == null)
            {
                // Every early exit says which one it was. Three of them used to
                // return Cancelled with no dialog and no log line, so the whole
                // run read as "RunCommand: start / RunCommand: done" seconds
                // apart with nothing between - indistinguishable from the command
                // dying. Observed three times on 2026-09-17 at 20:43-20:44.
                StingLog.Info("PropagateUniversalTag: no master chosen (picker cancelled) - nothing done");
                return Result.Cancelled;
            }

            // ── 2. Targets = every other loaded STING tag family, scoped ──
            var candidates = stingFamilies.Where(f => f.Id != master.Id).ToList();
            var targets = ChooseTargets(candidates, out string scopeLabel);
            if (targets == null)
            {
                StingLog.Info("PropagateUniversalTag: scope dialog cancelled - nothing done");
                return Result.Cancelled;
            }
            if (targets.Count == 0)
            {
                TaskDialog.Show("Propagate Universal Tag", "No target families selected.");
                StingLog.Info("PropagateUniversalTag: scope resolved to zero targets - nothing done");
                return Result.Cancelled;
            }

            // ── 3. Confirmation ──
            var variants = TagStyleCatalogue.EnumerateStandardVariants().ToList();
            var confirm = new TaskDialog("Propagate Universal Tag");
            confirm.MainInstruction =
                $"Propagate '{master.Name}' to {targets.Count} target families ({scopeLabel})?";
            confirm.MainContent =
                "For each target family this will:\n" +
                "  • Clone the universal master (EditFamily)\n" +
                "  • Recategorise the clone to the target's tag category\n" +
                "  • Save the clone under the target's exact .rfa file name (so LoadFamily overwrites it)\n" +
                $"  • Add any missing style/visibility params + re-create up to {variants.Count} type variants\n" +
                "  • Overwrite the target family (atomic SaveAs → LoadFamily → move)\n\n" +
                "The universal label rows carry over unchanged (recategorise preserves\n" +
                "label rows). Re-running is a safe RE-SYNC — master edits re-propagate.\n\n" +
                "SMOKE TEST: verify one family (Duct) in Revit before scaling to all.\n" +
                "Press Escape between families to cancel.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() != TaskDialogResult.Ok)
            {
                StingLog.Info($"PropagateUniversalTag: confirmation declined for {targets.Count} target(s) - nothing done");
                return Result.Cancelled;
            }

            // ── Purge pre-fix temp duplicates NOW the user has committed ──
            // Deferred from before the pickers so cancelling the command doesn't
            // delete families (and their placed tag instances) the user kept.
            if (junk.Count > 0)
            {
                using (var junkTx = new Transaction(doc, "STING Purge propagate temp duplicates"))
                {
                    junkTx.Start();
                    foreach (Family f in junk)
                    {
                        string junkName = f.Name; // read before Delete — invalid after
                        try { doc.Delete(f.Id); junkDeleted++; }
                        catch (Exception ex) { StingLog.Warn($"Purge temp duplicate '{junkName}': {ex.Message}"); }
                    }
                    junkTx.Commit();
                }
                StingLog.Info($"PropagateUniversalTag: purged {junkDeleted} temp-named duplicate families");
            }

            // ── Pre-resolve shared arrowhead types ──
            var arrowheads = TagTypeVariantWriter.BuildArrowheadLookup(doc);
            var styleAndVisParams = TagFamilyConfig.StyleParams
                .Concat(TagFamilyConfig.VisibilityParams)
                .Distinct()
                .ToList();

            // ── Pre-flight: will the clone even load back? ──
            // Every target gets a clone of the same master, so a shared-parameter
            // type conflict fails all of them identically — and LoadFamily reports
            // a bare false, which the loop could only write down as "LoadFamily
            // back into project failed". That is what a 17-minute run reported on
            // 2026-09-17 while the real cause (12 parameters the family offers as
            // Text that the project holds as Number/Currency/Length/Yes-No under
            // the same GUIDs) was readable in seconds. Read it here, name the
            // parameters, and stop before touching 206 families.
            {
                string preflightSp = app.SharedParametersFilename;
                MasterPreflight pre;
                try
                {
                    // The same file the loop binds from, so the definitions checked
                    // are the ones propagation would actually add.
                    app.SharedParametersFilename = sharedParamFile;
                    var willAdd = SharedParamPreflight.CollectDefinitions(
                        app.OpenSharedParameterFile(), styleAndVisParams);
                    pre = SharedParamPreflight.CheckMaster(doc, master, willAdd);
                }
                finally
                {
                    try { app.SharedParametersFilename = preflightSp ?? ""; }
                    catch (Exception ex) { StingLog.Warn($"Restore SharedParametersFilename after pre-flight: {ex.Message}"); }
                }

                if (pre.Conflicts.Count > 0)
                {
                    string detail = SharedParamConflictDetector.Describe(pre.Conflicts);
                    StingLog.Warn($"PropagateUniversalTag: aborted before any family — {detail?.Replace("\n", " ")}");

                    var block = new TaskDialog("Propagate Universal Tag");
                    block.MainInstruction =
                        $"'{master.Name}' cannot load into this project — nothing was propagated.";
                    block.MainContent =
                        detail + "\n\n" +
                        "Revit identifies a shared parameter by its GUID and refuses a load that " +
                        "would redefine one, so every target would fail the same way.\n\n" +
                        "Fix it in the FAMILY, not the project: delete the conflicting parameters " +
                        "from the master (a numeric parameter cannot be retyped to Text for a label — " +
                        "use its _TXT display mirror), then re-run.\n\n" +
                        "docs/UNIVERSAL_TAG_CONFLICT_RESOLUTION_RUNBOOK.md has the ordered steps.";
                    block.CommonButtons = TaskDialogCommonButtons.Close;
                    block.Show();
                    return Result.Cancelled;
                }

                // Not a blocker, but it multiplies by the number of targets: text
                // the author left for themselves is cloned into every family and
                // then prints. Spotted on 2026-09-17 as a red note in a tag family.
                if (pre.AuthoringNotes.Count > 0)
                {
                    var noteDlg = new TaskDialog("Propagate Universal Tag");
                    noteDlg.MainInstruction = pre.AuthoringNotes.Count == 1
                        ? $"'{master.Name}' carries a note that looks like an instruction to its author."
                        : $"'{master.Name}' carries {pre.AuthoringNotes.Count} notes that look like " +
                          "instructions to its author.";
                    noteDlg.MainContent =
                        "  • " + string.Join("\n  • ", pre.AuthoringNotes) + "\n\n" +
                        $"Everything in the master is cloned into each of the {targets.Count} target " +
                        "famil" + (targets.Count == 1 ? "y" : "ies") + ", so this text goes with it and " +
                        "will print on drawings." + "\n\n" +
                        "Delete it in the master and re-run, or propagate anyway.";
                    noteDlg.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                    noteDlg.DefaultButton = TaskDialogResult.No;
                    if (noteDlg.Show() != TaskDialogResult.Yes)
                    {
                        StingLog.Info($"PropagateUniversalTag: declined over {pre.AuthoringNotes.Count} " +
                                      "authoring note(s) in the master - nothing done");
                        return Result.Cancelled;
                    }
                    StingLog.Warn("PropagateUniversalTag: proceeding with authoring note(s) in the master " +
                                  "(operator confirmed)");
                }
            }

            // ── Which targets would have their category CHANGED? ──
            // Asked before the run because it decides whether the load can succeed,
            // and because it is the one variable that separates "the conveyor does
            // not work" from "the conveyor cannot change a category".
            var mode = RecategoriseMode.EnforceDeclared;
            {
                var changing = new List<string>();
                foreach (Family t in targets)
                {
                    var r = TagCategoryResolver.Resolve(doc, t);
                    if (r != null && r.IsMismatch && r.DeclaredTagCategory != null)
                        changing.Add($"{t.Name}: {r.ActualCategory} → {r.DeclaredTagCategory.Name}");
                }

                if (changing.Count > 0)
                {
                    var catDlg = new TaskDialog("Propagate Universal Tag — category");
                    catDlg.MainInstruction = changing.Count == 1
                        ? "One target family is categorised differently from its declaration."
                        : $"{changing.Count} target families are categorised differently from their declaration.";
                    catDlg.MainContent =
                        "  • " + string.Join("\n  • ", changing.Take(8)) +
                        (changing.Count > 8 ? $"\n  • … and {changing.Count - 8} more" : "") + "\n\n" +
                        "Revit will not change a loaded family's category by reloading over it. " +
                        "Enforcing the declaration is correct, but the load can be refused - with no " +
                        "message - which is what every run so far has hit.";
                    catDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
                    catDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        "KEEP each family's current category (recommended for the smoke test)",
                        "Same-name, same-category overwrite - the path this command was built for. " +
                        "The label propagates and the category stays as it is.");
                    catDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                        "ENFORCE the declared category",
                        "Correct, and the load may be refused. If it is, the report says so per family.");

                    var catChoice = catDlg.Show();
                    if (catChoice == TaskDialogResult.Cancel)
                    {
                        StingLog.Info("PropagateUniversalTag: category dialog cancelled - nothing done");
                        return Result.Cancelled;
                    }
                    mode = catChoice == TaskDialogResult.CommandLink1
                        ? RecategoriseMode.KeepExisting
                        : RecategoriseMode.EnforceDeclared;
                    StingLog.Info($"PropagateUniversalTag: category mode = {mode} " +
                                  $"({changing.Count} target(s) declared differently)");
                }
            }

            // ── Pre-flight: is any target open in the Family Editor? ──
            // Revit will not load a family while a document for it is open, and it
            // refuses by returning false with NO failure message - the 21:02 run
            // spent 90 seconds cloning, adding 138 parameters and minting 14 type
            // variants before hitting that wall. Cheap to ask first.
            {
                var openTargets = targets
                    .Select(t => new { Name = t.Name, Doc = FindOpenFamilyDocument(app, t.Name, null) })
                    .Where(x => x.Doc != null)
                    .ToList();
                if (openTargets.Count > 0)
                {
                    string names = string.Join("\n  • ", openTargets.Select(x => x.Name));
                    StingLog.Warn($"PropagateUniversalTag: aborted - {openTargets.Count} target(s) open in the " +
                                  $"Family Editor: {string.Join(", ", openTargets.Select(x => x.Name))}");

                    var openDlg = new TaskDialog("Propagate Universal Tag");
                    openDlg.MainInstruction = openTargets.Count == 1
                        ? "A target family is open in the Family Editor - nothing was propagated."
                        : $"{openTargets.Count} target families are open in the Family Editor - nothing was propagated.";
                    openDlg.MainContent =
                        "  • " + names + "\n\n" +
                        "Revit will not load a family while a document for it is open, and it says nothing " +
                        "when it refuses - the load just fails.\n\n" +
                        "Close those family tabs (Load into Project and Close, or close without saving) and re-run.";
                    openDlg.CommonButtons = TaskDialogCommonButtons.Close;
                    openDlg.Show();
                    return Result.Cancelled;
                }
            }

            var progress = StingProgressDialog.Show("Propagate Universal Tag", targets.Count);
            var rows = new List<List<string>>();
            int succeeded = 0, failed = 0, cancelled = 0, totalTypes = 0, totalParams = 0, totalScope = 0;
            string originalSp = app.SharedParametersFilename;

            try
            {
                app.SharedParametersFilename = sharedParamFile;

                for (int i = 0; i < targets.Count; i++)
                {
                    if ((i % 5) == 0 && EscapeChecker.IsEscapePressed())
                    {
                        cancelled = targets.Count - i;
                        StingLog.Info($"PropagateUniversalTag: cancelled after {i} of {targets.Count}");
                        break;
                    }

                    Family target = targets[i];
                    string targetName = target.Name;
                    string catName = target.FamilyCategory?.Name ?? "";
                    progress.Increment($"Propagating → {targetName} ({i + 1}/{targets.Count})");

                    var r = PropagateOne(doc, app, master, target, sharedParamFile,
                        styleAndVisParams, variants, arrowheads, mode);
                    totalTypes += r.TypesCreated;
                    totalParams += r.ParamsAdded;
                    totalScope += r.ScopeFixed;
                    if (r.Success) succeeded++; else failed++;

                    rows.Add(new List<string>
                    {
                        targetName, catName,
                        r.ParamsAdded.ToString(), r.TypesCreated.ToString(),
                        r.Success ? "OK" : "FAILED", r.ErrorMessage ?? ""
                    });
                }
            }
            finally
            {
                progress.Close();
                try { if (!string.IsNullOrEmpty(originalSp)) app.SharedParametersFilename = originalSp; }
                catch (Exception ex) { StingLog.Warn($"Restore SharedParametersFilename: {ex.Message}"); }
            }

            // ── Excel summary ──
            string xlsx = null;
            try
            {
                string outDir = OutputLocationHelper.GetOutputDirectory(doc);
                xlsx = Path.Combine(outDir, $"STING_PropagateUniversalTag_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                StingExcelExporter.ExportTable(
                    xlsx, "Propagation",
                    new List<string> { "Target", "Category", "ParamsAdded", "TypesCreated", "Status", "Error" },
                    rows, openFolder: false);
            }
            catch (Exception ex) { StingLog.Warn($"Excel export: {ex.Message}"); }

            // The failures, named in the dialog. This used to report "0
            // propagated, 1 failed" and stop there: the reason was a cell in an
            // .xlsx written to a %TEMP% GUID folder, which is why a 17-minute run
            // that DID explain itself in writing still read as the command dying.
            var failedRows = rows
                .Where(r => r.Count > 4 && string.Equals(r[4], "FAILED", StringComparison.Ordinal))
                .ToList();
            var why = new StringBuilder();
            foreach (var r in failedRows.Take(5))
            {
                string err = r.Count > 5 && !string.IsNullOrWhiteSpace(r[5]) ? r[5] : "(no reason recorded)";
                why.Append($"\n  • {r[0]}: {err}");
            }
            if (failedRows.Count > 5)
                why.Append($"\n  • … and {failedRows.Count - 5} more in the report");

            var td = new TaskDialog("Propagate Universal Tag — done");
            td.MainInstruction = $"{succeeded} propagated, {failed} failed" +
                                 (cancelled > 0 ? $", {cancelled} cancelled" : "");
            td.MainContent =
                $"Master: {master.Name}\n" +
                $"Scope:  {scopeLabel}\n\n" +
                $"Standard params added to each clone: {totalParams}\n" +
                // Named for what it is. "Params added: 139" reads as a side effect;
                // it is the standard style+visibility set that the master does not
                // carry, and it is why the two families differ afterwards.
                $"Type variants (re)created: {totalTypes}\n" +
                (totalScope > 0
                    ? $"Tier gates converted Instance -> Type: {totalScope}\n"
                    : "") +
                (junkDeleted > 0 ? $"Purged stale temp-named duplicates: {junkDeleted}\n" : "") +
                (why.Length > 0 ? $"\nFailed:{why}\n" : "") +
                (xlsx != null
                    ? $"\nReport: {xlsx}" +
                      // Where it went, and why it went there. An unsaved project has
                      // no _BIM_COORD to write to, so the report lands in a
                      // per-session %TEMP% GUID folder nobody finds by accident.
                      (xlsx.IndexOf(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) >= 0
                          ? "\n(Project is unsaved, so the report went to TEMP — save the project to keep reports.)"
                          : "")
                    : "\nNo report was written — see the log.");
            td.Show();

            StingLog.Info($"PropagateUniversalTag: master={master.Name}, succeeded={succeeded}, " +
                $"failed={failed}, cancelled={cancelled}, params={totalParams}, types={totalTypes}");
            return Result.Succeeded;
        }

        // ──────────────────────────────────────────────────────────────────
        //  Single-target propagation
        // ──────────────────────────────────────────────────────────────────

        private class PropResult
        {
            public int ParamsAdded;
            public int TypesCreated;
            public bool Success;
            public string ErrorMessage;
            // The family's own category disagreed with the declared one. A silent
            // correction would hide that the family was authored against the wrong
            // template, so it is carried to the results table as a FINDING.
            public bool CategoryMismatch;
            // Whether THIS run tried to move the family to another category -
            // the one thing Revit refuses to do on reload.
            public bool CategoryChanged;
            // Visibility gates converted from Instance to Type by this run.
            public int ScopeFixed;
            public string CategoryNote;
        }

        private PropResult PropagateOne(Document doc,
            Autodesk.Revit.ApplicationServices.Application app,
            Family master, Family target, string sharedParamFile,
            List<string> styleAndVisParams, List<TypeVariantSpec> variants,
            Dictionary<string, ElementId> arrowheads,
            RecategoriseMode mode = RecategoriseMode.EnforceDeclared)
        {
            var result = new PropResult();
            string targetName = target.Name;

            // Reading the category off the TARGET propagates whatever the target
            // already is, so a mis-categorised family stays mis-categorised through
            // every propagation forever. Confirmed live: "STING - Air Terminal Tag"
            // is a Generic Model Tag, so Revit never offers it for Air Terminals.
            //
            // Resolve against the DECLARED category in STING_TAG_CONFIG_v5_0_*.csv
            // instead, and fall back to the family's own only when nothing is declared.
            var catRes = TagCategoryResolver.Resolve(doc, target);
            ElementId existingCatId = target.FamilyCategory?.Id;
            string existingCatName = target.FamilyCategory?.Name ?? "";

            // KeepExisting makes the load a same-name-same-category overwrite.
            // EnforceDeclared is the correct end state and the one Revit can
            // refuse, so which one ran has to be visible in the result.
            ElementId targetCatId = mode == RecategoriseMode.KeepExisting
                ? existingCatId
                : (catRes.DeclaredTagCategory?.Id ?? existingCatId);
            result.CategoryMismatch = catRes.IsMismatch;
            result.CategoryNote = catRes.Note;
            result.CategoryChanged = targetCatId != null && existingCatId != null &&
                                    targetCatId != existingCatId;
            if (catRes.IsMismatch)
                StingLog.Warn($"PropagateUniversalTag: '{targetName}' — {catRes.Note}" +
                              (mode == RecategoriseMode.KeepExisting
                                  ? " (KeepExisting: left as it is for this run)"
                                  : ""));
            Document famDoc = null;
            string tempDir = null; // hoisted so the catch below can clean a half-made temp dir

            using (var tg = new TransactionGroup(doc, $"STING Propagate → {targetName}"))
            {
                try
                {
                    tg.Start();

                    // Fresh clone of the master each iteration — recategorise mutates
                    // the family document, so we must not reuse it across targets.
                    famDoc = doc.EditFamily(master);
                    if (famDoc == null)
                    {
                        result.ErrorMessage = "EditFamily(master) returned null";
                        tg.RollBack();
                        return result;
                    }

                    FamilyManager fm = famDoc.FamilyManager;
                    var defFile = app.OpenSharedParameterFile();
                    if (defFile == null)
                    {
                        result.ErrorMessage = "OpenSharedParameterFile returned null";
                        famDoc.Close(false); famDoc = null;
                        tg.RollBack();
                        return result;
                    }

                    // Resolve the target's tag category inside the family document.
                    // Built-in category ids are document-independent, so GetCategory
                    // by the target family's category id returns the same category
                    // in famDoc.
                    Category targetCat = (targetCatId != null)
                        ? Category.GetCategory(famDoc, targetCatId)
                        : null;
                    if (targetCat == null)
                    {
                        result.ErrorMessage = $"Could not resolve target tag category ({target.FamilyCategory?.Name})";
                        famDoc.Close(false); famDoc = null;
                        tg.RollBack();
                        return result;
                    }

                    using (var tx = new Transaction(famDoc, "STING Recategorise + author variants"))
                    {
                        tx.Start();

                        // (a) Recategorise — the single net-new Revit API call. Skip if
                        // the master is already in the target category (recategorising
                        // to the same category is a harmless no-op but avoid it anyway).
                        try
                        {
                            if (famDoc.OwnerFamily.FamilyCategory == null ||
                                famDoc.OwnerFamily.FamilyCategory.Id != targetCat.Id)
                            {
                                famDoc.OwnerFamily.FamilyCategory = targetCat;
                            }
                        }
                        catch (Exception catEx)
                        {
                            result.ErrorMessage = $"Set FamilyCategory failed: {catEx.Message}";
                            tx.RollBack();
                            famDoc.Close(false); famDoc = null;
                            tg.RollBack();
                            return result;
                        }

                        // (b) No rename here: a loaded family's project name comes
                        // from its .rfa FILE name, and renaming famDoc.OwnerFamily
                        // does not survive SaveAs. The save below writes the clone
                        // under the target's exact file name instead.

                        // (c) Ensure style/visibility params exist, then (re)create the
                        // data-driven depth/style type variants.
                        result.ParamsAdded = AddMissingParams(fm, defFile, styleAndVisParams);
                        result.ScopeFixed = MakeVisibilityParamsType(fm);
                        result.TypesCreated = TagTypeVariantWriter.CreateStandardVariants(fm, variants, arrowheads);

                        tx.Commit();
                    }

                    // ── Atomic save-then-publish (from MigrateTagLabelReferences) ──
                    // The temp copy keeps the target's EXACT file name (family name
                    // == file name in Revit) inside a throwaway subfolder, so the
                    // LoadFamily below overwrites the target family in the project
                    // instead of minting a duplicate named after a temp file.
                    string outDir = TagFamilyConfig.GetOutputDirectory();
                    Directory.CreateDirectory(outDir);
                    string rfaFileName = targetName.Replace('/', '-') + ".rfa";

                    // A slash (or any other char stripped by sanitisation) in the
                    // family name makes the .rfa FILE name diverge from the
                    // family's project name. LoadFamily keys on the file name, so
                    // saving under the sanitised name would mint a NEW mis-named
                    // family and leave the real target untouched — reintroducing
                    // the exact bug this fix closes, and the duplicate is NOT
                    // temp-named so the purge won't reap it. Fail explicitly.
                    if (!string.Equals(Path.GetFileNameWithoutExtension(rfaFileName), targetName, StringComparison.Ordinal))
                    {
                        result.ErrorMessage =
                            $"Family name '{targetName}' has no 1:1 file name (sanitised to '{rfaFileName}'); " +
                            "propagating would create a mis-named duplicate instead of overwriting the target. " +
                            "Rename the family without '/' and retry.";
                        StingLog.Warn($"PropagateUniversalTag: skipping '{targetName}' — sanitised file name diverges from family name");
                        famDoc.Close(false); famDoc = null;
                        tg.RollBack();
                        return result;
                    }

                    string finalPath = Path.Combine(outDir, rfaFileName);
                    tempDir = Path.Combine(outDir,
                        ".sting-propagate-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                    Directory.CreateDirectory(tempDir);
                    string tempPath = Path.Combine(tempDir, rfaFileName);

                    bool savedOk = false;
                    try
                    {
                        var saveOpts = new SaveAsOptions { OverwriteExistingFile = true, MaximumBackups = 1 };
                        famDoc.SaveAs(tempPath, saveOpts);
                        savedOk = true;
                    }
                    catch (Exception saveEx)
                    {
                        result.ErrorMessage = $"SaveAs failed: {saveEx.Message}";
                        StingLog.Warn($"{targetName}: SaveAs failed: {saveEx.Message}");
                    }
                    // Document.Close returns FALSE when Revit will not close the
                    // document, and that return was discarded. A clone that stays
                    // open is a family document named after the TARGET, and Revit
                    // refuses to load a family while a document for it is open -
                    // returning a bare false with no failure message, which is
                    // precisely what the 21:02 run reported.
                    bool closedOk;
                    try { closedOk = famDoc.Close(false); }
                    catch (Exception closeEx)
                    {
                        closedOk = false;
                        StingLog.Warn($"{targetName}: closing the clone threw: {closeEx.Message}");
                    }
                    famDoc = null;
                    if (!closedOk)
                        StingLog.Warn($"{targetName}: the clone document did not close - " +
                                      "it is still open under the target's name and will block the load");

                    if (!savedOk)
                    {
                        TryDeleteTempDir(tempDir);
                        try { tg.RollBack(); } catch (Exception rbEx) { StingLog.Warn($"{targetName}: tg.RollBack after save fail: {rbEx.Message}"); }
                        return result;
                    }

                    bool loadedOk = false;
                    string loadThrew = null;
                    // LoadFamily's false return carries no reason; Revit's own
                    // explanation arrives as failure messages on this transaction
                    // and, uncaptured, only ever reaches a modal dialog.
                    var loadFailures = new CapturingFailuresPreprocessor();
                    using (var loadTx = new Transaction(doc, $"STING Reload {targetName}"))
                    {
                        loadTx.Start();
                        var fho = loadTx.GetFailureHandlingOptions();
                        loadTx.SetFailureHandlingOptions(fho.SetFailuresPreprocessor(loadFailures));
                        try { loadedOk = doc.LoadFamily(tempPath, new TagFamilyLoadOptions(), out _); }
                        catch (Exception loadEx)
                        {
                            loadThrew = loadEx.Message;
                            StingLog.Warn($"{targetName}: LoadFamily: {loadEx.Message}");
                            loadedOk = false;
                        }
                        if (loadedOk) loadTx.Commit(); else loadTx.RollBack();
                    }

                    if (!loadedOk)
                    {
                        TryDeleteTempDir(tempDir);
                        string why = loadFailures.Summary() ?? loadThrew;
                        if (why == null)
                        {
                            // A refused load with no failure message has one common
                            // cause: a document for this family is open in the
                            // session. Revit will not overwrite a family it is
                            // editing, and says nothing. Look, rather than guess.
                            Document openDoc = FindOpenFamilyDocument(app, targetName, null);
                            if (openDoc != null)
                                why = $"'{targetName}' is open in the Family Editor " +
                                      $"({openDoc.PathName ?? openDoc.Title}). Revit will not load a family " +
                                      "while a document for it is open. Close that tab (or Load into Project " +
                                      "and Close) and re-run.";
                            else if (!closedOk)
                                why = "the clone document could not be closed, so it was still open under " +
                                      "this family's name when the load was attempted.";
                            else if (result.CategoryChanged)
                            {
                                // The leading explanation once the open-document theory
                                // is ruled out: Revit matches a reloaded family by name
                                // and will not move it to another category. Count what a
                                // delete would cost so the operator can decide, rather
                                // than deleting on a hypothesis.
                                int placed = CountPlacedInstances(doc, target);
                                string newCat = Category.GetCategory(doc, targetCatId)?.Name ?? "the declared category";
                                why = $"this run recategorised the family from '{existingCatName}' to " +
                                      $"'{newCat}', and Revit will not change a loaded family's " +
                                      "category by reloading over it. Re-run and choose KEEP each family's " +
                                      "current category to propagate the label, or delete " +
                                      $"'{targetName}' from the project first (" +
                                      (placed < 0 ? "instance count unavailable" :
                                          placed + " placed instance" + (placed == 1 ? "" : "s") + " would be lost") +
                                      ") and re-load it from disk.";
                            }
                        }
                        result.ErrorMessage = why == null
                            ? "LoadFamily back into project failed, and Revit reported no failure message. " +
                              "No document for this family is open either - see the log for the pre-flight line."
                            : $"LoadFamily back into project failed: {why}";
                        StingLog.Warn($"PropagateUniversalTag: '{targetName}' load refused — {result.ErrorMessage}");
                        try { tg.RollBack(); } catch (Exception rbEx) { StingLog.Warn($"{targetName}: tg.RollBack after load fail: {rbEx.Message}"); }
                        return result;
                    }

                    // Everything succeeded — atomically replace the canonical .rfa.
                    // On move failure the temp dir is kept so the artefact survives.
                    try
                    {
                        if (File.Exists(finalPath)) File.Delete(finalPath);
                        File.Move(tempPath, finalPath);
                        TryDeleteTempDir(tempDir);
                    }
                    catch (Exception mvEx)
                    {
                        StingLog.Warn($"{targetName}: move temp → final: {mvEx.Message} (project state OK; artefact at {tempPath})");
                    }

                    tg.Assimilate();
                    result.Success = true;
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = ex.Message;
                    StingLog.Error($"PropagateUniversalTag: {targetName}", ex);
                    try { if (tg.HasStarted() && !tg.HasEnded()) tg.RollBack(); } catch { }
                    try { famDoc?.Close(false); } catch (Exception closeEx) { StingLog.Warn($"Close famDoc: {closeEx.Message}"); }
                    if (tempDir != null) TryDeleteTempDir(tempDir); // don't leak a half-made temp dir on an unhandled throw
                }
            }
            return result;
        }

        // ──────────────────────────────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────────────────────────────

        /// <summary>True when a family name is a leftover from a pre-fix run
        /// that loaded the clone under its temp file name. Shared with
        /// <see cref="MigrateTagLabelReferencesCommand"/> so its collector can
        /// exclude the same junk (both markers covered here).</summary>
        internal static bool IsTempNamed(string familyName)
        {
            if (string.IsNullOrEmpty(familyName)) return false;
            return familyName.IndexOf(".rfa.sting-propagate-", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   familyName.IndexOf(".rfa.sting-migrate-", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// An open family document whose file name matches <paramref name="familyName"/>,
        /// or null. Revit refuses to load a family while a document for it is open, and
        /// reports nothing when it does, so this is the difference between a named cause
        /// and a shrug.
        /// </summary>
        internal static Document FindOpenFamilyDocument(
            Autodesk.Revit.ApplicationServices.Application app, string familyName, Document exclude)
        {
            if (app == null || string.IsNullOrEmpty(familyName)) return null;
            try
            {
                foreach (Document d in app.Documents)
                {
                    if (d == null || !d.IsFamilyDocument) continue;
                    if (exclude != null && ReferenceEquals(d, exclude)) continue;
                    // Title carries the file name (with or without .rfa depending on
                    // version); PathName is empty for a document opened by EditFamily.
                    string bare = Path.GetFileNameWithoutExtension(d.Title ?? "");
                    if (string.Equals(bare, familyName, StringComparison.OrdinalIgnoreCase)) return d;
                }
            }
            catch (Exception ex) { StingLog.Warn($"FindOpenFamilyDocument('{familyName}'): {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// How many placed elements use this family's types. Called only on the
        /// failure path, where a full-document pass is cheap next to the run itself,
        /// and only to tell the operator what deleting the family would cost.
        /// Returns -1 when it could not be counted: reporting that as 0 would
        /// invite a delete on no evidence.
        /// </summary>
        private static int CountPlacedInstances(Document doc, Family family)
        {
            try
            {
                var symbolIds = new HashSet<ElementId>(family.GetFamilySymbolIds());
                if (symbolIds.Count == 0) return 0;
                return new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Count(e => symbolIds.Contains(e.GetTypeId()));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CountPlacedInstances: {ex.Message}");
                return -1;
            }
        }

        private static void TryDeleteTempDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch (Exception ex) { StingLog.Warn($"Delete temp dir '{dir}': {ex.Message}"); }
        }

        /// <summary>
        /// Add every wanted shared parameter that the family does not already carry.
        /// Style/visibility params are TYPE params (mirrors MigrateTagFamilies).
        /// Must run inside an open transaction on the family document.
        /// </summary>
        /// <summary>
        /// Convert any Instance-scoped tier gate in the clone to Type, and report how
        /// many had to be converted.
        ///
        /// MR_PARAMETERS.csv declares every TAG_PARA_STATE_*_BOOL and
        /// TAG_WARN_VISIBLE_BOOL as Type, and SetParagraphDepthCommand writes to
        /// element TYPES - so an Instance-scoped gate is present, looks right in
        /// Family Types, and can never be driven. The universal master carries
        /// _1, _2 and WARN_VISIBLE as Instance while _4.._10 are Type (seen
        /// 2026-09-17), and AddMissingParams skips a parameter that already exists,
        /// so without this the split is copied into all 206 families.
        ///
        /// Must run inside an open transaction on the family document.
        /// </summary>
        private static int MakeVisibilityParamsType(FamilyManager fm)
        {
            if (fm == null) return 0;
            int converted = 0;
            foreach (string name in TagFamilyConfig.VisibilityParams)
            {
                try
                {
                    FamilyParameter fp = fm.get_Parameter(name);
                    if (fp == null || !fp.IsInstance) continue;
                    fm.MakeType(fp);
                    converted++;
                    StingLog.Info($"PropagateUniversalTag: {name} converted Instance -> Type in the clone");
                }
                catch (Exception ex)
                {
                    // Reported, not swallowed: a gate left Instance is a tier that
                    // silently cannot be switched.
                    StingLog.Warn($"PropagateUniversalTag: could not convert {name} to Type: {ex.Message}");
                }
            }
            return converted;
        }

        private static int AddMissingParams(FamilyManager fm, DefinitionFile defFile, List<string> wanted)
        {
            int added = 0;
            var existing = new HashSet<string>(
                fm.GetParameters().Select(p => p.Definition.Name),
                StringComparer.OrdinalIgnoreCase);

            foreach (string paramName in wanted)
            {
                if (string.IsNullOrEmpty(paramName) || existing.Contains(paramName)) continue;

                ExternalDefinition extDef = null;
                foreach (DefinitionGroup grp in defFile.Groups)
                {
                    foreach (Definition def in grp.Definitions)
                        if (def.Name == paramName && def is ExternalDefinition ed) { extDef = ed; break; }
                    if (extDef != null) break;
                }
                if (extDef == null) continue;

                try
                {
                    fm.AddParameter(extDef, GroupTypeId.General, /*isInstance*/ false);
                    added++;
                    existing.Add(paramName);
                }
                catch (Exception ex) { StingLog.Warn($"AddMissingParams '{paramName}': {ex.Message}"); }
            }
            return added;
        }

        /// <summary>Pick the universal master family from the loaded STING tag families.</summary>
        private static Family PickMaster(List<Family> families)
        {
            var items = families
                .Select(f => new StingListPicker.ListItem
                {
                    Label = f.Name,
                    Detail = f.FamilyCategory?.Name ?? "",
                    Tag = f,
                    IsSelected = false,
                })
                .ToList();

            // Pre-select the family that LOOKS like the master, because
            // StingListPicker.AcceptSelection falls back to the FIRST item when a
            // single-select list is OK'd with nothing highlighted. Unseeded, that
            // fallback silently nominates the alphabetically-first STING tag
            // family - "STING - 5-Gauss Marker Tag" - and propagates ITS label
            // over every target. Seeding makes the fallback land on the right one.
            var likely = items.FirstOrDefault(i => LooksLikeUniversalMaster(i.Label));
            if (likely != null) likely.IsSelected = true;

            List<StingListPicker.ListItem> picked;
            try
            {
                picked = StingListPicker.Show(
                    "Choose the UNIVERSAL master family",
                    "Pick the one family that carries the hand-built universal label " +
                    "(from UNIVERSAL_TAG_LABEL_BUILD_SHEET.md). Every OTHER loaded STING " +
                    "tag family becomes a propagation target.",
                    items, allowMultiSelect: false);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PickMaster: picker failed: {ex.Message}");
                return null;
            }

            var chosen = picked?.FirstOrDefault()?.Tag as Family;
            if (chosen == null) return null;

            // Belt as well as braces: whatever route produced this family, a
            // non-universal master overwrites 205 labels with the wrong one, and
            // that is not recoverable from inside Revit. Name it and make the
            // operator agree.
            if (!LooksLikeUniversalMaster(chosen.Name))
            {
                var warn = new TaskDialog("Propagate Universal Tag");
                warn.MainInstruction = $"'{chosen.Name}' does not look like the universal master.";
                warn.MainContent =
                    "Its label will be cloned over every target family, replacing theirs.\n\n" +
                    "The master is normally named 'STING_Tag_Universal' or similar. If you clicked " +
                    "OK without highlighting a family, this is the first one in the list, not your " +
                    "master.\n\nPropagate this family's label anyway?";
                warn.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                warn.DefaultButton = TaskDialogResult.No;
                if (warn.Show() != TaskDialogResult.Yes)
                {
                    StingLog.Info($"PickMaster: '{chosen.Name}' declined as master - nothing done");
                    return null;
                }
                StingLog.Warn($"PickMaster: proceeding with non-universal master '{chosen.Name}' (operator confirmed)");
            }
            return chosen;
        }

        /// <summary>
        /// Whether a family name reads as the hand-built universal master. Used only
        /// to seed the picker and to challenge an unlikely choice - never to pick
        /// silently on the operator's behalf.
        /// </summary>
        private static bool LooksLikeUniversalMaster(string familyName)
        {
            if (string.IsNullOrEmpty(familyName)) return false;
            return familyName.IndexOf("Tag_Universal", StringComparison.OrdinalIgnoreCase) >= 0
                || familyName.IndexOf("Universal Tag", StringComparison.OrdinalIgnoreCase) >= 0
                || familyName.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Scope the propagation targets: ALL candidates, or CHOOSE a subset
        /// (used for the mandatory one-family smoke test — pick Duct only).
        /// Returns null when cancelled.
        /// </summary>
        private static List<Family> ChooseTargets(List<Family> candidates, out string scopeLabel)
        {
            scopeLabel = "";
            var td = new TaskDialog("Propagate Universal Tag — scope");
            td.MainInstruction = $"Propagate to which of the {candidates.Count} target families?";
            td.MainContent =
                "SMOKE TEST FIRST: choose a single family (Duct) and verify the result " +
                "in Revit before scaling to all 206.";
            td.CommonButtons = TaskDialogCommonButtons.Cancel;
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "CHOOSE families … (recommended — start with Duct)",
                "Multi-select picker. Tick just the target(s) to propagate to.");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                $"ALL {candidates.Count} families",
                "Propagate to every loaded STING tag family. Only after the smoke test passes.");

            var choice = td.Show();
            if (choice == TaskDialogResult.Cancel)
            {
                StingLog.Info("ChooseTargets: scope dialog closed with Cancel");
                return null;
            }

            if (choice == TaskDialogResult.CommandLink2)
            {
                scopeLabel = "ALL";
                StingLog.Info($"ChooseTargets: ALL selected - {candidates.Count} target(s)");
                return candidates;
            }

            // CHOOSE
            var items = candidates
                .Select(f => new StingListPicker.ListItem
                {
                    Label = f.Name,
                    Detail = f.FamilyCategory?.Name ?? "",
                    Tag = f,
                    IsSelected = f.Name.IndexOf("Duct", StringComparison.OrdinalIgnoreCase) >= 0,
                })
                .ToList();

            List<StingListPicker.ListItem> picked;
            try
            {
                picked = StingListPicker.Show(
                    "Choose target families",
                    "Duct is ALREADY highlighted for the smoke test - just press OK. " +
                    "This list allows multiple selections, so clicking a highlighted row " +
                    "turns it OFF; use Ctrl+click to add another without losing it.",
                    items, allowMultiSelect: true);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ChooseTargets: picker failed: {ex.Message}");
                return null;
            }
            // A cancel and an empty tick-list are NOT the same thing, and treating
            // them the same is what made this look like the command dying. The
            // empty case has a specific cause: the Duct rows arrive PRE-SELECTED
            // above, the picker's list box is SelectionMode.Multiple, and in that
            // mode a plain click TOGGLES a row. So clicking the duct family to
            // "pick" it un-picks it, and OK then returns nothing. Observed
            // 2026-09-17 at 20:57:58: "scope dialog cancelled - nothing done"
            // seconds after the operator picked Duct.
            if (picked == null)
            {
                StingLog.Info("ChooseTargets: target picker cancelled");
                return null;
            }
            if (picked.Count == 0)
            {
                StingLog.Warn("ChooseTargets: OK pressed with nothing highlighted - " +
                              "likely the pre-selected row was clicked and toggled off");
                var empty = new TaskDialog("Propagate Universal Tag — nothing selected");
                empty.MainInstruction = "No target families were highlighted, so nothing was propagated.";
                empty.MainContent =
                    "The list allows multiple selections, which means a click TOGGLES a row.\n" +
                    "The Duct family starts out already highlighted for the smoke test, so " +
                    "clicking it turns it OFF.\n\n" +
                    "Re-run and either press OK straight away (Duct is already highlighted), " +
                    "or click a DIFFERENT row to add it. Ctrl+click toggles one row without " +
                    "disturbing the rest.";
                empty.CommonButtons = TaskDialogCommonButtons.Close;
                empty.Show();
                return null;
            }

            var chosen = picked.Select(p => p.Tag as Family).Where(f => f != null).ToList();
            scopeLabel = $"CHOSEN ({chosen.Count})";
            StingLog.Info($"ChooseTargets: {chosen.Count} target(s) chosen - " +
                          string.Join(", ", chosen.Take(5).Select(f => f.Name)) +
                          (chosen.Count > 5 ? $", +{chosen.Count - 5} more" : ""));
            return chosen;
        }
    }
}
