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
            BuiltInCategory.OST_Rooms, BuiltInCategory.OST_StructuralStiffener,
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
            ("STING - Sys: NCL", ParamRegistry.SYS, "Equals", "NCL"),
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
                                if (cat != null) catIds.Add(new ElementId((long)bic));
                            }
                            catch (Exception ex) { StingLog.Warn($"category not available in this Revit version: {ex.Message}"); }
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
                        if (cat != null) allCatIds.Add(new ElementId((long)bic));
                    }
                    catch (Exception ex) { StingLog.Warn($"Get category '{bic}' for filter: {ex.Message}"); }
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
                                        if (cat != null) csvCatIds.Add(new ElementId((long)bic));
                                    }
                                    catch (Exception ex) { StingLog.Warn($"Get CSV filter category '{catName}': {ex.Message}"); }
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
            // Reference-palette 3D presentation templates (duplicate the
            // pink/teal, sage/maroon, monochrome blue, grey-sketch, and
            // black-inverse reference renders — each with a focus category
            // accent: Roof for exterior, Rooms for cut-away interior).
            ("STING - Presentation Candy Exterior",  "PRES_CANDY_EXT", ViewDetailLevel.Fine, ViewType.ThreeD),
            ("STING - Presentation Candy Cutaway",   "PRES_CANDY_INT", ViewDetailLevel.Fine, ViewType.ThreeD),
            ("STING - Presentation Earth Exterior",  "PRES_EARTH_EXT", ViewDetailLevel.Fine, ViewType.ThreeD),
            ("STING - Presentation Earth Cutaway",   "PRES_EARTH_INT", ViewDetailLevel.Fine, ViewType.ThreeD),
            ("STING - Presentation Blue Exterior",   "PRES_BLUE_EXT",  ViewDetailLevel.Fine, ViewType.ThreeD),
            ("STING - Presentation Blue Cutaway",    "PRES_BLUE_INT",  ViewDetailLevel.Fine, ViewType.ThreeD),
            ("STING - Presentation Sketch",          "PRES_SKETCH",    ViewDetailLevel.Fine, ViewType.ThreeD),
            ("STING - Presentation Black",           "PRES_BLACK",     ViewDetailLevel.Fine, ViewType.ThreeD),
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
            { "STING - Rooms & Spaces", new Color(100, 200, 255) }, // Light Blue
            { "STING - Generic & Specialty", new Color(128, 128, 128) }, // Mid Grey
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
            catch (Exception ex) { StingLog.Warn($"OK — won't apply fill patterns: {ex.Message}"); }

            // Find base views for each view type to duplicate as template bases.
            // Different view types require different base views.
            // Single collector pass for all view types to avoid repeated scans.
            var baseViews = new Dictionary<ViewType, View>();

            foreach (View v in new FilteredElementCollector(doc)
                .OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate))
            {
                switch (v.ViewType)
                {
                    case ViewType.FloorPlan:
                        if (!baseViews.ContainsKey(ViewType.FloorPlan))
                            baseViews[ViewType.FloorPlan] = v;
                        break;
                    case ViewType.CeilingPlan:
                        if (!baseViews.ContainsKey(ViewType.CeilingPlan))
                            baseViews[ViewType.CeilingPlan] = v;
                        break;
                    case ViewType.Section:
                    case ViewType.Detail:
                        if (!baseViews.ContainsKey(ViewType.Section))
                            baseViews[ViewType.Section] = v;
                        break;
                    case ViewType.ThreeD:
                        if (!baseViews.ContainsKey(ViewType.ThreeD))
                            baseViews[ViewType.ThreeD] = v;
                        break;
                    case ViewType.Elevation:
                        if (!baseViews.ContainsKey(ViewType.Elevation))
                            baseViews[ViewType.Elevation] = v;
                        break;
                }
            }

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
                        StingLog.Warn($"[TemplateCommands] No '{baseViewType}' base view found — falling back to FloorPlan for template '{name}'. Review template manually.");
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
                    catch (Exception ex2)
                    {
                        StingLog.Warn($"View template create failed '{name}': {ex2.Message}");
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

                    // Map CSV discipline descriptor to internal discipline code.
                    // Check CSV name first for presentation/3D/section-specific
                    // variants so a row like "Presentation Monochrome" routes to
                    // PRES_MONO rather than the generic PRES_C.
                    string discCode = "A";
                    string combined = (csvName + " " + csvDisc);
                    if      (combined.Contains("Monochrome") || combined.Contains("Mono"))
                        discCode = combined.Contains("3D") ? "3D_MONO" : "PRES_MONO";
                    else if (combined.Contains("Dark"))
                        discCode = combined.Contains("3D") ? "3D_DARK" : "PRES_DARK";
                    else if (combined.Contains("Landscape"))
                        discCode = "PRES_LAND";
                    else if (combined.Contains("Presentation 3D") || combined.Contains("Pres 3D"))
                        discCode = "PRES_3D";
                    else if (combined.Contains("3D Arch"))                    discCode = "3D_A";
                    else if (combined.Contains("3D Struct"))                  discCode = "3D_S";
                    else if (combined.Contains("3D Elec"))                    discCode = "3D_E";
                    else if (combined.Contains("3D Plumb"))                   discCode = "3D_P";
                    else if (combined.Contains("MEP 3D") || combined.Contains("Coord 3D"))
                        discCode = "MEP_3D";
                    else if (combined.Contains("Presentation Arch") || combined.Contains("Pres Arch"))
                        discCode = "PRES_A";
                    else if (combined.Contains("Presentation Struct") || combined.Contains("Pres Struct"))
                        discCode = "PRES_S";
                    else if (combined.Contains("Presentation Elec") || combined.Contains("Pres Elec"))
                        discCode = "PRES_E_DISC";
                    else if (combined.Contains("Presentation Plumb") || combined.Contains("Pres Plumb"))
                        discCode = "PRES_P";
                    else if (combined.Contains("Presentation MEP") || combined.Contains("Pres MEP"))
                        discCode = "PRES_MEP";
                    else if (combined.Contains("Presentation Enhanced") || combined.Contains("Pres Enh"))
                        discCode = "PRES_E";
                    else if (combined.Contains("Presentation") || combined.Contains("Pres"))
                        discCode = "PRES_C";
                    else if (combined.Contains("Demolition") || combined.Contains("Demo"))
                        discCode = "DEMO";
                    else if (combined.Contains("As-Built") || combined.Contains("Existing"))
                        discCode = "EXIST";
                    else if (combined.Contains("Area"))                       discCode = "AREA";
                    else if (combined.Contains("Lighting RCP"))               discCode = "RCP_LTG";
                    else if (combined.Contains("Ceiling") && combined.Contains("RCP"))
                        discCode = "RCP_CLG";
                    else if (combined.Contains("Detail Section"))             discCode = "SEC_D";
                    else if (combined.Contains("Presentation Section") || combined.Contains("Pres Section"))
                        discCode = "SEC_P";
                    else if (combined.Contains("Section"))                    discCode = "SEC_W";
                    else if (combined.Contains("Presentation Elev") || combined.Contains("Pres Elev"))
                        discCode = "ELEV_P";
                    else if (combined.Contains("Elev"))                       discCode = "ELEV_W";
                    else if (csvDisc.Contains("Mech"))                        discCode = "M";
                    else if (csvDisc.Contains("Elec"))                        discCode = "E";
                    else if (csvDisc.Contains("Plumb"))                       discCode = "P";
                    else if (csvDisc.Contains("Struct"))                      discCode = "S";
                    else if (csvDisc.Contains("Fire"))                        discCode = "FP";
                    else if (csvDisc.Contains("Low Volt") || csvDisc.Contains("LV"))
                        discCode = "LV";
                    else if (csvDisc.Contains("Coord"))                       discCode = "MEP";
                    else if (csvDisc.Contains("Work"))                        discCode = "ALL";

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
                                catch (Exception ex2) { StingLog.Warn($"Set view template scale: {ex2.Message}"); }
                            }

                            csvCreated++;
                        }
                    }
                    catch (Exception ex2)
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
        /// Configure VG overrides on a view template.
        /// Each discipline code produces a UNIQUE combination of:
        ///   • discipline filter accent (color, projection + cut weight, surface fill)
        ///   • halftone / hidden treatment of non-focus disciplines
        ///   • status overlays (Existing, Demolished, Temporary)
        ///   • category line-weight hierarchy
        ///   • cut-plane graphics differentiated from projected geometry
        ///   • presentation-mode polish (DisplayStyle, monochrome line-only,
        ///     dark mode accent, landscape category hides)
        ///
        /// All overrides flow through PresentationStyleHelper so style assets
        /// (line patterns, fill patterns) are referenced consistently.
        /// </summary>
        internal static void ConfigureTemplateVG(View template, string discipline,
            Dictionary<string, ParameterFilterElement> filterLookup,
            FillPatternElement solidFill, ViewDetailLevel detailLevel)
        {
            try
            {
                template.DetailLevel = detailLevel;
                Document doc = template.Document;
                if (solidFill == null) solidFill = PresentationStyleHelper.GetSolidFill(doc);

                // ── Template-wide defaults ──
                // Hide analytical model categories on non-analysis templates,
                // set Revit view discipline so the Project Browser organises
                // templates correctly, hide DWG/import on presentation templates,
                // and control crop-box visibility (hidden on presentation).
                // All PRES_* and 3D_* codes are presentation. Sections/elevations
                // in "P" variant also get presentation treatment.
                bool isPresentation = discipline.StartsWith("PRES_") || discipline.StartsWith("3D_")
                                      || discipline == "PRES_3D" || discipline == "SEC_P"
                                      || discipline == "ELEV_P";
                bool isWorking3D    = discipline == "MEP_3D";
                PresentationStyleHelper.ApplyTemplateDefaults(template,
                    discipline: PresentationStyleHelper.MapViewDiscipline(discipline),
                    hideAnalytical: true,
                    hideImports: isPresentation,
                    cropBoxVisible: isPresentation ? false : (bool?)null,
                    cropBoxActive: isPresentation ? true  : (bool?)null);

                // Hide categories that don't belong on this template type.
                // Runs AFTER detail level is set so visibility-control checks
                // reflect the active view settings.
                try
                {
                    PresentationStyleHelper.HideCategories(template,
                        PresentationStyleHelper.GetHideCategoriesForTemplate(discipline));
                }
                catch (Exception ex) { StingLog.Warn($"HideCategories on '{template.Name}': {ex.Message}"); }

                // Local helpers
                OverrideGraphicSettings Halftone(int t = 50) => PresentationStyleHelper.Halftone(t);

                bool TryDisc(string key, out Color col) => DisciplineColors.TryGetValue(key, out col);

                // Build a quick filter classifier so we never rely on string.Contains alone
                bool IsArch(string n) => n.IndexOf("Architectural", StringComparison.OrdinalIgnoreCase) >= 0;
                bool IsStruct(string n) => n.IndexOf("Structural", StringComparison.OrdinalIgnoreCase) >= 0;
                bool IsRooms(string n) => n.IndexOf("Rooms", StringComparison.OrdinalIgnoreCase) >= 0;
                bool IsConduit(string n) => n.IndexOf("Conduit", StringComparison.OrdinalIgnoreCase) >= 0;
                bool IsLV(string n) => n.IndexOf("Low Voltage", StringComparison.OrdinalIgnoreCase) >= 0;
                bool IsFire(string n) => n.IndexOf("Fire Protection", StringComparison.OrdinalIgnoreCase) >= 0;
                bool IsMech(string n) => n.IndexOf("Mechanical", StringComparison.OrdinalIgnoreCase) >= 0
                                          && !IsConduit(n);
                bool IsElec(string n) => n.IndexOf("Electrical", StringComparison.OrdinalIgnoreCase) >= 0
                                          && !IsLV(n);
                bool IsPlumb(string n) => n.IndexOf("Plumbing", StringComparison.OrdinalIgnoreCase) >= 0;
                bool IsAnyMEP(string n) => IsMech(n) || IsElec(n) || IsPlumb(n) || IsFire(n) || IsLV(n) || IsConduit(n);
                bool IsDisciplineFilter(string n) => DisciplineColors.ContainsKey(n);

                // Map a discipline code (M/E/P/A/S/FP/LV) to the canonical
                // multi-category filter name used in DisciplineColors. Returns
                // null for coordination/special codes.
                string MapDiscToFilterName(string code) => code switch
                {
                    "M"  => "STING - Mechanical",
                    "E"  => "STING - Electrical",
                    "P"  => "STING - Plumbing",
                    "A"  => "STING - Architectural",
                    "S"  => "STING - Structural",
                    "FP" => "STING - Fire Protection",
                    "LV" => "STING - Low Voltage",
                    _    => null,
                };

                // Iterate only the discipline filters (the colored ones).
                IEnumerable<KeyValuePair<string, ParameterFilterElement>> DisciplineFilters() =>
                    filterLookup.Where(kv => IsDisciplineFilter(kv.Key));

                // ─────────────────────────────────────────────────────────────
                // DEMOLITION PLAN — everything halftoned, demolished elements
                // tinted bright red with cross-hatch cut fill.
                // ─────────────────────────────────────────────────────────────
                if (discipline == "DEMO")
                {
                    foreach (var kv in DisciplineFilters())
                        PresentationStyleHelper.AddOrSet(template, kv.Value, Halftone(60));
                    PresentationStyleHelper.ApplyStatusOverlays(template, filterLookup, doc, solidFill);
                    return;
                }

                // ─────────────────────────────────────────────────────────────
                // AS-BUILT PLAN — existing dashed grey, new halftoned, demolished hidden.
                // ─────────────────────────────────────────────────────────────
                if (discipline == "EXIST")
                {
                    foreach (var kv in DisciplineFilters())
                        PresentationStyleHelper.AddOrSet(template, kv.Value, null);
                    if (filterLookup.TryGetValue("STING - Status: New", out var fNe))
                        PresentationStyleHelper.AddOrSet(template, fNe, Halftone(70));
                    if (filterLookup.TryGetValue("STING - Status: Existing", out var fEx))
                        PresentationStyleHelper.AddOrSet(template, fEx,
                            PresentationStyleHelper.StatusExisting(doc, solidFill));
                    if (filterLookup.TryGetValue("STING - Status: Demolished", out var fDe))
                        PresentationStyleHelper.HideByFilter(template, fDe);
                    return;
                }

                // ─────────────────────────────────────────────────────────────
                // AREA PLAN — coarse, rooms accented, everything else hidden.
                // ─────────────────────────────────────────────────────────────
                if (discipline == "AREA")
                {
                    template.DetailLevel = ViewDetailLevel.Coarse;
                    foreach (var kv in DisciplineFilters())
                    {
                        if (IsRooms(kv.Key))
                        {
                            var roomOgs = PresentationStyleHelper.DisciplineAccent(
                                new Color(120, 180, 220), 3, solidFill, surfaceTransparency: 60, includeFill: true);
                            PresentationStyleHelper.AddOrSet(template, kv.Value, roomOgs);
                        }
                        else
                        {
                            PresentationStyleHelper.AddOrSet(template, kv.Value, Halftone(80));
                        }
                    }
                    return;
                }

                // ─────────────────────────────────────────────────────────────
                // RCP — Lighting reflected ceiling: target Lighting Fixtures
                // specifically, halftone everything else. Ceiling RCP: ceilings
                // accented, all other categories halftoned/hidden.
                // ─────────────────────────────────────────────────────────────
                if (discipline == "RCP_LTG")
                {
                    foreach (var kv in DisciplineFilters())
                    {
                        // Architectural needed (ceilings) — halftone but show
                        if (IsArch(kv.Key))
                            PresentationStyleHelper.AddOrSet(template, kv.Value, Halftone(40));
                        else if (IsElec(kv.Key))
                            // Electrical filter contains Lighting Fixtures — accent in bright yellow
                            PresentationStyleHelper.AddOrSet(template, kv.Value,
                                PresentationStyleHelper.DisciplineAccent(
                                    new Color(255, 200, 0), 3, solidFill, 0, true));
                        else
                            PresentationStyleHelper.AddOrSet(template, kv.Value, Halftone(80));
                    }
                    // Category hides (Furniture, Floors, PlumbingFixtures, StructuralFoundation)
                    // already applied by ApplyTemplateDefaults → GetHideCategoriesForTemplate.
                    return;
                }
                if (discipline == "RCP_CLG")
                {
                    foreach (var kv in DisciplineFilters())
                    {
                        if (IsArch(kv.Key))
                            PresentationStyleHelper.AddOrSet(template, kv.Value,
                                PresentationStyleHelper.DisciplineAccentWithCut(
                                    DisciplineColors[kv.Key], 2, 4, solidFill, 0, false));
                        else
                            PresentationStyleHelper.AddOrSet(template, kv.Value, Halftone(75));
                    }
                    // Strengthen the Ceiling category line weight specifically
                    PresentationStyleHelper.SetCategoryWeights(template, BuiltInCategory.OST_Ceilings, 4, 5);
                    // Furniture, Floors, PlumbingFixtures already hidden by matrix
                    return;
                }

                // ─────────────────────────────────────────────────────────────
                // 3D MEP COORDINATION — all MEP coloured, arch + struct halftoned.
                // Display style: ShadingWithEdges so disciplines pop visually.
                // ─────────────────────────────────────────────────────────────
                if (discipline == "MEP_3D")
                {
                    if (template is View3D)
                        PresentationStyleHelper.ApplyDisplayStyle(template, DisplayStyle.ShadingWithEdges);
                    foreach (var kv in DisciplineFilters())
                    {
                        if (IsArch(kv.Key) || IsStruct(kv.Key))
                            PresentationStyleHelper.AddOrSet(template, kv.Value, Halftone(60));
                        else if (TryDisc(kv.Key, out var c))
                            PresentationStyleHelper.AddOrSet(template, kv.Value,
                                PresentationStyleHelper.DisciplineAccent(c, 2, solidFill, 25, true));
                    }
                    // Coordination 3D needs toggleable subsystem isolators + QA on top
                    if (PresentationStyleHelper.DisciplineSystemFilters.TryGetValue("MEP", out var coordSys))
                        PresentationStyleHelper.AttachSystemFilters(template, filterLookup, coordSys);
                    PresentationStyleHelper.ApplyQAOverlays(template, filterLookup, solidFill);
                    return;
                }

                // ─────────────────────────────────────────────────────────────
                // 3D PRESENTATION — every discipline coloured + transparency,
                // shading-with-edges so silhouettes read on rendered output.
                // ─────────────────────────────────────────────────────────────
                if (discipline == "PRES_3D")
                {
                    template.DetailLevel = ViewDetailLevel.Fine;
                    if (template is View3D)
                        PresentationStyleHelper.ApplyDisplayStyle(template, DisplayStyle.Realistic);
                    foreach (var kv in DisciplineFilters())
                    {
                        if (TryDisc(kv.Key, out var c))
                            PresentationStyleHelper.AddOrSet(template, kv.Value,
                                PresentationStyleHelper.DisciplineAccent(c, 2, solidFill, 30, true));
                    }
                    return;
                }

                // ─────────────────────────────────────────────────────────────
                // REFERENCE-PALETTE 3D PRESENTATION TEMPLATES
                // Each of these duplicates a specific architectural render style
                // from the reference screenshots: Candy (pink/teal gradient),
                // Earth (sage/maroon), Blue (monochrome blue line-art), Sketch
                // (grey gradient), Black (inverse dark mode). Each has exterior
                // (roof-accented) and cutaway (room-accented) variants.
                //
                // The palette helper sets:
                //   • 3D view background (solid or gradient via reflection —
                //     silent skip on pre-Revit-2022 APIs)
                //   • DisplayStyle.HLR for line-art look
                //   • Base line-colour on every model category
                //   • Accent colour+fill on the focus category (Roofs or Rooms)
                //   • Topography base-slab colour (the tinted block in the
                //     reference renders)
                // ─────────────────────────────────────────────────────────────
                var paletteLookup = PresentationStyleHelper.PaletteFor(discipline);
                if (paletteLookup.HasValue)
                {
                    template.DetailLevel = ViewDetailLevel.Fine;
                    var (palette, paletteAccent) = paletteLookup.Value;
                    PresentationStyleHelper.ApplyPalette(template, palette, paletteAccent, solidFill);

                    // Discipline filters still apply — they just use the palette's
                    // base line colour so everything reads monochromatic with the
                    // accent category standing out.
                    foreach (var kv in DisciplineFilters())
                    {
                        var ogs = new OverrideGraphicSettings();
                        ogs.SetProjectionLineColor(palette.BaseLineColor);
                        ogs.SetProjectionLineWeight(2);
                        ogs.SetCutLineColor(palette.LineColor);
                        ogs.SetCutLineWeight(3);
                        PresentationStyleHelper.AddOrSet(template, kv.Value, ogs);
                    }
                    // Cutaway variants: nudge camera/scope expectation by leaving
                    // crop inactive — user sets the section box manually per view.
                    return;
                }

                // ─────────────────────────────────────────────────────────────
                // ELEVATIONS & SECTIONS — working/presentation/detail variants.
                // Each variant has differentiated cut weight + cut fill so plans,
                // presentation sections, and detail callouts read distinctly.
                // ─────────────────────────────────────────────────────────────
                if (discipline == "ELEV_W" || discipline == "ELEV_P" ||
                    discipline == "SEC_D"  || discipline == "SEC_W"  || discipline == "SEC_P")
                {
                    bool isFineDetail = discipline == "ELEV_P" || discipline == "SEC_D" || discipline == "SEC_P";
                    bool isWorking    = discipline == "ELEV_W" || discipline == "SEC_W";
                    bool isSection    = discipline.StartsWith("SEC");
                    bool isDetail     = discipline == "SEC_D";
                    bool isPres       = discipline == "ELEV_P" || discipline == "SEC_P";

                    if (isFineDetail) template.DetailLevel = ViewDetailLevel.Fine;

                    // Cut weight scales with view purpose: working=3, presentation=4, detail=5
                    int cutWeight = isDetail ? 5 : isPres ? 4 : 3;
                    int projWeight = isPres ? 2 : isWorking ? 1 : 2;

                    foreach (var kv in DisciplineFilters())
                    {
                        if (!TryDisc(kv.Key, out var c)) continue;
                        OverrideGraphicSettings ogs;
                        if (isWorking)
                        {
                            // Working: muted lines, strong cut for legibility
                            ogs = PresentationStyleHelper.DisciplineAccentWithCut(
                                c, projWeight, cutWeight, solidFill, 0, false);
                        }
                        else if (isPres)
                        {
                            // Presentation: full colour, tinted cut fill
                            ogs = PresentationStyleHelper.DisciplineAccentWithCut(
                                c, projWeight, cutWeight, solidFill, 10, true);
                        }
                        else // detail
                        {
                            // Detail: heavy black cut line, color projection
                            ogs = new OverrideGraphicSettings();
                            ogs.SetProjectionLineColor(c);
                            ogs.SetProjectionLineWeight(projWeight);
                            ogs.SetCutLineColor(PresentationStyleHelper.BLACK);
                            ogs.SetCutLineWeight(cutWeight);
                            if (solidFill != null)
                            {
                                ogs.SetCutForegroundPatternId(solidFill.Id);
                                ogs.SetCutForegroundPatternColor(PresentationStyleHelper.Lighten(c, 0.4));
                            }
                        }
                        PresentationStyleHelper.AddOrSet(template, kv.Value, ogs);
                    }
                    if (!isWorking)
                        PresentationStyleHelper.ApplyStatusOverlays(template, filterLookup, doc, solidFill);
                    return;
                }

                // ─────────────────────────────────────────────────────────────
                // PRESENTATION CLASSIC (PRES_C) — Architectural-led print plan.
                // Heavy arch lines + light material fill on cut walls/floors,
                // MEP/structural halftoned to the background.
                // ─────────────────────────────────────────────────────────────
                if (discipline == "PRES_C")
                {
                    template.DetailLevel = ViewDetailLevel.Fine;
                    foreach (var kv in DisciplineFilters())
                    {
                        if (IsArch(kv.Key))
                        {
                            var ogs = new OverrideGraphicSettings();
                            ogs.SetProjectionLineColor(PresentationStyleHelper.DIM_GREY);
                            ogs.SetProjectionLineWeight(4);
                            ogs.SetCutLineColor(PresentationStyleHelper.BLACK);
                            ogs.SetCutLineWeight(5);
                            // Light parchment cut fill so wall sections read as solid
                            if (solidFill != null)
                            {
                                ogs.SetCutForegroundPatternId(solidFill.Id);
                                ogs.SetCutForegroundPatternColor(new Color(225, 220, 205));
                            }
                            PresentationStyleHelper.AddOrSet(template, kv.Value, ogs);
                        }
                        else if (IsStruct(kv.Key))
                        {
                            var ogs = PresentationStyleHelper.DisciplineAccentWithCut(
                                PresentationStyleHelper.MID_GREY, 2, 3, solidFill, 0, false);
                            PresentationStyleHelper.AddOrSet(template, kv.Value, ogs);
                        }
                        else if (IsRooms(kv.Key))
                        {
                            // Rooms: no fill, just keep visible
                            PresentationStyleHelper.AddOrSet(template, kv.Value, null);
                        }
                        else
                        {
                            // MEP/LV/Fire/Conduit — muted halftone
                            PresentationStyleHelper.AddOrSet(template, kv.Value, Halftone(70));
                        }
                    }
                    PresentationStyleHelper.ApplyStatusOverlays(template, filterLookup, doc, solidFill);
                    return;
                }

                // ─────────────────────────────────────────────────────────────
                // PRESENTATION ENHANCED (PRES_E) — full colour coordination plan
                // for client review. Every discipline shown in its colour with
                // line-weight hierarchy (Arch heaviest, MEP lighter).
                // ─────────────────────────────────────────────────────────────
                if (discipline == "PRES_E")
                {
                    template.DetailLevel = ViewDetailLevel.Fine;
                    foreach (var kv in DisciplineFilters())
                    {
                        if (!TryDisc(kv.Key, out var c)) continue;
                        int projW = IsArch(kv.Key) ? 3 : IsStruct(kv.Key) ? 3 : 2;
                        int cutW  = IsArch(kv.Key) ? 5 : IsStruct(kv.Key) ? 4 : 3;
                        var ogs = PresentationStyleHelper.DisciplineAccentWithCut(
                            c, projW, cutW, solidFill, 25, true);
                        PresentationStyleHelper.AddOrSet(template, kv.Value, ogs);
                    }
                    PresentationStyleHelper.ApplyStatusOverlays(template, filterLookup, doc, solidFill);
                    return;
                }

                // ─────────────────────────────────────────────────────────────
                // PER-DISCIPLINE PRESENTATION ACCENT (PRES_A/S/E_DISC/P/MEP)
                // and 3D variants. Focus discipline gets accent colour, all
                // others get a single muted greyscale.
                // ─────────────────────────────────────────────────────────────
                var presentationAccentMap = new Dictionary<string, Color>
                {
                    { "PRES_A",      new Color(170, 195, 170) },
                    { "PRES_S",      new Color(180,  50,  50) },
                    { "PRES_E_DISC", new Color(240, 200,   0) },
                    { "PRES_P",      new Color( 40, 100, 200) },
                    { "PRES_MEP",    new Color( 80, 140, 200) },
                    { "PRES_MONO",   new Color( 60,  60,  60) },
                    { "PRES_DARK",   new Color(255, 255, 255) },
                    { "PRES_LAND",   new Color(140, 160, 130) },
                    { "3D_A",        new Color(170, 195, 170) },
                    { "3D_S",        new Color(180,  50,  50) },
                    { "3D_E",        new Color(240, 200,   0) },
                    { "3D_P",        new Color( 40, 100, 200) },
                    { "3D_MONO",     new Color( 60,  60,  60) },
                    { "3D_DARK",     new Color(255, 255, 255) },
                };

                if (presentationAccentMap.TryGetValue(discipline, out Color accent))
                {
                    bool isMono   = discipline == "PRES_MONO" || discipline == "3D_MONO";
                    bool isDark   = discipline == "PRES_DARK" || discipline == "3D_DARK";
                    bool isLand   = discipline == "PRES_LAND";
                    bool is3D     = discipline.StartsWith("3D_");

                    if (is3D && template is View3D)
                    {
                        if (isMono)      PresentationStyleHelper.ApplyDisplayStyle(template, DisplayStyle.HLR);
                        else if (isDark) PresentationStyleHelper.ApplyDisplayStyle(template, DisplayStyle.ShadingWithEdges);
                        else             PresentationStyleHelper.ApplyDisplayStyle(template, DisplayStyle.ShadingWithEdges);
                    }

                    // ── Build the focus filter set ──
                    // Map every accent template to a SPECIFIC set of focus filter
                    // names so each template is unique and well-targeted.
                    var focusSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    switch (discipline)
                    {
                        case "PRES_A":
                        case "3D_A":
                            focusSet.Add("STING - Architectural");
                            focusSet.Add("STING - Rooms & Spaces");
                            break;
                        case "PRES_S":
                        case "3D_S":
                            focusSet.Add("STING - Structural");
                            break;
                        case "PRES_E_DISC":
                        case "3D_E":
                            focusSet.Add("STING - Electrical");
                            focusSet.Add("STING - Conduits & Cable Trays");
                            focusSet.Add("STING - Low Voltage");
                            break;
                        case "PRES_P":
                        case "3D_P":
                            focusSet.Add("STING - Plumbing");
                            focusSet.Add("STING - Fire Protection");
                            break;
                        case "PRES_MEP":
                            focusSet.Add("STING - Mechanical");
                            focusSet.Add("STING - Electrical");
                            focusSet.Add("STING - Plumbing");
                            focusSet.Add("STING - Fire Protection");
                            focusSet.Add("STING - Low Voltage");
                            focusSet.Add("STING - Conduits & Cable Trays");
                            break;
                        case "PRES_DARK":
                        case "3D_DARK":
                            // Dark mode: focus the building shell so it shows in white
                            focusSet.Add("STING - Architectural");
                            focusSet.Add("STING - Structural");
                            break;
                        case "PRES_MONO":
                        case "3D_MONO":
                            // Monochrome has no "focus" — all elements get a line-weight
                            // hierarchy via the per-name switch below.
                            break;
                        case "PRES_LAND":
                            // Landscape: focus rooms + arch (site, topo). MEP hidden.
                            focusSet.Add("STING - Architectural");
                            focusSet.Add("STING - Rooms & Spaces");
                            break;
                    }

                    // Background grey override (used for non-focus or all-mono)
                    Color nonFocusLine = isDark ? new Color(70, 70, 70) : PresentationStyleHelper.LIGHT_GREY;
                    Color nonFocusFill = isDark ? new Color(50, 50, 50) : new Color(238, 238, 238);
                    int nonFocusTrans  = isDark ? 15 : 50;

                    foreach (var kv in DisciplineFilters())
                    {
                        bool isFocus = focusSet.Contains(kv.Key);

                        if (isLand && IsAnyMEP(kv.Key))
                        {
                            // Hard-hide MEP on landscape templates
                            PresentationStyleHelper.HideByFilter(template, kv.Value);
                            continue;
                        }

                        if (isMono)
                        {
                            // True monochrome — line weights only, NO surface fills
                            int lw = IsArch(kv.Key) ? 4
                                   : IsStruct(kv.Key) ? 3
                                   : IsRooms(kv.Key) ? 2
                                   : 1; // MEP / Fire / LV / Conduit
                            int cw = IsArch(kv.Key) ? 5
                                   : IsStruct(kv.Key) ? 4
                                   : 2;
                            Color tone = IsArch(kv.Key) ? PresentationStyleHelper.BLACK
                                       : IsStruct(kv.Key) ? PresentationStyleHelper.DIM_GREY
                                       : PresentationStyleHelper.MID_GREY;
                            PresentationStyleHelper.AddOrSet(template, kv.Value,
                                PresentationStyleHelper.MonochromeLine(tone, lw, cw));
                            continue;
                        }

                        if (isFocus)
                        {
                            // Focus discipline: accent colour + cut line weight boost
                            int focusW = IsArch(kv.Key) ? 4 : IsStruct(kv.Key) ? 3 : 2;
                            int focusCutW = IsArch(kv.Key) ? 5 : IsStruct(kv.Key) ? 4 : 3;
                            var ogs = PresentationStyleHelper.DisciplineAccentWithCut(
                                accent, focusW, focusCutW, solidFill,
                                surfaceTransparency: isDark ? 5 : 15, includeProjFill: true);
                            PresentationStyleHelper.AddOrSet(template, kv.Value, ogs);
                        }
                        else
                        {
                            // Non-focus: muted background tone
                            var ogs = new OverrideGraphicSettings();
                            ogs.SetProjectionLineColor(nonFocusLine);
                            ogs.SetProjectionLineWeight(1);
                            ogs.SetCutLineColor(nonFocusLine);
                            ogs.SetCutLineWeight(2);
                            if (solidFill != null)
                            {
                                ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                                ogs.SetSurfaceForegroundPatternColor(nonFocusFill);
                                ogs.SetCutForegroundPatternId(solidFill.Id);
                                ogs.SetCutForegroundPatternColor(nonFocusFill);
                            }
                            ogs.SetSurfaceTransparency(nonFocusTrans);
                            PresentationStyleHelper.AddOrSet(template, kv.Value, ogs);
                        }
                    }

                    // Per-discipline presentation refinement: on 2D plans, layer
                    // the Disc: parameter filter for the focus discipline so
                    // elements tagged with DISC=<code> are precisely highlighted
                    // regardless of their Revit category.
                    if (!is3D && !isMono && !isLand)
                    {
                        string discCode = discipline switch
                        {
                            "PRES_A"      => "A",
                            "PRES_S"      => "S",
                            "PRES_E_DISC" => "E",
                            "PRES_P"      => "P",
                            _             => null,
                        };
                        if (discCode != null)
                            PresentationStyleHelper.AttachDiscParameterFilters(template, filterLookup,
                                discCode, accent, solidFill);
                    }

                    // Filter layering order — status then QA, so QA stays on top.
                    // (No QA on mono/dark — they're for client output, not QA.)
                    if (!is3D && !isMono)
                        PresentationStyleHelper.ApplyStatusOverlays(template, filterLookup, doc, solidFill);
                    return;
                }

                // ─────────────────────────────────────────────────────────────
                // COORDINATION PLANS (MEP, ALL) — every MEP discipline coloured
                // distinctly, arch+struct halftoned (MEP) or shown muted (ALL).
                // ─────────────────────────────────────────────────────────────
                if (discipline == "MEP" || discipline == "ALL")
                {
                    bool isMEPCoord = discipline == "MEP";
                    foreach (var kv in DisciplineFilters())
                    {
                        if (!TryDisc(kv.Key, out var c)) continue;
                        bool muteThis = isMEPCoord && (IsArch(kv.Key) || IsStruct(kv.Key));
                        if (muteThis)
                            PresentationStyleHelper.AddOrSet(template, kv.Value, Halftone(60));
                        else
                        {
                            int projW = IsArch(kv.Key) || IsStruct(kv.Key) ? 1 : 2;
                            int cutW  = IsArch(kv.Key) || IsStruct(kv.Key) ? 2 : 3;
                            var ogs = PresentationStyleHelper.DisciplineAccentWithCut(
                                c, projW, cutW, solidFill, 0, false);
                            PresentationStyleHelper.AddOrSet(template, kv.Value, ogs);
                        }
                    }
                    // Filter application order — each subsequent call layers on top:
                    //   1. Discipline filters (above)
                    //   2. System filters (toggleable isolators, no override)
                    //   3. Status overlays (phase tint)
                    //   4. QA overlays (highest priority — must override everything)
                    if (PresentationStyleHelper.DisciplineSystemFilters.TryGetValue(discipline, out var mepSys))
                        PresentationStyleHelper.AttachSystemFilters(template, filterLookup, mepSys);
                    PresentationStyleHelper.ApplyStatusOverlays(template, filterLookup, doc, solidFill);
                    PresentationStyleHelper.ApplyQAOverlays(template, filterLookup, solidFill);
                    return;
                }

                // ─────────────────────────────────────────────────────────────
                // SINGLE-DISCIPLINE WORKING PLANS (M, E, P, A, S, FP, LV)
                // The focus discipline gets full accent + cut graphics + QA
                // overlay. All other disciplines are halftoned. Status overlays
                // applied so phase-coded items remain visible.
                // ─────────────────────────────────────────────────────────────
                HashSet<string> focusFilters = null;
                if (DisciplineFilterRules.TryGetValue(discipline, out string[] focusNames))
                    focusFilters = new HashSet<string>(focusNames, StringComparer.OrdinalIgnoreCase);

                if (focusFilters != null)
                {
                    foreach (var kv in DisciplineFilters())
                    {
                        bool isFocus = focusFilters.Contains(kv.Key);
                        if (isFocus && TryDisc(kv.Key, out var c))
                        {
                            int projW = IsArch(kv.Key) ? 4 : IsStruct(kv.Key) ? 3 : 3;
                            int cutW  = IsArch(kv.Key) ? 5 : IsStruct(kv.Key) ? 4 : 3;
                            var ogs = PresentationStyleHelper.DisciplineAccentWithCut(
                                c, projW, cutW, solidFill, 0, false);
                            PresentationStyleHelper.AddOrSet(template, kv.Value, ogs);
                        }
                        else
                        {
                            // Non-focus disciplines on a working plan: halftone
                            // arch heavier (you usually want walls visible) and
                            // unrelated MEP heavier-halftoned (out of the way).
                            int t = IsArch(kv.Key) || IsStruct(kv.Key) ? 50 : 75;
                            PresentationStyleHelper.AddOrSet(template, kv.Value, Halftone(t));
                        }
                    }
                    // Filter application order: discipline → system → Disc param refinement → status → QA (top)
                    if (PresentationStyleHelper.DisciplineSystemFilters.TryGetValue(discipline, out var sysFilters))
                        PresentationStyleHelper.AttachSystemFilters(template, filterLookup, sysFilters);
                    // Attach Disc: param filters for ISO 19650 precision — focus disc gets subtle accent,
                    // others attached without override so users can toggle them off.
                    string discFilterName = MapDiscToFilterName(discipline);
                    if (discFilterName != null && TryDisc(discFilterName, out var discColorParam))
                        PresentationStyleHelper.AttachDiscParameterFilters(template, filterLookup,
                            discipline, discColorParam, solidFill);
                    PresentationStyleHelper.ApplyStatusOverlays(template, filterLookup, doc, solidFill);
                    PresentationStyleHelper.ApplyQAOverlays(template, filterLookup, solidFill);
                    return;
                }

                // Fallback — unrecognised discipline code: just add filters
                // unchanged so the template is at least syntactically valid.
                foreach (var kv in DisciplineFilters())
                    PresentationStyleHelper.AddOrSet(template, kv.Value, null);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ConfigureTemplateVG failed for '{template.Name}': {ex.Message}");
            }
        }
    }
}
