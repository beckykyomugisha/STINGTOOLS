using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

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

            // When nothing to bind, still run material cleanup once so prior bad
            // bindings get repaired, then return.
            if (totalToBind == 0)
            {
                int matRemovedOnly = 0, matAddedOnly = 0;
                try
                {
                    (matRemovedOnly, matAddedOnly) = CleanMaterialBindings(doc, app);
                }
                catch (Exception ex2)
                {
                    StingLog.Error("CleanMaterialBindings (no-bind path) failed", ex2);
                }

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
                if (matRemovedOnly > 0 || matAddedOnly > 0)
                {
                    earlyMsg.AppendLine();
                    earlyMsg.AppendLine($"Material cleanup: removed Materials from {matRemovedOnly} params, added to {matAddedOnly} params.");
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

            // ── Per-parameter category bindings (accuracy fix) ──
            // CATEGORY_BINDINGS.csv is the authoritative per-param×category source of truth.
            // Each discipline-scoped parameter binds to ITS OWN category set (e.g.
            // HVC_TERMINAL_* → Air Terminals only; LIG_* → anti-ligature element types;
            // ICT_* → data/comms devices) instead of its container group's whole category
            // set. This stops cross-discipline leakage (params showing on Ducts, etc.).
            //
            // Performance: bindings are clustered by identical category-set signature so we
            // build ONE InstanceBinding per distinct signature (built here, OUTSIDE the
            // per-param transaction loop) and share it across every param that needs it.
            var perParamBindingMap = new Dictionary<string, InstanceBinding>(StringComparer.OrdinalIgnoreCase);
            int perParamSignatures = 0;
            try
            {
                var perParamCats = SharedParamGuids.PerParamCategoryBindings;
                var sigToBinding = new Dictionary<string, InstanceBinding>(StringComparer.Ordinal);
                foreach (var kvp in perParamCats)
                {
                    if (kvp.Value == null || kvp.Value.Length == 0) continue;
                    // Stable signature from the ordered enum values
                    var ordered = kvp.Value.Select(b => (int)b).OrderBy(x => x).ToArray();
                    string sig = string.Join(",", ordered);
                    if (!sigToBinding.TryGetValue(sig, out InstanceBinding ib))
                    {
                        CategorySet cs = SharedParamGuids.BuildCategorySet(doc, kvp.Value);
                        if (cs.Size == 0) continue; // none of the spec categories bind here — skip
                        ib = app.Create.NewInstanceBinding(cs);
                        sigToBinding[sig] = ib;
                        perParamSignatures++;
                    }
                    perParamBindingMap[kvp.Key] = ib;
                }
                StingLog.Info($"LoadSharedParams: per-param CSV bindings — {perParamBindingMap.Count} params across {perParamSignatures} distinct category-set signatures");
            }
            catch (Exception ex) { StingLog.Warn($"Per-param binding pre-build failed, falling back to group bindings: {ex.Message}"); }

            // Collect discipline-scoped params that had no per-param CSV row AND no group
            // override — they fall back to the broad core set (a coverage GAP). Logged so
            // gaps surface for follow-up rather than silently binding to everything.
            var bindingGapParams = new List<string>();

            // ── Step 5: Bind ONE GROUP per transaction ──
            int bound = 0;
            int skipped = 0;
            var errors = new List<string>();
            var boundByGroup = new Dictionary<string, int>();

            StingLog.Info($"Binding {totalToBind} params across {groupsToProcess.Count} groups");

            StingProgressDialog progress = null;
            try { progress = StingProgressDialog.Show($"Loading {totalToBind} Shared Parameters", groupsToProcess.Count); }
            catch (Exception ex) { StingLog.Warn($"Could not show progress dialog: {ex.Message}"); }

            for (int gi = 0; gi < groupsToProcess.Count; gi++)
            {
                var (groupName, defs) = groupsToProcess[gi];

                try { progress?.Increment($"Group {gi + 1}/{groupsToProcess.Count}: {groupName}"); }
                catch (Exception ex2) { StingLog.Warn($"Progress increment: {ex2.Message}"); }

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
                                // Binding selection precedence:
                                //   1. Material-relevant params (BLE_APP-*, BLE_MAT_*) in a group
                                //      without its own override → material-only binding (OST_Materials
                                //      handled later by CleanMaterialBindings). Preserved verbatim.
                                //   2. Discipline-scoped params (group NOT universal) with a per-param
                                //      CATEGORY_BINDINGS.csv row → bind to THAT param's exact category
                                //      set (the accuracy fix — no cross-discipline leakage).
                                //   3. Otherwise the group binding (override or the broad core set).
                                //      Universal groups always keep the broad core set; a discipline
                                //      param that reaches the core set here is a coverage GAP.
                                InstanceBinding paramBinding = binding;
                                bool isUniversalGroup = UniversalGroups.Contains(groupName);

                                if (matOnlyBinding != null
                                    && !groupCatOverrides.ContainsKey(groupName)
                                    && IsMaterialRelevantParam(extDef.Name))
                                {
                                    paramBinding = matOnlyBinding;
                                }
                                else if (!isUniversalGroup
                                    && perParamBindingMap.TryGetValue(extDef.Name, out InstanceBinding ppb))
                                {
                                    paramBinding = ppb;
                                }
                                else if (!isUniversalGroup && !groupCatOverrides.ContainsKey(groupName))
                                {
                                    // discipline param, no per-param row, no override → broad core = GAP
                                    if (bindingGapParams.Count < 500) bindingGapParams.Add(extDef.Name);
                                }

                                bool result = doc.ParameterBindings.Insert(
                                    extDef, paramBinding, GroupTypeId.General);

                                if (result)
                                    groupBound++;
                                else
                                    skipped++;
                            }
                            catch (Exception ex3) { StingLog.Warn($"Suppressed: {ex3.Message}"); skipped++; }
                        }

                        tx.Commit();
                        bound += groupBound;
                        boundByGroup[groupName] = groupBound;

                        // Log AFTER transaction, not inside
                        StingLog.Info($"  → committed: {groupBound} bound, {defs.Count - groupBound} skipped");
                    }
                    catch (Exception ex3)
                    {
                        StingLog.Error($"Group '{groupName}' transaction failed", ex3);
                        if (tx.HasStarted() && !tx.HasEnded())
                            tx.RollBack();

                        skipped += defs.Count;
                        if (errors.Count < 10)
                            errors.Add($"Group '{groupName}': {ex3.Message}");
                    }
                }
            }

            try { progress?.Close(); } catch (Exception ex) { StingLog.Warn($"Progress close: {ex.Message}"); }

            // Surface coverage GAPs: discipline-scoped params that bound to the broad core
            // set because they lack a per-param CATEGORY_BINDINGS.csv row and a group
            // override. These are candidates for a CSV row (see docs/ROADMAP.md).
            if (bindingGapParams.Count > 0)
            {
                StingLog.Warn($"Binding coverage GAP: {bindingGapParams.Count} discipline-scoped param(s) fell back to the broad core set (no per-param CATEGORY_BINDINGS.csv row, no group override):");
                foreach (string gp in bindingGapParams.Take(50))
                    StingLog.Warn($"  GAP {gp}");
            }

            // ── Step 6: Clean material bindings (single post-bind pass) ──
            // CleanMaterialBindings now handles both removing Materials from
            // non-material params AND adding Materials to material-relevant
            // params, in a single batched transaction. Step 6b (duplicate
            // OST_Materials cleanup) was removed — toRemoveMat covers it.
            int matRemoved = 0, matAdded = 0;
            try
            {
                var (r2, a2) = CleanMaterialBindings(doc, app);
                matRemoved = r2;
                matAdded = a2;
            }
            catch (Exception ex)
            {
                StingLog.Error("CleanMaterialBindings (post-bind) failed", ex);
                errors.Add($"Material cleanup: {ex.Message}");
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
            // NOTE: OST_ElectricalCircuit is intentionally NOT in this shared array — it is
            // added to the ELC_PWR group ONLY, via a dedicated CategorySet in
            // BuildGroupCategoryOverrides. Putting it here would bind circuits to all six MEP
            // groups (HVAC/plumbing/fire/lighting/generic) and the fallback core set.
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

        // Detail / annotation / generic-symbol categories that the ISO + MEP
        // symbol placers drop elements into. STING_PLACER group binds only to
        // these so the idempotency stamps don't pollute every model element.
        private static readonly BuiltInCategory[] PlacerCategories = new[]
        {
            BuiltInCategory.OST_DetailComponents,
            BuiltInCategory.OST_GenericAnnotation,
            BuiltInCategory.OST_GenericModel,
            BuiltInCategory.OST_Lines,
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

        // Definition groups (from MR_PARAMETERS.txt) whose parameters are genuinely
        // UNIVERSAL — identity, tag, cost, IFC, commissioning, as-built, regulatory and
        // project-tracking metadata that belongs on every element type. These bind to the
        // broad core category set and are NEVER narrowed from a (possibly incomplete)
        // per-param CATEGORY_BINDINGS.csv row. Everything NOT in this set is treated as
        // discipline-scoped and driven by the per-param CSV (see ExecuteCore step 5).
        // This is the mechanism that honours "AllCategoryEnums ONLY for the explicitly
        // universal set" — see docs/binding_audit_report.csv.
        internal static readonly HashSet<string> UniversalGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ASS_MNG", "CST_PROC", "PER_SUST", "TPL_TRACKING", "RGL_CMPL",
            "STINGTags_ISO19650", "WARN_THRESHOLDS", "ACC_SYNC", "IFC_EXCH",
            "HEALTH_METRICS", "ASBUILT", "COMMISSIONING", "TBL_TITLEBLOCK",
            "STING_DRAWING", "Identity",
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
                // ELC_PWR gets its OWN CategorySet = base MEP cats + Electrical Circuits, so
                // the cable-sizing params (ELC_WIRE_CSA_MM2_NUM / ELC_WIRE_VD_PCT_NUM) reach
                // circuits Instance-level. BuildCatSet returns a FRESH CategorySet instance,
                // so inserting circuits here cannot leak into the shared mepCats used by the
                // other five groups. TryInsert's AllowsBoundParameters guard safely skips the
                // category on a Revit version that disallows binding to it.
                CategorySet elcPwrCats = BuildCatSet(doc, MepCategories);
                TryInsert(doc, elcPwrCats, BuiltInCategory.OST_ElectricalCircuit);

                overrides["ELC_PWR"] = elcPwrCats;      // base MEP cats + Electrical Circuits
                overrides["HVC_SYSTEMS"] = mepCats;     // base MEP cats only (no circuits)
                overrides["PLM_DRN"] = mepCats;         // base MEP cats only
                overrides["LTG_CONTROLS"] = mepCats;    // base MEP cats only
                overrides["FLS_LIFE_SFTY"] = mepCats;   // base MEP cats only
                overrides["MEP_GENERIC"] = mepCats;     // base MEP cats only
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

            // STING_VIEWPARAMS group (group 36) holds view-scoped params such as
            // STING_VIEW_TOKEN_MASK_TXT (per-view tag-token visibility mask).
            // These must bind to OST_Views, which is NOT in the universal element
            // category set, so without this override the param would never bind
            // and the per-view mask read would silently no-op.
            try
            {
                var viewCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Views);
                if (viewCat != null && viewCat.AllowsBoundParameters)
                {
                    var viewSet = new CategorySet();
                    viewSet.Insert(viewCat);
                    overrides["STING_VIEWPARAMS"] = viewSet;
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildGroupCategoryOverrides STING_VIEWPARAMS: {ex.Message}"); }

            // STING_PLACER group (group 37) holds the symbol-placement idempotency
            // stamps (STING_PLACED_BY_SYMBOL_PLACER_BOOL + STING_PLACER_*). The
            // ISO / MEP symbol placers drop detail components, generic annotations,
            // generic-model symbols, and detail/symbolic curves into views and
            // stamp the placed element so re-runs skip it. Bind to those
            // detail/annotation/generic categories (none are in the universal
            // element set). TryInsert silently skips any category that does not
            // allow bound parameters.
            var placerCats = BuildCatSet(doc, PlacerCategories);
            if (placerCats.Size > 0)
                overrides["STING_PLACER"] = placerCats;

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
                    // Merge with existing PRJ_INFORMATION override if it already exists.
                    if (overrides.TryGetValue("PRJ_INFORMATION", out var existing))
                    {
                        foreach (Category c in existing) prjSet.Insert(c);
                    }
                    overrides["PRJ_INFORMATION"] = prjSet;

                    // WS H3 — Sustainability (group 35 SUS_SUSTAINABILITY) is project-
                    // scoped: SetBaseline stamps the resolved EUI / water / carbon / MJ
                    // intensities + EDGE level onto ProjectInformation, and the dashboard
                    // reads them back. Without this override the SUS_* params bind to the
                    // universal set (not ProjectInformation) and SetBaseline's
                    // LookupParameter returns null — the stamp silently no-ops. Bind the
                    // group to ProjectInformation so it persists to the model / schedules
                    // / IFC (matches CATEGORY_BINDINGS.csv).
                    var susSet = new CategorySet();
                    susSet.Insert(prjCat);
                    overrides["SUS_SUSTAINABILITY"] = susSet;
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

            // Sustainability / Material-Manager params authored under the STING_
            // prefix that nonetheless live on Material elements (embodied carbon,
            // EPD source/date, lifecycle state). These are read via
            // mat.LookupParameter in StingMaterialUpdater / MaterialRow /
            // IfcMaterialPsetWriter, so they must carry OST_Materials binding.
            // (The MAT_COST_* / MAT_VAT_* cost-split params already match the
            //  "MAT_" prefix above.)
            if (paramName.StartsWith("STING_MAT_", StringComparison.Ordinal)) return true;
            if (paramName == "STING_EMB_CARBON_NR") return true;

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

            // Early-out: nothing to scan, skip the two full ParameterBindings iterator passes
            if (speByName.Count == 0)
            {
                StingLog.Info("CleanMaterialBindings: no SharedParameterElements — skipping");
                return (0, 0);
            }

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

            // ── Steps C+D: Remove/add Materials in a single batched transaction ──
            // Previously this was one Transaction per parameter (200+ transactions);
            // now all rebinds happen inside one tx, dropping commit overhead drastically.
            using (Transaction tx = new Transaction(doc, "STING Fix Material Bindings"))
            {
                var failOpts = tx.GetFailureHandlingOptions();
                failOpts.SetFailuresPreprocessor(new BindingWarningSwallower());
                tx.SetFailureHandlingOptions(failOpts);

                tx.Start();
                try
                {
                    // Step C: Remove Materials from non-material params
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

                        Binding currentBinding = doc.ParameterBindings.get_Item(intDef);
                        if (!(currentBinding is InstanceBinding currentIB)) { removeFailed++; continue; }

                        var newCats = app.Create.NewCategorySet();
                        int kept = 0;
                        foreach (Category c in currentIB.Categories)
                        {
                            if (c.Id.Value != matCatIdVal)
                            { newCats.Insert(c); kept++; }
                        }
                        if (kept == 0) continue; // must keep at least one category

                        try
                        {
                            var newBinding = app.Create.NewInstanceBinding(newCats);
                            if (doc.ParameterBindings.ReInsert(intDef, newBinding))
                                removed++;
                            else
                            {
                                removeFailed++;
                                if (removeFailed <= 5)
                                    StingLog.Warn($"ReInsert(InternalDef) failed for '{name}'");
                            }
                        }
                        catch (Exception ex2)
                        {
                            removeFailed++;
                            if (removeFailed <= 5)
                                StingLog.Warn($"Remove mat from '{name}': {ex2.Message}");
                        }
                    }

                    // Step D: Add Materials to material-relevant params
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

                        var newCats = app.Create.NewCategorySet();
                        foreach (Category c in currentIB.Categories)
                            newCats.Insert(c);
                        newCats.Insert(matCat);

                        try
                        {
                            var newBinding = app.Create.NewInstanceBinding(newCats);
                            if (doc.ParameterBindings.ReInsert(intDef, newBinding))
                                added++;
                            else
                            {
                                addFailed++;
                                if (addFailed <= 5)
                                    StingLog.Warn($"ReInsert(InternalDef) add-mat failed for '{name}'");
                            }
                        }
                        catch (Exception ex2)
                        {
                            addFailed++;
                            if (addFailed <= 5)
                                StingLog.Warn($"Add mat to '{name}': {ex2.Message}");
                        }
                    }

                    tx.Commit();
                }
                catch (Exception ex2)
                {
                    StingLog.Error("CleanMaterialBindings batch tx failed", ex2);
                    if (tx.HasStarted() && !tx.HasEnded())
                        tx.RollBack();
                }
            }

            StingLog.Info($"Material cleanup done: removed={removed} (failed={removeFailed}), added={added} (failed={addFailed})");

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
    /// Bindings_PruneToSpec — reconcile the ACTIVE project's parameter bindings to the
    /// per-param category spec in CATEGORY_BINDINGS.csv.
    ///
    /// The plain "Load Shared Params" pass skips parameters that are already bound, so a
    /// model that was bound BEFORE the accuracy fix keeps its old, over-broad category
    /// sets (e.g. LIG_*/ICT_*/HVC_TERMINAL_* still showing on Ducts). This command walks
    /// every bound discipline-scoped param whose current CategorySet differs from its spec
    /// and ReInserts a corrected binding — removing categories that are not in the spec
    /// (over-bindings) and adding any spec categories that are missing (under-bindings).
    ///
    /// Universal params (identity/tag/cost/IFC groups) are guarded and never narrowed.
    /// Idempotent: a param already matching its spec is left untouched, so re-running does
    /// not thrash bindings.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PruneBindingsToSpecCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;
                var app = doc.Application;

                var spec = SharedParamGuids.PerParamCategoryBindings;
                if (spec.Count == 0)
                {
                    TaskDialog.Show("STING — Reconcile Bindings",
                        "CATEGORY_BINDINGS.csv could not be loaded — nothing to reconcile.");
                    return Result.Failed;
                }

                // param → group name (to guard universal groups from narrowing)
                var paramGroup = LoadParamGroupMap();

                // Index bound SharedParameterElements by name (InternalDefinition is the
                // BindingMap key — the only reference ReInsert reliably accepts).
                var speByName = new Dictionary<string, SharedParameterElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var spe in new FilteredElementCollector(doc)
                             .OfClass(typeof(SharedParameterElement)).Cast<SharedParameterElement>())
                {
                    try
                    {
                        var idef = spe.GetDefinition();
                        if (idef != null && !string.IsNullOrEmpty(idef.Name))
                            speByName[idef.Name] = spe;
                    }
                    catch (Exception ex) { StingLog.Warn($"PruneToSpec inspect SPE: {ex.Message}"); }
                }

                // Plan the changes (read-only pass) before opening a transaction.
                var plan = new List<(string name, SharedParameterElement spe, CategorySet target,
                                     List<string> removed, List<string> added)>();
                int universalSkipped = 0, notBound = 0, alreadyOk = 0, unresolved = 0;

                foreach (var kvp in spec)
                {
                    string name = kvp.Key;
                    // guard universal groups — they must stay broad
                    if (paramGroup.TryGetValue(name, out string grp) && LoadSharedParamsCommand.UniversalGroups.Contains(grp))
                    { universalSkipped++; continue; }

                    if (!speByName.TryGetValue(name, out SharedParameterElement spe)) { notBound++; continue; }
                    InternalDefinition idef = spe.GetDefinition();
                    if (idef == null) { notBound++; continue; }
                    if (!(doc.ParameterBindings.get_Item(idef) is InstanceBinding curIB)) { notBound++; continue; }

                    // desired category set (spec ∩ categories that accept bound params in this doc)
                    var target = SharedParamGuids.BuildCategorySet(doc, kvp.Value);
                    if (target.Size == 0) { unresolved++; continue; }

                    // diff current vs target by category id
                    var curIds = new HashSet<long>();
                    var curNames = new Dictionary<long, string>();
                    foreach (Category c in curIB.Categories) { curIds.Add(c.Id.Value); curNames[c.Id.Value] = c.Name; }
                    var tgtIds = new HashSet<long>();
                    var tgtNames = new Dictionary<long, string>();
                    foreach (Category c in target) { tgtIds.Add(c.Id.Value); tgtNames[c.Id.Value] = c.Name; }

                    if (curIds.SetEquals(tgtIds)) { alreadyOk++; continue; } // idempotent no-op

                    var removed = curIds.Except(tgtIds).Select(id => curNames[id]).OrderBy(s => s).ToList();
                    var added = tgtIds.Except(curIds).Select(id => tgtNames[id]).OrderBy(s => s).ToList();
                    plan.Add((name, spe, target, removed, added));
                }

                if (plan.Count == 0)
                {
                    TaskDialog.Show("STING — Reconcile Bindings",
                        $"Nothing to reconcile.\n\n" +
                        $"Already correct: {alreadyOk}\n" +
                        $"Universal (kept broad): {universalSkipped}\n" +
                        $"In spec but not bound here: {notBound}\n" +
                        $"Unresolvable categories: {unresolved}");
                    return Result.Succeeded;
                }

                int overBound = plan.Count(p => p.removed.Count > 0);
                var preview = new StringBuilder();
                preview.AppendLine($"{plan.Count} parameter binding(s) differ from CATEGORY_BINDINGS.csv");
                preview.AppendLine($"  {overBound} are over-bound (extra categories will be removed).");
                preview.AppendLine($"  Universal params kept broad: {universalSkipped}");
                preview.AppendLine();
                preview.AppendLine("Examples:");
                foreach (var p in plan.Take(12))
                    preview.AppendLine($"  {p.name}: -[{string.Join(", ", p.removed)}]" +
                                       (p.added.Count > 0 ? $" +[{string.Join(", ", p.added)}]" : ""));
                if (plan.Count > 12) preview.AppendLine($"  ... and {plan.Count - 12} more (see StingTools.log)");

                var confirm = new TaskDialog("STING — Reconcile Bindings");
                confirm.MainInstruction = $"Narrow {plan.Count} parameter binding(s) to spec?";
                confirm.MainContent = preview.ToString();
                confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
                confirm.DefaultButton = TaskDialogResult.Cancel;
                if (confirm.Show() != TaskDialogResult.Ok) return Result.Cancelled;

                int reinserted = 0, failed = 0;
                using (var tx = new Transaction(doc, "STING Reconcile Bindings"))
                {
                    var fo = tx.GetFailureHandlingOptions();
                    fo.SetFailuresPreprocessor(new BindingWarningSwallower());
                    tx.SetFailureHandlingOptions(fo);
                    tx.Start();
                    foreach (var p in plan)
                    {
                        try
                        {
                            InternalDefinition idef = p.spe.GetDefinition();
                            if (idef == null) { failed++; continue; }
                            var nb = app.Create.NewInstanceBinding(p.target);
                            if (doc.ParameterBindings.ReInsert(idef, nb))
                            {
                                reinserted++;
                                StingLog.Info($"PruneToSpec '{p.name}': removed [{string.Join(",", p.removed)}]" +
                                              (p.added.Count > 0 ? $" added [{string.Join(",", p.added)}]" : ""));
                            }
                            else { failed++; StingLog.Warn($"PruneToSpec ReInsert failed for '{p.name}'"); }
                        }
                        catch (Exception ex) { failed++; StingLog.Warn($"PruneToSpec '{p.name}': {ex.Message}"); }
                    }
                    tx.Commit();
                }

                ParameterHelpers.ClearParamCache();
                TaskDialog.Show("STING — Reconcile Bindings",
                    $"Reconciled {reinserted} binding(s) to spec ({overBound} were over-bound).\n" +
                    $"Failed: {failed}\n" +
                    $"Already correct: {alreadyOk}\n" +
                    $"Universal (kept broad): {universalSkipped}\n\n" +
                    "Not verified in Revit at author time — see StingTools.log for per-param detail.");
                StingLog.Info($"PruneToSpec complete: reinserted={reinserted}, failed={failed}, alreadyOk={alreadyOk}, universalSkipped={universalSkipped}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("PruneBindingsToSpecCommand failed", ex);
                try { TaskDialog.Show("STING", $"Reconcile Bindings failed:\n{ex.Message}"); }
                catch (Exception dex) { StingLog.Warn($"dialog: {dex.Message}"); }
                return Result.Failed;
            }
        }

        /// <summary>Load param → definition-group-name from MR_PARAMETERS.txt.</summary>
        private static Dictionary<string, string> LoadParamGroupMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var groups = new Dictionary<string, string>();
            string f = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
            if (string.IsNullOrEmpty(f)) return map;
            try
            {
                foreach (string l in File.ReadAllLines(f))
                {
                    var p = l.Split('\t');
                    if (p.Length >= 3 && p[0] == "GROUP") groups[p[1]] = p[2];
                    else if (p.Length >= 6 && p[0] == "PARAM" && groups.TryGetValue(p[5], out string gn))
                        map[p[2]] = gn;
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadParamGroupMap: {ex.Message}"); }
            return map;
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
