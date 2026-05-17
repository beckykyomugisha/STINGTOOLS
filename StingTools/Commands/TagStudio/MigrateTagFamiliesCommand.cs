// ============================================================================
// MigrateTagFamiliesCommand.cs — Upgrade existing STING tag families in-place.
//
// For every loaded STING-prefixed tag family in the project:
//   1. EditFamily into an in-memory family document
//   2. Add every missing parameter from TagFamilyConfig.{TagParams, VisibilityParams,
//      StyleParams} — closes the Phase-10/11 gap where PARA_STATE_4..10, the 128
//      TAG_{size}{style}_{colour}_BOOL variants, box colour, leader colour,
//      and the new scale/depth caches were NEVER bound on families.
//   3. For each TypeVariantSpec in TagStyleCatalogue.EnumerateStandardVariants:
//        - Ensure a type exists with the canonical name (e.g. "2.5_BOLD_RED_Filled30_T3")
//        - Set PARA_STATE_1..depth_tier = Yes, others No
//        - Set the matching TAG_{size}{style}_{colour}_BOOL = Yes, others No
//        - Set LEADER_ARROWHEAD to the ElementId of the matching OST_ArrowHeads type
//        - Cache the depth tier in TAG_DEPTH_TIER_INT
//   4. Save + Load Family back into the project
//   5. Excel summary via StingExcelExporter (Family, Category, ParamsAdded,
//      TypesCreated, Status, Error)
//
// Arrowheads that do not exist in the project as OST_ArrowHeads element types
// are logged as warnings and skipped (variant is still created without the
// arrowhead override — the type's LEADER_ARROWHEAD keeps its existing value).
//
// Cancellation: EscapeChecker every 10 families.  Typical runtime for 50
// families ~= 3-8 minutes (I/O-bound on EditFamily + SaveAs).
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
                $"  • Create up to {variants.Count} standard type variants\n" +
                $"  • Author T4..T10 tier rows from the active mode CSV\n" +
                $"  • Author warning-row formulas (gated by TAG_WARN_VISIBLE_BOOL)\n" +
                $"  • Assign arrowheads by name (OST_ArrowHeads)\n\n" +
                $"Pick the option below that matches your preservation needs.\n\n" +
                $"Runtime: ~3–8 minutes for {stingFamilies.Count} families.\n" +
                $"Press Escape at any time to cancel.";
            confirm.CommonButtons = TaskDialogCommonButtons.Cancel;
            confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Upgrade in place — preserve T1..T3 + hand-authored warnings",
                "Add missing params, types, and author T4..T10 + warning rows from the CSV. " +
                "T1..T3 hand-edits are detected and left untouched. " +
                "WARN_xxx parameters that already carry a non-empty formula on the Family " +
                "are also preserved — the CSV warning is only stamped on first run.");
            confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Upgrade in place — preserve T1..T3 only (re-stamp warnings)",
                "Same as above but always re-stamps WARN_xxx formulas from the CSV. " +
                "Use this if a CSV warning row was updated and you want all families realigned.");
            confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Upgrade and overwrite ALL tier rows + warnings",
                "Rebuild every T4..T10 row + every warning formula from the CSV without preservation. " +
                "Use only if you want a clean re-author.");
            TaskDialogResult mig = confirm.Show();
            if (mig == TaskDialogResult.Cancel) return Result.Cancelled;
            bool preserveHandEdits = (mig == TaskDialogResult.CommandLink1 || mig == TaskDialogResult.CommandLink2);
            bool preserveHandWarnings = (mig == TaskDialogResult.CommandLink1);

            // ── Pre-resolve arrowhead types in the project ──
            var arrowheads = BuildArrowheadLookup(doc);

            // ── Pre-load TierPlans for active mode + every available mode (dual-wire) ──
            Dictionary<string, Dictionary<string, TierPlan>> plansByMode =
                TagConfigPlanResolver.LoadAllPerMode(doc);
            Dictionary<string, TierPlan> plansByFamily = TagConfigPlanResolver.LoadAll(doc);

            var rows = new List<List<string>>();
            var progress = StingProgressDialog.Show("Migrate Tag Families", stingFamilies.Count);
            int migrated = 0, failed = 0, cancelled = 0;
            int totalParamsAdded = 0, totalTypesCreated = 0;
            int totalFormulasApplied = 0, totalTiersPreserved = 0, totalNoPlan = 0;
            int totalWarningsApplied = 0, totalWarningsSkipped = 0;
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

                    var result = MigrateOne(doc, app, sharedParamFile, fam, variants, arrowheads,
                        plansByMode, plansByFamily, preserveHandEdits, preserveHandWarnings);
                    totalParamsAdded += result.ParamsAdded;
                    totalTypesCreated += result.TypesCreated;
                    totalFormulasApplied += result.FormulasApplied;
                    totalTiersPreserved += result.TiersPreserved;
                    totalWarningsApplied += result.WarningsApplied;
                    totalWarningsSkipped += result.WarningsSkipped;
                    if (!result.HadPlan) totalNoPlan++;
                    if (result.Success) migrated++; else failed++;

                    rows.Add(new List<string>
                    {
                        famName,
                        catName,
                        result.ParamsAdded.ToString(),
                        result.TypesCreated.ToString(),
                        result.HadPlan ? result.FormulasApplied.ToString() : "—",
                        result.HadPlan ? result.TiersPreserved.ToString() : "—",
                        result.HadPlan ? result.WarningsApplied.ToString() : "—",
                        result.HadPlan ? result.WarningsSkipped.ToString() : "—",
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
                                       "FormulasApplied", "TiersPreserved",
                                       "WarningsApplied", "WarningsSkipped",
                                       "Status", "Error" },
                    rows, openFolder: false);
            }
            catch (Exception ex) { StingLog.Warn($"Excel export: {ex.Message}"); }

            var td = new TaskDialog("Migrate Tag Families");
            td.MainInstruction = $"Migrated {migrated} / {stingFamilies.Count} tag families";
            td.MainContent =
                $"Params added: {totalParamsAdded}\n" +
                $"Types created: {totalTypesCreated}\n" +
                $"T4..T10 formulas applied: {totalFormulasApplied}\n" +
                $"Tiers preserved (hand-edits): {totalTiersPreserved}\n" +
                $"Warning rows applied: {totalWarningsApplied}\n" +
                $"Warning rows skipped: {totalWarningsSkipped}\n" +
                $"Families without a CSV plan: {totalNoPlan}\n" +
                $"Failed: {failed}\n" +
                $"Cancelled: {cancelled}\n\n" +
                (xlsx != null ? $"Report: {xlsx}" : "");
            td.Show();

            StingLog.Info($"MigrateTagFamilies: migrated={migrated}, failed={failed}, " +
                $"cancelled={cancelled}, paramsAdded={totalParamsAdded}, typesCreated={totalTypesCreated}, " +
                $"formulasApplied={totalFormulasApplied}, tiersPreserved={totalTiersPreserved}, " +
                $"warningsApplied={totalWarningsApplied}, warningsSkipped={totalWarningsSkipped}, " +
                $"noPlan={totalNoPlan}");
            return Result.Succeeded;
        }

        // ──────────────────────────────────────────────────────────────────
        //  Single-family migration
        // ──────────────────────────────────────────────────────────────────

        private class MigrationResult
        {
            public int ParamsAdded;
            public int TypesCreated;
            public int FormulasApplied;
            public int TiersPreserved;
            public int WarningsApplied;
            public int WarningsSkipped;
            public bool HadPlan;
            public bool Success;
            public string ErrorMessage;
        }

        private MigrationResult MigrateOne(
            Document doc, Autodesk.Revit.ApplicationServices.Application app,
            string sharedParamFile, Family fam,
            List<TypeVariantSpec> variants, Dictionary<string, ElementId> arrowheads,
            Dictionary<string, Dictionary<string, TierPlan>> plansByMode,
            Dictionary<string, TierPlan> plansByFamily,
            bool preserveHandEdits, bool preserveHandWarnings)
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

                    result.TypesCreated = CreateStandardVariants(fm, variants, arrowheads);

                    tx.Commit();
                }

                // ── Author T4..T10 tier rows + warning gates from CSV plan ──
                // FamilyLabelAuthor handles its own transactions internally and
                // detects already-bound tiers when PreserveHandEdits=true so
                // T1..T3 hand-configured rows are left untouched.
                var modePlans = new List<FamilyLabelAuthor.ModePlan>();
                if (plansByMode != null)
                {
                    foreach (var kv in plansByMode)
                    {
                        if (kv.Value == null) continue;
                        if (!kv.Value.TryGetValue(fam.Name, out TierPlan plan) || plan == null) continue;
                        modePlans.Add(new FamilyLabelAuthor.ModePlan
                        {
                            Mode = kv.Key,
                            GateParam = HandoverModeHelper.GetSelectorBool(kv.Key),
                            Plan = plan,
                        });
                    }
                }
                if (modePlans.Count == 0 && plansByFamily != null &&
                    plansByFamily.TryGetValue(fam.Name, out TierPlan single) && single != null)
                {
                    modePlans.Add(new FamilyLabelAuthor.ModePlan { Mode = "", GateParam = null, Plan = single });
                }

                if (modePlans.Count > 0)
                {
                    result.HadPlan = true;
                    try
                    {
                        var opts = new FamilyLabelAuthor.Options
                        {
                            App = app,
                            SharedParamFile = sharedParamFile,
                            PreserveHandEdits = preserveHandEdits,
                            PreserveHandWarnings = preserveHandWarnings,
                            FamilyName = fam.Name,
                        };
                        var ar = FamilyLabelAuthor.AuthorLabelsMulti(famDoc, modePlans, opts);
                        result.FormulasApplied = ar.FormulasApplied;
                        result.TiersPreserved = ar.TiersPreserved;
                        result.WarningsApplied = ar.WarningsApplied;
                        result.WarningsSkipped = ar.WarningsSkipped;
                        foreach (var w in ar.Warnings) StingLog.Warn($"{fam.Name}: {w}");
                    }
                    catch (Exception authEx)
                    {
                        StingLog.Error($"MigrateTagFamilies AuthorLabelsMulti({fam.Name})", authEx);
                    }
                }
                else
                {
                    StingLog.Info($"MigrateTagFamilies: no CSV plan for {fam.Name} — params + types only");
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

        private int CreateStandardVariants(FamilyManager fm,
            List<TypeVariantSpec> variants, Dictionary<string, ElementId> arrowheads)
        {
            int created = 0;
            var paramByName = fm.GetParameters()
                .ToDictionary(p => p.Definition.Name, p => p, StringComparer.OrdinalIgnoreCase);

            var existingTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FamilyType ft in fm.Types)
                if (ft != null && !string.IsNullOrEmpty(ft.Name)) existingTypes.Add(ft.Name);

            foreach (var spec in variants)
            {
                string typeName = spec.CanonicalTypeName;
                FamilyType target;

                if (existingTypes.Contains(typeName))
                {
                    target = null;
                    foreach (FamilyType ft in fm.Types)
                        if (string.Equals(ft.Name, typeName, StringComparison.OrdinalIgnoreCase))
                        { target = ft; break; }
                    if (target == null) continue;
                }
                else
                {
                    try { target = fm.NewType(typeName); created++; existingTypes.Add(typeName); }
                    catch (Exception ex) { StingLog.Warn($"NewType '{typeName}': {ex.Message}"); continue; }
                }

                fm.CurrentType = target;

                // 1. Depth tiers: PARA_STATE_1..depth = Yes, rest = No.
                // Tag-formula BOOLs are TEXT in MR_PARAMETERS v5.3+ so Revit label
                // Calculated Values can reference them inside if(...); the Integer
                // branch keeps legacy YESNO families migrating cleanly.
                for (int t = 1; t <= 10; t++)
                {
                    string pname = $"TAG_PARA_STATE_{t}_BOOL";
                    if (paramByName.TryGetValue(pname, out var pfp))
                    {
                        try { SetFamilyBool(fm, pfp, t <= spec.DepthTier); }
                        catch (Exception ex) { StingLog.Warn($"Set {pname} on {typeName}: {ex.Message}"); }
                    }
                }

                // 2. Style BOOLs: only the matching combo = Yes
                string activeStyle = ParamRegistry.TagStyleParamName(spec.Size, spec.Style, spec.Colour);
                foreach (string pname in ParamRegistry.AllTagStyleParams)
                {
                    if (!paramByName.TryGetValue(pname, out var pfp)) continue;
                    try { SetFamilyBool(fm, pfp, string.Equals(pname, activeStyle, StringComparison.OrdinalIgnoreCase)); }
                    catch (Exception ex) { StingLog.Warn($"Set {pname} on {typeName}: {ex.Message}"); }
                }

                // 3. Arrowhead (type param LEADER_ARROWHEAD via BuiltInParameter)
                try
                {
                    if (!string.IsNullOrEmpty(spec.Arrowhead) &&
                        !string.Equals(spec.Arrowhead, "None", StringComparison.OrdinalIgnoreCase))
                    {
                        if (arrowheads.TryGetValue(spec.Arrowhead, out var arrowId) && arrowId != ElementId.InvalidElementId)
                        {
                            var arrowFp = fm.get_Parameter(BuiltInParameter.LEADER_ARROWHEAD);
                            if (arrowFp != null && !arrowFp.IsReadOnly)
                            {
                                try { fm.Set(arrowFp, arrowId); }
                                catch (Exception ex) { StingLog.Warn($"Set arrowhead {spec.Arrowhead} on {typeName}: {ex.Message}"); }
                            }
                        }
                        else
                        {
                            StingLog.Warn($"MigrateTagFamilies: arrowhead '{spec.Arrowhead}' not present in project — skipped for {typeName}");
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Arrowhead assign {typeName}: {ex.Message}"); }

                // 4. Cache active depth tier on the type for fast reads (Task 2 new param)
                if (paramByName.TryGetValue(ParamRegistry.TAG_DEPTH_TIER, out var depthFp))
                {
                    try { fm.Set(depthFp, spec.DepthTier); }
                    catch (Exception ex2) { StingLog.Warn($"Set TAG_DEPTH_TIER_INT on {typeName}: {ex2.Message}"); }
                }
            }

            return created;
        }

        /// <summary>
        /// Set a tag-formula BOOL on a family type, regardless of whether the parameter
        /// is stored as TEXT ("Yes"/"No") or INTEGER (1/0). TEXT is the v5.3+ default —
        /// YESNO is not allowed as the condition of a Revit label Calculated Value.
        /// </summary>
        private static void SetFamilyBool(FamilyManager fm, FamilyParameter fp, bool value)
        {
            if (fp == null) return;
            switch (fp.StorageType)
            {
                case StorageType.String:
                    fm.Set(fp, value ? "Yes" : "No");
                    break;
                case StorageType.Integer:
                    fm.Set(fp, value ? 1 : 0);
                    break;
                default:
                    StingLog.Warn($"SetFamilyBool: unsupported storage {fp.StorageType} on '{fp.Definition.Name}'");
                    break;
            }
        }

        /// <summary>
        /// Build a lookup of arrowhead display name → ElementType Id (OST_ArrowHeads).
        /// Names are matched case-insensitively against TagStyleCatalogue.Arrowheads.
        /// </summary>
        private Dictionary<string, ElementId> BuildArrowheadLookup(Document doc)
        {
            var lookup = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // Arrowhead ElementType has no BuiltInCategory in Revit 2025 —
                // Category is null and FamilyName is "Arrowhead".
                var types = new FilteredElementCollector(doc)
                    .OfClass(typeof(ElementType))
                    .Cast<ElementType>()
                    .Where(et => string.Equals(et.FamilyName, "Arrowhead", StringComparison.Ordinal))
                    .ToList();

                var wanted = new HashSet<string>(TagStyleCatalogue.Arrowheads, StringComparer.OrdinalIgnoreCase);
                foreach (var et in types)
                {
                    string n = et.Name ?? "";
                    if (wanted.Contains(n)) lookup[n] = et.Id;
                }

                // Best-effort fuzzy matches so the catalogue is not held hostage to exact names
                foreach (string want in TagStyleCatalogue.Arrowheads)
                {
                    if (lookup.ContainsKey(want)) continue;
                    var match = types.FirstOrDefault(et =>
                        et.Name != null &&
                        et.Name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (match != null) lookup[want] = match.Id;
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildArrowheadLookup: {ex.Message}"); }
            return lookup;
        }
    }
}
