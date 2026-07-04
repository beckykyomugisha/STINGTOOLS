// StingTools — Matrix circuiting (M5): connect placed fixtures into power circuits.
//
// From the matrix ledger, gather the placed electrical devices (per room / per column
// / fill-to-breaker), then create Revit power circuits assigned to a chosen panel by
// REUSING StingTools.Core.Mep.MepCircuitBuilder.CreateFromSelection — no fork. That
// method auto-detects the panel from its selection collection, so we add the panel's
// ElementId to each device group. The caller passes an OPEN transaction owner via this
// engine's own transaction (MepCircuitBuilder requires an open tx).
//
// MEP (pipe/duct) connection was assessed as a follow-up: unlike electrical, MEP
// connections need matched connector directions + a routing solve to be valid, which
// is out of scope here — this engine is electrical-only and says so.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using StingTools.Core.Mep;

namespace StingTools.Core.Placement.Matrix
{
    public enum MatrixCircuitGrouping { PerRoom, PerColumn, FillToBreaker }

    public sealed class MatrixCircuitResult
    {
        public int Circuits;
        public int DevicesCircuited;
        public int Groups;
        public List<string> Messages = new List<string>();
        public List<string> Warnings = new List<string>();
    }

    public static class MatrixCircuiting
    {
        /// <summary>List candidate panels (electrical equipment) for the picker.</summary>
        public static List<FamilyInstance> Panels(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                    .WhereElementIsNotElementType()
                    .OfType<FamilyInstance>()
                    .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex) { StingLog.Warn($"MatrixCircuiting.Panels: {ex.Message}"); return new List<FamilyInstance>(); }
        }

        /// <summary>Circuit the matrix-placed electrical devices to <paramref name="panelId"/>.
        /// Grouping controls how devices are binned into circuits. maxLoadVaPerCircuit gates the
        /// FillToBreaker mode (e.g. 32A * 230V * 0.8). Opens its own transaction.</summary>
        public static MatrixCircuitResult Circuit(
            Document doc, MatrixDocument matrix, MatrixScanResult scan, ElementId panelId,
            MatrixCircuitGrouping grouping, double maxLoadVaPerCircuit)
        {
            var res = new MatrixCircuitResult();
            if (doc == null || matrix == null || scan == null) { res.Warnings.Add("No document / matrix."); return res; }
            var panel = doc.GetElement(panelId) as FamilyInstance;
            if (panel == null) { res.Warnings.Add("No panel selected."); return res; }

            // Electrical columns only (those with a non-zero indicative VA / power load type).
            var elecCols = (matrix.Columns ?? new List<MatrixColumnDef>())
                .Where(c => IsElectrical(doc, c)).Select(c => c.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (elecCols.Count == 0) { res.Warnings.Add("No electrical columns in the matrix."); return res; }

            // Resolve ledger uids -> live device elements, tagged with (room, column, VA).
            var devices = new List<(ElementId id, string roomUid, string colId, double va)>();
            foreach (var kvRoom in matrix.Placements ?? new Dictionary<string, Dictionary<string, List<string>>>())
            {
                foreach (var kvCol in kvRoom.Value ?? new Dictionary<string, List<string>>())
                {
                    if (!elecCols.Contains(kvCol.Key)) continue;
                    var col = matrix.Column(kvCol.Key);
                    double per = col != null ? (col.LoadVaOverride > 0 ? col.LoadVaOverride : MatrixDefaults.LoadVa(doc, col.Category)) : 0;
                    foreach (var uid in kvCol.Value ?? new List<string>())
                    {
                        Element el = null; try { el = doc.GetElement(uid); } catch { }
                        if (el is FamilyInstance fi && HasElectricalConnector(fi))
                            devices.Add((el.Id, kvRoom.Key, kvCol.Key, per));
                    }
                }
            }
            if (devices.Count == 0) { res.Warnings.Add("No circuitable devices found (no electrical connectors on the placed seeds)."); return res; }

            // Build groups.
            var groups = BuildGroups(devices, grouping, maxLoadVaPerCircuit);
            res.Groups = groups.Count;

            using (var t = new Transaction(doc, "STING Matrix Circuit"))
            {
                t.Start();
                foreach (var g in groups)
                {
                    if (g.Count == 0) continue;
                    var selection = new List<ElementId>(g) { panelId };   // panel auto-detected by the builder
                    var mcr = new MepCircuitResult();
                    var sys = MepCircuitBuilder.CreateFromSelection(doc, selection, mcr);
                    if (sys != null) { res.Circuits++; res.DevicesCircuited += g.Count; }
                    if (mcr.Warnings.Count > 0) res.Warnings.AddRange(mcr.Warnings);
                }
                t.Commit();
            }

            res.Messages.Add($"Created {res.Circuits} circuit(s) for {res.DevicesCircuited} device(s) on panel '{panel.Name}' ({grouping}).");
            return res;
        }

        private static List<List<ElementId>> BuildGroups(
            List<(ElementId id, string roomUid, string colId, double va)> devices,
            MatrixCircuitGrouping grouping, double maxLoadVa)
        {
            var groups = new List<List<ElementId>>();
            switch (grouping)
            {
                case MatrixCircuitGrouping.PerRoom:
                    foreach (var g in devices.GroupBy(d => d.roomUid))
                        groups.Add(g.Select(d => d.id).ToList());
                    break;
                case MatrixCircuitGrouping.PerColumn:
                    foreach (var g in devices.GroupBy(d => d.colId))
                        groups.Add(g.Select(d => d.id).ToList());
                    break;
                case MatrixCircuitGrouping.FillToBreaker:
                    double cap = maxLoadVa > 0 ? maxLoadVa : 5800.0;   // ~32A @ 230V @ 80%
                    var cur = new List<ElementId>(); double acc = 0;
                    foreach (var d in devices.OrderBy(d => d.roomUid))
                    {
                        double va = d.va > 0 ? d.va : 100;
                        if (cur.Count > 0 && acc + va > cap) { groups.Add(cur); cur = new List<ElementId>(); acc = 0; }
                        cur.Add(d.id); acc += va;
                    }
                    if (cur.Count > 0) groups.Add(cur);
                    break;
            }
            return groups;
        }

        private static bool IsElectrical(Document doc, MatrixColumnDef c)
        {
            if (c == null) return false;
            string lt = MatrixDefaults.LoadType(doc, c.Category);
            return lt == "power" || lt == "lighting";
        }

        private static bool HasElectricalConnector(FamilyInstance fi)
        {
            try
            {
                var cm = fi?.MEPModel?.ConnectorManager;
                if (cm == null) return false;
                foreach (Connector cn in cm.Connectors)
                    if (cn.Domain == Domain.DomainElectrical) return true;
            }
            catch { }
            return false;
        }
    }
}
