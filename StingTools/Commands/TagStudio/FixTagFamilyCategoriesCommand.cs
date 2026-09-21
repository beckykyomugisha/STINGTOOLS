// ============================================================================
// FixTagFamilyCategoriesCommand.cs — correct a tag family's category ON DISK.
//
// WHY THIS EXISTS
//
// Most of the 206 STING tag families are categorised "Generic Model Tags"
// rather than the category they were written for, so Revit never offers them
// for the elements they tag: "STING - Duct Tag" as a Generic Model Tag is not
// offered for a duct, and "Tag All Not Tagged" lists it against the wrong row.
// The intended category is already declared, per family, in
// STING_TAG_CONFIG_v5_0_*.csv — TagCategoryResolver reads it.
//
// PropagateUniversalTagCommand already sets the category on its clone, and that
// half works. What does not work is getting the corrected family back into a
// project that already holds it: Revit matches a reloaded family by NAME and
// refuses to move a loaded family to another category, returning a bare false.
// Proven on this machine — three propagation runs refused with no failure
// message, then succeeded the moment the category no longer needed changing
// (2026-09-17, 19:58 / 21:02 / 21:23 refused, 22:16 succeeded).
//
// WHAT THIS DOES
//
// Sidesteps the reload entirely. Each .rfa is opened STANDALONE
// (app.OpenDocumentFile — no project involved), its category is set inside a
// transaction on the family document, and the file is saved. There is no
// LoadFamily, so there is nothing to refuse. A project that loads the corrected
// file afterwards gets the right category from the start.
//
// WHAT IT CANNOT DO
//
// It cannot fix a project that ALREADY has the stale family loaded. That
// project keeps its Generic Model Tag version until the family is deleted from
// it and re-loaded — which takes any placed tag with it. Fixing the library is
// the part that can be automated; adopting it into an existing model is a
// decision with a cost, and the report names that cost rather than paying it.
//
// SAFETY
//
// Audit is the default and writes nothing. Apply copies every file it is about
// to touch into a timestamped _precategory\ folder first, and it is deliberately
// easy to run on one family: whether a category change preserves a family's
// label rows is still UNPROVEN (docs/UNIVERSAL_TAG_PROPAGATION_TEST.md V2), and
// this command would otherwise be a way to find that out 206 times at once.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Select;   // StingListPicker lives here, not in .UI
using StingTools.Tags;
using StingTools.UI;

namespace StingTools.Commands.TagStudio
{
    /// <summary>One family's category audit, and what was done about it.</summary>
    public class CategoryFixRow
    {
        public string FamilyName { get; set; } = "";
        public string Actual { get; set; } = "";
        public string Declared { get; set; } = "";
        /// <summary>SKIP / FIX / ALREADY-CORRECT / NO-DECLARATION / UNRESOLVED / ERROR.</summary>
        public string Verdict { get; set; } = "";
        public string Detail { get; set; } = "";
        public int PlacedInProject { get; set; } = -1;
    }

    /// <summary>
    /// Opens each tag family standalone and sets its category to the one declared
    /// for it, writing the .rfa in place. Audit-only unless the operator asks for
    /// the write.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class FixTagFamilyCategoriesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // NOT commandData.Application. A dock-panel button reaches a command through
            // StingCommandHandler.RunCommand<T>, which calls Execute(null, ...) by design -
            // "commands use StingCommandHandler.CurrentApp as fallback". Reading
            // commandData directly is why this reported "No Revit application." on the
            // first press (2026-09-18). ParameterHelpers.GetApp is the accessor that
            // knows about both callers.
            UIApplication uiApp;
            try { uiApp = ParameterHelpers.GetApp(commandData); }
            catch (Exception ex)
            {
                StingLog.Warn($"FixTagFamilyCategories: no UIApplication: {ex.Message}");
                TaskDialog.Show("Fix Tag Family Categories",
                    "Could not reach Revit. Run this from the STING dock panel or the ribbon.");
                return Result.Failed;
            }
            var app = uiApp.Application;
            Document projectDoc = null;
            try { projectDoc = uiApp.ActiveUIDocument?.Document; } catch { }

            // ── 1. Where are the families? ──
            string folder = PickFolder();
            if (string.IsNullOrEmpty(folder))
            {
                StingLog.Info("FixTagFamilyCategories: no folder chosen - nothing done");
                return Result.Cancelled;
            }

