// StingTools — SLD circuit traverser (Phase 175 + Phase 179 enhancements)
//
// Walks the project's electrical hierarchy starting from root panels.
//
// Phase 179 changes:
//  - BuildHierarchyAll() returns every root (multi-feed / multi-building support).
//  - ReadCircuitData() now reads ELC_CIRCUIT_* STING shared parameters first,
//    falling back to Revit native params — fixing the {rating} always-blank bug.
//  - ReadCircuitData() is now also called for leaf circuit-breaker/load nodes.
//  - SLDNode gains CsaMm2, VdPct, FaultKa fields for BS 7671 annotation.
//  - SymbolConceptForElement() infers MCB/MCCB/RCBO/RCD/isolator concept from
//    the family name when STING_SYMBOL_ID is not stamped.

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
        // Phase 179 — BS 7671 / IEC 60364 engineering data fields.
        public string CsaMm2 { get; set; }
        public double VdPct { get; set; }
        public string FaultKa { get; set; }
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
        /// <summary>
        /// Returns the single root panel (backwards-compat overload).
        /// Projects with multiple feeds — use <see cref="BuildHierarchyAll"/>.
        /// </summary>
        public static SLDNode BuildHierarchy(Document doc)
        {
            return BuildHierarchyAll(doc)?.FirstOrDefault();
        }

        /// <summary>
        /// Returns every independent distribution root in the project.
        /// A root is any electrical equipment element that is not listed
        /// as a load on any ElectricalSystem. Typical multi-root causes:
        /// multi-building, utility + generator feeds, separate LV networks.
        /// </summary>
        public static List<SLDNode> BuildHierarchyAll(Document doc)
        {
            if (doc == null) return null;
            try
            {
                var equipment = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .ToList();

                var allSystems = new FilteredElementCollector(doc)
                    .OfClass(typeof(ElectricalSystem))
                    .Cast<ElectricalSystem>()
                    .ToList();

                // Build set of element IDs that appear as loads on any system.
                var loadIds = new HashSet<long>();
                foreach (var sys in allSystems)
                {
                    try
                    {
                        foreach (Element el in sys.Elements)
                            loadIds.Add(el.Id.Value);
                    }
                    catch (Exception ex) { StingLog.Warn($"Traverser scan systems: {ex.Message}"); }
                }

                // Every equipment element NOT in loadIds is a root.
                var roots = equipment
                    .Where(e => !loadIds.Contains(e.Id.Value))
                    .Select(r => BuildNode(r, null, 0, allSystems, doc))
                    .ToList();

                return roots.Count > 0 ? roots : null;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"BuildHierarchyAll: {ex.Message}");
                return null;
            }
        }

        private static SLDNode BuildNode(FamilyInstance fi, SLDNode parent, int level,
            List<ElectricalSystem> allSystems, Document doc)
        {
            var node = new SLDNode
            {
                ElementId     = fi.Id,
                Parent        = parent,
                HierarchyLevel = level,
                IsPanel       = true,
                RevitElement  = fi,
                Label         = fi.Name,
                ConceptId     = SymbolConceptForElement(fi),
            };

            // Read element-level STING params for the panel itself.
            ReadElementParams(fi, node);

            try
            {
                var downstream = allSystems.Where(s =>
                {
                    try { return s.BaseEquipment != null && s.BaseEquipment.Id == fi.Id; }
                    catch { return false; }
                }).ToList();

                foreach (var sys in downstream)
                {
                    // Populate rating/poles/load on the parent panel node from
                    // its downstream circuits (feeds the panel's own label).
                    ReadCircuitData(sys, node);
                    try
                    {
                        foreach (Element el in sys.Elements)
                        {
                            if (el is FamilyInstance child)
                            {
                                bool isSubPanel = child.Category?.Id?.Value
                                    == (long)BuiltInCategory.OST_ElectricalEquipment;
                                if (isSubPanel)
                                {
                                    node.Children.Add(BuildNode(child, node, level + 1, allSystems, doc));
                                }
                                else
                                {
                                    var leaf = new SLDNode
                                    {
                                        ElementId      = child.Id,
                                        Parent         = node,
                                        HierarchyLevel = level + 1,
                                        IsLoad         = true,
                                        RevitElement   = child,
                                        Label          = child.Name,
                                        ConceptId      = SymbolConceptForElement(child),
                                        CircuitRef     = sys.CircuitNumber,
                                    };
                                    // Phase 179: read ELC_CIRCUIT_* + engineering data for leaves.
                                    ReadCircuitData(sys, leaf);
                                    node.Children.Add(leaf);
                                }
                            }
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"BuildNode children: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"BuildNode {fi.Name}: {ex.Message}");
            }
            return node;
        }

        /// <summary>
        /// Reads circuit data from an ElectricalSystem into a node.
        /// Phase 179: STING shared params (ELC_CIRCUIT_*) take priority
        /// over Revit native params; falls back gracefully.
        /// </summary>
        public static void ReadCircuitData(ElectricalSystem circuit, SLDNode node)
        {
            if (circuit == null) return;
            try
            {
                // Circuit reference — STING param wins over native.
                string stingRef = GetParamString(node.RevitElement, ParamRegistry.CIRCUIT_REF);
                node.CircuitRef = !string.IsNullOrEmpty(stingRef)
                    ? stingRef
                    : (circuit.CircuitNumber ?? node.CircuitRef);

                // Poles — STING param wins over native.
                int stingPoles = GetParamInt(node.RevitElement, ParamRegistry.CIRCUIT_POLES);
                if (stingPoles > 0)
                    node.Poles = stingPoles;
                else
                {
                    try { node.Poles = circuit.PolesNumber; }
                    catch (Exception ex) { StingLog.Warn($"Poles: {ex.Message}"); }
                }

                // Rating — STING param wins; fall back to Revit's RBS_ELEC_CIRCUIT_RATING.
                string stingRating = GetParamString(node.RevitElement, ParamRegistry.CIRCUIT_RATING);
                if (!string.IsNullOrEmpty(stingRating))
                {
                    node.Rating = stingRating;
                }
                else
                {
                    try
                    {
                        var ratingParam = circuit.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_RATING);
                        if (ratingParam != null)
                        {
                            // RBS_ELEC_CIRCUIT_RATING is stored as Double (amperes) in internal units.
                            double a = ratingParam.AsDouble();
                            if (a > 0) node.Rating = $"{(int)Math.Round(a)}A";
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Rating: {ex.Message}"); }
                }

                // Label override.
                string stingLabel = GetParamString(node.RevitElement, ParamRegistry.CIRCUIT_LABEL);
                if (!string.IsNullOrEmpty(stingLabel)) node.Label = stingLabel;

                // Load (kW).
                try
                {
                    var loadParam = circuit.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD);
                    if (loadParam != null) node.LoadKW = loadParam.AsDouble() / 1000.0;
                }
                catch (Exception ex) { StingLog.Warn($"Load: {ex.Message}"); }

                // Phase 179 — Engineering annotation fields.
                ReadElementParams(node.RevitElement, node);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ReadCircuitData: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads element-level STING engineering params (CSA, VD, fault level)
        /// directly from the FamilyInstance shared parameters.
        /// </summary>
        private static void ReadElementParams(FamilyInstance fi, SLDNode node)
        {
            if (fi == null) return;
            try
            {
                string csa = GetParamString(fi, ParamRegistry.ELC_FEEDER_CSA);
                if (!string.IsNullOrEmpty(csa)) node.CsaMm2 = csa;

                string fault = GetParamString(fi, ParamRegistry.ELC_PNL_FAULT_KA);
                if (!string.IsNullOrEmpty(fault)) node.FaultKa = fault;

                string vdStr = GetParamString(fi, ParamRegistry.ELC_CKT_VD_PCT);
                if (!string.IsNullOrEmpty(vdStr) && double.TryParse(vdStr,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double vd))
                    node.VdPct = vd;
            }
            catch (Exception ex) { StingLog.Warn($"ReadElementParams: {ex.Message}"); }
        }

        // ── Symbol concept resolution ────────────────────────────────────────

        private static string SymbolConceptForElement(Element el)
        {
            try
            {
                // 1. Explicit STING_SYMBOL_ID stamp wins.
                var explicitId = el.LookupParameter("STING_SYMBOL_ID")?.AsString();
                if (!string.IsNullOrEmpty(explicitId)) return explicitId;

                // 2. Infer from family name and rating.
                string familyName = (el as FamilyInstance)?.Symbol?.Family?.Name ?? "";
                string rating = GetParamString(el, ParamRegistry.CIRCUIT_RATING);
                string inferred = InferConceptFromFamily(familyName, rating);
                if (!string.IsNullOrEmpty(inferred)) return inferred;

                // 3. Category fallback.
                var concept = Symbols.SymbolConceptRegistry
                    .GetConceptsForCategory(el.Category?.Name)
                    .FirstOrDefault();
                return concept?.ConceptId;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SymbolConceptForElement: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Keyword-based family-name → concept mapper.  Checks longest/most-
        /// specific tokens first so "RCBO" wins over "RCD" wins over "MCB".
        /// A frame-size heuristic (≥125 A → MCCB) handles generic family names.
        /// </summary>
        private static string InferConceptFromFamily(string familyName, string rating)
        {
            string u = (familyName ?? "").ToUpperInvariant();

            if (u.Contains("MCCB") || u.Contains("MOULDED") || u.Contains("MOLDED"))
                return "SLD_MCCB";
            if (u.Contains("RCBO"))
                return "SLD_RCBO_COMPOUND";
            if (u.Contains("RCCB") || (u.Contains("RCD") && !u.Contains("RCBO")))
                return "SLD_RCD";
            if (u.Contains("MCB"))
                return "SLD_MCB";
            if (u.Contains("STAR") && u.Contains("DELTA"))
                return "SLD_STAR_DELTA_STARTER";
            if (u.Contains("DOL") || (u.Contains("STARTER") && !u.Contains("STAR")))
                return "SLD_DOL_STARTER";
            if (u.Contains("ISOLAT") || u.Contains("SWITCH-FUSE") || u.Contains("SWITCHFUSE"))
                return "SLD_ISOLATOR";
            if (u.Contains("FUSE") && !u.Contains("SWITCH"))
                return "SLD_FUSE";

            // Frame-size heuristic: rated ≥125 A → assume MCCB.
            if (!string.IsNullOrEmpty(rating))
            {
                string numStr = rating.Replace("A", "").Replace("a", "").Trim();
                if (double.TryParse(numStr, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double a) && a >= 125)
                    return "SLD_MCCB";
            }

            return null;
        }

        // ── Param helpers ────────────────────────────────────────────────────

        private static string GetParamString(Element el, string paramName)
        {
            try { return el?.LookupParameter(paramName)?.AsString(); }
            catch { return null; }
        }

        private static int GetParamInt(Element el, string paramName)
        {
            try
            {
                var p = el?.LookupParameter(paramName);
                if (p == null) return 0;
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                if (p.StorageType == StorageType.Double)  return (int)p.AsDouble();
                if (p.StorageType == StorageType.String)
                    return int.TryParse(p.AsString(), out int v) ? v : 0;
                return 0;
            }
            catch { return 0; }
        }
    }
}
