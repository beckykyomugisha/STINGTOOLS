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
    /// Ported from STINGTemp 6_Templates.panel — Create Filters.
    /// Creates view filters from standard category/parameter rules.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateFiltersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            // Standard discipline filters
            var filterDefs = new[]
            {
                ("STING - Architectural", BuiltInCategory.OST_Walls),
                ("STING - Structural", BuiltInCategory.OST_StructuralColumns),
                ("STING - Mechanical", BuiltInCategory.OST_MechanicalEquipment),
                ("STING - Electrical", BuiltInCategory.OST_ElectricalEquipment),
                ("STING - Plumbing", BuiltInCategory.OST_PlumbingFixtures),
                ("STING - Fire Protection", BuiltInCategory.OST_Sprinklers),
            };

            var existingNames = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(ParameterFilterElement))
                    .Select(e => e.Name));

            int created = 0;
            int skipped = 0;

            using (Transaction tx = new Transaction(doc, "Create Filters"))
            {
                tx.Start();

                foreach (var (name, bic) in filterDefs)
                {
                    if (existingNames.Contains(name))
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        var catIds = new List<ElementId> { new ElementId(bic) };
                        ParameterFilterElement.Create(doc, name, catIds);
                        created++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Filter create failed '{name}': {ex.Message}");
                        skipped++;
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Create Filters",
                $"Created {created} view filters.\nSkipped {skipped} (exist or failed).");

            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Ported from STINGTemp 6_Templates.panel — Create Worksets.
    /// Creates worksets from a standard definition set.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateWorksetsCommand : IExternalCommand
    {
        private static readonly string[] WorksetNames = new[]
        {
            // Architecture
            "A-ARCH-Walls", "A-ARCH-Floors", "A-ARCH-Ceilings", "A-ARCH-Roofs",
            "A-ARCH-Doors", "A-ARCH-Windows", "A-ARCH-Furniture", "A-ARCH-Stairs",
            // Structure
            "S-STRC-Columns", "S-STRC-Framing", "S-STRC-Foundation",
            // Mechanical
            "M-MECH-Equipment", "M-MECH-Ducts", "M-MECH-Pipes",
            // Electrical
            "E-ELEC-Equipment", "E-ELEC-Lighting", "E-ELEC-Conduits", "E-ELEC-CableTrays",
            // Plumbing
            "P-PLMB-Fixtures", "P-PLMB-Pipes",
            // Fire Protection
            "FP-FPRT-Sprinklers", "FP-FPRT-Devices",
            // Site
            "Z-SITE-Topography", "Z-SITE-Parking",
            // Shared
            "X-SHARED-Grids", "X-SHARED-Levels", "X-SHARED-Links",
        };

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

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

            using (Transaction tx = new Transaction(doc, "Create Worksets"))
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
    /// Ported from STINGTemp 6_Templates.panel — View Templates.
    /// Creates and applies view templates for standard disciplines.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ViewTemplatesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            // Find existing view templates
            var existingTemplates = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate)
                .Select(v => v.Name)
                .ToHashSet();

            // Find a floor plan view to duplicate as template base
            var basePlan = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan);

            if (basePlan == null)
            {
                TaskDialog.Show("View Templates",
                    "No floor plan view found to use as a template base.\n" +
                    "Create at least one floor plan view first.");
                return Result.Failed;
            }

            string[] templateNames = new[]
            {
                "STING - Architectural Plan",
                "STING - Structural Plan",
                "STING - Mechanical Plan",
                "STING - Electrical Plan",
                "STING - Plumbing Plan",
                "STING - Fire Protection Plan",
                "STING - Coordination Plan",
            };

            int created = 0;
            int skipped = 0;

            using (Transaction tx = new Transaction(doc, "Create View Templates"))
            {
                tx.Start();

                foreach (string name in templateNames)
                {
                    if (existingTemplates.Contains(name))
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        ElementId newId = basePlan.CreateViewTemplate();
                        View template = doc.GetElement(newId) as View;
                        if (template != null)
                        {
                            template.Name = name;
                            created++;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"View template create failed '{name}': {ex.Message}");
                        skipped++;
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("View Templates",
                $"Created {created} view templates.\nSkipped {skipped} (exist or failed).");

            return Result.Succeeded;
        }
    }
}