            var rfas = Directory.EnumerateFiles(folder, "*.rfa", SearchOption.TopDirectoryOnly)
                                .Where(p => !Path.GetFileName(p).StartsWith(".", StringComparison.Ordinal))
                                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                                .ToList();
            if (rfas.Count == 0)
            {
                TaskDialog.Show("Fix Tag Family Categories", $"No .rfa files directly under:\n{folder}");
                StingLog.Info($"FixTagFamilyCategories: no .rfa under {folder} - nothing done");
                return Result.Cancelled;
            }

            StingLog.Info($"FixTagFamilyCategories: {rfas.Count} .rfa file(s) under {folder}; " +
                          $"{TagCategoryResolver.DeclaredCount} family/families have a declared category; " +
                          $"categories resolved against {(projectDoc != null ? "the open project" : "each family document (no project open - expect more UNRESOLVED)")}");

            // ── 2. Audit or apply? ──
            var mode = ChooseMode(rfas.Count);
            if (mode == RunMode.Cancel)
            {
                StingLog.Info("FixTagFamilyCategories: mode dialog cancelled - nothing done");
                return Result.Cancelled;
            }

            // ── 3. Scope ──
            if (mode == RunMode.Apply)
            {
                rfas = ChooseFiles(rfas);
                if (rfas == null || rfas.Count == 0)
                {
                    StingLog.Info("FixTagFamilyCategories: no files chosen for apply - nothing done");
                    return Result.Cancelled;
                }

                var confirm = new TaskDialog("Fix Tag Family Categories");
                confirm.MainInstruction = $"Rewrite the category in {rfas.Count} .rfa file(s)?";
                confirm.MainContent =
                    "Each file is opened on its own, its category set to the one declared in " +
                    "STING_TAG_CONFIG_v5_0_*.csv, and saved in place.\n\n" +
                    "A copy of every file is taken first, into a _precategory folder beside them.\n\n" +
                    "⚠ Whether a category change preserves a family's LABEL ROWS is still " +
                    "unproven (see UNIVERSAL_TAG_PROPAGATION_TEST.md V2). Do one family, open it, " +
                    "count the rows, and only then do the rest.\n\n" +
                    "This does NOT change any project. A project that already has one of these " +
                    "families loaded keeps its old category until the family is deleted from it " +
                    "and re-loaded.";
                confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
                confirm.DefaultButton = TaskDialogResult.Cancel;
                if (confirm.Show() != TaskDialogResult.Ok)
                {
                    StingLog.Info("FixTagFamilyCategories: apply declined - nothing done");
                    return Result.Cancelled;
                }
            }

