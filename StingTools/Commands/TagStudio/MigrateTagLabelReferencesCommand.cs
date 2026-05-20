using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Tags;
using StingTools.UI;

namespace StingTools.Commands.TagStudio
{
    /// <summary>
    /// Walks every loaded STING-prefixed tag family and rewrites parameter
    /// references from <c>SCHEDULE_FIELD_REMAP.csv</c>'s
    /// <c>Old_Schedule_Field → Consolidated_Parameter</c> map. Closes the
    /// Phase 187 caveat that T1-T3 hand-edited labels can't pick up renamed
    /// parameters under <c>MigrateTagFamilies</c>'s preserve-hand-edits mode.
    ///
    /// <para><b>Scope picker (Phase 188 follow-up).</b> Before iterating
    /// families the user picks ALL / RECENT (≤180 days) / CHOOSE from a
    /// TaskDialog. CHOOSE opens a <see cref="UI.StingListPicker"/> with every
    /// remap row pre-ticked when its <c>Deprecated_Date</c> is recent. This
    /// prevents bulk application of every historical consolidation
    /// (<c>SCHEDULE_FIELD_REMAP.csv</c> ships 119+ rows) against a freshly-
    /// edited project that doesn't expect them.</para>
    ///
    /// Per family, in one TransactionGroup:
    ///   1. <c>EditFamily</c> opens the .rfa.
    ///   2. For each <c>OLD → NEW</c> mapping where the family carries the OLD
    ///      shared parameter AND the storage types match: call
    ///      <c>FamilyManager.ReplaceParameter(oldParam, newExtDef, group, isInstance)</c>
    ///      — the Revit API preserves every label cell / formula / type value
    ///      automatically (the underlying shared-parameter binding is swapped
    ///      in place).
    ///   3. For each formula that references the OLD name as a token: rewrite
    ///      the formula text via <c>FamilyManager.SetFormula(target, newText)</c>.
    ///   4. <b>Atomic save-then-publish.</b> <c>SaveAs</c> writes to a temp
    ///      <c>.rfa.sting-migrate-&lt;guid&gt;.tmp</c> alongside the canonical
    ///      output path. Only after <c>LoadFamily</c> succeeds does the temp
    ///      get atomically <c>File.Move</c>'d over the canonical .rfa. If
    ///      anything fails (SaveAs / LoadFamily / unhandled exception), the
    ///      temp is deleted and the canonical .rfa stays untouched — the
    ///      project keeps its previous binding. Closes the original Phase 188
    ///      caveat about TG-rollback leaving a stale .rfa on disk.
    ///
    /// Type-mismatch cases (OLD is text, NEW is number, etc.) are reported
    /// but skipped — the API requires storage-type parity. Add a separate
    /// add-new-then-manual-rebind pass if your consolidation crossed types.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MigrateTagLabelReferencesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null) { message = "No active document."; return Result.Failed; }
            Document doc = uidoc.Document;
            Autodesk.Revit.ApplicationServices.Application app = doc.Application;

            // ── Load remap rows (with metadata) ──
            var remapRows = LoadRemapRows(out string remapReport);
            if (remapRows.Count == 0)
            {
                TaskDialog.Show("Migrate Tag Label References",
                    "No REMAPPED entries found in SCHEDULE_FIELD_REMAP.csv.\n\n" + remapReport);
                return Result.Cancelled;
            }

            // ── Let the user scope which mappings to apply ──
            //
            // SCHEDULE_FIELD_REMAP.csv may carry well over 100 historical
            // mappings; applying all of them in one pass against a freshly-
            // edited project may be more than the user wants. Prompt with
            // a 3-option dialog and, when "Choose" is picked, open a
            // multi-select StingListPicker prefilled with all rows ticked.
            Dictionary<string, string> remap;
            string scopeLabel;
            var scopeChoice = ChooseRemapScope(remapRows, out remap, out scopeLabel);
            if (scopeChoice == ScopeResult.Cancelled) return Result.Cancelled;
            if (remap.Count == 0)
            {
                TaskDialog.Show("Migrate Tag Label References",
                    "No mappings selected for this run.");
                return Result.Cancelled;
            }

