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

            // ── Step 3b: Scan ALL SharedParameterElements for GUID+type conflicts ──
            // Revit stores shared parameter definitions in SharedParameterElement.
            // These can exist WITHOUT any binding (orphaned by prior Remove, imported
            // from family/CAD, or created by another addin). doc.ParameterBindings
            // does not enumerate these, so the name-based filter above misses them.
            //
            // When ParameterBindings.Insert() is called with an ExternalDefinition whose
            // GUID matches an existing SharedParameterElement of a different data type
            // (e.g. project has CST_LOCAL_MAT_BOOL as Text, MR file has it as Yes/No),
            // Revit raises an "Error - cannot be ignored" modal dialog that
            // BindingWarningSwallower cannot suppress (it only handles Warning severity).
            // The only safe fix is to detect these conflicts up-front and skip them.
            StingLog.Info("LoadSharedParams: step 3b — scanning SharedParameterElements for type conflicts");
            var existingSpecByGuid = new Dictionary<Guid, (string name, string typeId)>();
            // Parallel name-keyed index. Catches the case where the project has a
            // SharedParameterElement of the same NAME but a different GUID — this
            // also produces an unrecoverable "cannot be added" Error modal because
            // Revit treats name as a unique key inside a definition store.
            var existingSpecByName = new Dictionary<string, (Guid guid, string typeId)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var spes = new FilteredElementCollector(doc)
                    .OfClass(typeof(SharedParameterElement))
                    .Cast<SharedParameterElement>();
                foreach (var spe in spes)
                {
                    try
                    {
                        Guid g = spe.GuidValue;
                        InternalDefinition intDef = spe.GetDefinition();
                        string name = intDef?.Name ?? "";
                        string typeId = null;
                        try { typeId = intDef?.GetDataType()?.TypeId; }
                        catch (Exception gdtEx) { StingLog.Warn($"GetDataType '{name}': {gdtEx.Message}"); }
                        existingSpecByGuid[g] = (name, typeId);
                        if (!string.IsNullOrEmpty(name))
                            existingSpecByName[name] = (g, typeId);
                    }
                    catch (Exception speEx) { StingLog.Warn($"Inspect SharedParameterElement: {speEx.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Collect SharedParameterElements: {ex.Message}"); }
            StingLog.Info($"Pre-scan: {existingSpecByGuid.Count} SharedParameterElements in project");

            // Filter each group to only unbound params AND skip GUID/type conflicts
            int totalToBind = 0;
            int alreadyBound = 0;
            int typeConflicts = 0;
            var typeConflictDetails = new List<string>();
            var groupsToProcess = new List<(string groupName, List<ExternalDefinition> defs)>();
            foreach (var (groupName, defs) in groupDefs)
            {
                var unbound = new List<ExternalDefinition>();
                foreach (var d in defs)
                {
                    // Skip if already bound by name
                    if (existingBindings.Contains(d.Name))
                    {
                        alreadyBound++;
                        continue;
                    }

                    // Skip if a SharedParameterElement already holds this GUID
                    // (or this name) in the project. Revit refuses to add a definition
                    // whose GUID matches an existing one if name OR data type differs,
                    // and the failure is severity=Error so BindingWarningSwallower
                    // (warning-only) cannot dismiss the resulting modal.
                    bool guidExists = existingSpecByGuid.TryGetValue(d.GUID, out var existing);
                    bool nameAlreadyOnSomeGuid = existingSpecByName.TryGetValue(d.Name, out var existingByName);

                    if (guidExists || nameAlreadyOnSomeGuid)
                    {
                        string newTypeId = null;
                        try { newTypeId = d.GetDataType()?.TypeId; }
                        catch (Exception gdtEx) { StingLog.Warn($"GetDataType def '{d.Name}': {gdtEx.Message}"); }

                        // Indeterminate type on either side → treat as conflict and skip.
                        // Inserting blind risks Revit's unrecoverable "cannot be added"
                        // Error-severity modal that the failure preprocessor can't eat.
                        bool typeIndeterminate = guidExists
                            ? (existing.typeId == null || newTypeId == null)
                            : (existingByName.typeId == null || newTypeId == null);

                        bool nameMismatch = guidExists
                            && !string.Equals(existing.name, d.Name, StringComparison.OrdinalIgnoreCase);
                        bool typeMismatch = guidExists
                            ? (!typeIndeterminate && !string.Equals(existing.typeId, newTypeId, StringComparison.Ordinal))
                            : (!typeIndeterminate && !string.Equals(existingByName.typeId, newTypeId, StringComparison.Ordinal));
                        // Name-collision-with-different-GUID: also a hard conflict
                        bool nameOwnedByOtherGuid = !guidExists && nameAlreadyOnSomeGuid
                            && existingByName.guid != d.GUID;

                        if (nameMismatch || typeMismatch || typeIndeterminate || nameOwnedByOtherGuid)
                        {
                            typeConflicts++;
                            if (typeConflictDetails.Count < 20)
                            {
                                string reason;
                                if (nameOwnedByOtherGuid)
                                    reason = $"'{d.Name}': name held by a different GUID in project ({existingByName.guid})";
                                else if (nameMismatch)
                                    reason = $"'{d.Name}': GUID already held by '{existing.name}' in project";
                                else if (typeMismatch)
                                    reason = $"'{d.Name}': project has type {ShortTypeId(guidExists ? existing.typeId : existingByName.typeId)}, MR file has {ShortTypeId(newTypeId)}";
                                else
                                    reason = $"'{d.Name}': data type indeterminate on either side — skipping to avoid Revit's unrecoverable Error modal";
                                typeConflictDetails.Add(reason);
                            }
                            continue;
                        }
                    }

                    unbound.Add(d);
                }
                if (unbound.Count > 0)
                {
                    groupsToProcess.Add((groupName, unbound));
                    totalToBind += unbound.Count;
                }
            }

            if (typeConflicts > 0)
            {
                StingLog.Warn($"Skipped {typeConflicts} parameter(s) due to GUID/type conflicts with existing project params:");
                foreach (string d in typeConflictDetails)
                    StingLog.Warn($"  {d}");
            }

            StingLog.Info($"To bind: {totalToBind}, already bound: {alreadyBound}, type conflicts: {typeConflicts}");

            // ── Always clean material bindings, even when nothing new to bind ──
            // CRITICAL: This must run BEFORE the early return below. Prior sessions
            // may have bound ALL 2300+ params to OST_Materials via coreCats. This
            // cleanup removes Materials from non-material params and adds it to
            // material-relevant params (MAT_*, PROP_*, BLE_APP-*, BLE_MAT_*, COMP_MAT_*).
            int matRemovedEarly = 0, matAddedEarly = 0;
            try
            {
                (matRemovedEarly, matAddedEarly) = CleanMaterialBindings(doc, app);
            }
            catch (Exception ex)
            {
                StingLog.Error("CleanMaterialBindings (early) failed", ex);
            }

            if (totalToBind == 0)
            {
                var earlyMsg = new StringBuilder();
                earlyMsg.AppendLine($"All {totalDefs} parameters are already bound — nothing to do.");
                earlyMsg.AppendLine($"{alreadyBound} parameters already present in project.");
                if (typeConflicts > 0)
                {
                    earlyMsg.AppendLine($"{typeConflicts} parameter(s) skipped due to GUID/type conflicts.");
                    earlyMsg.AppendLine();
                    earlyMsg.AppendLine("These exist in the project with a different data type or name.");
                    earlyMsg.AppendLine("Skipped to avoid Revit's unrecoverable 'cannot be added' error.");
                    earlyMsg.AppendLine("First 10:");
                    foreach (string d in typeConflictDetails.Take(10))
                        earlyMsg.AppendLine($"  {d}");
                    if (typeConflictDetails.Count > 10)
                        earlyMsg.AppendLine($"  ... and {typeConflictDetails.Count - 10} more (see StingTools.log)");
                }
                if (matRemovedEarly > 0 || matAddedEarly > 0)
                {
                    earlyMsg.AppendLine();
                    earlyMsg.AppendLine($"Material cleanup: removed Materials from {matRemovedEarly} params, added to {matAddedEarly} params.");
                }
                earlyMsg.AppendLine($"\nSource: {spFile}");
                TaskDialog.Show("STING Tools - Load Shared Params", earlyMsg.ToString());
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
            // via dedicated matCats override in BuildGroupCategoryOverrides() to BLE
            // element categories (walls, floors, ceilings, etc.), NOT to OST_Materials
            // (which doesn't support AllowsBoundParameters in Revit API).
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

            // Pre-build a material-only binding for per-parameter overrides
            // (BLE_APP-*, BLE_MAT_* params in CST_PROC group that need Materials binding)
            InstanceBinding matOnlyBinding = null;
            if (groupCatOverrides.TryGetValue("MAT_INFO", out CategorySet matOverrideCats))
                matOnlyBinding = app.Create.NewInstanceBinding(matOverrideCats);

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
                                // Per-parameter override: if this param is material-relevant
                                // (BLE_APP-*, BLE_MAT_*) but the group binding targets core cats,
                                // use the material-only binding instead so it binds to OST_Materials.
                                InstanceBinding paramBinding = binding;
                                if (matOnlyBinding != null
                                    && !groupCatOverrides.ContainsKey(groupName)
                                    && IsMaterialRelevantParam(extDef.Name))
                                {
                                    paramBinding = matOnlyBinding;
                                }

                                bool result = doc.ParameterBindings.Insert(
                                    extDef, paramBinding, GroupTypeId.General);

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

            // ── Step 6: Clean material bindings (second pass) ──
            // Run again after new bindings — new params may have been bound to
            // non-material groups that include OST_Materials by mistake.
            int matRemoved = matRemovedEarly, matAdded = matAddedEarly;
            try
            {
                var (r2, a2) = CleanMaterialBindings(doc, app);
                matRemoved += r2;
                matAdded += a2;
            }
            catch (Exception ex)
            {
                StingLog.Error("CleanMaterialBindings (post-bind) failed", ex);
                errors.Add($"Material cleanup: {ex.Message}");
            }

            // ── Step 6b: Remove NON-MATERIAL shared parameter bindings from OST_Materials ──
            // Only material-relevant params (MAT_*, PROP_*, BLE_APP-*, BLE_MAT_*, COMP_MAT_*)
            // should be bound to OST_Materials. All others are removed to prevent
            // 2300+ irrelevant parameters appearing on every material element.
            int materialsUnbound = 0;
            int materialsKept = 0;
            try
            {
                Category matCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Materials);
                if (matCat != null)
                {
                    // Collect bindings that include OST_Materials but are NOT material-relevant
                    var toFix = new List<(Definition def, ElementBinding binding)>();
                    var scanIter = doc.ParameterBindings.ForwardIterator();
                    while (scanIter.MoveNext())
                    {
                        if (scanIter.Current is ElementBinding eb && eb.Categories.Contains(matCat))
                        {
                            string paramName = scanIter.Key?.Name ?? "";
                            if (IsMaterialRelevantParam(paramName))
                            {
                                materialsKept++;
                                continue; // Keep material-relevant params bound to OST_Materials
                            }
                            toFix.Add((scanIter.Key, eb));
                        }
                    }

                    if (toFix.Count > 0)
                    {
                        StingLog.Info($"Cleaning up {toFix.Count} non-material parameter bindings from OST_Materials (keeping {materialsKept} material-relevant)");
                        using (Transaction txClean = new Transaction(doc, "STING Remove Material Bindings"))
                        {
                            txClean.Start();
                            foreach (var (def, eb) in toFix)
                            {
                                try
                                {
                                    eb.Categories.Erase(matCat);
                                    if (eb.Categories.Size > 0)
                                        doc.ParameterBindings.ReInsert(def, eb);
                                    else
                                        doc.ParameterBindings.Remove(def);
                                    materialsUnbound++;
                                }
                                catch (Exception ex2)
                                {
                                    StingLog.Warn($"Failed to unbind '{def.Name}' from Materials: {ex2.Message}");
                                }
                            }
                            txClean.Commit();
                        }
                        StingLog.Info($"Removed {materialsUnbound} non-material parameter bindings from OST_Materials, kept {materialsKept} material-relevant");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Material binding cleanup: {ex.Message}");
            }

            // ── Report ──
            var report = new StringBuilder();
            report.AppendLine($"Bound: {bound} parameters");
            report.AppendLine($"Already present: {alreadyBound}");
            report.AppendLine($"Skipped/failed: {skipped}");
            if (typeConflicts > 0)
                report.AppendLine($"Skipped (type conflicts): {typeConflicts} — see Details");
            report.AppendLine($"Total in file: {totalDefs}");
            report.AppendLine($"Categories: {coreCats.Size}");
            report.AppendLine($"Groups processed: {groupsToProcess.Count}");
            if (matRemoved > 0 || matAdded > 0)
                report.AppendLine($"Material cleanup: removed Materials from {matRemoved} params, added to {matAdded} params");
            if (materialsUnbound > 0)
                report.AppendLine($"Material cleanup: removed {materialsUnbound} stale bindings from OST_Materials");
            report.AppendLine();

            if (typeConflicts > 0)
            {
                report.AppendLine($"Type conflicts skipped ({typeConflicts}): the project already has these");
                report.AppendLine("shared parameters with a different data type or name. Skipped to avoid");
                report.AppendLine("Revit's unrecoverable 'cannot be added' error. To re-load as the MR file");
                report.AppendLine("type, first purge the conflicting parameters (STING → Purge Shared Params):");
                foreach (string d in typeConflictDetails.Take(10))
                    report.AppendLine($"  {d}");
                if (typeConflictDetails.Count > 10)
                    report.AppendLine($"  ... and {typeConflictDetails.Count - 10} more (see StingTools.log)");
                report.AppendLine();
            }

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

            // A-5: refresh ParamRegistry from PARAMETER_REGISTRY.json so any
            // binding additions / new system codes are visible to in-session
            // commands without a Revit restart. SharedParamGuids cached
            // properties are flushed inside Reload().
            try
            {
                ParamRegistry.Reload();
                SharedParamGuids.InvalidateCache();
                StingLog.Info("ParamRegistry reloaded after parameter binding.");
            }
            catch (Exception ex) { StingLog.Warn($"LoadSharedParams ParamRegistry.Reload: {ex.Message}"); }

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

        // Clash rec-3: Categories watched by LiveClashUpdater. CLASH_COORDINATION
        // group binds only to these so clash parameters don't pollute every
        // element in the model. Keep this list in sync with
        // StingTools/Clash/LiveClashUpdater.cs Register() triggers.
        private static readonly BuiltInCategory[] ClashCategories = new[]
        {
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_CableTray,
            BuiltInCategory.OST_Conduit,
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Ceilings,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_StructuralColumns,
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

            // Clash rec-3: CLASH_COORDINATION group binds only to the 9 categories
            // LiveClashUpdater.Register watches. Keeps CLASH_LIVE_FLAG /
            // CLASH_LAST_RUN_DT / CLASH_OTHER_ELEMENT_TXT off every element in
            // the model (which would spam parameter-editor dropdowns).
            var clashCats = BuildCatSet(doc, ClashCategories);
            if (clashCats.Size > 0)
            {
                overrides["CLASH_COORDINATION"] = clashCats;
            }

            // BOQ Cost Manager (Phase 91) — project-level parameters bind to ProjectInformation only
            // so the budget, variance, coverage % and last-costed timestamp sit on the ProjectInfo
            // element (accessible via doc.ProjectInformation) instead of every modeled element.
            // Without this override the new group 13 params would be bound to the universal
            // category set which mostly isn't useful for project-wide metrics.
            try
            {
                var prjSet = new CategorySet();
                var prjCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_ProjectInformation);
                if (prjCat != null && prjCat.AllowsBoundParameters)
                {
                    prjSet.Insert(prjCat);
                    // Merge with existing PRJ_INFORMATION override if it already exists —
                    // some existing PRJ_TB_* params may want both ProjectInfo AND sheets.
                    if (overrides.TryGetValue("PRJ_INFORMATION", out var existing))
                    {
                        foreach (Category c in existing) prjSet.Insert(c);
                    }
                    overrides["PRJ_INFORMATION"] = prjSet;
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildGroupCategoryOverrides PRJ_INFORMATION: {ex.Message}"); }

            // OST_Materials does NOT support AllowsBoundParameters in Revit API,
            // so we bind material-relevant params (MAT_INFO, PROP_PHYSICAL) to
            // BLE element categories (Walls, Floors, Ceilings, Roofs, etc.) —
            // the elements that USE materials. This makes material properties
            // visible on those elements and schedulable in material takeoffs.
            var matCats = BuildCatSet(doc, BleCategories);
            if (matCats.Size > 0)
            {
                overrides["MAT_INFO"] = matCats;
                overrides["PROP_PHYSICAL"] = matCats;
                // NOTE: Individual BLE_APP-* and BLE_MAT_* params from CST_PROC group
                // are handled per-parameter in the binding loop via IsMaterialRelevantParam(),
                // since the CST_PROC group also contains 100+ non-material params that
                // should NOT be bound to Materials.
            }

            return overrides;
        }

        // ════════════════════════════════════════════════════════════════
        // Material binding cleanup
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Determines whether a parameter name is material-relevant and should
        /// be bound to OST_Materials. Only these prefixes/groups belong on materials.
        /// </summary>
        private static bool IsMaterialRelevantParam(string paramName)
        {
            if (string.IsNullOrEmpty(paramName)) return false;

            // Group 12 (MAT_INFO): all MAT_ prefixed params
            if (paramName.StartsWith("MAT_", StringComparison.Ordinal)) return true;

            // Group 14 (PROP_PHYSICAL): all PROP_ prefixed params
            if (paramName.StartsWith("PROP_", StringComparison.Ordinal)) return true;

            // Group 2 (CST_PROC) material-relevant subsets:
            // 23 BLE_APP-* appearance/asset params (hyphens in names)
            if (paramName.StartsWith("BLE_APP-", StringComparison.Ordinal)) return true;

            // 19 BLE_MAT_* material metadata params
            if (paramName.StartsWith("BLE_MAT_", StringComparison.Ordinal)) return true;

            // Composite material tags
            if (paramName.StartsWith("COMP_MAT_", StringComparison.Ordinal)) return true;

            return false;
        }

        /// <summary>
        /// NUCLEAR material binding cleanup — completely removes and rebinds every
        /// parameter that has wrong Materials category assignment.
        ///
        /// Strategy: for each wrongly-bound param, look up its ExternalDefinition
        /// from the shared parameter FILE (not from the BindingMap iterator — those
        /// references become invalid after Remove). Then Remove the old binding and
        /// Insert a fresh one with the correct CategorySet.
        ///
        /// This is the only approach that reliably works in the Revit API because:
        /// - ReInsert() silently fails to actually change categories in many cases
        /// - Remove+Insert using ExternalDefinition from file FAILS silently
        /// - ReInsert using ExternalDefinition FAILS silently
        /// - The BindingMap is keyed by InternalDefinition, NOT ExternalDefinition
        ///
        /// APPROACH (6th attempt — InternalDefinition via SharedParameterElement):
        /// Use SharedParameterElement.GetDefinition() to get the project-internal
        /// Definition reference. This is the actual key in the BindingMap. Then use
        /// ReInsert(internalDef, newBinding) to change the category set.
        ///
        /// Returns (removed, added) counts.
        /// </summary>
        private static (int removed, int added) CleanMaterialBindings(
            Document doc, Autodesk.Revit.ApplicationServices.Application app)
        {
            Category matCat;
            try
            {
                matCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Materials);
            }
            catch (Exception ex) { StingLog.Warn($"Cannot get OST_Materials: {ex.Message}"); return (0, 0); }

            if (matCat == null || !matCat.AllowsBoundParameters)
                return (0, 0);

            long matCatIdVal = matCat.Id.Value;

            // ── Step A: Build name→SharedParameterElement lookup ──
            // SharedParameterElement.GetDefinition() returns InternalDefinition,
            // which is the ACTUAL key used by the BindingMap. All prior approaches
            // used ExternalDefinition (from the .txt file) which is a DIFFERENT
            // object identity — that's why Remove/Insert/ReInsert all failed silently.
            var speByName = new Dictionary<string, SharedParameterElement>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var spes = new FilteredElementCollector(doc)
                    .OfClass(typeof(SharedParameterElement))
                    .Cast<SharedParameterElement>();
                foreach (var spe in spes)
                {
                    InternalDefinition intDef = spe.GetDefinition();
                    if (intDef != null && !string.IsNullOrEmpty(intDef.Name))
                        speByName[intDef.Name] = spe;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Cannot collect SharedParameterElements: {ex.Message}");
                return (0, 0);
            }

            StingLog.Info($"Material cleanup: found {speByName.Count} SharedParameterElements in project");

            if (speByName.Count == 0)
                return (0, 0);

            // ── Step B: Classify params by current binding vs desired ──
            var toRemoveMat = new List<string>(); // params that HAVE Materials but SHOULDN'T
            var toAddMat = new List<string>();     // params that LACK Materials but SHOULD have it

            var iter = doc.ParameterBindings.ForwardIterator();
            while (iter.MoveNext())
            {
                var def = iter.Key;
                if (def == null || string.IsNullOrEmpty(def.Name)) continue;
                if (!(iter.Current is InstanceBinding ib)) continue;

                bool hasMat = false;
                foreach (Category c in ib.Categories)
                {
                    if (c.Id.Value == matCatIdVal)
                    { hasMat = true; break; }
                }

                bool shouldHaveMat = IsMaterialRelevantParam(def.Name);

                if (hasMat && !shouldHaveMat)
                    toRemoveMat.Add(def.Name);
                else if (!hasMat && shouldHaveMat)
                    toAddMat.Add(def.Name);
            }

            StingLog.Info($"Material cleanup: {toRemoveMat.Count} to remove Materials, {toAddMat.Count} to add Materials");

            if (toRemoveMat.Count == 0 && toAddMat.Count == 0)
                return (0, 0);

            int removed = 0, added = 0;
            int removeFailed = 0, addFailed = 0;

            // ── Step C: Remove Materials from non-material params using InternalDefinition ──
            // Process in individual transactions for safety.
            foreach (string name in toRemoveMat)
            {
                if (!speByName.TryGetValue(name, out SharedParameterElement spe))
                {
                    removeFailed++;
                    if (removeFailed <= 5)
                        StingLog.Warn($"No SharedParameterElement for '{name}' — cannot rebind");
                    continue;
                }

                InternalDefinition intDef = spe.GetDefinition();
                if (intDef == null) { removeFailed++; continue; }

                // Read current binding via the InternalDefinition key
                Binding currentBinding = doc.ParameterBindings.get_Item(intDef);
                if (!(currentBinding is InstanceBinding currentIB)) { removeFailed++; continue; }

                // Build new CategorySet WITHOUT Materials
                var newCats = app.Create.NewCategorySet();
                int kept = 0;
                foreach (Category c in currentIB.Categories)
                {
                    if (c.Id.Value != matCatIdVal)
                    { newCats.Insert(c); kept++; }
                }
                if (kept == 0) continue; // must keep at least one category

                using (Transaction tx = new Transaction(doc, $"STING Fix Mat: {name}"))
                {
                    var failOpts = tx.GetFailureHandlingOptions();
                    failOpts.SetFailuresPreprocessor(new BindingWarningSwallower());
                    tx.SetFailureHandlingOptions(failOpts);

                    tx.Start();
                    try
                    {
                        var newBinding = app.Create.NewInstanceBinding(newCats);
                        // ReInsert using InternalDefinition — the actual BindingMap key
                        if (doc.ParameterBindings.ReInsert(intDef, newBinding))
                        {
                            removed++;
                        }
                        else
                        {
                            removeFailed++;
                            if (removeFailed <= 5)
                                StingLog.Warn($"ReInsert(InternalDef) failed for '{name}'");
                        }
                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        removeFailed++;
                        if (removeFailed <= 5)
                            StingLog.Warn($"Remove mat from '{name}': {ex.Message}");
                        if (tx.HasStarted() && !tx.HasEnded())
                            tx.RollBack();
                    }
                }
            }

            // ── Step D: Add Materials to material-relevant params using InternalDefinition ──
            foreach (string name in toAddMat)
            {
                if (!speByName.TryGetValue(name, out SharedParameterElement spe))
                {
                    addFailed++;
                    if (addFailed <= 5)
                        StingLog.Warn($"No SharedParameterElement for '{name}' — cannot add Materials");
                    continue;
                }

                InternalDefinition intDef = spe.GetDefinition();
                if (intDef == null) { addFailed++; continue; }

                Binding currentBinding = doc.ParameterBindings.get_Item(intDef);
                if (!(currentBinding is InstanceBinding currentIB)) { addFailed++; continue; }

                // Build new CategorySet WITH Materials added
                var newCats = app.Create.NewCategorySet();
                foreach (Category c in currentIB.Categories)
                    newCats.Insert(c);
                newCats.Insert(matCat);

                using (Transaction tx = new Transaction(doc, $"STING Add Mat: {name}"))
                {
                    var failOpts = tx.GetFailureHandlingOptions();
                    failOpts.SetFailuresPreprocessor(new BindingWarningSwallower());
                    tx.SetFailureHandlingOptions(failOpts);

                    tx.Start();
                    try
                    {
                        var newBinding = app.Create.NewInstanceBinding(newCats);
                        if (doc.ParameterBindings.ReInsert(intDef, newBinding))
                        {
                            added++;
                        }
                        else
                        {
                            addFailed++;
                            if (addFailed <= 5)
                                StingLog.Warn($"ReInsert(InternalDef) add-mat failed for '{name}'");
                        }
                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        addFailed++;
                        if (addFailed <= 5)
                            StingLog.Warn($"Add mat to '{name}': {ex.Message}");
                        if (tx.HasStarted() && !tx.HasEnded())
                            tx.RollBack();
                    }
                }
            }

            StingLog.Info($"Material cleanup done: removed={removed} (failed={removeFailed}), added={added} (failed={addFailed})");

            // ── Step E: Verification pass ──
            int stillPolluted = 0;
            int materialParamsMissing = 0;
            var verifyIter = doc.ParameterBindings.ForwardIterator();
            while (verifyIter.MoveNext())
            {
                var vDef = verifyIter.Key;
                if (vDef == null || string.IsNullOrEmpty(vDef.Name)) continue;
                if (!(verifyIter.Current is InstanceBinding vib)) continue;

                bool hasMat = false;
                foreach (Category c in vib.Categories)
                {
                    if (c.Id.Value == matCatIdVal)
                    { hasMat = true; break; }
                }

                bool shouldHaveMat = IsMaterialRelevantParam(vDef.Name);

                if (hasMat && !shouldHaveMat) stillPolluted++;
                if (!hasMat && shouldHaveMat) materialParamsMissing++;
            }

            if (stillPolluted > 0)
                StingLog.Warn($"VERIFICATION FAILED: {stillPolluted} non-material params STILL have Materials bound — " +
                    "InternalDefinition approach may need SharedParameterElement deletion as nuclear fallback");
            else if (removed > 0)
                StingLog.Info("VERIFICATION PASSED: All non-material params cleaned of Materials binding");

            if (materialParamsMissing > 0)
                StingLog.Warn($"VERIFICATION: {materialParamsMissing} material-relevant params still missing Materials binding");
            else if (added > 0)
                StingLog.Info("VERIFICATION PASSED: All material-relevant params have Materials binding");

            return (removed, added);
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
            // MAT_INFO and PROP_PHYSICAL groups bound to BLE categories
            // via group overrides (OST_Materials doesn't support bound params)
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

        /// <summary>
        /// Extract a short, user-friendly label from a Revit ForgeTypeId TypeId string.
        /// E.g. "autodesk.spec:spec.boolean.yesno-1.0.0" → "yesno",
        ///      "autodesk.spec:spec.string.text-1.0.0"   → "text".
        /// Falls back to the raw TypeId when the pattern does not match so the
        /// message is still actionable.
        /// </summary>
        private static string ShortTypeId(string typeId)
        {
            if (string.IsNullOrEmpty(typeId)) return "(unknown)";
            // Strip "autodesk.spec:spec." prefix and "-x.y.z" version suffix
            int colon = typeId.IndexOf(':');
            string tail = colon >= 0 && colon + 1 < typeId.Length
                ? typeId.Substring(colon + 1) : typeId;
            int dash = tail.IndexOf('-');
            if (dash > 0) tail = tail.Substring(0, dash);
            if (tail.StartsWith("spec.", StringComparison.Ordinal))
                tail = tail.Substring("spec.".Length);
            return tail;
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
    /// Dismisses warnings during parameter binding transactions and resolves the
    /// specific "shared parameter cannot be added — name/type conflicts with
    /// existing" Error. The conflict is severity=Error (not Warning) so without
    /// explicit handling Revit shows an unrecoverable modal listing every clash.
    /// We resolve such Errors by skipping the offending element — the binding
    /// just doesn't happen, which is the same effect as the upfront skip in
    /// LoadSharedParamsCommand step 3b.
    /// </summary>
    internal class BindingWarningSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            try
            {
                var failures = failuresAccessor.GetFailureMessages();
                if (failures == null) return FailureProcessingResult.Continue;
                bool resolvedAny = false;
                foreach (FailureMessageAccessor failure in failures)
                {
                    var sev = failure.GetSeverity();
                    if (sev == FailureSeverity.Warning)
                    {
                        failuresAccessor.DeleteWarning(failure);
                        continue;
                    }
                    if (sev != FailureSeverity.Error) continue;

                    // Match Revit's "shared parameter ... cannot be added with name ...
                    // because it conflicts with the existing name ... and type ..." message.
                    string desc = "";
                    try { desc = failure.GetDescriptionText() ?? ""; }
                    catch (Exception descEx) { StingLog.Warn($"Failure desc read: {descEx.Message}"); }
                    if (desc.IndexOf("shared parameter", StringComparison.OrdinalIgnoreCase) >= 0
                        && desc.IndexOf("conflicts", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        try
                        {
                            failuresAccessor.DeleteWarning(failure);
                            resolvedAny = true;
                            StingLog.Warn($"BindingWarningSwallower: dismissed shared-parameter conflict — {desc}");
                        }
                        catch (Exception delEx) { StingLog.Warn($"Dismiss shared-param conflict: {delEx.Message}"); }
                    }
                }
                if (resolvedAny)
                    return FailureProcessingResult.ProceedWithCommit;
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