            // ── 4. Pre-change copies ──
            string backupDir = null;
            if (mode == RunMode.Apply)
            {
                backupDir = Path.Combine(folder, "_precategory_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                try
                {
                    Directory.CreateDirectory(backupDir);
                    foreach (string src in rfas)
                        File.Copy(src, Path.Combine(backupDir, Path.GetFileName(src)), overwrite: false);
                    StingLog.Info($"FixTagFamilyCategories: {rfas.Count} file(s) copied to {backupDir}");
                }
                catch (Exception ex)
                {
                    // No backup, no write. A category change that cannot be undone is not
                    // worth the risk while V2 is unproven.
                    StingLog.Error("FixTagFamilyCategories: pre-change copy failed", ex);
                    TaskDialog.Show("Fix Tag Family Categories",
                        $"Could not copy the files to a backup folder, so nothing was changed.\n\n{ex.Message}");
                    return Result.Failed;
                }
            }

            // ── 5. Walk the families ──
            var rows = new List<CategoryFixRow>(rfas.Count);
            var progress = rfas.Count > 5
                ? StingProgressDialog.Show("Fix Tag Family Categories", rfas.Count)
                : null;
            int fixedCount = 0, alreadyOk = 0, undeclared = 0, unresolved = 0, failedCount = 0;

            try
            {
                for (int i = 0; i < rfas.Count; i++)
                {
                    string path = rfas[i];
                    string name = Path.GetFileNameWithoutExtension(path) ?? "";
                    if (progress != null)
                    {
                        progress.Increment($"{name} ({i + 1}/{rfas.Count})");
                        if (progress.IsCancelled)
                        {
                            StingLog.Info($"FixTagFamilyCategories: cancelled after {i} of {rfas.Count}");
                            break;
                        }
                    }

                    var row = InspectOne(app, projectDoc, path, mode == RunMode.Apply);
                    rows.Add(row);
                    switch (row.Verdict)
                    {
                        case "FIX": fixedCount++; break;
                        case "ALREADY-CORRECT": alreadyOk++; break;
                        case "NO-DECLARATION": undeclared++; break;
                        case "UNRESOLVED": unresolved++; break;
                        case "ERROR": failedCount++; break;
                    }
                }
            }
            finally { progress?.Close(); }

            // ── 6. Report ──
            string xlsx = null;
            try
            {
                string outDir = OutputLocationHelper.GetOutputDirectory(projectDoc);
                xlsx = Path.Combine(outDir,
                    $"STING_TagFamilyCategories_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                StingExcelExporter.ExportTable(
                    xlsx, "Categories",
                    new List<string> { "Family", "Actual", "Declared", "Verdict", "Detail", "PlacedInProject" },
                    rows.Select(r => new List<string>
                    {
                        r.FamilyName, r.Actual, r.Declared, r.Verdict, r.Detail,
                        r.PlacedInProject < 0 ? "" : r.PlacedInProject.ToString()
                    }).ToList(),
                    openFolder: false);
            }
            catch (Exception ex) { StingLog.Warn($"FixTagFamilyCategories: Excel export: {ex.Message}"); }

            string verb = mode == RunMode.Apply ? "changed" : "would change";
            var td = new TaskDialog("Fix Tag Family Categories — " +
                                    (mode == RunMode.Apply ? "done" : "audit"));
            td.MainInstruction = $"{fixedCount} famil{(fixedCount == 1 ? "y" : "ies")} {verb} category" +
                                 (failedCount > 0 ? $", {failedCount} failed" : "");
            td.MainContent =
                $"Scanned: {rows.Count}\n" +
                $"Already correct: {alreadyOk}\n" +
                $"No category declared in the tag config: {undeclared}\n" +
                $"Declared but not resolvable in this document: {unresolved}\n" +
                (mode == RunMode.Apply
                    ? $"\nPre-change copies: {backupDir}\n"
                    : "\nNothing was written — this was an audit.\n") +
                (projectDoc == null
                    ? "\nNo project open, so the \"placed instances\" column is blank.\n"
                    : "") +
                (xlsx != null ? $"\nReport: {xlsx}" : "\nNo report was written — see the log.");
            td.Show();

            StingLog.Info($"FixTagFamilyCategories: mode={mode}, scanned={rows.Count}, " +
                          $"fixed={fixedCount}, alreadyOk={alreadyOk}, undeclared={undeclared}, " +
                          $"unresolved={unresolved}, failed={failedCount}");
            return Result.Succeeded;
        }

        // ──────────────────────────────────────────────────────────────────
        //  One family
        // ──────────────────────────────────────────────────────────────────

