using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Temp
{
    /// <summary>
    /// Creates comprehensive view filters for all disciplines, systems, tag status,
    /// and QA checking. Includes multi-category filters so a single filter covers
    /// all categories within a discipline (e.g., "Mechanical" covers equipment +
    /// ducts + fittings + accessories + flex + air terminals).
    ///
    /// Filter groups:
    ///   1. Discipline filters (multi-category: M, E, P, A, S, FP, LV)
    ///   2. System filters (HVAC-SUP, HVAC-RET, HWS, DCW, SAN, RWD, etc.)
    ///   3. Tag status filters (Tagged, Untagged, Incomplete)
    ///   4. QA filters (Missing DISC, Missing SEQ, No Room)
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateFiltersCommand : IExternalCommand
    {
        /// <summary>Multi-category discipline filter definitions.</summary>
        private static readonly (string name, BuiltInCategory[] categories)[] DisciplineFilters = new[]
        {
            ("STING - Mechanical", new[] {
                BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_DuctAccessory,
                BuiltInCategory.OST_DuctTerminal, BuiltInCategory.OST_FlexDuctCurves }),
            ("STING - Electrical", new[] {
                BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_ElectricalFixtures,
                BuiltInCategory.OST_LightingFixtures, BuiltInCategory.OST_LightingDevices }),
            ("STING - Plumbing", new[] {
                BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_PipeFitting, BuiltInCategory.OST_PipeAccessory,
                BuiltInCategory.OST_FlexPipeCurves }),
            ("STING - Architectural", new[] {
                BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_Roofs,
                BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_Furniture, BuiltInCategory.OST_FurnitureSystems,
                BuiltInCategory.OST_Casework, BuiltInCategory.OST_Stairs,
                BuiltInCategory.OST_Railings, BuiltInCategory.OST_Ramps }),
            ("STING - Structural", new[] {
                BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_StructuralFoundation, BuiltInCategory.OST_StructuralStiffener,
                BuiltInCategory.OST_Columns }),
            ("STING - Fire Protection", new[] {
                BuiltInCategory.OST_Sprinklers, BuiltInCategory.OST_FireAlarmDevices }),
            ("STING - Low Voltage", new[] {
                BuiltInCategory.OST_CommunicationDevices, BuiltInCategory.OST_DataDevices,
                BuiltInCategory.OST_SecurityDevices, BuiltInCategory.OST_NurseCallDevices,
                BuiltInCategory.OST_TelephoneDevices }),
            ("STING - Conduits & Cable Trays", new[] {
                BuiltInCategory.OST_Conduit, BuiltInCategory.OST_ConduitFitting,
                BuiltInCategory.OST_CableTray, BuiltInCategory.OST_CableTrayFitting }),
            ("STING - Rooms & Spaces", new[] {
                BuiltInCategory.OST_Rooms }),
            ("STING - Generic & Specialty", new[] {
                BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_SpecialityEquipment }),
        };

        /// <summary>
        /// All taggable categories — union of all discipline filters.
        /// Used for parameter-based cross-discipline filters (tag status, QA).
        /// </summary>
        private static readonly BuiltInCategory[] AllTaggableCategories = new[]
        {
            BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_DuctTerminal, BuiltInCategory.OST_FlexDuctCurves,
            BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_LightingFixtures, BuiltInCategory.OST_LightingDevices,
            BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_PipeFitting, BuiltInCategory.OST_PipeAccessory,
            BuiltInCategory.OST_FlexPipeCurves,
            BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_Roofs,
            BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows,
            BuiltInCategory.OST_Furniture, BuiltInCategory.OST_FurnitureSystems,
            BuiltInCategory.OST_Casework, BuiltInCategory.OST_Stairs,
            BuiltInCategory.OST_Railings, BuiltInCategory.OST_Ramps,
            BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_StructuralFoundation, BuiltInCategory.OST_Columns,
            BuiltInCategory.OST_Sprinklers, BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_CommunicationDevices, BuiltInCategory.OST_DataDevices,
            BuiltInCategory.OST_SecurityDevices, BuiltInCategory.OST_NurseCallDevices,
            BuiltInCategory.OST_TelephoneDevices,
            BuiltInCategory.OST_Conduit, BuiltInCategory.OST_ConduitFitting,
            BuiltInCategory.OST_CableTray, BuiltInCategory.OST_CableTrayFitting,
            BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_SpecialityEquipment,
        };

        /// <summary>
        /// Parameter-based filter definitions for tag-status, discipline-code, QA,
        /// and element-status checking. Created only if shared parameters are bound.
        /// Format: (filter name, parameter name, rule type, comparison value).
        /// Rule types: HasValue, HasNoValue, Equals, Contains.
        /// </summary>
        private static readonly (string name, string paramName, string ruleType, string value)[] ParameterFilterDefs = new[]
        {
            // Tag status filters
            ("STING - Tagged Elements", ParamRegistry.TAG1, "HasValue", ""),
            ("STING - Untagged Elements", ParamRegistry.TAG1, "HasNoValue", ""),
            ("STING - Incomplete Tags", ParamRegistry.TAG1, "Contains", "-XX-"),
            // Discipline code filters (parameter-based for precision)
            ("STING - Disc: Mechanical", ParamRegistry.DISC, "Equals", "M"),
            ("STING - Disc: Electrical", ParamRegistry.DISC, "Equals", "E"),
            ("STING - Disc: Plumbing", ParamRegistry.DISC, "Equals", "P"),
            ("STING - Disc: Architectural", ParamRegistry.DISC, "Equals", "A"),
            ("STING - Disc: Structural", ParamRegistry.DISC, "Equals", "S"),
            ("STING - Disc: Fire Protection", ParamRegistry.DISC, "Equals", "FP"),
            ("STING - Disc: Low Voltage", ParamRegistry.DISC, "Equals", "LV"),
            // QA / checking filters
            ("STING - QA: Missing Discipline", ParamRegistry.DISC, "HasNoValue", ""),
            ("STING - QA: Missing Sequence", ParamRegistry.SEQ, "HasNoValue", ""),
            ("STING - QA: Missing Location", ParamRegistry.LOC, "HasNoValue", ""),
            ("STING - QA: Missing System", ParamRegistry.SYS, "HasNoValue", ""),
            // System-level filters (for subsystem isolation in MEP templates)
            ("STING - Sys: HVAC", ParamRegistry.SYS, "Equals", "HVAC"),
            ("STING - Sys: DCW", ParamRegistry.SYS, "Equals", "DCW"),
            ("STING - Sys: HWS", ParamRegistry.SYS, "Equals", "HWS"),
            ("STING - Sys: SAN", ParamRegistry.SYS, "Equals", "SAN"),
            ("STING - Sys: RWD", ParamRegistry.SYS, "Equals", "RWD"),
            ("STING - Sys: GAS", ParamRegistry.SYS, "Equals", "GAS"),
            ("STING - Sys: FP", ParamRegistry.SYS, "Equals", "FP"),
            ("STING - Sys: FLS", ParamRegistry.SYS, "Equals", "FLS"),
            ("STING - Sys: LV", ParamRegistry.SYS, "Equals", "LV"),
            ("STING - Sys: COM", ParamRegistry.SYS, "Equals", "COM"),
            ("STING - Sys: SEC", ParamRegistry.SYS, "Equals", "SEC"),
            ("STING - Sys: ICT", ParamRegistry.SYS, "Equals", "ICT"),
            // Element status filters
            ("STING - Status: Existing", ParamRegistry.STATUS, "Equals", "EXISTING"),
            ("STING - Status: New", ParamRegistry.STATUS, "Equals", "NEW"),
            ("STING - Status: Demolished", ParamRegistry.STATUS, "Equals", "DEMOLISHED"),
            ("STING - Status: Temporary", ParamRegistry.STATUS, "Equals", "TEMPORARY"),
            // Extended QA filters (missing tokens)
            ("STING - QA: Missing Zone", ParamRegistry.ZONE, "HasNoValue", ""),
            ("STING - QA: Missing Function", ParamRegistry.FUNC, "HasNoValue", ""),
            ("STING - QA: Missing Product", ParamRegistry.PROD, "HasNoValue", ""),
            ("STING - QA: No Level", ParamRegistry.LVL, "HasNoValue", ""),
            // System-specific filters (parameter-based for MEP subsystem isolation)
            ("STING - Sys: HVAC", ParamRegistry.SYS, "Equals", "HVAC"),
            ("STING - Sys: DCW", ParamRegistry.SYS, "Equals", "DCW"),
            ("STING - Sys: HWS", ParamRegistry.SYS, "Equals", "HWS"),
            ("STING - Sys: SAN", ParamRegistry.SYS, "Equals", "SAN"),
            ("STING - Sys: RWD", ParamRegistry.SYS, "Equals", "RWD"),
            ("STING - Sys: GAS", ParamRegistry.SYS, "Equals", "GAS"),
            ("STING - Sys: FP", ParamRegistry.SYS, "Equals", "FP"),
            ("STING - Sys: FLS", ParamRegistry.SYS, "Equals", "FLS"),
            ("STING - Sys: COM", ParamRegistry.SYS, "Equals", "COM"),
            ("STING - Sys: ICT", ParamRegistry.SYS, "Equals", "ICT"),
            ("STING - Sys: NCL", ParamRegistry.SYS, "Equals", "NCL"),
            ("STING - Sys: SEC", ParamRegistry.SYS, "Equals", "SEC"),
            ("STING - Sys: LV", ParamRegistry.SYS, "Equals", "LV"),
        };

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var existingNames = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(ParameterFilterElement))
                    .Select(e => e.Name));

            int created = 0;
            int skipped = 0;

            using (Transaction tx = new Transaction(doc, "STING Create Filters"))
            {
                tx.Start();

                // ── Phase 1: Multi-category discipline filters (no parameter rules) ──
                foreach (var (name, categories) in DisciplineFilters)
                {
                    if (existingNames.Contains(name))
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        var catIds = new List<ElementId>();
                        foreach (var bic in categories)
                        {
                            try
                            {
                                Category cat = doc.Settings.Categories.get_Item(bic);
                                if (cat != null) catIds.Add(new ElementId(bic));
                            }
                            catch { /* category not available in this Revit version */ }
                        }
                        if (catIds.Count > 0)
                        {
                            ParameterFilterElement.Create(doc, name, catIds);
                            created++;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Filter create failed '{name}': {ex.Message}");
                        skipped++;
                    }
                }

                // ── Phase 2: Parameter-based filters (tag status, QA, discipline codes) ──
                // Requires shared parameters to be bound; gracefully skip if not available.
                int paramCreated = 0;
                int paramSkipped = 0;

                // Build shared parameter lookup by name
                var spLookup = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
                foreach (SharedParameterElement sp in new FilteredElementCollector(doc)
                    .OfClass(typeof(SharedParameterElement))
                    .Cast<SharedParameterElement>())
                {
                    spLookup[sp.Name] = sp.Id;
                }

                // Build category ID list for parameter-based filters
                var allCatIds = new List<ElementId>();
                foreach (var bic in AllTaggableCategories)
                {
                    try
                    {
                        Category cat = doc.Settings.Categories.get_Item(bic);
                        if (cat != null) allCatIds.Add(new ElementId(bic));
                    }
                    catch { }
                }

                if (allCatIds.Count > 0 && spLookup.Count > 0)
                {
                    foreach (var (name, paramName, ruleType, value) in ParameterFilterDefs)
                    {
                        if (existingNames.Contains(name)) { paramSkipped++; continue; }

                        if (!spLookup.TryGetValue(paramName, out ElementId paramId))
                        {
                            paramSkipped++;
                            continue;
                        }

                        try
                        {
                            FilterRule rule = null;
                            switch (ruleType)
                            {
                                case "HasValue":
                                    rule = ParameterFilterRuleFactory
                                        .CreateHasValueParameterRule(paramId);
                                    break;
                                case "HasNoValue":
                                    rule = ParameterFilterRuleFactory
                                        .CreateHasNoValueParameterRule(paramId);
                                    break;
                                case "Equals":
                                    rule = ParameterFilterRuleFactory
                                        .CreateEqualsRule(paramId, value);
                                    break;
                                case "Contains":
                                    rule = ParameterFilterRuleFactory
                                        .CreateContainsRule(paramId, value);
                                    break;
                            }

                            if (rule != null)
                            {
                                var epf = new ElementParameterFilter(rule);
                                ParameterFilterElement.Create(doc, name, allCatIds, epf);
                                paramCreated++;
                                created++;
                            }
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Parameter filter '{name}': {ex.Message}");
                            paramSkipped++;
                            skipped++;
                        }
                    }
                }
                else if (spLookup.Count == 0)
                {
                    StingLog.Info("No shared parameters bound — skipping parameter-based filters. " +
                        "Run 'Load Parameters' first for tag-status and QA filters.");
                    paramSkipped = ParameterFilterDefs.Length;
                    skipped += paramSkipped;
                }

                // ── Phase 3: CSV-driven filters from MR_SCHEDULES.csv VIEW_FILTER rows ──
                int csvCreated = 0, csvSkipped = 0;
                var csvFilters = TemplateManager.LoadViewFiltersFromCsv();
                foreach (var (csvName, csvDisc, csvCategories, csvFields) in csvFilters)
                {
                    string fullName = $"STING - {csvName}";
                    if (existingNames.Contains(fullName)) { csvSkipped++; continue; }

                    try
                    {
                        // Parse categories from Multi_Categories (semicolon-separated)
                        var csvCatIds = new List<ElementId>();
                        if (csvCategories != "N/A" && !string.IsNullOrEmpty(csvCategories))
                        {
                            foreach (string catPart in csvCategories.Split(';'))
                            {
                                string catName = catPart.Trim();
                                if (TemplateManager.CategoryNameToEnum.TryGetValue(catName,
                                    out BuiltInCategory bic))
                                {
                                    try
                                    {
                                        Category cat = doc.Settings.Categories.get_Item(bic);
                                        if (cat != null) csvCatIds.Add(new ElementId(bic));
                                    }
                                    catch { }
                                }
                            }
                        }
                        // Fall back to all taggable categories if none parsed
                        if (csvCatIds.Count == 0)
                            csvCatIds = allCatIds;

                        if (csvCatIds.Count == 0) { csvSkipped++; continue; }

                        // Parse rule from Fields: "Rule=Equals, Param=ASS_STATUS_TXT, Value=NEW"
                        string ruleType = null, paramName = null, ruleValue = "";
                        foreach (string part in csvFields.Split(','))
                        {
                            string p = part.Trim();
                            if (p.StartsWith("Rule=")) ruleType = p.Substring(5).Trim();
                            else if (p.StartsWith("Param=")) paramName = p.Substring(6).Trim();
                            else if (p.StartsWith("Value=")) ruleValue = p.Substring(6).Trim();
                        }

                        if (!string.IsNullOrEmpty(ruleType) && !string.IsNullOrEmpty(paramName) &&
                            spLookup.TryGetValue(paramName, out ElementId csvParamId))
                        {
                            FilterRule csvRule = null;
                            switch (ruleType)
                            {
                                case "HasValue":
                                    csvRule = ParameterFilterRuleFactory.CreateHasValueParameterRule(csvParamId);
                                    break;
                                case "HasNoValue":
                                    csvRule = ParameterFilterRuleFactory.CreateHasNoValueParameterRule(csvParamId);
                                    break;
                                case "Equals":
                                    csvRule = ParameterFilterRuleFactory.CreateEqualsRule(csvParamId, ruleValue);
                                    break;
                                case "Contains":
                                    csvRule = ParameterFilterRuleFactory.CreateContainsRule(csvParamId, ruleValue);
                                    break;
                            }
                            if (csvRule != null)
                            {
                                var epf = new ElementParameterFilter(csvRule);
                                ParameterFilterElement.Create(doc, fullName, csvCatIds, epf);
                                csvCreated++;
                                created++;
                            }
                        }
                        else
                        {
                            // No parameter rule — create category-only filter
                            ParameterFilterElement.Create(doc, fullName, csvCatIds);
                            csvCreated++;
                            created++;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"CSV filter '{csvName}': {ex.Message}");
                        csvSkipped++;
                        skipped++;
                    }
                }

                tx.Commit();
                string paramNote = spLookup.Count > 0
                    ? $"\nParameter-based: {paramCreated} created, {paramSkipped} skipped."
                    : "\nParameter-based filters skipped (load shared parameters first).";

                string csvNote = csvFilters.Count > 0
                    ? $"\nCSV-driven: {csvCreated} created, {csvSkipped} skipped (from {csvFilters.Count} VIEW_FILTER rows)."
                    : "";

                TaskDialog.Show("Create Filters",
                    $"Created {created} view filters.\nSkipped {skipped} (exist or failed).\n" +
                    $"Discipline: {DisciplineFilters.Length} defined.\n" +
                    $"Parameter-based: {ParameterFilterDefs.Length} defined." +
                    paramNote + csvNote);
            }

            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Creates ISO 19650 worksets following BS 1192 / ISO 19650-2 naming convention:
    ///   {Role}-{Description} e.g., A-ARCH-Walls, M-MECH-Equipment
    /// Includes discipline, shared, and site worksets.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateWorksetsCommand : IExternalCommand
    {
        /// <summary>
        /// ISO 19650 / AEC (UK) BIM Protocol compliant workset names.
        /// Format: {Role}-{Sub}-{Element}
        /// Z- prefix reserved for linked models (per AEC UK convention).
        /// </summary>
        private static readonly string[] WorksetNames = new[]
        {
            // Architecture
            "A-ARCH-Walls", "A-ARCH-Floors", "A-ARCH-Ceilings", "A-ARCH-Roofs",
            "A-ARCH-Doors", "A-ARCH-Windows", "A-ARCH-Furniture", "A-ARCH-Stairs",
            "A-ARCH-Fitout",
            // Structure
            "S-STRC-Columns", "S-STRC-Framing", "S-STRC-Foundation", "S-STRC-Slabs",
            // Mechanical (HVAC)
            "M-MECH-Equipment", "M-MECH-Ducts", "M-MECH-AirTerminals",
            // Plumbing
            "P-PLMB-Equipment", "P-PLMB-HotWater", "P-PLMB-ColdWater",
            "P-PLMB-Drainage", "P-PLMB-Fixtures",
            // Electrical
            "E-ELEC-Equipment", "E-ELEC-Lighting", "E-ELEC-CableTrays",
            "E-ELEC-Conduits", "E-ELEC-LowVoltage",
            // Fire Protection
            "FP-FPRT-Sprinklers", "FP-FPRT-Pipes", "FP-FPRT-Devices",
            // Site
            "A-SITE-Topography", "A-SITE-Parking",
            // Linked models (Z- prefix per AEC UK convention)
            "Z-Linked-Architectural", "Z-Linked-Structural", "Z-Linked-MEP",
            // Shared datum
            "X-SHARED-Grids", "X-SHARED-Levels",
        };

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            if (!doc.IsWorkshared)
            {
                TaskDialog.Show("Create Worksets",
                    "This project is not worksharing-enabled.\n" +
                    "Enable worksharing first via Collaborate tab.");
                return Result.Failed;
            }

            var existing = new HashSet<string>(
                new FilteredWorksetCollector(doc)
                    .OfKind(WorksetKind.UserWorkset)
                    .Select(w => w.Name));

            int created = 0;
            int skipped = 0;

            using (Transaction tx = new Transaction(doc, "STING Create Worksets"))
            {
                tx.Start();

                foreach (string name in WorksetNames)
                {
                    if (existing.Contains(name))
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        Workset.Create(doc, name);
                        created++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Workset create failed '{name}': {ex.Message}");
                        skipped++;
                    }
                }

                tx.Commit();
            }
            TaskDialog.Show("Create Worksets",
                $"Created {created} worksets.\nSkipped {skipped} (exist or failed).\n" +
                $"Total defined: {WorksetNames.Length}");

            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Creates comprehensive view templates for all disciplines plus presentation,
    /// coordination, and RCP views. Each template has appropriate VG overrides
    /// applied via filters — discipline-specific elements highlighted, others halftoned.
    ///
    /// Template categories:
    ///   1. Working Plans: per-discipline with correct VG/filters (M, E, P, A, S, FP, LV)
    ///   2. Coordination: MEP coordination, combined services, clash resolution
    ///   3. RCP (Reflected Ceiling Plans): lighting + ceiling focus
    ///   4. Presentation: clean views for client documents (Classic + Enhanced)
    ///   5. Sections: working + presentation section templates
    ///   6. 3D: coordination + presentation 3D templates
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ViewTemplatesCommand : IExternalCommand
    {
        /// <summary>
        /// Template definition: name, discipline hint for VG configuration,
        /// and optional detail level override.
        /// </summary>
        /// <summary>
        /// Template definitions: name, discipline hint, detail level, required base view type.
        /// Templates are grouped by base view type — each group requires a different
        /// base view to duplicate (plan, section, 3D, elevation, ceiling plan).
        /// </summary>
        private static readonly (string name, string discipline, ViewDetailLevel detailLevel,
            ViewType baseViewType)[] TemplateDefs = new[]
        {
            // Working Plans — per discipline
            ("STING - Mechanical Plan", "M", ViewDetailLevel.Medium, ViewType.FloorPlan),
            ("STING - Electrical Plan", "E", ViewDetailLevel.Medium, ViewType.FloorPlan),
            ("STING - Plumbing Plan", "P", ViewDetailLevel.Medium, ViewType.FloorPlan),
            ("STING - Architectural Plan", "A", ViewDetailLevel.Medium, ViewType.FloorPlan),
            ("STING - Structural Plan", "S", ViewDetailLevel.Medium, ViewType.FloorPlan),
            ("STING - Fire Protection Plan", "FP", ViewDetailLevel.Medium, ViewType.FloorPlan),
            ("STING - Low Voltage Plan", "LV", ViewDetailLevel.Medium, ViewType.FloorPlan),
            // Coordination Plans — multi-discipline
            ("STING - MEP Coordination", "MEP", ViewDetailLevel.Medium, ViewType.FloorPlan),
            ("STING - Combined Services", "ALL", ViewDetailLevel.Fine, ViewType.FloorPlan),
            // Special purpose plans
            ("STING - Demolition Plan", "DEMO", ViewDetailLevel.Medium, ViewType.FloorPlan),
            ("STING - As-Built Plan", "EXIST", ViewDetailLevel.Medium, ViewType.FloorPlan),
            ("STING - Area Plan", "AREA", ViewDetailLevel.Coarse, ViewType.FloorPlan),
            // RCP — ceiling/lighting focus
            ("STING - Lighting RCP", "RCP_LTG", ViewDetailLevel.Medium, ViewType.CeilingPlan),
            ("STING - Ceiling RCP", "RCP_CLG", ViewDetailLevel.Medium, ViewType.CeilingPlan),
            // Presentation — clean client-facing views
            ("STING - Presentation Classic", "PRES_C", ViewDetailLevel.Fine, ViewType.FloorPlan),
            ("STING - Presentation Enhanced", "PRES_E", ViewDetailLevel.Fine, ViewType.FloorPlan),
            // Presentation — per-discipline accent views (matching reference renders)
            ("STING - Presentation Architectural", "PRES_A", ViewDetailLevel.Fine, ViewType.FloorPlan),
            ("STING - Presentation Structural", "PRES_S", ViewDetailLevel.Fine, ViewType.FloorPlan),
            ("STING - Presentation Electrical", "PRES_E_DISC", ViewDetailLevel.Fine, ViewType.FloorPlan),
            ("STING - Presentation Plumbing", "PRES_P", ViewDetailLevel.Fine, ViewType.FloorPlan),
            ("STING - Presentation MEP", "PRES_MEP", ViewDetailLevel.Fine, ViewType.FloorPlan),
            ("STING - Presentation Monochrome", "PRES_MONO", ViewDetailLevel.Fine, ViewType.FloorPlan),
            ("STING - Presentation Dark", "PRES_DARK", ViewDetailLevel.Fine, ViewType.FloorPlan),
            ("STING - Presentation Landscape", "PRES_LAND", ViewDetailLevel.Fine, ViewType.FloorPlan),
            // Section templates
            ("STING - Working Section", "SEC_W", ViewDetailLevel.Medium, ViewType.Section),
            ("STING - Presentation Section", "SEC_P", ViewDetailLevel.Fine, ViewType.Section),
            ("STING - Detail Section", "SEC_D", ViewDetailLevel.Fine, ViewType.Section),
            // 3D templates
            ("STING - Coordination 3D", "MEP_3D", ViewDetailLevel.Medium, ViewType.ThreeD),
            ("STING - Presentation 3D", "PRES_3D", ViewDetailLevel.Fine, ViewType.ThreeD),
            // Per-discipline presentation 3D
            ("STING - 3D Architectural", "3D_A", ViewDetailLevel.Fine, ViewType.ThreeD),
            ("STING - 3D Structural", "3D_S", ViewDetailLevel.Fine, ViewType.ThreeD),
            ("STING - 3D Electrical", "3D_E", ViewDetailLevel.Fine, ViewType.ThreeD),
            ("STING - 3D Plumbing", "3D_P", ViewDetailLevel.Fine, ViewType.ThreeD),
            ("STING - 3D Monochrome", "3D_MONO", ViewDetailLevel.Fine, ViewType.ThreeD),
            ("STING - 3D Dark", "3D_DARK", ViewDetailLevel.Fine, ViewType.ThreeD),
            // Elevation templates
            ("STING - Working Elevation", "ELEV_W", ViewDetailLevel.Medium, ViewType.Elevation),
            ("STING - Presentation Elevation", "ELEV_P", ViewDetailLevel.Fine, ViewType.Elevation),
        };

        /// <summary>
        /// Discipline filter name → which other disciplines should be halftoned.
        /// The discipline's own filter is shown normally; all others are halftoned.
        /// </summary>
        private static readonly Dictionary<string, string[]> DisciplineFilterRules =
            new Dictionary<string, string[]>
        {
            { "M", new[] { "STING - Mechanical" } },
            { "E", new[] { "STING - Electrical", "STING - Conduits & Cable Trays" } },
            { "P", new[] { "STING - Plumbing" } },
            { "A", new[] { "STING - Architectural" } },
            { "S", new[] { "STING - Structural" } },
            { "FP", new[] { "STING - Fire Protection" } },
            { "LV", new[] { "STING - Low Voltage" } },
            { "MEP", new[] { "STING - Mechanical", "STING - Electrical", "STING - Plumbing",
                             "STING - Fire Protection", "STING - Low Voltage",
                             "STING - Conduits & Cable Trays" } },
        };

        /// <summary>
        /// Standard discipline colors for filter overrides (ISO-aligned).
        /// </summary>
        private static readonly Dictionary<string, Color> DisciplineColors =
            new Dictionary<string, Color>
        {
            { "STING - Mechanical", new Color(0, 128, 255) },       // Blue
            { "STING - Electrical", new Color(255, 200, 0) },       // Yellow/Gold
            { "STING - Plumbing", new Color(0, 180, 0) },           // Green
            { "STING - Architectural", new Color(160, 160, 160) },  // Grey
            { "STING - Structural", new Color(200, 0, 0) },         // Red
            { "STING - Fire Protection", new Color(255, 100, 0) },  // Orange
            { "STING - Low Voltage", new Color(160, 0, 200) },      // Purple
            { "STING - Conduits & Cable Trays", new Color(180, 180, 0) }, // Olive
        };

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Find existing view templates
            var existingTemplates = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => v.IsTemplate)
                    .Select(v => v.Name));

            // Build filter lookup for VG configuration
            var filterLookup = new Dictionary<string, ParameterFilterElement>();
            foreach (ParameterFilterElement pfe in new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement))
                .Cast<ParameterFilterElement>())
            {
                filterLookup[pfe.Name] = pfe;
            }

            // Find the solid fill pattern for presentation overrides
            FillPatternElement solidFill = null;
            try
            {
                solidFill = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                    .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
            }
            catch { /* OK — won't apply fill patterns */ }

            // Find base views for each view type to duplicate as template bases.
            // Different view types require different base views.
            var baseViews = new Dictionary<ViewType, View>();

            // Floor plan base
            var basePlan = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan);
            if (basePlan != null) baseViews[ViewType.FloorPlan] = basePlan;

            // Ceiling plan base
            var baseRcp = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.CeilingPlan);
            if (baseRcp != null) baseViews[ViewType.CeilingPlan] = baseRcp;

            // Section base
            var baseSection = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSection)).Cast<ViewSection>()
                .FirstOrDefault(v => !v.IsTemplate &&
                    (v.ViewType == ViewType.Section || v.ViewType == ViewType.Detail));
            if (baseSection != null) baseViews[ViewType.Section] = baseSection;

            // 3D view base
            var base3D = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D)).Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate);
            if (base3D != null) baseViews[ViewType.ThreeD] = base3D;

            // Elevation base
            var baseElev = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSection)).Cast<ViewSection>()
                .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.Elevation);
            if (baseElev != null) baseViews[ViewType.Elevation] = baseElev;

            if (!baseViews.ContainsKey(ViewType.FloorPlan))
            {
                TaskDialog.Show("View Templates",
                    "No floor plan view found to use as a template base.\n" +
                    "Create at least one floor plan view first.");
                return Result.Failed;
            }

            int created = 0;
            int configured = 0;
            int skipped = 0;
            int noBase = 0;
            int csvCreated = 0, csvSkipped = 0;
            var csvTemplates = TemplateManager.LoadViewTemplatesFromCsv();

            using (Transaction tx = new Transaction(doc, "STING Create View Templates"))
            {
                tx.Start();

                foreach (var (name, discipline, detailLevel, baseViewType) in TemplateDefs)
                {
                    if (existingTemplates.Contains(name))
                    {
                        // Still try to configure existing templates with VG overrides
                        View existingTmpl = new FilteredElementCollector(doc)
                            .OfClass(typeof(View))
                            .Cast<View>()
                            .FirstOrDefault(v => v.IsTemplate && v.Name == name);
                        if (existingTmpl != null)
                        {
                            ConfigureTemplateVG(existingTmpl, discipline, filterLookup,
                                solidFill, detailLevel);
                            configured++;
                        }
                        skipped++;
                        continue;
                    }

                    // Find appropriate base view, falling back to floor plan
                    if (!baseViews.TryGetValue(baseViewType, out View baseView))
                    {
                        if (!baseViews.TryGetValue(ViewType.FloorPlan, out baseView))
                        {
                            noBase++;
                            continue;
                        }
                    }

                    try
                    {
                        View template = baseView.CreateViewTemplate();
                        if (template != null)
                        {
                            template.Name = name;
                            ConfigureTemplateVG(template, discipline, filterLookup,
                                solidFill, detailLevel);
                            created++;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"View template create failed '{name}': {ex.Message}");
                        skipped++;
                    }
                }

                // ── Phase 2: CSV-driven templates from MR_SCHEDULES.csv VIEW_TEMPLATE rows ──

                // Refresh existing templates set after Phase 1
                var existingAfterP1 = new HashSet<string>(
                    new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                        .Where(v => v.IsTemplate).Select(v => v.Name));

                foreach (var (csvName, csvDisc, csvVgScheme, csvDetail, csvScale) in csvTemplates)
                {
                    string fullName = $"STING - {csvName}";
                    if (existingAfterP1.Contains(fullName)) { csvSkipped++; continue; }

                    // Determine base view type from template name prefix
                    ViewType csvViewType = ViewType.FloorPlan;
                    if (csvName.Contains("Section") || csvName.Contains("SEC_"))
                        csvViewType = ViewType.Section;
                    else if (csvName.Contains("3D") || csvName.Contains("Isometric"))
                        csvViewType = ViewType.ThreeD;
                    else if (csvName.Contains("Elev") || csvName.Contains("ELEV_"))
                        csvViewType = ViewType.Elevation;
                    else if (csvName.Contains("RCP") || csvName.Contains("Ceiling"))
                        csvViewType = ViewType.CeilingPlan;

                    if (!baseViews.TryGetValue(csvViewType, out View csvBase))
                    {
                        if (!baseViews.TryGetValue(ViewType.FloorPlan, out csvBase))
                        { csvSkipped++; continue; }
                    }

                    // Parse detail level
                    ViewDetailLevel csvDl = ViewDetailLevel.Medium;
                    if (csvDetail.Equals("Fine", StringComparison.OrdinalIgnoreCase))
                        csvDl = ViewDetailLevel.Fine;
                    else if (csvDetail.Equals("Coarse", StringComparison.OrdinalIgnoreCase))
                        csvDl = ViewDetailLevel.Coarse;

                    // Map discipline string to code
                    string discCode = "A";
                    if (csvDisc.Contains("Mech")) discCode = "M";
                    else if (csvDisc.Contains("Elec")) discCode = "E";
                    else if (csvDisc.Contains("Plumb")) discCode = "P";
                    else if (csvDisc.Contains("Struct")) discCode = "S";
                    else if (csvDisc.Contains("Fire")) discCode = "FP";
                    else if (csvDisc.Contains("Coord")) discCode = "MEP";
                    else if (csvDisc.Contains("Pres")) discCode = "PRES_C";
                    else if (csvDisc.Contains("Work")) discCode = "ALL";

                    try
                    {
                        View csvTemplate = csvBase.CreateViewTemplate();
                        if (csvTemplate != null)
                        {
                            csvTemplate.Name = fullName;
                            ConfigureTemplateVG(csvTemplate, discCode, filterLookup,
                                solidFill, csvDl);

                            // Set scale if available
                            if (int.TryParse(csvScale, out int scaleVal) && scaleVal > 0)
                            {
                                try
                                {
                                    Parameter scaleP = csvTemplate.get_Parameter(
                                        BuiltInParameter.VIEW_SCALE_PULLDOWN_METRIC);
                                    if (scaleP != null && !scaleP.IsReadOnly)
                                        scaleP.Set(scaleVal);
                                }
                                catch { }
                            }

                            csvCreated++;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"CSV template '{fullName}': {ex.Message}");
                        csvSkipped++;
                    }
                }

                tx.Commit();
            }
            var baseReport = new StringBuilder();
            foreach (var kvp in baseViews)
                baseReport.Append($"{kvp.Key}, ");

            string csvNote = csvTemplates.Count > 0
                ? $"\n\nCSV-driven: {csvCreated} created, {csvSkipped} skipped " +
                  $"(from {csvTemplates.Count} VIEW_TEMPLATE rows)"
                : "";

            string result = $"Created {created + csvCreated} view templates.\n" +
                $"Configured VG on {configured} existing templates.\n" +
                $"Skipped {skipped + csvSkipped} (already exist).\n" +
                (noBase > 0 ? $"No base view: {noBase}\n" : "") +
                $"\nHardcoded: {TemplateDefs.Length} templates ({created} new).\n" +
                $"  • 7 discipline plans, 2 coordination, 3 special, 2 RCP\n" +
                $"  • 2 presentation, 3 section, 2 3D, 2 elevation" +
                csvNote +
                $"\n\nBase views: {baseReport.ToString().TrimEnd(',', ' ')}";

            TaskDialog.Show("View Templates", result);

            return Result.Succeeded;
        }

        /// <summary>
        /// Configure VG overrides on a view template based on discipline.
        /// For discipline-specific templates:
        ///   - That discipline's filter is shown with colour override
        ///   - All other discipline filters are halftoned/transparent
        /// For coordination templates:
        ///   - All MEP disciplines shown with distinct colours
        /// For presentation templates:
        ///   - Clean appearance with fine detail level
        /// </summary>
        internal static void ConfigureTemplateVG(View template, string discipline,
            Dictionary<string, ParameterFilterElement> filterLookup,
            FillPatternElement solidFill, ViewDetailLevel detailLevel)
        {
            try
            {
                // Set detail level
                template.DetailLevel = detailLevel;

                // Build halftone override (for non-focus disciplines)
                var halftone = new OverrideGraphicSettings();
                halftone.SetHalftone(true);
                halftone.SetSurfaceTransparency(50);

                // Demolition plan: halftone everything, highlight demolished in red
                if (discipline == "DEMO")
                {
                    foreach (var kvp in filterLookup)
                    {
                        if (!kvp.Key.StartsWith("STING - ")) continue;
                        try
                        {
                            template.AddFilter(kvp.Value.Id);
                            template.SetFilterOverrides(kvp.Value.Id, halftone);
                        }
                        catch { }
                    }
                    // If status filter exists, highlight demolished elements in red
                    if (filterLookup.TryGetValue("STING - Status: Demolished", out var demoFilter))
                    {
                        try
                        {
                            template.AddFilter(demoFilter.Id);
                            var redOgs = new OverrideGraphicSettings();
                            redOgs.SetProjectionLineColor(new Color(255, 0, 0));
                            redOgs.SetProjectionLineWeight(4);
                            if (solidFill != null)
                            {
                                redOgs.SetSurfaceForegroundPatternId(solidFill.Id);
                                redOgs.SetSurfaceForegroundPatternColor(new Color(255, 200, 200));
                            }
                            template.SetFilterOverrides(demoFilter.Id, redOgs);
                        }
                        catch { }
                    }
                    return;
                }

                // As-Built plan: halftone new work, highlight existing
                if (discipline == "EXIST")
                {
                    foreach (var kvp in filterLookup)
                    {
                        if (!kvp.Key.StartsWith("STING - ")) continue;
                        try { template.AddFilter(kvp.Value.Id); }
                        catch { }
                    }
                    // Halftone new elements if the status filter exists
                    if (filterLookup.TryGetValue("STING - Status: New", out var newFilter))
                    {
                        try
                        {
                            template.AddFilter(newFilter.Id);
                            template.SetFilterOverrides(newFilter.Id, halftone);
                        }
                        catch { }
                    }
                    return;
                }

                // Area plan: coarse detail, rooms only focus
                if (discipline == "AREA")
                {
                    template.DetailLevel = ViewDetailLevel.Coarse;
                    foreach (var kvp in filterLookup)
                    {
                        if (!kvp.Key.StartsWith("STING - ")) continue;
                        bool isRooms = kvp.Key.Contains("Rooms");
                        try
                        {
                            template.AddFilter(kvp.Value.Id);
                            if (!isRooms)
                                template.SetFilterOverrides(kvp.Value.Id, halftone);
                        }
                        catch { }
                    }
                    return;
                }

                // 3D templates: reuse coordination/presentation logic
                if (discipline == "MEP_3D")
                {
                    // Same as MEP coordination but for 3D context
                    foreach (var kvp in filterLookup)
                    {
                        if (!kvp.Key.StartsWith("STING - ")) continue;
                        try
                        {
                            template.AddFilter(kvp.Value.Id);
                            if (DisciplineColors.TryGetValue(kvp.Key, out Color col3d))
                            {
                                var colorOgs3d = new OverrideGraphicSettings();
                                colorOgs3d.SetProjectionLineColor(col3d);
                                if (solidFill != null)
                                {
                                    colorOgs3d.SetSurfaceForegroundPatternId(solidFill.Id);
                                    colorOgs3d.SetSurfaceForegroundPatternColor(col3d);
                                }
                                template.SetFilterOverrides(kvp.Value.Id, colorOgs3d);
                            }
                            if (kvp.Key.Contains("Architectural") || kvp.Key.Contains("Structural"))
                                template.SetFilterOverrides(kvp.Value.Id, halftone);
                        }
                        catch { }
                    }
                    return;
                }

                if (discipline == "PRES_3D")
                {
                    template.DetailLevel = ViewDetailLevel.Fine;
                    foreach (var kvp in filterLookup)
                    {
                        if (!kvp.Key.StartsWith("STING - ")) continue;
                        try
                        {
                            template.AddFilter(kvp.Value.Id);
                            if (DisciplineColors.TryGetValue(kvp.Key, out Color colP3d))
                            {
                                var cOgs = new OverrideGraphicSettings();
                                cOgs.SetProjectionLineColor(colP3d);
                                cOgs.SetSurfaceTransparency(30);
                                template.SetFilterOverrides(kvp.Value.Id, cOgs);
                            }
                        }
                        catch { }
                    }
                    return;
                }

                // Elevation & Section templates: working = standard, presentation/detail = fine + colours
                if (discipline == "ELEV_W" || discipline == "ELEV_P" ||
                    discipline == "SEC_D" || discipline == "SEC_W" || discipline == "SEC_P")
                {
                    bool isFineDetail = discipline == "ELEV_P" || discipline == "SEC_D" || discipline == "SEC_P";
                    bool isWorking = discipline == "ELEV_W" || discipline == "SEC_W";
                    bool isSection = discipline.StartsWith("SEC");

                    if (isFineDetail)
                        template.DetailLevel = ViewDetailLevel.Fine;

                    foreach (var kvp in filterLookup)
                    {
                        if (!kvp.Key.StartsWith("STING - ")) continue;
                        try
                        {
                            template.AddFilter(kvp.Value.Id);

                            if (!isWorking && DisciplineColors.TryGetValue(kvp.Key, out Color colElev))
                            {
                                var elevOgs = new OverrideGraphicSettings();
                                elevOgs.SetProjectionLineColor(colElev);

                                // Section templates: also set cut-plane graphics
                                if (isSection)
                                {
                                    elevOgs.SetCutLineColor(colElev);
                                    elevOgs.SetCutLineWeight(isFineDetail ? 4 : 3);
                                    if (solidFill != null)
                                    {
                                        // Cut fill: lighter tint of discipline colour
                                        byte tintR = (byte)Math.Min(255, colElev.Red + (255 - colElev.Red) / 2);
                                        byte tintG = (byte)Math.Min(255, colElev.Green + (255 - colElev.Green) / 2);
                                        byte tintB = (byte)Math.Min(255, colElev.Blue + (255 - colElev.Blue) / 2);
                                        elevOgs.SetCutForegroundPatternId(solidFill.Id);
                                        elevOgs.SetCutForegroundPatternColor(new Color(tintR, tintG, tintB));
                                    }
                                }

                                template.SetFilterOverrides(kvp.Value.Id, elevOgs);
                            }
                            else if (isWorking && isSection)
                            {
                                // Working sections: add cut weight boost for readability
                                if (DisciplineColors.TryGetValue(kvp.Key, out Color colSW))
                                {
                                    var secOgs = new OverrideGraphicSettings();
                                    secOgs.SetCutLineWeight(3);
                                    template.SetFilterOverrides(kvp.Value.Id, secOgs);
                                }
                            }
                        }
                        catch { }
                    }
                    return;
                }

                // Presentation templates: set to Fine detail, no filter overrides
                if (discipline.StartsWith("PRES") || discipline.StartsWith("SEC_P"))
                {
                    template.DetailLevel = ViewDetailLevel.Fine;
                    // Presentation: halftone MEP, show architectural
                    foreach (var kvp in filterLookup)
                    {
                        if (!kvp.Key.StartsWith("STING - ")) continue;
                        try
                        {
                            template.AddFilter(kvp.Value.Id);
                            bool isArch = kvp.Key.Contains("Architectural");
                            if (discipline == "PRES_C")
                            {
                                if (isArch)
                                {
                                    // Classic: highlight Architectural with slightly heavier lines
                                    var archOgs = new OverrideGraphicSettings();
                                    archOgs.SetProjectionLineWeight(3);
                                    archOgs.SetProjectionLineColor(new Color(80, 80, 80));
                                    template.SetFilterOverrides(kvp.Value.Id, archOgs);
                                }
                                else
                                {
                                    // Classic: halftone non-architectural
                                    template.SetFilterOverrides(kvp.Value.Id, halftone);
                                }
                            }
                            else if (discipline == "PRES_E")
                            {
                                // Enhanced presentation: show all with discipline colours
                                if (DisciplineColors.TryGetValue(kvp.Key, out Color col))
                                {
                                    var colorOgs = new OverrideGraphicSettings();
                                    colorOgs.SetProjectionLineColor(col);
                                    if (solidFill != null)
                                    {
                                        colorOgs.SetSurfaceForegroundPatternId(solidFill.Id);
                                        colorOgs.SetSurfaceForegroundPatternColor(col);
                                    }
                                    colorOgs.SetSurfaceTransparency(30);
                                    template.SetFilterOverrides(kvp.Value.Id, colorOgs);
                                }
                            }
                        }
                        catch { /* filter might not be compatible */ }
                    }
                    return;
                }

                // ── Per-discipline presentation templates ──
                // Each has a single accent colour with everything else in light grey/white.
                // Ground plane / topography uses the accent colour. Matching reference renders.
                var presentationAccentMap = new Dictionary<string, Color>
                {
                    { "PRES_A", new Color(170, 195, 170) },       // Sage green (ss3-4)
                    { "PRES_S", new Color(180, 50, 50) },         // Deep red (ss7-8)
                    { "PRES_E_DISC", new Color(240, 200, 0) },    // Bright yellow (ss10-11)
                    { "PRES_P", new Color(40, 100, 200) },        // Blue (ss20-22)
                    { "PRES_MEP", new Color(80, 140, 200) },      // Steel blue
                    { "PRES_MONO", new Color(90, 90, 90) },       // Dark grey (ss16)
                    { "PRES_DARK", new Color(255, 255, 255) },    // White on black (ss14-15)
                    { "PRES_LAND", new Color(140, 160, 130) },    // Muted green/brown (ss28)
                    { "3D_A", new Color(170, 195, 170) },         // Sage green
                    { "3D_S", new Color(180, 50, 50) },           // Deep red
                    { "3D_E", new Color(240, 200, 0) },           // Yellow
                    { "3D_P", new Color(40, 100, 200) },          // Blue
                    { "3D_MONO", new Color(90, 90, 90) },         // Dark grey
                    { "3D_DARK", new Color(255, 255, 255) },      // White on black
                };
                if (presentationAccentMap.TryGetValue(discipline, out Color accent))
                {
                    // Accent-tinted presentation: discipline elements in accent colour,
                    // everything else light grey with transparency
                    var accentOgs = new OverrideGraphicSettings();
                    accentOgs.SetProjectionLineColor(accent);
                    accentOgs.SetProjectionLineWeight(2);
                    if (solidFill != null)
                    {
                        accentOgs.SetSurfaceForegroundPatternId(solidFill.Id);
                        accentOgs.SetSurfaceForegroundPatternColor(accent);
                    }
                    accentOgs.SetSurfaceTransparency(15);

                    // Light grey for non-focus elements
                    var greyOgs = new OverrideGraphicSettings();
                    Color lightGrey = discipline == "PRES_DARK" || discipline == "3D_DARK"
                        ? new Color(60, 60, 60) : new Color(200, 200, 200);
                    greyOgs.SetProjectionLineColor(lightGrey);
                    greyOgs.SetProjectionLineWeight(1);
                    if (solidFill != null)
                    {
                        greyOgs.SetSurfaceForegroundPatternId(solidFill.Id);
                        greyOgs.SetSurfaceForegroundPatternColor(
                            discipline == "PRES_DARK" || discipline == "3D_DARK"
                            ? new Color(40, 40, 40) : new Color(240, 240, 240));
                    }
                    greyOgs.SetSurfaceTransparency(
                        discipline == "PRES_DARK" || discipline == "3D_DARK" ? 10 : 40);

                    // Map discipline code → focus filter names
                    string focusDisc = discipline.Replace("PRES_", "").Replace("3D_", "")
                        .Replace("_DISC", "");
                    var presAccentFilters = focusDisc switch
                    {
                        "A" or "LAND" => new[] { "Architectural" },
                        "S" => new[] { "Structural" },
                        "E" => new[] { "Electrical", "Conduits" },
                        "P" => new[] { "Plumbing" },
                        "MEP" => new[] { "Mechanical", "Electrical", "Plumbing", "Fire", "Low Voltage" },
                        _ => Array.Empty<string>(),
                    };
                    var focusSet = new HashSet<string>(presAccentFilters);

                    foreach (var kvp in filterLookup)
                    {
                        if (!kvp.Key.StartsWith("STING - ")) continue;
                        try
                        {
                            template.AddFilter(kvp.Value.Id);
                            bool isFocus = focusSet.Any(f => kvp.Key.Contains(f));

                            if (discipline == "PRES_MONO" || discipline == "3D_MONO")
                            {
                                // Monochrome: all elements in single grey tone
                                template.SetFilterOverrides(kvp.Value.Id, greyOgs);
                            }
                            else if (discipline == "PRES_DARK" || discipline == "3D_DARK")
                            {
                                // Dark mode: white lines on dark background
                                template.SetFilterOverrides(kvp.Value.Id,
                                    isFocus ? accentOgs : greyOgs);
                            }
                            else if (isFocus)
                            {
                                template.SetFilterOverrides(kvp.Value.Id, accentOgs);
                            }
                            else
                            {
                                template.SetFilterOverrides(kvp.Value.Id, greyOgs);
                            }
                        }
                        catch { }
                    }
                    return;
                }

                // RCP templates
                if (discipline.StartsWith("RCP"))
                {
                    // Halftone everything except lighting/ceilings
                    foreach (var kvp in filterLookup)
                    {
                        if (!kvp.Key.StartsWith("STING - ")) continue;
                        try
                        {
                            template.AddFilter(kvp.Value.Id);
                            bool isRelevant = (discipline == "RCP_LTG" && kvp.Key.Contains("Electrical")) ||
                                              (discipline == "RCP_CLG" && kvp.Key.Contains("Architectural"));
                            if (!isRelevant)
                                template.SetFilterOverrides(kvp.Value.Id, halftone);
                        }
                        catch { }
                    }
                    return;
                }

                // Discipline-specific or coordination templates
                bool isCoordination = discipline == "MEP" || discipline == "ALL";
                HashSet<string> focusFilters = null;

                if (DisciplineFilterRules.TryGetValue(discipline, out string[] focusNames))
                    focusFilters = new HashSet<string>(focusNames, StringComparer.OrdinalIgnoreCase);

                foreach (var kvp in filterLookup)
                {
                    if (!kvp.Key.StartsWith("STING - ")) continue;

                    try
                    {
                        template.AddFilter(kvp.Value.Id);

                        if (isCoordination)
                        {
                            // Coordination: show all MEP with discipline colours
                            if (DisciplineColors.TryGetValue(kvp.Key, out Color col))
                            {
                                var colorOgs = new OverrideGraphicSettings();
                                colorOgs.SetProjectionLineColor(col);
                                if (solidFill != null)
                                {
                                    colorOgs.SetSurfaceForegroundPatternId(solidFill.Id);
                                    colorOgs.SetSurfaceForegroundPatternColor(col);
                                }
                                template.SetFilterOverrides(kvp.Value.Id, colorOgs);
                            }

                            // Halftone architectural + structural in MEP coordination
                            if (discipline == "MEP" &&
                                (kvp.Key.Contains("Architectural") || kvp.Key.Contains("Structural")))
                            {
                                template.SetFilterOverrides(kvp.Value.Id, halftone);
                            }
                        }
                        else if (focusFilters != null)
                        {
                            // Discipline-specific: highlight focus, halftone others
                            if (focusFilters.Contains(kvp.Key))
                            {
                                // Focus discipline: colour override
                                if (DisciplineColors.TryGetValue(kvp.Key, out Color col))
                                {
                                    var colorOgs = new OverrideGraphicSettings();
                                    colorOgs.SetProjectionLineColor(col);
                                    colorOgs.SetProjectionLineWeight(3);
                                    template.SetFilterOverrides(kvp.Value.Id, colorOgs);
                                }
                            }
                            else
                            {
                                // Non-focus: halftone
                                template.SetFilterOverrides(kvp.Value.Id, halftone);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"VG filter config failed '{kvp.Key}' on '{template.Name}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ConfigureTemplateVG failed for '{template.Name}': {ex.Message}");
            }
        }
    }
}
