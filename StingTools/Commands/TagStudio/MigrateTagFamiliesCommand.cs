// ============================================================================
// MigrateTagFamiliesCommand.cs — Upgrade existing STING tag families in-place.
//
//   *** Phase 195 — Universal Tag pivot, Task 4 step 2 ***
//
// TRIMMED to a param + type-variant migrator. Tier-row authoring (T4..T10 +
// warnings from the v5.0 CSVs, via FamilyLabelAuthor / TagConfigPlanResolver /
// HandoverModeHelper) has been REMOVED — under the universal-tag model, label
// rows come from the hand-built universal master propagated by
// PropagateUniversalTagCommand, not from per-family CSV authoring.
//
// For every loaded STING-prefixed tag family in the project this now:
//   1. EditFamily into an in-memory family document
//   2. Add every missing parameter from TagFamilyConfig.{TagParams,
//      VisibilityParams, StyleParams}
//   3. (Re)create the standard depth/style/colour type variants via the shared
//      TagTypeVariantWriter (identical to PropagateUniversalTagCommand)
//   4. Save + Load Family back into the project
//   5. Excel summary (Family, Category, ParamsAdded, TypesCreated, Status, Error)
//
// Arrowheads absent from the project (OST_ArrowHeads) are logged + skipped.
// Cancellation: EscapeChecker every 10 families.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Tags;
using StingTools.UI;