        private static CategoryFixRow InspectOne(
            Autodesk.Revit.ApplicationServices.Application app,
            Document projectDoc, string rfaPath, bool apply)
        {
            var row = new CategoryFixRow { FamilyName = Path.GetFileNameWithoutExtension(rfaPath) ?? "" };
            Document famDoc = null;
            try
            {
                famDoc = app.OpenDocumentFile(rfaPath);
                if (famDoc == null || !famDoc.IsFamilyDocument)
                {
                    row.Verdict = "ERROR";
                    row.Detail = "not a family document";
                    return row;
                }

                Family owner = famDoc.OwnerFamily;
                row.Actual = owner?.FamilyCategory?.Name ?? "(none)";

                // Resolve by the FILE name, not owner.Name. The declarations are keyed on
                // the name a family has as a file, and a standalone-opened family document
                // does not reliably report that through OwnerFamily.Name: doing it the other
                // way round returned "no Category declared" for all 212 files on 2026-09-18
                // while 137 of them match a declaration exactly, and the audit reported
                // "0 families would change category" as a result.
                //
                // Built-in category ids are document-independent, so a tag category resolved
                // against the family document is the same one a project would resolve.
                // Resolve the DECLARED category against the project when one is open.
                // A family document carries a reduced category set, so
                // FindTagCategory could not see categories that plainly exist: 20 of the
                // 206 families came back UNRESOLVED on 2026-09-21 naming host categories
                // like "Duct Accessories", "Pipe Accessories", "Structural Trusses" and
                // "Spaces (MEP)", every one of which has a real tag category in a
                // project. Built-in category ids are document-independent, so the
                // category found in the project is mapped back into the family document
                // by id before anything is written (see the APPLY path below).
                var res = TagCategoryResolver.Resolve(
                    projectDoc ?? famDoc, row.FamilyName, owner?.FamilyCategory);

                // If the two names disagree, say so. It is the fact that hid the bug above,
                // and it is the only place it can be seen.
                string ownerName = null;
                try { ownerName = owner?.Name; } catch { }
                if (!string.IsNullOrEmpty(ownerName) &&
                    !string.Equals(ownerName, row.FamilyName, StringComparison.OrdinalIgnoreCase))
                {
                    row.Detail = $"file name '{row.FamilyName}' vs OwnerFamily.Name '{ownerName}'";
                    StingLog.Info($"FixTagFamilyCategories: '{row.FamilyName}' reports OwnerFamily.Name '{ownerName}'");
                }
                row.Declared = res?.DeclaredTagCategory?.Name ?? res?.DeclaredHostCategory ?? "";

                if (res == null || string.IsNullOrEmpty(res.DeclaredHostCategory))
                {
                    row.Verdict = "NO-DECLARATION";
                    row.Detail = Join(row.Detail,
                        res?.Note ?? "no Category declared in STING_TAG_CONFIG_v5_0_*.csv");
                    return row;   // never guess a category
                }
                if (res.DeclaredTagCategory == null)
                {
                    row.Verdict = "UNRESOLVED";
                    row.Detail = res.Note ?? "declared category has no match in this document";
                    return row;
                }
                if (!res.IsMismatch)
                {
                    row.Verdict = "ALREADY-CORRECT";
                    return row;
                }

                // How much a project would lose by adopting this correction, if one is open.
                if (projectDoc != null)
                    row.PlacedInProject = CountPlacedInProject(projectDoc, row.FamilyName);

                if (!apply)
                {
                    row.Verdict = "FIX";
                    row.Detail = Join(row.Detail,
                        $"{row.Actual} → {res.DeclaredTagCategory.Name} (audit only — not written)");
                    return row;
                }

                // The resolved Category may belong to the PROJECT document. Map it into
                // the family document by id - built-in ids are document-independent -
                // because assigning a foreign Category object is not something to find
                // out about from a half-written .rfa.
                Category target = res.DeclaredTagCategory;
                try
                {
                    Category inFam = Category.GetCategory(famDoc, target.Id);
                    if (inFam == null)
                    {
                        row.Verdict = "UNRESOLVED";
                        row.Detail = Join(row.Detail,
                            $"'{target.Name}' is not available in this family document - not written");
                        return row;
                    }
                    target = inFam;
                }
                catch (Exception mapEx)
                {
                    row.Verdict = "ERROR";
                    row.Detail = Join(row.Detail, $"mapping '{target.Name}' into the family failed: {mapEx.Message}");
                    return row;
                }

                using (var tx = new Transaction(famDoc, $"STING Set category → {target.Name}"))
                {
                    tx.Start();
                    try { famDoc.OwnerFamily.FamilyCategory = target; }
                    catch (Exception setEx)
                    {
                        tx.RollBack();
                        row.Verdict = "ERROR";
                        row.Detail = $"set FamilyCategory failed: {setEx.Message}";
                        return row;
                    }
                    tx.Commit();
                }

                // Read it back before saving. Revit can accept the assignment and land
                // somewhere else; a report that assumes the write took is worth nothing.
                string after = famDoc.OwnerFamily?.FamilyCategory?.Name ?? "(none)";
                if (!string.Equals(after, target.Name, StringComparison.OrdinalIgnoreCase))
                {
                    row.Verdict = "ERROR";
                    row.Detail = $"category read back as '{after}', not '{target.Name}' — not saved";
                    return row;
                }

                try { famDoc.Save(); }
                catch (Exception saveEx)
                {
                    row.Verdict = "ERROR";
                    row.Detail = $"save failed: {saveEx.Message}";
                    return row;
                }

                row.Verdict = "FIX";
                row.Detail = Join(row.Detail, $"{row.Actual} → {target.Name}, saved");
                StingLog.Info($"FixTagFamilyCategories: '{row.FamilyName}' {row.Actual} → {target.Name}");
                return row;
            }
            catch (Exception ex)
            {
                row.Verdict = "ERROR";
                row.Detail = ex.Message;
                StingLog.Warn($"FixTagFamilyCategories '{row.FamilyName}': {ex.Message}");
                return row;
            }
            finally
            {
                try { famDoc?.Close(false); }
                catch (Exception ex) { StingLog.Warn($"FixTagFamilyCategories: close '{row.FamilyName}': {ex.Message}"); }
            }
        }

