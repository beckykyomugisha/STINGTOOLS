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
                return Result.Cancelled;
            }

            // ── 1. Pick the universal master from the loaded families ──
            Family master = PickMaster(stingFamilies);
            if (master == null) return Result.Cancelled;

            // ── 2. Targets = every other loaded STING tag family, scoped ──
            var candidates = stingFamilies.Where(f => f.Id != master.Id).ToList();
            var targets = ChooseTargets(candidates, out string scopeLabel);
            if (targets == null) return Result.Cancelled;
            if (targets.Count == 0)
            {
                TaskDialog.Show("Propagate Universal Tag", "No target families selected.");
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
            if (confirm.Show() != TaskDialogResult.Ok) return Result.Cancelled;

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

            var progress = StingProgressDialog.Show("Propagate Universal Tag", targets.Count);
            var rows = new List<List<string>>();
            int succeeded = 0, failed = 0, cancelled = 0, totalTypes = 0, totalParams = 0;
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
                        styleAndVisParams, variants, arrowheads);
                    totalTypes += r.TypesCreated;
                    totalParams += r.ParamsAdded;
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

            var td = new TaskDialog("Propagate Universal Tag — done");
            td.MainInstruction = $"{succeeded} propagated, {failed} failed" +
                                 (cancelled > 0 ? $", {cancelled} cancelled" : "");
            td.MainContent =
                $"Master: {master.Name}\n" +
                $"Scope:  {scopeLabel}\n\n" +
                $"Params added: {totalParams}\n" +
                $"Type variants (re)created: {totalTypes}\n" +
                (junkDeleted > 0 ? $"Purged stale temp-named duplicates: {junkDeleted}\n" : "") +
                (xlsx != null ? $"\nReport: {xlsx}" : "");
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
        }

        private PropResult PropagateOne(Document doc,
            Autodesk.Revit.ApplicationServices.Application app,
            Family master, Family target, string sharedParamFile,
            List<string> styleAndVisParams, List<TypeVariantSpec> variants,
            Dictionary<string, ElementId> arrowheads)
        {
            var result = new PropResult();
            string targetName = target.Name;
            ElementId targetCatId = target.FamilyCategory?.Id;
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
                    famDoc.Close(false);
                    famDoc = null;

                    if (!savedOk)
                    {
                        TryDeleteTempDir(tempDir);
                        try { tg.RollBack(); } catch (Exception rbEx) { StingLog.Warn($"{targetName}: tg.RollBack after save fail: {rbEx.Message}"); }
                        return result;
                    }

                    bool loadedOk = false;
                    using (var loadTx = new Transaction(doc, $"STING Reload {targetName}"))
                    {
                        loadTx.Start();
                        try { loadedOk = doc.LoadFamily(tempPath, new TagFamilyLoadOptions(), out _); }
                        catch (Exception loadEx)
                        {
                            StingLog.Warn($"{targetName}: LoadFamily: {loadEx.Message}");
                            loadedOk = false;
                        }
                        if (loadedOk) loadTx.Commit(); else loadTx.RollBack();
                    }

                    if (!loadedOk)
                    {
                        TryDeleteTempDir(tempDir);
                        result.ErrorMessage = "LoadFamily back into project failed";
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
            return picked?.FirstOrDefault()?.Tag as Family;
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
            if (choice == TaskDialogResult.Cancel) return null;

            if (choice == TaskDialogResult.CommandLink2)
            {
                scopeLabel = "ALL";
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
                    "Tick the families to propagate the universal label to. " +
                    "Duct families are pre-ticked for the smoke test.",
                    items, allowMultiSelect: true);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ChooseTargets: picker failed: {ex.Message}");
                return null;
            }
            if (picked == null || picked.Count == 0) return null;

            var chosen = picked.Select(p => p.Tag as Family).Where(f => f != null).ToList();
            scopeLabel = $"CHOSEN ({chosen.Count})";
            return chosen;
        }
    }
}
