// StingTools — SLD circuit traverser (Phase 175)
//
// Walks the project's electrical hierarchy starting from any panel
// without an upstream feed. Each node in the resulting tree carries
// circuit data (rating, poles, load) so the layout engine can place
// labels next to the symbol.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace StingTools.Core.SLD
{
    public sealed class SLDNode
    {
        public ElementId ElementId { get; set; }
        public string ConceptId { get; set; }
        public string Label { get; set; }
        public string Rating { get; set; }
        public string CircuitRef { get; set; }
        public int Poles { get; set; }
        public double LoadKW { get; set; }
        public SLDNode Parent { get; set; }
        public List<SLDNode> Children { get; set; } = new List<SLDNode>();
        public int HierarchyLevel { get; set; }
        public bool IsPanel { get; set; }
        public bool IsLoad { get; set; }
        public bool IsProtection { get; set; }
        public FamilyInstance RevitElement { get; set; }
    }

    public static class SLDCircuitTraverser
    {
        public static SLDNode BuildHierarchy(Document doc)
        {
            if (doc == null) return null;
            try
            {
                // Collect all electrical equipment + fixtures.
                var equipment = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .ToList();

                // Find root: panel that is NOT a load on any ElectricalSystem.
                var loadIds = new HashSet<int>();
                var allSystems = new FilteredElementCollector(doc)
                    .OfClass(typeof(ElectricalSystem))
                    .Cast<ElectricalSystem>()
                    .ToList();
                foreach (var sys in allSystems)
                {
                    try
                    {
                        // TODO-VERIFY-API: ElectricalSystem.Elements property.
                        foreach (Element el in sys.Elements)
                            loadIds.Add(el.Id.IntegerValue);
                    }
                    catch (Exception ex) { StingTools.Core.StingLog.Warn($"Traverser scan systems: {ex.Message}"); }
                }

                var root = equipment.FirstOrDefault(e => !loadIds.Contains(e.Id.IntegerValue));
                if (root == null) return null;

                var rootNode = BuildNode(root, null, 0, allSystems, doc);
                return rootNode;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"BuildHierarchy: {ex.Message}");
                return null;
            }
        }

        private static SLDNode BuildNode(FamilyInstance fi, SLDNode parent, int level,
            List<ElectricalSystem> allSystems, Document doc)
        {
            var node = new SLDNode
            {
                ElementId = fi.Id,
                Parent = parent,
                HierarchyLevel = level,
                IsPanel = true,
                RevitElement = fi,
                Label = fi.Name,
                ConceptId = SymbolConceptForElement(fi)
            };

            try
            {
                // Find downstream circuits where this panel is BaseEquipment.
                var downstream = allSystems.Where(s =>
                {
                    try { return s.BaseEquipment != null && s.BaseEquipment.Id == fi.Id; }
                    catch { return false; }
                }).ToList();

                foreach (var sys in downstream)
                {
                    ReadCircuitData(sys, node);
                    try
                    {
                        foreach (Element el in sys.Elements)
                        {
                            if (el is FamilyInstance child)
                            {
                                bool isPanel = child.Category?.Id?.IntegerValue
                                    == (int)BuiltInCategory.OST_ElectricalEquipment;
                                if (isPanel)
                                {
                                    node.Children.Add(BuildNode(child, node, level + 1, allSystems, doc));
                                }
                                else
                                {
                                    var leaf = new SLDNode
                                    {
                                        ElementId = child.Id,
                                        Parent = node,
                                        HierarchyLevel = level + 1,
                                        IsLoad = true,
                                        RevitElement = child,
                                        Label = child.Name,
                                        ConceptId = SymbolConceptForElement(child),
                                        CircuitRef = sys.CircuitNumber,
                                    };
                                    node.Children.Add(leaf);
                                }
                            }
                        }
                    }
                    catch (Exception ex) { StingTools.Core.StingLog.Warn($"BuildNode children: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"BuildNode {fi.Name}: {ex.Message}");
            }
            return node;
        }

        public static void ReadCircuitData(ElectricalSystem circuit, SLDNode node)
        {
            if (circuit == null) return;
            try
            {
                node.CircuitRef = circuit.CircuitNumber ?? node.CircuitRef;
                // TODO-VERIFY-API: ElectricalSystem.PolesNumber and ApparentLoad.
                try { node.Poles = circuit.PolesNumber; } catch (Exception ex) { StingTools.Core.StingLog.Warn($"Poles: {ex.Message}"); }
                try
                {
                    var loadParam = circuit.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD);
                    if (loadParam != null) node.LoadKW = loadParam.AsDouble() / 1000.0;
                }
                catch (Exception ex) { StingTools.Core.StingLog.Warn($"Load: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"ReadCircuitData: {ex.Message}");
            }
        }

        private static string SymbolConceptForElement(Element el)
        {
            try
            {
                var p = el.LookupParameter("STING_SYMBOL_ID");
                var existing = p?.AsString();
                if (!string.IsNullOrEmpty(existing)) return existing;
                var concept = StingTools.Core.Symbols.SymbolConceptRegistry
                    .GetConceptsForCategory(el.Category?.Name)
                    .FirstOrDefault();
                return concept?.ConceptId;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"SymbolConceptForElement: {ex.Message}");
                return null;
            }
        }
    }
}
