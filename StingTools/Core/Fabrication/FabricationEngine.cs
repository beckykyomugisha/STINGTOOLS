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

        // ISO 6412 symbol placement results — populated by IsoSymbolPlacer
        // after each discipline's transaction commits. Surfaced in the
        // FabricationResultDialog so users see whether the option had effect.
        public int SymbolsPlaced { get; set; }
        public int SymbolsReplaced { get; set; }
        public int UnmatchedMembers { get; set; }
        public List<ElementId> SymbolIds { get; } = new List<ElementId>();
        public List<string> UnmatchedSamples { get; } = new List<string>();
        public HashSet<string> MissingFamilies { get; }
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> SymbolsByDiscipline { get; }
            = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public string FormatSummary()
        {
            int total = AssembliesByDiscipline.Values.Sum();
            return $"Assemblies: {total}, Sheets: {SheetIds.Count}, Failed: {FailedCount}, Symbols: {SymbolsPlaced}";
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

        /// <summary>
        /// Run IsoSymbolPlacer for each (assembly, ISO view) pair when the
        /// user has ticked PlaceISO6412Symbols on the Fabrication tab and
        /// the per-discipline toggle for <paramref name="discipline"/> is
        /// also on. Must be called AFTER the discipline's transaction
        /// commits — the placer opens its own Transaction.
        /// </summary>
        public static void PlaceSymbolsIfRequested(
            Document doc,
            string discipline,
            IList<(ElementId AssyId, ElementId IsoViewId)> targets,
            FabricationResult result)
        {
            if (doc == null || result == null) return;
            if (targets == null || targets.Count == 0) return;
            var opts = StingTools.Commands.Fabrication.FabricationOptions;
            if (!opts.PlaceISO6412Symbols) return;
            if (opts.SymbolPlacementMode ==
                StingTools.Commands.Fabrication.FabricationOptions.PlacementMode.Off) return;
            if (!IsDisciplineOn(discipline)) return;

            int totalForDisc = 0;
            foreach (var pair in targets)
            {
                try
                {
                    var view = doc.GetElement(pair.IsoViewId) as View;
                    if (view == null) continue;
                    int placed = IsoSymbolPlacer.PlaceSymbolsForAssembly(
                        doc, pair.AssyId, view, result);
                    result.SymbolsPlaced += placed;
                    totalForDisc += placed;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"PlaceSymbolsIfRequested {pair.AssyId}: {ex.Message}");
                }
            }
            if (totalForDisc > 0)
            {
                if (!result.SymbolsByDiscipline.ContainsKey(discipline))
                    result.SymbolsByDiscipline[discipline] = 0;
                result.SymbolsByDiscipline[discipline] += totalForDisc;
            }
        }

        private static bool IsDisciplineOn(string discipline)
        {
            var o = StingTools.Commands.Fabrication.FabricationOptions;
            switch ((discipline ?? "").ToUpperInvariant())
            {
                case "PIPE":       return o.PlaceISOPipe;
                case "DUCT":       return o.PlaceISODuct;
                case "ELECTRICAL": return o.PlaceISOElectrical;
                default:           return true;
            }
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
