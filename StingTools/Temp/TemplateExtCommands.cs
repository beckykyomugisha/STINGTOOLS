using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Temp
{
    /// <summary>
    /// Additional template commands ported from STINGTemp 6_Templates.panel:
    /// Line Styles, Line Patterns, Dimension Styles, Text Styles, Phases,
    /// VG Overrides, Object Styles, Fill Patterns.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateLinePatternsCommand : IExternalCommand
    {
        /// <summary>
        /// Line patterns per ISO 128-2:2020 and UK BIM drawing standards.
        /// Segments: positive = dash length (ft), negative = space length (ft).
        /// Values converted from mm: mm / 304.8 = feet.
        /// </summary>
        private static readonly (string name, double[] segments)[] Patterns = new[]
        {
            // ISO 128 standard patterns
            ("STING - Dashed", new[] { 6.0/304.8, -3.0/304.8 }),
            ("STING - Dash Dot", new[] { 6.0/304.8, -2.0/304.8, 1.0/304.8, -2.0/304.8 }),
            ("STING - Hidden", new[] { 3.0/304.8, -1.5/304.8 }),
            ("STING - Center", new[] { 12.0/304.8, -3.0/304.8, 3.0/304.8, -3.0/304.8 }),
            ("STING - Demolition", new[] { 1.0/304.8, -2.0/304.8 }),
            ("STING - Setout", new[] { 10.0/304.8, -2.0/304.8, 2.0/304.8, -2.0/304.8, 2.0/304.8, -2.0/304.8 }),
            // Extended patterns for professional BIM templates
            ("STING - Long Dash", new[] { 20.0/304.8, -5.0/304.8 }),
            ("STING - Dot", new[] { 0.5/304.8, -2.0/304.8 }),
            ("STING - Phase Boundary", new[] { 6.0/304.8, -2.0/304.8, 6.0/304.8, -2.0/304.8, 1.0/304.8, -2.0/304.8 }),
            ("STING - Fire Compartment", new[] { 8.0/304.8, -3.0/304.8, 1.0/304.8, -3.0/304.8, 1.0/304.8, -3.0/304.8 }),
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            var existing = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(LinePatternElement))
                    .Select(e => e.Name));

            int created = 0;
            using (Transaction tx = new Transaction(doc, "STING Create Line Patterns"))
            {
                tx.Start();
                foreach (var (name, segments) in Patterns)
                {
                    if (existing.Contains(name)) continue;
                    try
                    {
                        var segList = new List<LinePatternSegment>();
                        for (int i = 0; i < segments.Length; i++)
                        {
                            double val = segments[i];
                            if (val > 0)
                                segList.Add(new LinePatternSegment(
                                    LinePatternSegmentType.Dash, val));
                            else
                                segList.Add(new LinePatternSegment(
                                    LinePatternSegmentType.Space, Math.Abs(val)));
                        }
                        var lp = new LinePattern(name);
                        lp.SetSegments(segList);
                        LinePatternElement.Create(doc, lp);
                        created++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Line pattern '{name}': {ex.Message}");
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Create Line Patterns",
                $"Created {created} line patterns.\n" +
                $"Total defined: {Patterns.Length}");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreatePhasesCommand : IExternalCommand
    {
        /// <summary>
        /// Standard phases per ISO 19650 / UK BIM best practice.
        /// NOTE: "Demolition" is NOT a Revit phase — it is a Phase Status
        /// (element's "Phase Demolished" property). Never create demolition phases.
        /// Phases represent construction time periods only.
        /// </summary>
        private static readonly string[] PhaseNames = new[]
        {
            "Existing",                 // Survey/as-built elements
            "Enabling Works",           // Temporary works, site preparation, early demolition
            "Phase 1 - Construction",   // First construction stage (demo shown via Phase Demolished)
            "Phase 2 - Construction",   // Second construction stage
            "Future Phase",             // Planned but not yet constructed
            "Temporary Works",          // Hoarding, scaffolding, temporary roads
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            var existingPhases = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(Phase))
                    .Select(e => e.Name));

            // Revit API does not support programmatic phase creation (known limitation).
            // Report which phases exist and which are missing for manual creation.
            var missing = PhaseNames.Where(n => !existingPhases.Contains(n)).ToList();
            var present = PhaseNames.Where(n => existingPhases.Contains(n)).ToList();

            var report = new System.Text.StringBuilder();
            report.AppendLine($"STING Phase Configuration ({PhaseNames.Length} required):");
            report.AppendLine($"  Present: {present.Count}");
            report.AppendLine($"  Missing: {missing.Count}");

            if (missing.Count > 0)
            {
                report.AppendLine("\nPlease create these phases manually via Manage → Phases:");
                foreach (string name in missing)
                    report.AppendLine($"  • {name}");
            }
            else
            {
                report.AppendLine("\nAll required phases are present.");
            }

            TaskDialog.Show("Create Phases", report.ToString());
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ApplyFiltersToViewsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            // Get all STING filters
            var filters = new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement))
                .Cast<ParameterFilterElement>()
                .Where(f => f.Name.StartsWith("STING"))
                .ToList();

            if (filters.Count == 0)
            {
                TaskDialog.Show("Apply Filters", "No STING filters found. Create filters first.");
                return Result.Succeeded;
            }

            // Get all view templates
            var templates = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate && v.Name.StartsWith("STING"))
                .ToList();

            if (templates.Count == 0)
            {
                TaskDialog.Show("Apply Filters", "No STING view templates found. Create templates first.");
                return Result.Succeeded;
            }

            int applied = 0;
            using (Transaction tx = new Transaction(doc, "STING Apply Filters to Views"))
            {
                tx.Start();
                foreach (View template in templates)
                {
                    foreach (var filter in filters)
                    {
                        try
                        {
                            if (!template.GetFilters().Contains(filter.Id))
                            {
                                template.AddFilter(filter.Id);
                                template.SetFilterVisibility(filter.Id, true);
                                applied++;
                            }
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Filter apply '{filter.Name}' → '{template.Name}': {ex.Message}");
                        }
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Apply Filters",
                $"Applied {applied} filter assignments across {templates.Count} view templates.");
            return Result.Succeeded;
        }
    }

    /// <summary>Create cable tray types from MEP_MATERIALS.csv.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateCableTraysCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            return CompoundTypeCreator.CreateTypes(doc, "Cable Trays",
                "MEP_MATERIALS.csv",
                new[] { "E-TRY" },
                CompoundTypeCreator.ElementKind.Duct);
        }
    }

    /// <summary>Create conduit types from MEP_MATERIALS.csv.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateConduitsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            return CompoundTypeCreator.CreateTypes(doc, "Conduits",
                "MEP_MATERIALS.csv",
                new[] { "E-CDT" },
                CompoundTypeCreator.ElementKind.Pipe);
        }
    }

    /// <summary>Create material takeoff schedules from CSV.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateMaterialSchedulesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            string[] categories = new[]
            {
                "Walls", "Floors", "Ceilings", "Roofs", "Doors", "Windows",
                "Structural Columns", "Structural Framing",
            };

            var existing = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Select(e => e.Name));

            int created = 0;
            using (Transaction tx = new Transaction(doc, "STING Create Material Schedules"))
            {
                tx.Start();
                foreach (string cat in categories)
                {
                    string name = $"STING - {cat} Material Takeoff";
                    if (existing.Contains(name)) continue;

                    BuiltInCategory bic;
                    switch (cat)
                    {
                        case "Walls": bic = BuiltInCategory.OST_Walls; break;
                        case "Floors": bic = BuiltInCategory.OST_Floors; break;
                        case "Ceilings": bic = BuiltInCategory.OST_Ceilings; break;
                        case "Roofs": bic = BuiltInCategory.OST_Roofs; break;
                        case "Doors": bic = BuiltInCategory.OST_Doors; break;
                        case "Windows": bic = BuiltInCategory.OST_Windows; break;
                        case "Structural Columns": bic = BuiltInCategory.OST_StructuralColumns; break;
                        case "Structural Framing": bic = BuiltInCategory.OST_StructuralFraming; break;
                        default: continue;
                    }

                    try
                    {
                        ViewSchedule vs = ViewSchedule.CreateMaterialTakeoff(
                            doc, new ElementId(bic));
                        vs.Name = name;
                        created++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Material schedule '{name}': {ex.Message}");
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Material Schedules",
                $"Created {created} material takeoff schedules.");
            return Result.Succeeded;
        }
    }
}
