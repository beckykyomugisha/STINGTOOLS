using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// Bind ALL shared parameters from MR_PARAMETERS.txt to project categories.
    ///
    /// Always uses MR_PARAMETERS.txt from the data directory (overrides whatever
    /// shared parameter file is currently set in Revit).
    ///
    /// CRASH PREVENTION — multiple vectors addressed:
    ///   1. Volume: group-per-transaction with targeted category sets (~40K bindings vs 305K)
    ///   2. InstanceBinding reuse: ONE per group, never inside the per-param loop
    ///   3. No logging inside transactions: prevents disk I/O stalling Revit
    ///   4. No BuildCategorySet inside loops: all sets pre-built before transactions
    ///   5. No recursive directory search: capped search prevents hanging on broad dirs
    ///   6. BindingWarningSwallower: prevents FailureMessage memory buildup
    ///   7. Null guards: ActiveUIDocument, ParamRegistry fallback capped
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LoadSharedParamsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                return ExecuteCore(commandData, ref message, elements);
            }
            catch (Exception ex)
            {
                StingLog.Error("LoadSharedParamsCommand crashed", ex);
                try
                {
                    TaskDialog.Show("STING Tools - Load Shared Params",
                        $"Command failed with an unexpected error:\n\n{ex.Message}\n\n" +
                        "Check StingTools.log for details.");
                }
                catch (Exception ex2) { StingLog.Warn($"If even the dialog fails, don't crash Revit: {ex2.Message}"); }
                return Result.Failed;
            }
        }

        private Result ExecuteCore(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uiApp = ParameterHelpers.GetApp(commandData);

            // BUG FIX: null check ActiveUIDocument — crashes if no document open
            if (uiApp.ActiveUIDocument == null)
            {
                TaskDialog.Show("STING Tools - Load Shared Params",
                    "No document is open. Please open a project first.");
                return Result.Failed;
            }

            Document doc = uiApp.ActiveUIDocument.Document;
            Autodesk.Revit.ApplicationServices.Application app =
                uiApp.Application;

            // ── Step 1: Locate and set MR_PARAMETERS.txt ──
            StingLog.Info("LoadSharedParams: step 1 — locating MR_PARAMETERS.txt");
            string previousSpFile = app.SharedParametersFilename;
            string mrParamsPath = FindMrParametersFile(previousSpFile);

            if (!string.IsNullOrEmpty(mrParamsPath) && File.Exists(mrParamsPath))
            {
                app.SharedParametersFilename = mrParamsPath;
                StingLog.Info($"Set shared parameter file: {mrParamsPath}");
            }
            else
            {
                if (string.IsNullOrEmpty(previousSpFile) || !File.Exists(previousSpFile))
                {
                    TaskDialog.Show("STING Tools - Load Shared Params",
                        "Could not find MR_PARAMETERS.txt.\n\n" +
                        "Expected location: " +
                        (StingToolsApp.DataPath ?? "(DataPath not set)") +
                        "\n\nEither place the file in the data directory or go to " +
                        "Manage → Shared Parameters and set the path manually.");
                    return Result.Failed;
                }
                mrParamsPath = previousSpFile;
                StingLog.Warn($"MR_PARAMETERS.txt not found, using existing: {previousSpFile}");
            }

            string spFile = mrParamsPath;

            // ── Step 2: Open definition file ──
            StingLog.Info("LoadSharedParams: step 2 — opening definition file");
            DefinitionFile defFile = app.OpenSharedParameterFile();
            if (defFile == null)
            {
                TaskDialog.Show("STING Tools - Load Shared Params",
                    "Could not open shared parameter file:\n" + spFile);
                return Result.Failed;
            }

            // Index params by group
            var groupDefs = new List<(string groupName, List<ExternalDefinition> defs)>();
            int totalDefs = 0;
            foreach (DefinitionGroup group in defFile.Groups)
            {
                var defs = new List<ExternalDefinition>();
                foreach (Definition def in group.Definitions)
                {
                    if (def is ExternalDefinition ext)
                        defs.Add(ext);
                }
                if (defs.Count > 0)
                {
                    groupDefs.Add((group.Name, defs));
                    totalDefs += defs.Count;
                }
            }
            StingLog.Info($"LoadSharedParams: {totalDefs} definitions in {groupDefs.Count} groups");

            if (totalDefs < 100)
            {
                StingLog.Warn($"Only {totalDefs} parameters found — expected 1,527+. " +
                    "Ensure MR_PARAMETERS.txt is the full STING parameter file.");
            }

            // ── Step 3: Pre-scan existing bindings ──
            StingLog.Info("LoadSharedParams: step 3 — scanning existing bindings");
            var existingBindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var iter = doc.ParameterBindings.ForwardIterator();
            while (iter.MoveNext())
            {
                if (iter.Key is ExternalDefinition exDef)
                    existingBindings.Add(exDef.Name);
                else if (iter.Key != null)
                    existingBindings.Add(iter.Key.Name);
            }
            StingLog.Info($"Pre-scan: {existingBindings.Count} parameters already bound");

            // Filter each group to only unbound params
            int totalToBind = 0;
            int alreadyBound = 0;
            var groupsToProcess = new List<(string groupName, List<ExternalDefinition> defs)>();
            foreach (var (groupName, defs) in groupDefs)
            {
                var unbound = defs.Where(d => !existingBindings.Contains(d.Name)).ToList();
                alreadyBound += defs.Count - unbound.Count;
                if (unbound.Count > 0)
                {
                    groupsToProcess.Add((groupName, unbound));
                    totalToBind += unbound.Count;
                }
            }

            StingLog.Info($"To bind: {totalToBind}, already bound: {alreadyBound}");

            if (totalToBind == 0)
            {
                TaskDialog.Show("STING Tools - Load Shared Params",
                    $"All {totalDefs} parameters are already bound — nothing to do.\n\n" +
                    $"{alreadyBound} parameters already present in project.\n" +
                    $"\nSource: {spFile}");
                return Result.Succeeded;
            }

            // ── Step 4: Build ALL category sets BEFORE any transactions ──
            // CRITICAL: Never call BuildCategorySet or NewInstanceBinding inside
            // the per-param loop — each call does doc.Settings.Categories.get_Item()
            // lookups and object allocation that stall Revit mid-transaction.
            StingLog.Info("LoadSharedParams: step 4 — building category sets");

            var coreEnums = SharedParamGuids.AllCategoryEnums;
            CategorySet coreCats = SharedParamGuids.BuildCategorySet(doc, coreEnums);

            // NOTE: OST_Materials is NOT added to coreCats.
            // Material-specific parameters (MAT_INFO, PROP_PHYSICAL groups) are bound
            // via dedicated matCats override in BuildGroupCategoryOverrides().
            // Adding Materials to coreCats would bind ALL 2300+ parameters to materials,
            // polluting every material's custom properties panel.

            // Phase 39: Add Sheets category (needed for SHT_* params)
            try
            {
                Category shtCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Sheets);
                if (shtCat != null && shtCat.AllowsBoundParameters && !coreCats.Contains(shtCat))
                    coreCats.Insert(shtCat);
            }
            catch (Exception ex) { StingLog.Warn($"Add Sheets category to core set: {ex.Message}"); }

            StingLog.Info($"Core CategorySet: {coreCats.Size} categories");

            if (coreCats.Size == 0)
            {
                // BUG FIX: Fallback must NOT use all doc categories (causes the 305K hang).
                // Instead, build from our hardcoded MEP + BLE sets.
                StingLog.Warn("ParamRegistry categories empty — building from hardcoded fallback");
                coreCats = BuildFallbackCoreSet(doc);
                StingLog.Info($"Fallback CategorySet: {coreCats.Size} categories");
            }

            if (coreCats.Size == 0)
            {
                TaskDialog.Show("STING Tools - Load Shared Params",
                    "No categories found that accept bound parameters.");
                return Result.Failed;
            }

            // Pre-build group-specific category sets
            var groupCatOverrides = BuildGroupCategoryOverrides(doc);

            // Pre-build InstanceBindings for each group OUTSIDE the transaction loops.
            // This avoids calling NewInstanceBinding() inside per-param loops.
            var groupBindings = new Dictionary<string, InstanceBinding>(StringComparer.OrdinalIgnoreCase);
            InstanceBinding coreBinding = app.Create.NewInstanceBinding(coreCats);
            foreach (var kvp in groupCatOverrides)
                groupBindings[kvp.Key] = app.Create.NewInstanceBinding(kvp.Value);

            // ── Step 5: Bind ONE GROUP per transaction ──
            int bound = 0;
            int skipped = 0;
            var errors = new List<string>();
            var boundByGroup = new Dictionary<string, int>();

            StingLog.Info($"Binding {totalToBind} params across {groupsToProcess.Count} groups");

            for (int gi = 0; gi < groupsToProcess.Count; gi++)
            {
                var (groupName, defs) = groupsToProcess[gi];

                // Pick pre-built binding for this group
                InstanceBinding binding = groupBindings.TryGetValue(groupName, out InstanceBinding gb)
                    ? gb : coreBinding;

                int catCount = groupCatOverrides.TryGetValue(groupName, out CategorySet gcs)
                    ? gcs.Size : coreCats.Size;

                // Log BEFORE transaction, not inside
                StingLog.Info($"Group [{gi + 1}/{groupsToProcess.Count}] '{groupName}': " +
                    $"{defs.Count} params → {catCount} categories");

                using (Transaction tx = new Transaction(doc,
                    $"STING Params: {groupName}"))
                {
                    var failOpts = tx.GetFailureHandlingOptions();
                    failOpts.SetFailuresPreprocessor(new BindingWarningSwallower());
                    tx.SetFailureHandlingOptions(failOpts);

                    tx.Start();
                    try
                    {
                        int groupBound = 0;
                        foreach (ExternalDefinition extDef in defs)
                        {
                            try
                            {
                                // Use the pre-built group binding — NEVER create
                                // new InstanceBinding or call BuildCategorySet here
                                bool result = doc.ParameterBindings.Insert(
                                    extDef, binding, GroupTypeId.General);

                                if (result)
                                    groupBound++;
                                else
                                    skipped++;
                            }
                            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); skipped++; }
                        }

                        tx.Commit();
                        bound += groupBound;
                        boundByGroup[groupName] = groupBound;

                        // Log AFTER transaction, not inside
                        StingLog.Info($"  → committed: {groupBound} bound, {defs.Count - groupBound} skipped");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error($"Group '{groupName}' transaction failed", ex);
                        if (tx.HasStarted() && !tx.HasEnded())
                            tx.RollBack();

                        skipped += defs.Count;
                        if (errors.Count < 10)
                            errors.Add($"Group '{groupName}': {ex.Message}");
                    }
                }
            }

            // ── Report ──
            var report = new StringBuilder();
            report.AppendLine($"Bound: {bound} parameters");
            report.AppendLine($"Already present: {alreadyBound}");
            report.AppendLine($"Skipped/failed: {skipped}");
            report.AppendLine($"Total in file: {totalDefs}");
            report.AppendLine($"Categories: {coreCats.Size}");
            report.AppendLine($"Groups processed: {groupsToProcess.Count}");
            report.AppendLine();

            if (boundByGroup.Count > 0)
            {
                report.AppendLine("Bound by group:");
                foreach (var kvp in boundByGroup.OrderByDescending(kv => kv.Value))
                    report.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }

            report.AppendLine($"\nSource: {spFile}");

            if (errors.Count > 0)
            {
                report.AppendLine($"\nErrors ({errors.Count}):");
                foreach (string err in errors.Take(5))
                    report.AppendLine($"  {err}");
            }

            // DATA-02: Validate required parameters are bound after binding pass
            var allBound = new HashSet<string>(StringComparer.Ordinal);
            var iter2 = doc.ParameterBindings.ForwardIterator();
            while (iter2.MoveNext())
            {
                if (iter2.Key is InternalDefinition def && !string.IsNullOrEmpty(def.Name))
                    allBound.Add(def.Name);
            }

            int requiredMissing = 0;
            foreach (string pn in ParamRegistry.AllTokenParams ?? Array.Empty<string>())
            {
                if (ParamRegistry.IsRequired(pn) && !allBound.Contains(pn))
                {
                    StingLog.Error($"DATA-02: REQUIRED parameter '{pn}' is NOT bound — tagging will fail");
                    requiredMissing++;
                }
            }
            if (requiredMissing > 0)
            {
                report.AppendLine($"\n⚠ {requiredMissing} REQUIRED parameter(s) failed to bind (see log).");
            }

            // Clear parameter lookup cache so newly bound parameters are found immediately
            ParameterHelpers.ClearParamCache();

            var td = new TaskDialog("STING Tools - Load Shared Params");
            td.MainInstruction = requiredMissing > 0
                ? $"Binding complete — {bound} bound, {requiredMissing} REQUIRED missing!"
                : $"Shared parameter binding complete — {bound} bound.";
            td.MainContent = report.ToString();
            td.CommonButtons = TaskDialogCommonButtons.Ok;
            td.DefaultButton = TaskDialogResult.Ok;
            td.Show();
            StingLog.Info($"LoadSharedParams complete: {bound} bound, {alreadyBound} already present, {skipped} skipped");

            return Result.Succeeded;
        }

        // ════════════════════════════════════════════════════════════════
        // Category set builders — called ONCE before any transactions
        // ════════════════════════════════════════════════════════════════

        private static readonly BuiltInCategory[] MepCategories = new[]
        {
            BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_DuctTerminal,
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_FlexDuctCurves,
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_PipeFitting,
            BuiltInCategory.OST_PipeAccessory,
            BuiltInCategory.OST_FlexPipeCurves,
            BuiltInCategory.OST_PlumbingFixtures,
            BuiltInCategory.OST_Sprinklers,
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_LightingDevices,
            BuiltInCategory.OST_CableTray,
            BuiltInCategory.OST_CableTrayFitting,
            BuiltInCategory.OST_Conduit,
            BuiltInCategory.OST_ConduitFitting,
            BuiltInCategory.OST_CommunicationDevices,
            BuiltInCategory.OST_DataDevices,
            BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_NurseCallDevices,
            BuiltInCategory.OST_SecurityDevices,
            BuiltInCategory.OST_TelephoneDevices,
        };

        private static readonly BuiltInCategory[] BleCategories = new[]
        {
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Ceilings,
            BuiltInCategory.OST_Roofs,
            BuiltInCategory.OST_Doors,
            BuiltInCategory.OST_Windows,
            BuiltInCategory.OST_Columns,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_StructuralFoundation,
            BuiltInCategory.OST_Stairs,
            BuiltInCategory.OST_StairsRailing,
            BuiltInCategory.OST_Ramps,
            BuiltInCategory.OST_CurtainWallPanels,
            BuiltInCategory.OST_CurtainWallMullions,
            BuiltInCategory.OST_Casework,
            BuiltInCategory.OST_Furniture,
            BuiltInCategory.OST_FurnitureSystems,
            BuiltInCategory.OST_GenericModel,
            BuiltInCategory.OST_SpecialityEquipment,
        };

        /// <summary>
        /// Build group-specific category overrides. Groups with discipline-specific
        /// parameters get smaller, targeted category sets instead of the full core set.
        /// Called ONCE before transactions — never inside a loop.
        /// </summary>
        private static Dictionary<string, CategorySet> BuildGroupCategoryOverrides(Document doc)
        {
            var overrides = new Dictionary<string, CategorySet>(StringComparer.OrdinalIgnoreCase);

            var mepCats = BuildCatSet(doc, MepCategories);
            if (mepCats.Size > 0)
            {
                overrides["ELC_PWR"] = mepCats;
                overrides["HVC_SYSTEMS"] = mepCats;
                overrides["PLM_DRN"] = mepCats;
                overrides["LTG_CONTROLS"] = mepCats;
                overrides["FLS_LIFE_SFTY"] = mepCats;
                overrides["MEP_GENERIC"] = mepCats;
            }

            var bleCats = BuildCatSet(doc, BleCategories);
            if (bleCats.Size > 0)
            {
                overrides["BLE_ELES"] = bleCats;
                overrides["BLE_STRUCTURE"] = bleCats;
            }

            var matCats = BuildCatSet(doc, new[] { BuiltInCategory.OST_Materials });
            if (matCats.Size > 0)
            {
                overrides["MAT_INFO"] = matCats;
                overrides["PROP_PHYSICAL"] = matCats;
            }

            return overrides;
        }

        /// <summary>
        /// Fallback core category set when ParamRegistry fails to load.
        /// Uses the MEP + BLE arrays (capped, known-good categories) instead
        /// of doc.Settings.Categories which includes 200+ internal categories.
        /// </summary>
        private static CategorySet BuildFallbackCoreSet(Document doc)
        {
            var set = new CategorySet();
            // Combine MEP + BLE + a few extra common categories
            // NOTE: OST_Materials is NOT added here — material params are bound
            // via dedicated matCats override (MAT_INFO, PROP_PHYSICAL groups only)
            foreach (var bic in MepCategories) TryInsert(doc, set, bic);
            foreach (var bic in BleCategories) TryInsert(doc, set, bic);
            TryInsert(doc, set, BuiltInCategory.OST_Rooms);
            TryInsert(doc, set, BuiltInCategory.OST_Areas);
            TryInsert(doc, set, BuiltInCategory.OST_Parking);
            return set;
        }

        private static void TryInsert(Document doc, CategorySet set, BuiltInCategory bic)
        {
            try
            {
                Category cat = doc.Settings.Categories.get_Item(bic);
                if (cat != null && cat.AllowsBoundParameters)
                    set.Insert(cat);
            }
            catch (Exception ex) { StingLog.Warn($"Insert category {bic}: {ex.Message}"); }
        }

        private static CategorySet BuildCatSet(Document doc, BuiltInCategory[] enums)
        {
            var set = new CategorySet();
            foreach (var bic in enums) TryInsert(doc, set, bic);
            return set;
        }

        // ════════════════════════════════════════════════════════════════
        // File search — finds MR_PARAMETERS.txt across deployment layouts
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Search multiple locations for MR_PARAMETERS.txt.
        /// HANG FIX: No recursive directory search (Directory.GetFiles with
        /// SearchOption.AllDirectories can scan thousands of files on broad paths
        /// like C:\ProgramData\Autodesk\, freezing Revit for minutes).
        /// </summary>
        private static string FindMrParametersFile(string currentSpFile)
        {
            const string fileName = "MR_PARAMETERS.txt";

            // 1. Standard data path lookup
            string found = StingToolsApp.FindDataFile(fileName);
            if (!string.IsNullOrEmpty(found))
                return found;

            // 2. Search next to the currently set shared parameter file
            if (!string.IsNullOrEmpty(currentSpFile))
            {
                try
                {
                    if (File.Exists(currentSpFile))
                    {
                        string spDir = Path.GetDirectoryName(currentSpFile);
                        if (!string.IsNullOrEmpty(spDir))
                        {
                            string candidate = Path.Combine(spDir, fileName);
                            if (File.Exists(candidate))
                            {
                                StingLog.Info($"Found {fileName} next to current SP file: {candidate}");
                                return candidate;
                            }
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"path resolution failed: {ex.Message}"); }
            }

            // 3. Search known deployment paths — flat lookups only, NO recursive search
            string dllDir = !string.IsNullOrEmpty(StingToolsApp.AssemblyPath)
                ? Path.GetDirectoryName(StingToolsApp.AssemblyPath) : null;

            if (!string.IsNullOrEmpty(dllDir))
            {
                string[] candidates = new[]
                {
                    Path.Combine(dllDir, "data", fileName),
                    Path.Combine(dllDir, "Data", fileName),
                    Path.Combine(dllDir, fileName),
                    Path.Combine(dllDir, "..", "data", fileName),
                    Path.Combine(dllDir, "..", "Data", fileName),
                    Path.Combine(dllDir, "..", "StingTools", "Data", fileName),
                    Path.Combine(dllDir, "..", "StingTools", "data", fileName),
                };

                foreach (string path in candidates)
                {
                    try
                    {
                        if (File.Exists(path))
                        {
                            string resolved = Path.GetFullPath(path);
                            StingLog.Info($"Found {fileName} at: {resolved}");
                            return resolved;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"File search path probe failed: {ex.Message}"); }
                }
            }

            if (!string.IsNullOrEmpty(StingToolsApp.DataPath))
            {
                try
                {
                    string inData = Path.Combine(StingToolsApp.DataPath, fileName);
                    if (File.Exists(inData)) return inData;
                    string aboveData = Path.Combine(StingToolsApp.DataPath, "..", fileName);
                    if (File.Exists(aboveData)) return Path.GetFullPath(aboveData);
                }
                catch (Exception ex) { StingLog.Warn($"DataPath file search failed: {ex.Message}"); }
            }

            StingLog.Warn($"{fileName} not found in any search path");
            return null;
        }
    }

    /// <summary>
    /// FIX-8.1: Purge shared parameters from project.
    /// Mode 1 — Audit: report bound params vs MR_PARAMETERS.txt.
    /// Mode 2 — Purge orphaned: remove params NOT in MR file.
    /// Mode 3 — Purge all STING: remove all ASS_* / STING_* bindings.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PurgeSharedParamsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var td = new TaskDialog("STING — Purge Shared Parameters");
            td.MainInstruction = "Shared parameter management";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Audit only", "Count bound vs MR file — no changes");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Purge orphaned", "Remove params NOT in MR_PARAMETERS.txt");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Purge ALL STING", "Remove all ASS_* and STING_* bindings");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            switch (td.Show())
            {
                case TaskDialogResult.CommandLink1: return Audit(doc);
                case TaskDialogResult.CommandLink2: return Purge(doc, false);
                case TaskDialogResult.CommandLink3: return Purge(doc, true);
                default: return Result.Cancelled;
            }
        }

        private static HashSet<string> LoadMR()
        {
            var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string f = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
            if (string.IsNullOrEmpty(f)) return s;
            foreach (string l in File.ReadAllLines(f))
            {
                if (!l.StartsWith("PARAM")) continue;
                var p = l.Split('\t');
                if (p.Length >= 3) s.Add(p[2]);
            }
            return s;
        }

        private static Result Audit(Document doc)
        {
            var known = LoadMR();
            var iter = doc.ParameterBindings.ForwardIterator();
            int total = 0, inMR = 0;
            var orphans = new List<string>();
            while (iter.MoveNext())
            {
                if (iter.Key is InternalDefinition def)
                {
                    total++;
                    if (known.Contains(def.Name)) inMR++;
                    else orphans.Add(def.Name);
                }
            }
            var sb = new StringBuilder();
            sb.AppendLine($"MR_PARAMETERS.txt: {known.Count}  |  Bound: {total}  |  Matched: {inMR}  |  Orphaned: {orphans.Count}");
            if (orphans.Count > 0) { sb.AppendLine("\nOrphaned:"); foreach (string n in orphans.Take(20)) sb.AppendLine($"  {n}"); }
            TaskDialog.Show("Param Audit", sb.ToString());
            return Result.Succeeded;
        }

        private static Result Purge(Document doc, bool allSting)
        {
            var known = LoadMR();
            var iter = doc.ParameterBindings.ForwardIterator();
            var remove = new List<(string n, Definition d)>();
            while (iter.MoveNext())
            {
                if (iter.Key is InternalDefinition def)
                {
                    bool isSting = def.Name.StartsWith("ASS_", StringComparison.OrdinalIgnoreCase)
                                || def.Name.StartsWith("STING_", StringComparison.OrdinalIgnoreCase);
                    if (allSting ? isSting : !known.Contains(def.Name))
                        remove.Add((def.Name, def));
                }
            }
            if (remove.Count == 0)
            { TaskDialog.Show("Purge", "Nothing to remove."); return Result.Succeeded; }

            var c = new TaskDialog("Confirm Purge");
            c.MainInstruction = $"Remove {remove.Count} parameter bindings?";
            c.MainContent = string.Join("\n", remove.Take(15).Select(r => $"  {r.n}"))
                + (remove.Count > 15 ? $"\n  ... and {remove.Count - 15} more" : "")
                + "\n\nElements lose stored values. Run 'Load Shared Params' to rebind.";
            c.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (c.Show() == TaskDialogResult.Cancel) return Result.Cancelled;

            int removed = 0;
            using (var tx = new Transaction(doc, "STING Purge Params"))
            {
                tx.Start();
                foreach (var (n, d) in remove)
                    try { doc.ParameterBindings.Remove(d); removed++; }
                    catch (Exception ex) { StingLog.Warn($"PurgeParams '{n}': {ex.Message}"); }
                tx.Commit();
            }
            ParameterHelpers.ClearParamCache();
            TaskDialog.Show("Purge Done", $"Removed {removed}/{remove.Count} bindings.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Dismisses all warnings during parameter binding transactions.
    /// Without this, Revit accumulates FailureMessage objects in memory
    /// for each binding operation, causing slowdown and eventual crash.
    /// </summary>
    internal class BindingWarningSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            try
            {
                var failures = failuresAccessor.GetFailureMessages();
                if (failures == null) return FailureProcessingResult.Continue;
                foreach (FailureMessageAccessor failure in failures)
                {
                    if (failure.GetSeverity() == FailureSeverity.Warning)
                        failuresAccessor.DeleteWarning(failure);
                }
            }
            catch
            {
                // Never crash inside the failure preprocessor — Revit will hang
            }
            return FailureProcessingResult.Continue;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  StingParamManagerCommand — Unified parameter management UI
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Unified parameter management: browse bound parameters, add missing
    /// parameters, and view parameter statistics for the current project.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StingParamManagerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                var dlg = new TaskDialog("STING Parameter Manager");
                dlg.MainInstruction = "Parameter Management";
                dlg.MainContent = "Choose an action for shared parameter management.";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Browse All Bound Parameters",
                    "View all parameters currently bound in this project.");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Add Missing Parameters",
                    "Bind any STING parameters not yet present in this project.");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Parameter Statistics",
                    "Show counts of bound, STING-prefixed, and orphaned parameters.");
                dlg.CommonButtons = TaskDialogCommonButtons.Close;

                var result = dlg.Show();

                if (result == TaskDialogResult.CommandLink1)
                {
                    // Browse all bound parameters
                    var bindings = doc.ParameterBindings;
                    var iter = bindings.ForwardIterator();
                    var paramNames = new List<string>();
                    while (iter.MoveNext())
                    {
                        if (iter.Key is InternalDefinition def)
                            paramNames.Add(def.Name);
                    }
                    paramNames.Sort(StringComparer.OrdinalIgnoreCase);

                    var sb = new StringBuilder();
                    sb.AppendLine($"Total bound parameters: {paramNames.Count}\n");
                    foreach (string name in paramNames)
                        sb.AppendLine($"  {name}");

                    TaskDialog.Show("Bound Parameters", sb.ToString());
                    StingLog.Info($"ParamManager: browsed {paramNames.Count} bound parameters");
                }
                else if (result == TaskDialogResult.CommandLink2)
                {
                    // Delegate to LoadSharedParamsCommand
                    string msg = "";
                    var cmd = new LoadSharedParamsCommand();
                    cmd.Execute(commandData, ref msg, elements);
                }
                else if (result == TaskDialogResult.CommandLink3)
                {
                    // Parameter statistics
                    var bindings = doc.ParameterBindings;
                    var iter = bindings.ForwardIterator();
                    int total = 0, stingPrefixed = 0, orphaned = 0;
                    var stingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // Collect all known STING parameter names from registry
                    try
                    {
                        foreach (var kvp in ParamRegistry.AllParamGuids)
                            stingNames.Add(kvp.Key);
                    }
                    catch (Exception ex) { StingLog.Warn($"Load param registry GUIDs: {ex.Message}"); }

                    while (iter.MoveNext())
                    {
                        total++;
                        if (iter.Key is InternalDefinition def)
                        {
                            string name = def.Name ?? "";
                            if (name.StartsWith("ASS_", StringComparison.OrdinalIgnoreCase) ||
                                name.StartsWith("HVC_", StringComparison.OrdinalIgnoreCase) ||
                                name.StartsWith("ELC_", StringComparison.OrdinalIgnoreCase) ||
                                name.StartsWith("PLM_", StringComparison.OrdinalIgnoreCase) ||
                                name.StartsWith("FLS_", StringComparison.OrdinalIgnoreCase) ||
                                name.StartsWith("LTG_", StringComparison.OrdinalIgnoreCase) ||
                                name.StartsWith("MAT_", StringComparison.OrdinalIgnoreCase) ||
                                name.StartsWith("COM_", StringComparison.OrdinalIgnoreCase) ||
                                name.StartsWith("SEC_", StringComparison.OrdinalIgnoreCase) ||
                                name.StartsWith("NCL_", StringComparison.OrdinalIgnoreCase) ||
                                name.StartsWith("ICT_", StringComparison.OrdinalIgnoreCase) ||
                                name.StartsWith("ELE_", StringComparison.OrdinalIgnoreCase) ||
                                name.StartsWith("TAG_", StringComparison.OrdinalIgnoreCase) ||
                                name.StartsWith("STING_", StringComparison.OrdinalIgnoreCase) ||
                                name.StartsWith("ARCH_", StringComparison.OrdinalIgnoreCase) ||
                                name.StartsWith("BLE_", StringComparison.OrdinalIgnoreCase))
                            {
                                stingPrefixed++;
                            }

                            if (!stingNames.Contains(name) &&
                                !name.StartsWith("ASS_", StringComparison.OrdinalIgnoreCase) &&
                                !name.StartsWith("STING_", StringComparison.OrdinalIgnoreCase))
                            {
                                orphaned++;
                            }
                        }
                    }

                    int missing = Math.Max(0, stingNames.Count - stingPrefixed);

                    TaskDialog.Show("Parameter Statistics",
                        $"Total bound parameters: {total}\n" +
                        $"STING-prefixed parameters: {stingPrefixed}\n" +
                        $"Registry parameters: {stingNames.Count}\n" +
                        $"Estimated missing: {missing}\n" +
                        $"Non-STING (third-party): {orphaned}");
                    StingLog.Info($"ParamManager stats: total={total}, sting={stingPrefixed}, missing~{missing}");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StingParamManagerCommand failed", ex);
                try { TaskDialog.Show("STING", $"Parameter Manager failed:\n{ex.Message}"); } catch (Exception dlgEx) { StingLog.Warn($"TaskDialog fallback: {dlgEx.Message}"); }
                return Result.Failed;
            }
        }
    }
}
