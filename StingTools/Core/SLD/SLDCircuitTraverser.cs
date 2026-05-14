// StingTools — SLD circuit traverser (Phase 175)
//
// Walks the project's electrical hierarchy starting from any panel
// without an upstream feed. Each node in the resulting tree carries
// circuit data (rating, poles, load) so the layout engine can place
// labels next to the symbol.
//
// SLD-01: supports multiple root panels — they are collected and
//         wrapped in a virtual root when more than one is found.
// SLD-02: IsProtection is set for MCB/RCBO/RCCB/fuse elements.
// SLD-03: Rating read from RBS_ELEC_CIRCUIT_FRAME_PARAM or the STING
//         shared param ELC_CKT_PROTECTION_RATING_TXT.
// SLD-15: SymbolConceptForElement logs a warning when no concept found.

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
        // SLD-02: keywords that identify protection devices
        private static readonly string[] ProtectionKeywords = new[]
        {
            "MCB", "RCBO", "RCCB", "RCD", "MCCB", "FUSE", "BREAKER",
            "CIRCUIT BREAKER", "OVERCURRENT", "PROTECTION", "ISOLATOR",
            "SWITCH DISCONNECTOR", "MOTORISED BREAKER"
        };

        public static SLDNode BuildHierarchy(Document doc)
        {
            if (doc == null) return null;
            try
            {
                var equipment = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .ToList();

                var loadIds = new HashSet<long>();
                var allSystems = new FilteredElementCollector(doc)
                    .OfClass(typeof(ElectricalSystem))
                    .Cast<ElectricalSystem>()
                    .ToList();
                foreach (var sys in allSystems)
                {
                    try
                    {
                        foreach (Element el in sys.Elements)
                            loadIds.Add(el.Id.Value);
                    }
                    catch (Exception ex)
                    {
                        StingTools.Core.StingLog.Warn($"Traverser scan systems: {ex.Message}");
                    }
                }

                // SLD-01: collect ALL root panels (not just the first one)
                var roots = equipment.Where(e => !loadIds.Contains(e.Id.Value)).ToList();
                if (roots.Count == 0) return null;

                if (roots.Count == 1)
                    return BuildNode(roots[0], null, 0, allSystems, doc);

                // Multiple roots — wrap in a virtual supply node
                var virtualRoot = new SLDNode
                {
                    ElementId = ElementId.InvalidElementId,
                    Label = "Supply",
                    IsPanel = true,
                    HierarchyLevel = 0,
                };
                foreach (var r in roots)
                    virtualRoot.Children.Add(BuildNode(r, virtualRoot, 1, allSystems, doc));
                return virtualRoot;
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
                                bool isPanel = child.Category?.Id?.Value
                                    == (long)BuiltInCategory.OST_ElectricalEquipment;
                                if (isPanel)
                                {
                                    node.Children.Add(BuildNode(child, node, level + 1, allSystems, doc));
                                }
                                else
                                {
                                    // SLD-02: detect protection devices
                                    bool isProtection = IsProtectionDevice(child);
                                    var leaf = new SLDNode
                                    {
                                        ElementId = child.Id,
                                        Parent = node,
                                        HierarchyLevel = level + 1,
                                        IsLoad = !isProtection,
                                        IsProtection = isProtection,
                                        RevitElement = child,
                                        Label = child.Name,
                                        ConceptId = SymbolConceptForElement(child),
                                        CircuitRef = sys.CircuitNumber,
                                    };
                                    ReadCircuitData(sys, leaf);
                                    node.Children.Add(leaf);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StingTools.Core.StingLog.Warn($"BuildNode children: {ex.Message}");
                    }
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

                try { node.Poles = circuit.PolesNumber; }
                catch (Exception ex) { StingTools.Core.StingLog.Warn($"Poles: {ex.Message}"); }

                try
                {
                    var loadParam = circuit.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD);
                    if (loadParam != null) node.LoadKW = loadParam.AsDouble() / 1000.0;
                }
                catch (Exception ex) { StingTools.Core.StingLog.Warn($"Load: {ex.Message}"); }

                // SLD-03: read protection rating
                try
                {
                    // Prefer STING shared param
                    string stingRating = ParameterHelpers.GetString(circuit, "ELC_CKT_PROTECTION_RATING_TXT");
                    if (!string.IsNullOrEmpty(stingRating))
                    {
                        node.Rating = stingRating;
                    }
                    else
                    {
                        // Fall back to Revit native circuit frame/trip rating
                        var frameParam = circuit.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_FRAME_PARAM);
                        if (frameParam != null && frameParam.StorageType == StorageType.Double)
                        {
                            double amps = frameParam.AsDouble();
                            if (amps > 0) node.Rating = $"{(int)Math.Round(amps)}";
                        }
                    }
                }
                catch (Exception ex) { StingTools.Core.StingLog.Warn($"Rating: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"ReadCircuitData: {ex.Message}");
            }
        }

        // SLD-02: return true when the element's family name contains a protection keyword
        private static bool IsProtectionDevice(FamilyInstance fi)
        {
            try
            {
                string famName = (fi.Symbol?.FamilyName ?? fi.Name ?? "").ToUpperInvariant();
                foreach (var kw in ProtectionKeywords)
                    if (famName.Contains(kw)) return true;
                // Also check a STING param if present
                string stingType = ParameterHelpers.GetString(fi, "ELC_DEVICE_TYPE_TXT");
                if (!string.IsNullOrEmpty(stingType))
                    foreach (var kw in ProtectionKeywords)
                        if (stingType.ToUpperInvariant().Contains(kw)) return true;
            }
            catch { }
            return false;
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

                // SLD-15: warn when no concept binding found
                if (concept == null)
                    StingTools.Core.StingLog.Warn(
                        $"SLD: no STING_SYMBOL_ID and no concept for " +
                        $"category='{el.Category?.Name}' element='{el.Name}'. " +
                        $"Bind STING_SYMBOL_ID shared parameter to fix this.");

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
