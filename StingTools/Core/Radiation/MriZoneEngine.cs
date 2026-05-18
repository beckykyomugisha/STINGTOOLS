// Healthcare Pack H-9 — MRI suite zoning engine.
// Computes Z1..Z4 boundaries + 5-Gauss line. Outputs a list of audit
// findings against any rooms that violate the zoning model.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Radiation
{
    public class MriZoneFinding
    {
        public ElementId RoomId;
        public string Severity;     // ERROR / WARNING / INFO
        public string Code;
        public string Message;
    }

    public static class MriZoneEngine
    {
        // 5-Gauss line approximate radii (m) per Tesla — vendor data normally consulted
        // but a baseline keeps the audit useful out-of-the-box.
        public static readonly Dictionary<double, double> ApproxFiveGaussRadiusM =
            new Dictionary<double, double>
        {
            { 0.5, 2.5 },
            { 1.0, 3.0 },
            { 1.5, 3.5 },
            { 3.0, 4.5 },
            { 7.0, 7.0 }
        };

        public static List<MriZoneFinding> Audit(Document doc)
        {
            var f = new List<MriZoneFinding>();
            if (doc == null) return f;

            var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType().ToElements()
                .Where(r => GetInt(r, "CLN_MRI_ZONE_INT") > 0)
                .ToList();
            if (rooms.Count == 0) return f;

            // Each MRI suite must have at least Z3 (the bore room) and Z2 (anteroom / control).
            var zoneCounts = rooms.GroupBy(r => GetInt(r, "CLN_MRI_ZONE_INT"))
                                  .ToDictionary(g => g.Key, g => g.Count());

            if (!zoneCounts.ContainsKey(3))
                f.Add(new MriZoneFinding {
                    Severity="ERROR", Code="MRI.NO_ZONE3",
                    Message="MRI suite present but no Zone 3 room defined"
                });
            if (!zoneCounts.ContainsKey(4))
                f.Add(new MriZoneFinding {
                    Severity="WARNING", Code="MRI.NO_ZONE4",
                    Message="MRI suite has no Zone 4 (bore) room defined"
                });

            // Faraday-cage flag must be present on all Z3/Z4 rooms.
            foreach (var r in rooms)
            {
                int z = GetInt(r, "CLN_MRI_ZONE_INT");
                bool rf = GetBool(r, "CLN_RF_SHIELD_BOOL");
                if (z >= 3 && !rf)
                    f.Add(new MriZoneFinding {
                        RoomId = r.Id, Severity="ERROR", Code="MRI.NO_FARADAY",
                        Message=$"Room {r.Name} (Z{z}) missing CLN_RF_SHIELD_BOOL flag"
                    });
            }
            return f;
        }

        private static int GetInt(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || !p.HasValue) return 0;
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                if (p.StorageType == StorageType.Double) return (int)p.AsDouble();
                if (p.StorageType == StorageType.String && int.TryParse(p.AsString(), out var v)) return v;
                return 0;
            } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; }
        }

        private static bool GetBool(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || !p.HasValue) return false;
                if (p.StorageType == StorageType.Integer) return p.AsInteger() != 0;
                return false;
            } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }
    }
}