namespace StingTools.Commands.TagStudio
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MigrateTagFamiliesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            var app = ctx.App.Application;

            string sharedParamFile = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
            if (string.IsNullOrEmpty(sharedParamFile))
            {
                TaskDialog.Show("Migrate Tag Families",
                    "MR_PARAMETERS.txt not found in data directory. Run 'Check Data' first.");
                return Result.Failed;
            }

            // ── Collect STING-prefixed tag families ──
            var stingFamilies = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.Name != null &&
                            f.Name.StartsWith(TagFamilyConfig.FamilyPrefix, StringComparison.OrdinalIgnoreCase) &&
                            f.FamilyCategory != null &&
                            f.FamilyCategory.CategoryType == CategoryType.Annotation)
                .OrderBy(f => f.Name)
                .ToList();

            if (stingFamilies.Count == 0)
            {
                TaskDialog.Show("Migrate Tag Families",
                    "No STING-prefixed tag families are loaded in this project.\n" +
                    "Run 'Create Tag Families' first.");
                return Result.Succeeded;
            }

            // ── Confirmation ──
            var variants = TagStyleCatalogue.EnumerateStandardVariants().ToList();
            var confirm = new TaskDialog("Migrate Tag Families");
            confirm.MainInstruction = $"Migrate {stingFamilies.Count} tag families?";
            confirm.MainContent =
                $"For each family this will:\n" +
                $"  • Add ~{TagFamilyConfig.StyleParams.Length + TagFamilyConfig.VisibilityParams.Length} style & visibility params (if missing)\n" +
                $"  • (Re)create up to {variants.Count} standard type variants\n" +
                $"  • Assign arrowheads by name (OST_ArrowHeads)\n\n" +
                $"Label ROWS are NOT authored here — they come from the universal master\n" +
                $"via 'Propagate Universal'. This command only syncs params + type variants.\n\n" +
                $"Runtime: ~3–8 minutes for {stingFamilies.Count} families.\n" +
                $"Press Escape at any time to cancel.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() != TaskDialogResult.Ok) return Result.Cancelled;

            // ── Pre-resolve arrowhead types in the project ──
            var arrowheads = TagTypeVariantWriter.BuildArrowheadLookup(doc);

            var rows = new List<List<string>>();
            var progress = StingProgressDialog.Show("Migrate Tag Families", stingFamilies.Count);
            int migrated = 0, failed = 0, cancelled = 0;
            int totalParamsAdded = 0, totalTypesCreated = 0;
            string originalSharedFile = app.SharedParametersFilename;

            try
            {
                app.SharedParametersFilename = sharedParamFile;

                for (int i = 0; i < stingFamilies.Count; i++)
                {
                    if ((i % 10) == 0 && EscapeChecker.IsEscapePressed())
                    {
                        cancelled = stingFamilies.Count - i;
                        StingLog.Info($"MigrateTagFamilies: cancelled after {i} of {stingFamilies.Count} families");
                        break;
                    }

                    Family fam = stingFamilies[i];
                    string famName = fam.Name;
                    string catName = fam.FamilyCategory?.Name ?? "";
                    progress.Increment($"Migrating {famName} ({i + 1}/{stingFamilies.Count})");

                    var result = MigrateOne(doc, app, fam, variants, arrowheads);
                    totalParamsAdded += result.ParamsAdded;
                    totalTypesCreated += result.TypesCreated;
                    if (result.Success) migrated++; else failed++;

                    rows.Add(new List<string>
                    {
                        famName,
                        catName,
                        result.ParamsAdded.ToString(),
                        result.TypesCreated.ToString(),
                        result.Success ? "OK" : "FAILED",
                        result.ErrorMessage ?? ""
                    });
                }
            }
            finally
            {
                progress.Close();
                try { if (!string.IsNullOrEmpty(originalSharedFile)) app.SharedParametersFilename = originalSharedFile; }
                catch (Exception ex) { StingLog.Warn($"Restore SharedParametersFilename: {ex.Message}"); }
            }

            // ── Excel summary ──
            string xlsx = null;
            try
            {
                string outDir = OutputLocationHelper.GetOutputDirectory(doc);
                xlsx = Path.Combine(outDir, $"STING_MigrateTagFamilies_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                StingExcelExporter.ExportTable(
                    xlsx, "Migration",
                    new List<string> { "Family", "Category", "ParamsAdded", "TypesCreated",
                                       "Status", "Error" },
                    rows, openFolder: false);
            }
            catch (Exception ex) { StingLog.Warn($"Excel export: {ex.Message}"); }

            var td = new TaskDialog("Migrate Tag Families");
            td.MainInstruction = $"Migrated {migrated} / {stingFamilies.Count} tag families";
            td.MainContent =
                $"Params added: {totalParamsAdded}\n" +
                $"Types created: {totalTypesCreated}\n" +
                $"Failed: {failed}\n" +
                $"Cancelled: {cancelled}\n\n" +
                (xlsx != null ? $"Report: {xlsx}" : "");
            td.Show();

            StingLog.Info($"MigrateTagFamilies: migrated={migrated}, failed={failed}, " +
                $"cancelled={cancelled}, paramsAdded={totalParamsAdded}, typesCreated={totalTypesCreated}");
            return Result.Succeeded;
        }

        // ──────────────────────────────────────────────────────────────────
        //  Single-family migration
        // ──────────────────────────────────────────────────────────────────

        private class MigrationResult
        {
            public int ParamsAdded;
            public int TypesCreated;
            public bool Success;
            public string ErrorMessage;
        }

        private MigrationResult MigrateOne(
            Document doc, Autodesk.Revit.ApplicationServices.Application app,
            Family fam, List<TypeVariantSpec> variants, Dictionary<string, ElementId> arrowheads)
        {
            var result = new MigrationResult();
            Document famDoc = null;
            try
            {
                famDoc = doc.EditFamily(fam);
                if (famDoc == null)
                {
                    result.ErrorMessage = "EditFamily returned null";
                    return result;
                }

                FamilyManager fm = famDoc.FamilyManager;
                var defFile = app.OpenSharedParameterFile();
                if (defFile == null)
                {
                    result.ErrorMessage = "OpenSharedParameterFile returned null";
                    famDoc.Close(false);
                    return result;
                }

                using (var tx = new Transaction(famDoc, "STING Migrate Tag Family"))
                {
                    tx.Start();

                    result.ParamsAdded = AddMissingParams(fm, defFile,
                        TagFamilyConfig.TagParams
                            .Concat(TagFamilyConfig.VisibilityParams)
                            .Concat(TagFamilyConfig.StyleParams)
                            .Distinct()
                            .ToList());

                    result.TypesCreated = TagTypeVariantWriter.CreateStandardVariants(fm, variants, arrowheads);

                    tx.Commit();
                }

                // Save to the family's stored path if known, else a temp file next to the plugin.
                string savePath = Path.Combine(TagFamilyConfig.GetOutputDirectory(), fam.Name + ".rfa");
                try
                {
                    var saveOpts = new SaveAsOptions { OverwriteExistingFile = true, MaximumBackups = 1 };
                    famDoc.SaveAs(savePath, saveOpts);
                }
                catch (Exception saveEx)
                {
                    StingLog.Warn($"Migrate SaveAs failed for {fam.Name}: {saveEx.Message}");
                }
                famDoc.Close(false);
                famDoc = null;

                // Reload into project (overwrite) so new params/types are live.
                using (var tx = new Transaction(doc, $"STING Reload {fam.Name}"))
                {
                    tx.Start();
                    if (File.Exists(savePath))
                    {
                        try { doc.LoadFamily(savePath, new TagFamilyLoadOptions(), out _); }
                        catch (Exception loadEx) { StingLog.Warn($"Reload {fam.Name}: {loadEx.Message}"); }
                    }
                    tx.Commit();
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                StingLog.Error($"MigrateTagFamilies: {fam.Name}", ex);
                try { famDoc?.Close(false); } catch (Exception closeEx) { StingLog.Warn($"Close famDoc: {closeEx.Message}"); }
            }
            return result;
        }

        private int AddMissingParams(FamilyManager fm, DefinitionFile defFile, List<string> wanted)
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
                    {
                        if (def.Name == paramName && def is ExternalDefinition ed) { extDef = ed; break; }
                    }
                    if (extDef != null) break;
                }
                if (extDef == null) continue;

                // Style/visibility/depth-tier params are TYPE params.
                bool isInstance = false;
                if (paramName.StartsWith("ASS_TAG", StringComparison.OrdinalIgnoreCase))
                    isInstance = true; // tag container values come from the instance

                try
                {
                    fm.AddParameter(extDef, GroupTypeId.General, isInstance);
                    added++;
                    existing.Add(paramName);
                }
                catch (Exception ex) { StingLog.Warn($"AddMissingParams '{paramName}': {ex.Message}"); }
            }

            return added;
        }
    }
}
