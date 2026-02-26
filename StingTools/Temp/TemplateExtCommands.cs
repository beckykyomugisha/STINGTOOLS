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
    public class CreateLinePatternsCommand : IExternalCommand
    {
        private static readonly (string name, double[] segments)[] Patterns = new[]
        {
            ("STING - Dashed", new[] { 6.0/304.8, -3.0/304.8 }),
            ("STING - Dash Dot", new[] { 6.0/304.8, -2.0/304.8, 1.0/304.8, -2.0/304.8 }),
            ("STING - Hidden", new[] { 3.0/304.8, -1.5/304.8 }),
            ("STING - Center", new[] { 12.0/304.8, -3.0/304.8, 3.0/304.8, -3.0/304.8 }),
            ("STING - Demolition", new[] { 1.0/304.8, -2.0/304.8 }),
            ("STING - Setout", new[] { 10.0/304.8, -2.0/304.8, 2.0/304.8, -2.0/304.8, 2.0/304.8, -2.0/304.8 }),
        };

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            Document doc = cmd.Application.ActiveUIDocument.Document;

            var existing = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(LinePatternElement))
                    .Select(e => e.Name));

            int created = 0;
            using (Transaction tx = new Transaction(doc, "Create Line Patterns"))
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
                        LinePatternElement.Create(doc, new LinePattern(name, segList));
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

    [Transaction(TransactionMode.Manual)]
    public class CreatePhasesCommand : IExternalCommand
    {
        private static readonly string[] PhaseNames = new[]
        {
            "Existing", "Phase 1 - Demolition", "Phase 1 - New Construction",
            "Phase 2 - Demolition", "Phase 2 - New Construction",
            "Phase 3 - Future", "Temporary Works",
        };

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            Document doc = cmd.Application.ActiveUIDocument.Document;

            var existingPhases = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(Phase))
                    .Select(e => e.Name));

            int created = 0;
            using (Transaction tx = new Transaction(doc, "Create Phases"))
            {
                tx.Start();
                foreach (string name in PhaseNames)
                {
                    if (existingPhases.Contains(name)) continue;
                    try
                    {
                        Phase phase = Phase.Create(doc, name);
                        if (phase != null) created++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Phase '{name}': {ex.Message}");
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Create Phases",
                $"Created {created} phases.\n" +
                $"Total defined: {PhaseNames.Length}");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class ApplyFiltersToViewsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            Document doc = cmd.Application.ActiveUIDocument.Document;

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
            using (Transaction tx = new Transaction(doc, "Apply Filters to Views"))
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
    public class CreateCableTraysCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            Document doc = cmd.Application.ActiveUIDocument.Document;
            return CompoundTypeCreator.CreateTypes(doc, "Cable Trays",
                "MEP_MATERIALS.csv",
                new[] { "E-TRY" },
                CompoundTypeCreator.ElementKind.Duct);
        }
    }

    /// <summary>Create conduit types from MEP_MATERIALS.csv.</summary>
    [Transaction(TransactionMode.Manual)]
    public class CreateConduitsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            Document doc = cmd.Application.ActiveUIDocument.Document;
            return CompoundTypeCreator.CreateTypes(doc, "Conduits",
                "MEP_MATERIALS.csv",
                new[] { "E-CDT" },
                CompoundTypeCreator.ElementKind.Pipe);
        }
    }

    /// <summary>Create material takeoff schedules from CSV.</summary>
    [Transaction(TransactionMode.Manual)]
    public class CreateMaterialSchedulesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            Document doc = cmd.Application.ActiveUIDocument.Document;

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
            using (Transaction tx = new Transaction(doc, "Create Material Schedules"))
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