        /// <summary>Two details in one cell, without losing either.</summary>
        private static string Join(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a)) return b;
            if (string.IsNullOrWhiteSpace(b)) return a;
            return a + "; " + b;
        }

        /// <summary>
        /// Elements in the open project placed with this family's types. Reported so the
        /// operator can see what adopting the correction would cost — a project cannot
        /// reload a family into a new category, so adoption means delete and re-load.
        /// </summary>
        private static int CountPlacedInProject(Document projectDoc, string familyName)
        {
            try
            {
                var fam = new FilteredElementCollector(projectDoc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .FirstOrDefault(f => string.Equals(f.Name, familyName, StringComparison.OrdinalIgnoreCase));
                if (fam == null) return -1;   // not loaded here at all

                var symbolIds = new HashSet<ElementId>(fam.GetFamilySymbolIds());
                if (symbolIds.Count == 0) return 0;
                return new FilteredElementCollector(projectDoc)
                    .WhereElementIsNotElementType()
                    .Count(e => symbolIds.Contains(e.GetTypeId()));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CountPlacedInProject '{familyName}': {ex.Message}");
                return -1;
            }
        }

        // ──────────────────────────────────────────────────────────────────
        //  Dialogs
        // ──────────────────────────────────────────────────────────────────

        private enum RunMode { Cancel, Audit, Apply }

        private static RunMode ChooseMode(int fileCount)
        {
            var td = new TaskDialog("Fix Tag Family Categories");
            td.MainInstruction = $"{fileCount} tag famil{(fileCount == 1 ? "y" : "ies")} found.";
            td.MainContent =
                "Most STING tag families are categorised \"Generic Model Tags\" rather than the " +
                "category declared for them, so Revit never offers them for the elements they " +
                "tag.\n\n" +
                "This corrects the .rfa files themselves — no project is touched, and no family " +
                "is re-loaded, which is what Revit refuses to do when a category changes.";
            td.CommonButtons = TaskDialogCommonButtons.Cancel;
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "AUDIT — report only (recommended first)",
                "Opens each family, compares its category with the declaration, writes nothing.");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "APPLY — rewrite the .rfa files",
                "Choose which files. Copies them first. Label rows after a category change are " +
                "still unproven — do one and check it.");

            var choice = td.Show();
            if (choice == TaskDialogResult.CommandLink1) return RunMode.Audit;
            if (choice == TaskDialogResult.CommandLink2) return RunMode.Apply;
            return RunMode.Cancel;
        }

        private static string PickFolder()
        {
            string start = null;
            try { start = TagFamilyConfig.GetOutputDirectory(); }
            catch (Exception ex) { StingLog.Warn($"FixTagFamilyCategories: default folder: {ex.Message}"); }

            try
            {
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dlg.Description = "Select the folder of STING tag families (.rfa) to correct.";
                    dlg.ShowNewFolderButton = false;
                    if (!string.IsNullOrEmpty(start) && Directory.Exists(start)) dlg.SelectedPath = start;
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return null;
                    return dlg.SelectedPath;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FixTagFamilyCategories: folder picker: {ex.Message}");
                return null;
            }
        }

        private static List<string> ChooseFiles(List<string> rfas)
        {
            var items = rfas.Select(p => new StingListPicker.ListItem
            {
                Label = Path.GetFileNameWithoutExtension(p),
                Detail = "",
                Tag = p,
                // Duct pre-highlighted, matching the smoke-test order of business. The
                // picker's list is multi-select, so a click TOGGLES a row: pressing OK
                // straight away is what selects the pre-highlighted one.
                IsSelected = (Path.GetFileNameWithoutExtension(p) ?? "")
                    .IndexOf("Duct", StringComparison.OrdinalIgnoreCase) >= 0,
            }).ToList();

            List<StingListPicker.ListItem> picked;
            try
            {
                picked = StingListPicker.Show(
                    "Choose families to re-categorise",
                    "Duct is already highlighted — press OK to take just that one. This list " +
                    "allows multiple selections, so clicking a highlighted row turns it OFF; " +
                    "Ctrl+click adds another.",
                    items, allowMultiSelect: true);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FixTagFamilyCategories: file picker: {ex.Message}");
                return null;
            }

            if (picked == null)
            {
                StingLog.Info("FixTagFamilyCategories: file picker cancelled");
                return null;
            }
            if (picked.Count == 0)
            {
                StingLog.Warn("FixTagFamilyCategories: OK pressed with nothing highlighted");
                TaskDialog.Show("Fix Tag Family Categories",
                    "No families were highlighted, so nothing was changed.\n\n" +
                    "The list toggles on click and Duct starts highlighted, so clicking it " +
                    "turns it off. Re-run and press OK without clicking.");
                return null;
            }
            return picked.Select(p => p.Tag as string).Where(s => !string.IsNullOrEmpty(s)).ToList();
        }
    }
}
