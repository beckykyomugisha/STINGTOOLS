// StingTools v4 MVP — FabricationEngine coordinator.
//
// Public entry point for the v4 fabrication package. Given a set of
// element ids, dispatches each element to the right per-discipline
// fabricator (Electrical / Pipe / Duct), assembles them into spool
// groups via AssemblyGrouper, asks AssemblyBuilder to materialise
// the AssemblyInstance, AssemblyViewBuilder to create the views,
// and ShopDrawingComposer to lay them out on title block sheets.
//
// Returns FabricationResult with per-discipline assembly counts,
// generated sheet ids, and a warnings list.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core.Fabrication.Duct;
using StingTools.Core.Fabrication.Electrical;
using StingTools.Core.Fabrication.Pipe;

namespace StingTools.Core.Fabrication
{
    public class FabricationResult
    {
        public List<ElementId> AssemblyIds { get; } = new List<ElementId>();
        public List<ElementId> SheetIds    { get; } = new List<ElementId>();
        public Dictionary<string, int> AssembliesByDiscipline { get; }
            = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public List<string> Warnings { get; } = new List<string>();
        public int FailedCount { get; set; }

        public string FormatSummary()
        {
            int total = AssembliesByDiscipline.Values.Sum();
            return $"Assemblies: {total}, Sheets: {SheetIds.Count}, Failed: {FailedCount}";
        }
    }

    public static class FabricationEngine
    {
        /// <summary>
        /// Build a fabrication package from a flat list of element ids.
        /// Discipline of each element is inferred from its category.
        /// </summary>
        public static FabricationResult GenerateFabricationPackage(
            Document doc,
            IList<ElementId> elementIds)
        {
            var result = new FabricationResult();
            if (doc == null) { result.Warnings.Add("Document is null"); return result; }
            if (elementIds == null || elementIds.Count == 0)
            {
                result.Warnings.Add("No elements supplied to FabricationEngine");
                return result;
            }

            // Group by discipline
            var byDisc = new Dictionary<string, List<ElementId>>(StringComparer.OrdinalIgnoreCase)
            {
                { "Electrical", new List<ElementId>() },
                { "Pipe",       new List<ElementId>() },
                { "Duct",       new List<ElementId>() }
            };
            foreach (var id in elementIds)
            {
                var el = doc.GetElement(id);
                string disc = DisciplineFor(el);
                if (disc != null && byDisc.ContainsKey(disc)) byDisc[disc].Add(id);
            }

            // Per-discipline pass
            try
            {
                if (byDisc["Electrical"].Count > 0)
                    new ElectricalFabricator().Fabricate(doc, byDisc["Electrical"], result);
                if (byDisc["Pipe"].Count > 0)
                    new PipeFabricator().Fabricate(doc, byDisc["Pipe"], result);
                if (byDisc["Duct"].Count > 0)
                    new DuctFabricator().Fabricate(doc, byDisc["Duct"], result);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"FabricationEngine fatal: {ex.Message}");
            }
            return result;
        }

        public static string DisciplineFor(Element el)
        {
            if (el?.Category == null) return null;
            var bic = (BuiltInCategory)el.Category.Id.Value;
            switch (bic)
            {
                case BuiltInCategory.OST_Conduit:
                case BuiltInCategory.OST_ConduitFitting:
                case BuiltInCategory.OST_CableTray:
                case BuiltInCategory.OST_CableTrayFitting:
                case BuiltInCategory.OST_ElectricalCircuit:
                    return "Electrical";

                case BuiltInCategory.OST_PipeCurves:
                case BuiltInCategory.OST_PipeFitting:
                case BuiltInCategory.OST_PipeAccessory:
                case BuiltInCategory.OST_PipeInsulations:
                case BuiltInCategory.OST_PlumbingFixtures:
                    return "Pipe";

                case BuiltInCategory.OST_DuctCurves:
                case BuiltInCategory.OST_DuctFitting:
                case BuiltInCategory.OST_DuctAccessory:
                case BuiltInCategory.OST_DuctInsulations:
                case BuiltInCategory.OST_DuctTerminal:
                    return "Duct";
            }
            return null;
        }
    }
}