            // ── Resolve shared-parameter file + open it once ──
            string sharedParamFile = app.SharedParametersFilename;
            if (string.IsNullOrEmpty(sharedParamFile) || !File.Exists(sharedParamFile))
            {
                string fallback = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
                if (!string.IsNullOrEmpty(fallback)) sharedParamFile = fallback;
            }
            if (string.IsNullOrEmpty(sharedParamFile) || !File.Exists(sharedParamFile))
            {
                message = "No shared parameter file available — set it in Manage → Shared Parameters.";
                return Result.Failed;
            }

            // ── Enumerate STING tag families ──
            var families = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.Name != null && f.Name.StartsWith(TagFamilyConfig.FamilyPrefix, StringComparison.OrdinalIgnoreCase))
                .Where(f => f.FamilyCategory != null)
                .OrderBy(f => f.Name)
                .ToList();

            if (families.Count == 0)
            {
                TaskDialog.Show("Migrate Tag Label References",
                    "No STING-prefixed tag families are loaded in this project.\n" +
                    "Run 'Create Tag Families' first.");
                return Result.Cancelled;
            }

            // ── Confirmation ──
            var confirm = new TaskDialog("Migrate Tag Label References");
            confirm.MainInstruction = $"Rewrite parameter references in {families.Count} tag families?";
            confirm.MainContent =
                $"Source remap: SCHEDULE_FIELD_REMAP.csv\n" +
                $"Scope:       {scopeLabel} ({remap.Count} mappings)\n" +
                $"Shared params: {Path.GetFileName(sharedParamFile)}\n\n" +
                "For each family, where the OLD parameter is present and the\n" +
                "NEW parameter has the same storage type, the underlying\n" +
                "shared-parameter binding is swapped via FamilyManager.\n" +
                "ReplaceParameter — every label cell, formula, and type value\n" +
                "is preserved automatically.\n\n" +
                "Formulas that reference an OLD name as a token are rewritten\n" +
                "via SetFormula. Type-mismatched remaps are reported + skipped.\n\n" +
                $"Runtime: ~2–5 minutes for {families.Count} families.\n" +
                "Press Escape between families to cancel.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel) return Result.Cancelled;

            var progress = StingProgressDialog.Show("Migrate Tag Label References", families.Count);
            var rows = new List<List<string>>();
            int succeeded = 0, failed = 0, cancelled = 0;
            int totalParamsReplaced = 0, totalFormulasRewritten = 0, totalSkippedTypeMismatch = 0;
            string originalSp = app.SharedParametersFilename;

            try
            {
                app.SharedParametersFilename = sharedParamFile;
                DefinitionFile defFile = app.OpenSharedParameterFile();
                if (defFile == null)
                {
                    message = $"OpenSharedParameterFile returned null for {sharedParamFile}";
                    return Result.Failed;
                }

                for (int i = 0; i < families.Count; i++)
                {
                    if ((i % 5) == 0 && EscapeChecker.IsEscapePressed())
                    {
                        cancelled = families.Count - i;
                        StingLog.Info($"MigrateTagLabelReferences: cancelled after {i} of {families.Count}");
                        break;
                    }

                    Family fam = families[i];
                    progress.Increment($"Rewriting {fam.Name} ({i + 1}/{families.Count})");

                    var r = MigrateOneFamily(doc, app, fam, defFile, remap);
                    totalParamsReplaced     += r.ParamsReplaced;
                    totalFormulasRewritten  += r.FormulasRewritten;
                    totalSkippedTypeMismatch += r.SkippedTypeMismatch;
                    if (r.Success) succeeded++; else failed++;

                    rows.Add(new List<string>
                    {
                        fam.Name,
                        fam.FamilyCategory?.Name ?? "",
                        r.ParamsReplaced.ToString(),
                        r.FormulasRewritten.ToString(),
                        r.SkippedTypeMismatch.ToString(),
                        r.Success ? "OK" : "FAILED",
                        r.ErrorMessage ?? "",
                    });
                }
            }
            finally
            {
                try { if (!string.IsNullOrEmpty(originalSp)) app.SharedParametersFilename = originalSp; }
                catch (Exception ex) { StingLog.Warn($"Restore shared param file: {ex.Message}"); }
                progress.Close();
            }

            // ── Report ──
            var report = new StringBuilder();
            report.AppendLine($"Families processed: {families.Count}");
            report.AppendLine($"  Succeeded:        {succeeded}");
            report.AppendLine($"  Failed:           {failed}");
            if (cancelled > 0) report.AppendLine($"  Cancelled:        {cancelled}");
            report.AppendLine();
            report.AppendLine($"Parameters replaced (binding swap): {totalParamsReplaced}");
            report.AppendLine($"Formulas rewritten:                 {totalFormulasRewritten}");
            report.AppendLine($"Skipped (storage-type mismatch):    {totalSkippedTypeMismatch}");
            report.AppendLine();
            report.AppendLine($"Remap source: SCHEDULE_FIELD_REMAP.csv ({remap.Count} mappings)");
            // Top-15 families by activity for the TaskDialog (full list in StingLog).
            var top = rows
                .OrderByDescending(r => int.TryParse(r[2], out int p) ? p : 0)
                .ThenByDescending(r => int.TryParse(r[3], out int f) ? f : 0)
                .Take(15)
                .ToList();
            if (top.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("Top families by activity:");
                report.AppendLine("  Family                                            Repl  Form  Skip  Status");
                foreach (var r in top)
                {
                    string famPad = r[0].Length > 48 ? r[0].Substring(0, 48) : r[0].PadRight(48);
                    report.AppendLine($"  {famPad}  {r[2],4}  {r[3],4}  {r[4],4}  {r[5]}");
                }
                if (rows.Count > top.Count)
                    report.AppendLine($"  … {rows.Count - top.Count} more — see StingTools.log for the full list");
            }
            foreach (var r in rows)
                StingLog.Info($"MigrateTagLabelRefs|{r[0]}|{r[1]}|repl={r[2]}|form={r[3]}|skip={r[4]}|{r[5]}|{r[6]}");

            var td = new TaskDialog("Migrate Tag Label References — done");
            td.MainInstruction = $"{succeeded} migrated, {failed} failed" +
                                 (cancelled > 0 ? $", {cancelled} cancelled" : "");
            td.MainContent = report.ToString();
            td.Show();

            return Result.Succeeded;
        }

        // ──────────────────────────────────────────────────────────────────
        //  Per-family migration
        // ──────────────────────────────────────────────────────────────────

        private class FamilyResult
        {
            public int ParamsReplaced;
            public int FormulasRewritten;
            public int SkippedTypeMismatch;
            public bool Success;
            public string ErrorMessage;
        }

        private FamilyResult MigrateOneFamily(Document doc,
            Autodesk.Revit.ApplicationServices.Application app,
            Family fam, DefinitionFile defFile, Dictionary<string, string> remap)
        {
            var result = new FamilyResult();
            Document famDoc = null;
            using (var tg = new TransactionGroup(doc, $"STING Migrate Refs: {fam.Name}"))
            {
                try
                {
                    tg.Start();
                    famDoc = doc.EditFamily(fam);
                    if (famDoc == null)
                    {
                        result.ErrorMessage = "EditFamily returned null";
                        tg.RollBack();
                        return result;
                    }

                    FamilyManager fm = famDoc.FamilyManager;

                    // Snapshot params once via GetParameters() — the FamilyParameterSet
                    // returned by fm.Parameters can be invalidated by ReplaceParameter
                    // on some Revit builds. GetParameters() returns a List we can
                    // re-snapshot safely after each mutation.
                    Dictionary<string, FamilyParameter> SnapshotByName()
                    {
                        var d = new Dictionary<string, FamilyParameter>(StringComparer.Ordinal);
                        foreach (var fp in fm.GetParameters())
                        {
                            var n = fp?.Definition?.Name;
                            if (!string.IsNullOrEmpty(n) && !d.ContainsKey(n)) d[n] = fp;
                        }
                        return d;
                    }
                    var byName = SnapshotByName();

                    // ── Pass 1: ReplaceParameter where OLD is bound + types match ──
                    using (var tx = new Transaction(famDoc, "STING Replace shared param bindings"))
                    {
                        tx.Start();
                        foreach (var kv in remap)
                        {
                            string oldName = kv.Key;
                            string newName = kv.Value;
                            if (oldName == newName) continue;
                            if (!byName.TryGetValue(oldName, out FamilyParameter oldFp)) continue;
                            if (oldFp == null || !oldFp.IsShared) continue;

                            ExternalDefinition newExt = FindSharedDefinition(defFile, newName);
                            if (newExt == null)
                            {
                                StingLog.Warn($"{fam.Name}: '{newName}' not in shared param file — skipped");
                                continue;
                            }

                            // Storage-type parity gate: ReplaceParameter throws otherwise.
                            StorageType oldStorage = SafeStorageType(oldFp);
                            StorageType newStorage = ExternalDefStorageType(newExt);
                            if (oldStorage != newStorage)
                            {
                                result.SkippedTypeMismatch++;
                                StingLog.Info($"{fam.Name}: '{oldName}' ({oldStorage}) → '{newName}' ({newStorage}) — storage mismatch, skipped");
                                continue;
                            }

                            try
                            {
                                ForgeTypeId groupId = SafeGroupTypeId(oldFp);
                                fm.ReplaceParameter(oldFp, newExt, groupId, oldFp.IsInstance);
                                result.ParamsReplaced++;
                                // Re-snapshot — the old FamilyParameter reference is now
                                // invalid; other params in byName may also have been
                                // reordered by Revit, so a full rebuild is safer than
                                // a partial patch.
                                byName = SnapshotByName();
                            }
                            catch (Exception rpEx)
                            {
                                StingLog.Warn($"{fam.Name}: ReplaceParameter('{oldName}' → '{newName}'): {rpEx.Message}");
                            }
                        }
                        tx.Commit();
                    }

                    // ── Pass 2: Rewrite formulas that reference old names as tokens ──
                    using (var tx = new Transaction(famDoc, "STING Rewrite formula references"))
                    {
                        tx.Start();
                        // Snapshot before mutating — SetFormula can reorder params
                        // on some Revit builds, same hazard as Pass 1.
                        var paramsSnap = fm.GetParameters().ToList();
                        foreach (FamilyParameter fp in paramsSnap)
                        {
                            string formula = fp?.Formula;
                            if (string.IsNullOrEmpty(formula)) continue;

                            string rewritten = RewriteFormula(formula, remap, out bool changed);
                            if (!changed) continue;

                            try
                            {
                                fm.SetFormula(fp, rewritten);
                                result.FormulasRewritten++;
                            }
                            catch (Exception fEx)
                            {
                                StingLog.Warn($"{fam.Name}: SetFormula('{fp.Definition.Name}'): {fEx.Message}");
                            }
                        }
                        tx.Commit();
                    }

                    // ── Save + load back ──
                    // Atomic save-then-publish: write to a temp .rfa first, only
                    // move into place once LoadFamily succeeds. This makes the
                    // operation transactional on disk too — if any later stage
                    // fails (SaveAs / LoadFamily / unhandled exception), the
                    // canonical TagFamilies/<name>.rfa is never replaced and
                    // the project keeps its previous family binding.
                    string outDir = TagFamilyConfig.GetOutputDirectory();
                    string finalPath = Path.Combine(outDir, fam.Name + ".rfa");
                    string tempPath = Path.Combine(outDir,
                        fam.Name + ".rfa.sting-migrate-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
                    Directory.CreateDirectory(outDir);

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
                        StingLog.Warn($"{fam.Name}: SaveAs failed: {saveEx.Message}");
                    }
                    famDoc.Close(false);
                    famDoc = null;

                    // SaveAs failure → roll Pass-1 bindings back + clean up temp.
                    if (!savedOk)
                    {
                        try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                        catch (Exception delEx) { StingLog.Warn($"{fam.Name}: delete tempPath: {delEx.Message}"); }
                        try { tg.RollBack(); } catch (Exception rbEx) { StingLog.Warn($"{fam.Name}: tg.RollBack after save fail: {rbEx.Message}"); }
                        return result;
                    }

                    bool loadedOk = false;
                    using (var loadTx = new Transaction(doc, $"STING Reload {fam.Name}"))
                    {
                        loadTx.Start();
                        try
                        {
                            loadedOk = doc.LoadFamily(tempPath, new TagFamilyLoadOptions(), out _);
                        }
                        catch (Exception loadEx)
                        {
                            StingLog.Warn($"{fam.Name}: LoadFamily back: {loadEx.Message}");
                            loadedOk = false;
                        }
                        if (loadedOk) loadTx.Commit(); else loadTx.RollBack();
                    }

                    if (!loadedOk)
                    {
                        // LoadFamily failed → the project still references the
                        // pre-migration family. Discard the temp file and roll
                        // the TG back so no half-state survives.
                        try { File.Delete(tempPath); }
                        catch (Exception delEx) { StingLog.Warn($"{fam.Name}: delete tempPath: {delEx.Message}"); }
                        result.ErrorMessage = "LoadFamily back into project failed";
                        try { tg.RollBack(); } catch (Exception rbEx) { StingLog.Warn($"{fam.Name}: tg.RollBack after load fail: {rbEx.Message}"); }
                        return result;
                    }

                    // Everything succeeded — atomically replace the canonical
                    // .rfa with the temp. File.Move(overwrite=true) is atomic
                    // on NTFS; on failure (e.g. file locked) we keep both
                    // the temp + the old canonical and report a warning, but
                    // the project itself is fine (LoadFamily already imported
                    // the temp's contents into the project document).
                    try
                    {
                        if (File.Exists(finalPath)) File.Delete(finalPath);
                        File.Move(tempPath, finalPath);
                    }
                    catch (Exception mvEx)
                    {
                        StingLog.Warn($"{fam.Name}: move tempPath → finalPath: {mvEx.Message} (project state OK; disk artefact at {tempPath})");
                    }

                    tg.Assimilate();
                    result.Success = true;
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = ex.Message;
                    StingLog.Error($"MigrateTagLabelReferences: {fam.Name}", ex);
                    try { if (tg.HasStarted() && !tg.HasEnded()) tg.RollBack(); } catch { }
                    try { famDoc?.Close(false); } catch (Exception closeEx) { StingLog.Warn($"Close famDoc: {closeEx.Message}"); }
                }
            }
            return result;
        }

        // ──────────────────────────────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────────────────────────────

        /// <summary>One row of SCHEDULE_FIELD_REMAP.csv carrying both the
        /// OLD→NEW pair and the surrounding metadata used by the scope picker
        /// (deprecation date, owner, sunset date, migration notes).</summary>
        public class RemapRow
        {
            public string OldName;
            public string NewName;
            public string DeprecatedDate;
            public string DeprecationOwner;
            public string SunsetDate;
            public string MigrationNotes;
        }

        /// <summary>Result of <see cref="ChooseRemapScope"/>.</summary>
        private enum ScopeResult { Cancelled, All, Recent, Choose }

        /// <summary>
        /// Loads REMAPPED rows from SCHEDULE_FIELD_REMAP.csv preserving metadata
        /// columns. Drop-in replacement for the earlier flat-dict
        /// <c>LoadRemap</c>; callers that just want the OLD→NEW map call
        /// <see cref="ToDict(List{RemapRow})"/> after scope filtering.
        /// </summary>
        private static List<RemapRow> LoadRemapRows(out string report)
        {
            var rows = new List<RemapRow>();
            var sb = new StringBuilder();
            string path = StingToolsApp.FindDataFile("SCHEDULE_FIELD_REMAP.csv");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                sb.AppendLine("SCHEDULE_FIELD_REMAP.csv not found on disk.");
                report = sb.ToString();
                return rows;
            }
            int total = 0, mapped = 0, selfRef = 0, skipped = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines(path))
            {
                string t = (line ?? "").Trim();
                if (t.Length == 0 || t.StartsWith("#") || t.StartsWith("Old_Schedule_Field"))
                    continue;
                total++;
                string[] parts = StingToolsApp.ParseCsvLine(t);
                if (parts.Length < 3) { skipped++; continue; }
                string oldName = parts[0]?.Trim() ?? "";
                string newName = parts[1]?.Trim() ?? "";
                string action  = parts[2]?.Trim() ?? "";
                if (oldName.Length == 0 || newName.Length == 0) { skipped++; continue; }
                if (!string.Equals(action, "REMAPPED", StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
                if (oldName == newName) { selfRef++; continue; }
                if (seen.Contains(oldName)) continue; // first-occurrence wins (mirrors LoadRemap)
                seen.Add(oldName);

                rows.Add(new RemapRow
                {
                    OldName          = oldName,
                    NewName          = newName,
                    DeprecatedDate   = parts.Length > 3 ? parts[3]?.Trim() : "",
                    DeprecationOwner = parts.Length > 4 ? parts[4]?.Trim() : "",
                    SunsetDate       = parts.Length > 5 ? parts[5]?.Trim() : "",
                    MigrationNotes   = parts.Length > 6 ? parts[6]?.Trim() : "",
                });
                mapped++;
            }
            sb.AppendLine($"  Total rows: {total}");
            sb.AppendLine($"  Loaded: {mapped} REMAPPED entries");
            sb.AppendLine($"  Skipped: {skipped} (wrong action or malformed)");
            if (selfRef > 0) sb.AppendLine($"  Skipped self-references: {selfRef}");
            report = sb.ToString();
            return rows;
        }

        private static Dictionary<string, string> ToDict(IEnumerable<RemapRow> rows)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var r in rows)
                if (!string.IsNullOrEmpty(r.OldName) && !d.ContainsKey(r.OldName))
                    d[r.OldName] = r.NewName;
            return d;
        }

        /// <summary>
        /// Prompts the user to pick a scope: ALL rows, RECENT only (Deprecated
        /// within the last 180 days), or CHOOSE (multi-select picker). Returns
        /// the filtered dict via <paramref name="remap"/> and a human label
        /// via <paramref name="scopeLabel"/>.
        /// </summary>
        private static ScopeResult ChooseRemapScope(List<RemapRow> rows,
            out Dictionary<string, string> remap, out string scopeLabel)
        {
            remap = new Dictionary<string, string>(StringComparer.Ordinal);
            scopeLabel = "";

            const int RecentDays = 180;
            int recentCount = rows.Count(r => IsWithinDays(r.DeprecatedDate, RecentDays));

            var td = new TaskDialog("Migrate Tag Label References — choose mapping scope");
            td.MainInstruction = $"Which {rows.Count} REMAPPED entries to apply?";
            td.MainContent =
                $"SCHEDULE_FIELD_REMAP.csv ships {rows.Count} historical OLD → NEW " +
                "consolidations. Applying every row against a freshly-edited project " +
                "can clobber more than you intended — pick a tighter scope below.";
            td.CommonButtons = TaskDialogCommonButtons.Cancel;
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Apply ALL {rows.Count} mappings",
                "Use every REMAPPED entry in SCHEDULE_FIELD_REMAP.csv (the original behaviour).");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                $"Apply RECENT only ({recentCount} of {rows.Count})",
                $"Only rows whose Deprecated_Date is within the last {RecentDays} days. " +
                "Best for projects where you only want to pick up the latest consolidations.");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "CHOOSE individually …",
                "Multi-select picker pre-filtered to recent rows. Search-as-you-type, " +
                "tick / untick freely.");

            var choice = td.Show();
            if (choice == TaskDialogResult.Cancel) return ScopeResult.Cancelled;

            switch (choice)
            {
                case TaskDialogResult.CommandLink1:
                    remap = ToDict(rows);
                    scopeLabel = "ALL";
                    return ScopeResult.All;

                case TaskDialogResult.CommandLink2:
                {
                    var recent = rows.Where(r => IsWithinDays(r.DeprecatedDate, RecentDays)).ToList();
                    remap = ToDict(recent);
                    scopeLabel = $"RECENT ≤{RecentDays}d";
                    return ScopeResult.Recent;
                }

                case TaskDialogResult.CommandLink3:
                {
                    // Build picker items; tick recent rows by default.
                    var items = rows.Select(r => new UI.StingListPicker.ListItem
                    {
                        Label = $"{r.OldName}  →  {r.NewName}",
                        Detail = string.IsNullOrEmpty(r.DeprecatedDate)
                            ? r.MigrationNotes
                            : $"{r.DeprecatedDate} • {r.DeprecationOwner} • {r.MigrationNotes}",
                        Tag = r,
                        IsSelected = IsWithinDays(r.DeprecatedDate, RecentDays),
                    }).ToList();

                    List<UI.StingListPicker.ListItem> picked;
                    try
                    {
                        picked = UI.StingListPicker.Show(
                            "Choose mappings to apply",
                            $"{rows.Count} REMAPPED entries — tick the ones to apply. " +
                            $"Default selection: {recentCount} rows deprecated within the last {RecentDays} days.",
                            items,
                            allowMultiSelect: true);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"ChooseRemapScope: picker failed, falling back to ALL: {ex.Message}");
                        remap = ToDict(rows);
                        scopeLabel = "ALL (picker fallback)";
                        return ScopeResult.All;
                    }

                    if (picked == null || picked.Count == 0)
                        return ScopeResult.Cancelled;

                    var chosen = new List<RemapRow>();
                    foreach (var p in picked)
                        if (p?.Tag is RemapRow rr) chosen.Add(rr);
                    remap = ToDict(chosen);
                    scopeLabel = $"CHOSEN ({chosen.Count})";
                    return ScopeResult.Choose;
                }

                default:
                    return ScopeResult.Cancelled;
            }
        }

        /// <summary>True when an ISO yyyy-MM-dd date string is within
        /// <paramref name="days"/> of today. Unparseable / empty strings
        /// return false so we never falsely classify a missing date as
        /// "recent".</summary>
        private static bool IsWithinDays(string isoDate, int days)
        {
            if (string.IsNullOrWhiteSpace(isoDate)) return false;
            if (!DateTime.TryParseExact(isoDate.Trim(), "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime dt))
                return false;
            return (DateTime.UtcNow.Date - dt.Date).TotalDays <= days;
        }

        private static ExternalDefinition FindSharedDefinition(DefinitionFile defFile, string paramName)
        {
            if (defFile == null || string.IsNullOrEmpty(paramName)) return null;
            foreach (DefinitionGroup g in defFile.Groups)
                foreach (Definition d in g.Definitions)
                    if (d.Name == paramName && d is ExternalDefinition ext) return ext;
            return null;
        }

        private static StorageType SafeStorageType(FamilyParameter fp)
        {
            try { return fp.StorageType; }
            catch { return StorageType.None; }
        }

        /// <summary>
        /// Map an ExternalDefinition's data type to Revit StorageType. STING's
        /// shared params are overwhelmingly TEXT/NUMBER/INTEGER/YESNO so we
        /// cover those explicitly; anything unrecognised falls back to Double
        /// (every numeric spec — Length / Area / Volume / Currency — is stored
        /// as Double). Text is the catch-all if even GetDataType() throws.
        /// </summary>
        private static StorageType ExternalDefStorageType(ExternalDefinition ext)
        {
            try
            {
                ForgeTypeId dt = ext.GetDataType();
                if (dt == SpecTypeId.String.Text)   return StorageType.String;
                if (dt == SpecTypeId.Boolean.YesNo) return StorageType.Integer;
                if (dt == SpecTypeId.Int.Integer)   return StorageType.Integer;
                // Every other numeric spec (Number / Length / Area / Volume /
                // Currency / Angle …) is stored as Double in Revit.
                // SpecTypeId.Int.Number does NOT exist — "Number" is the
                // unitless real spec at SpecTypeId.Number, not under .Int.
                return StorageType.Double;
            }
            catch
            {
                return StorageType.String;
            }
        }

        /// <summary>
        /// Read a FamilyParameter's parameter-group ForgeTypeId. Revit 2025
        /// exposes this via <c>Definition.GetGroupTypeId()</c> (the legacy
        /// <c>Definition.ParameterGroup</c> property was deprecated). Falls
        /// back to <c>GroupTypeId.General</c> when the definition is missing
        /// or the API call throws — that's the same default
        /// <c>fm.AddParameter</c> uses elsewhere in STING.
        /// </summary>
        private static ForgeTypeId SafeGroupTypeId(FamilyParameter fp)
        {
            try
            {
                ForgeTypeId g = fp?.Definition?.GetGroupTypeId();
                if (g != null) return g;
            }
            catch { }
            return GroupTypeId.General;
        }

        /// <summary>
        /// Rewrite a Revit formula string, replacing whole-token occurrences of
        /// OLD parameter names with their NEW counterparts. Tokens are matched
        /// only at identifier boundaries; substrings inside other identifiers
        /// and text inside double-quoted string literals are left alone. Returns
        /// the new string and sets <paramref name="changed"/> if any
        /// substitution happened.
        /// </summary>
        public static string RewriteFormula(string formula, Dictionary<string, string> remap, out bool changed)
        {
            changed = false;
            if (string.IsNullOrEmpty(formula) || remap == null || remap.Count == 0) return formula;

            // Split the formula into alternating non-string and string segments,
            // rewrite only the non-string segments, then re-assemble. Revit
            // formula strings are double-quoted with no escape sequence — a
            // literal " can't appear inside a string at all — so we can split
            // on '"' boundaries safely.
            var parts = formula.Split('"');
            // parts[0,2,4…] are code (rewrite); parts[1,3,5…] are string literals (preserve).
            for (int i = 0; i < parts.Length; i += 2)
            {
                string segment = parts[i];
                foreach (var kv in remap)
                {
                    string oldName = kv.Key;
                    string newName = kv.Value;
                    if (oldName == newName) continue;
                    if (segment.IndexOf(oldName, StringComparison.Ordinal) < 0) continue;

                    string pattern = "(?<![A-Za-z0-9_])" + Regex.Escape(oldName) + "(?![A-Za-z0-9_])";
                    string after = Regex.Replace(segment, pattern, newName);
                    if (!string.Equals(after, segment, StringComparison.Ordinal))
                    {
                        segment = after;
                        changed = true;
                    }
                }
                parts[i] = segment;
            }
            return changed ? string.Join("\"", parts) : formula;
        }
    }
}
