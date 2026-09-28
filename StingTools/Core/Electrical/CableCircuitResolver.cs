// CableCircuitResolver — Revit-bound adapter for CableCircuitIdentity.
//
// Collects every ElectricalSystem in the document ONCE, reduces each to a
// CircuitCandidate, and resolves manifest cables against that snapshot. The
// matching rules themselves live in CableCircuitIdentity (Revit-free, unit
// tested in StingTools.Routing.Tests).

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace StingTools.Core.Electrical
{
    internal sealed class CableCircuitResolver
    {
        private readonly Document _doc;
        private readonly List<CircuitCandidate> _candidates;

        private CableCircuitResolver(Document doc, List<CircuitCandidate> candidates)
        { _doc = doc; _candidates = candidates; }

        public int CircuitCount => _candidates.Count;

        public static CableCircuitResolver Build(Document doc)
        {
            var list = new List<CircuitCandidate>();
            if (doc != null)
            {
                foreach (var sys in new FilteredElementCollector(doc)
                             .OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>())
                {
                    var c = ToCandidate(sys);
                    if (c != null) list.Add(c);
                }
            }
            return new CableCircuitResolver(doc, list);
        }

        public CircuitMatch Resolve(StingCable cable, out ElectricalSystem system)
        {
            system = null;
            if (cable == null) return CableCircuitIdentity.Resolve(null, _candidates);
            var match = CableCircuitIdentity.Resolve(new CableIdentityKey
            {
                CircuitElementId = cable.CircuitElementId,
                CircuitId        = cable.CircuitId ?? "",
                PanelName        = cable.PanelName ?? "",
                SourceUniqueId   = cable.SourceEquipmentId ?? "",
                DestUniqueId     = cable.DestEquipmentId ?? "",
            }, _candidates);
            if (match.Found)
            {
                system = _doc.GetElement(new ElementId(match.SystemId)) as ElectricalSystem;
                if (system == null)
                    return new CircuitMatch { Reason = $"circuit element {match.SystemId} no longer exists" };
            }
            return match;
        }

        public static CircuitCandidate ToCandidate(ElectricalSystem sys)
        {
            if (sys == null) return null;
            var c = new CircuitCandidate { SystemId = sys.Id.Value };
            try { c.CircuitNumber = sys.CircuitNumber ?? ""; }
            catch (Exception ex) { StingLog.Warn($"CableCircuitResolver CircuitNumber {sys.Id.Value}: {ex.Message}"); }
            if (string.IsNullOrEmpty(c.CircuitNumber))
            {
                try { c.CircuitNumber = sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER)?.AsString() ?? ""; }
                catch (Exception ex) { StingLog.Warn($"CableCircuitResolver circuit no. {sys.Id.Value}: {ex.Message}"); }
            }
            try { AddName(c, sys.PanelName); }
            catch (Exception ex) { StingLog.Warn($"CableCircuitResolver PanelName {sys.Id.Value}: {ex.Message}"); }
            try
            {
                var baseEq = sys.BaseEquipment;
                if (baseEq != null)
                {
                    c.BaseEquipmentUniqueId = baseEq.UniqueId ?? "";
                    AddName(c, baseEq.Name);
                    AddName(c, baseEq.get_Parameter(BuiltInParameter.RBS_ELEC_PANEL_NAME)?.AsString());
                }
            }
            catch (Exception ex) { StingLog.Warn($"CableCircuitResolver BaseEquipment {sys.Id.Value}: {ex.Message}"); }
            try
            {
                var members = sys.Elements;
                if (members != null)
                {
                    foreach (Element el in members)
                    {
                        if (el == null) continue;
                        c.MemberElementIds.Add(el.Id.Value);
                        if (!string.IsNullOrEmpty(el.UniqueId)) c.MemberUniqueIds.Add(el.UniqueId);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"CableCircuitResolver members {sys.Id.Value}: {ex.Message}"); }
            return c;
        }

        private static void AddName(CircuitCandidate c, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (!c.PanelNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                c.PanelNames.Add(name);
            // AddCableCommand stored the source name with spaces replaced by
            // underscores in CircuitId; PanelName kept the raw name. Accept both.
            string underscored = name.Replace(' ', '_');
            if (!c.PanelNames.Any(n => string.Equals(n, underscored, StringComparison.OrdinalIgnoreCase)))
                c.PanelNames.Add(underscored);
        }
    }
}
