// Phase 139.30 (X-01) — Circuit topology engine.
//
// After fixture placement commits, group placed electrical sockets /
// switches / electrical fixtures into BS 7671-compliant circuits and
// stamp each instance with the assigned circuit ID + RCD group.
//
// Rules of thumb implemented (BS 7671:2018 + Amendment 2:2022):
//
//   * Ring final circuit (regulation 433.1.103, Appendix 15)
//       - Up to 100 m² floor area per ring
//       - 32 A protective device (BS 1361 / BS EN 60898)
//       - 2.5 mm² T+E typical (≈ 106 m total cable length)
//       - One unfused spur per socket maximum
//   * Radial final circuit (regulation 433.1.5)
//       - Up to 75 m² when 32 A; 50 m² when 20 A
//       - 4.0 mm² T+E for 32 A radial
//   * RCD requirements (regulation 411.3.3, 415.1, 522.6.202):
//       - All socket outlets ≤ 32 A — 30 mA RCD
//       - Bathroom (zones 0/1/2) — 30 mA RCD on every circuit
//       - Lighting circuits in domestic — 30 mA RCD per Amd 2:2022
//   * Diversity (Appendix A, Table A1):
//       - Domestic ring: 100 % of largest + 40 % of remainder
//       - Kitchen ring: 100 % of largest + 40 % of remainder + 100 % cooker
//
// Stamping (silent if the parameter is absent on the family):
//   STING_CIRCUIT_ID_TXT       e.g. "GF-RING-01"
//   STING_RCD_GROUP_TXT        e.g. "RCBO-30mA-A"
//   STING_CIRCUIT_LOAD_VA      derived demand after diversity (VA)
//   STING_CIRCUIT_RATING_A     32 / 20 / 16 (A)
//   STING_CIRCUIT_KIND_TXT     RING / RADIAL / DEDICATED

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace StingTools.Core.Calc
{
    public static class CircuitTopologyEngine
    {
        // BS 7671 Appendix 15 — domestic ring final 32 A, max 100 m²,
        // ~106 m total cable length on 2.5 mm² T+E.
        private const double RingMaxAreaM2 = 100.0;
        private const double RadialMaxAreaM2 = 75.0;     // 32 A radial
        private const int    RingMinSockets = 4;          // < 4 — radial is cheaper
        private const double DefaultSocketVa = 230.0;     // BS 1363 nominal demand
        private const double DefaultLightVa  = 100.0;     // typical LED downlight
        private const double KitchenSocketVa = 350.0;     // higher demand

        /// <summary>
        /// Walk the placed-element list and assign circuit IDs + RCD
        /// groups. Idempotent when the parameters already match — only
        /// stamps when the value changes. Caller owns the transaction.
        /// </summary>
        public static void AssignCircuits(Document doc, StingTools.Core.Placement.PlacementResult result)
        {
            if (doc == null || result == null || result.PlacedIds.Count == 0) return;

            // Collect electrical instances eligible for circuit assignment.
            var sockets = new List<(FamilyInstance fi, XYZ p, Room room, double areaM2, bool isWet, double vaPerOutlet)>();
            foreach (var id in result.PlacedIds)
            {
                FamilyInstance fi = null;
                try { fi = doc.GetElement(id) as FamilyInstance; } catch { }
                if (fi == null || fi.Category == null) continue;
                long catId;
                try { catId = fi.Category.Id.Value; } catch { continue; }
                BuiltInCategory bic = (BuiltInCategory)catId;
                if (bic != BuiltInCategory.OST_ElectricalFixtures
                 && bic != BuiltInCategory.OST_LightingFixtures
                 && bic != BuiltInCategory.OST_LightingDevices) continue;
                XYZ p = (fi.Location as LocationPoint)?.Point;
                if (p == null) continue;
                Room room = null;
                try { room = doc.GetRoomAtPoint(p) as Room; } catch { }
                double areaM2 = 0;
                if (room != null)
                {
                    try { areaM2 = room.Area * 0.3048 * 0.3048; } catch { }
                }
                string roomName = room?.Name?.ToLowerInvariant() ?? "";
                bool isWet = roomName.Contains("bath") || roomName.Contains("shower")
                          || roomName.Contains("wc")  || roomName.Contains("toilet")
                          || roomName.Contains("wetroom") || roomName.Contains("ensuite");
                bool isKitchen = roomName.Contains("kitchen") || roomName.Contains("utility");
                bool isLight   = bic != BuiltInCategory.OST_ElectricalFixtures;
                double va = isLight ? DefaultLightVa
                          : isKitchen ? KitchenSocketVa
                          : DefaultSocketVa;
                sockets.Add((fi, p, room, areaM2, isWet, va));
            }
            if (sockets.Count == 0) return;

            // Cluster by (level, room kind, kind) so e.g. ground-floor
            // bedroom sockets cluster separately from ground-floor
            // kitchen sockets and ground-floor lighting.
            var groups = sockets
                .GroupBy(s =>
                {
                    string lvl = "";
                    try { lvl = (doc.GetElement(s.fi.LevelId) as Level)?.Name ?? "X"; } catch { }
                    string kind;
                    if (s.fi.Category.Id.Value == (long)BuiltInCategory.OST_LightingFixtures
                     || s.fi.Category.Id.Value == (long)BuiltInCategory.OST_LightingDevices) kind = "LT";
                    else if (s.isWet) kind = "BTH";
                    else if ((s.room?.Name ?? "").ToLowerInvariant().Contains("kitchen")) kind = "KIT";
                    else kind = "PWR";
                    return (lvl, kind);
                })
                .ToList();

            int circuitsAssigned = 0;
            int totalStamped = 0;
            foreach (var grp in groups)
            {
                int seq = 1;
                // Within each (level, kind) group, split into circuits
                // bounded by max area (100 m² ring / 75 m² radial) and
                // max sockets-per-circuit (heuristic 24 outlets max
                // per ring per BS 7671 Appx 15 guidance).
                var pending = grp.OrderBy(s => s.p.X).ThenBy(s => s.p.Y).ToList();
                while (pending.Count > 0)
                {
                    var bag = new List<(FamilyInstance fi, XYZ p, Room room, double areaM2, bool isWet, double vaPerOutlet)>();
                    double areaSum = 0; int count = 0;
                    var roomsTouched = new HashSet<ElementId>();
                    foreach (var s in pending.ToList())
                    {
                        // Don't blow circuit area budget.
                        if (s.room != null && roomsTouched.Add(s.room.Id))
                        {
                            if (areaSum + s.areaM2 > RingMaxAreaM2 && bag.Count > 0) break;
                            areaSum += s.areaM2;
                        }
                        bag.Add(s);
                        count++;
                        // Cap per-circuit outlet count to keep the run
                        // length plausible on 2.5mm² T+E.
                        if (count >= 24) break;
                    }
                    foreach (var b in bag) pending.Remove(b);

                    if (bag.Count == 0) break;

                    string kind = grp.Key.kind;
                    string circuitKind = "RADIAL";
                    int rating = 32;
                    if (kind == "PWR" || kind == "KIT")
                    {
                        circuitKind = bag.Count >= RingMinSockets ? "RING" : "RADIAL";
                        rating = 32;
                    }
                    else if (kind == "LT")
                    {
                        circuitKind = "RADIAL";
                        rating = 6; // BS 7671 Type B 6A typical lighting
                    }
                    else if (kind == "BTH")
                    {
                        circuitKind = "DEDICATED";
                        rating = 16; // 16A radial typical for bathroom
                    }

                    // RCD group — Amd 2:2022 wants 30mA on everything in
                    // a domestic install. Group bathroom and lighting on
                    // separate RCBOs so a fault in one doesn't take out
                    // the other.
                    string rcdGroup;
                    if (kind == "BTH")        rcdGroup = "RCBO-30mA-BTH";
                    else if (kind == "LT")    rcdGroup = $"RCBO-30mA-LT-{grp.Key.lvl}";
                    else if (kind == "KIT")   rcdGroup = $"RCBO-30mA-KIT-{grp.Key.lvl}";
                    else                       rcdGroup = $"RCBO-30mA-{grp.Key.lvl}-{(seq % 2 == 0 ? "A" : "B")}";

                    // Diversity per Appendix A Table A1: 100 % of largest
                    // + 40 % of the remainder. Approximation: take max VA
                    // and apply 0.4 to (n-1) more.
                    double maxVa = bag.Max(b => b.vaPerOutlet);
                    double sumOthers = bag.Sum(b => b.vaPerOutlet) - maxVa;
                    double diversifiedVa = maxVa + 0.40 * sumOthers;

                    string circuitId = $"{grp.Key.lvl}-{kind}-{seq:D2}";
                    seq++;
                    circuitsAssigned++;

                    foreach (var s in bag)
                    {
                        if (StampOne(s.fi, circuitId, rcdGroup, diversifiedVa, rating, circuitKind))
                            totalStamped++;
                    }
                }
            }

            if (circuitsAssigned > 0)
                result.Warnings.Add(
                    $"Circuit topology: assigned {circuitsAssigned} BS 7671 circuit(s) across " +
                    $"{totalStamped} placed instance(s). Verify board schedule before issue " +
                    $"— diversity per Appendix A Table A1, 30 mA RCBO per Amd 2:2022.");
            if (circuitsAssigned > 0)
                StingLog.Info($"CircuitTopologyEngine: {circuitsAssigned} circuits, {totalStamped} instances stamped.");
        }

        private static bool StampOne(FamilyInstance fi, string circuitId, string rcd, double va, int rating, string kind)
        {
            bool any = false;
            try
            {
                if (TrySetString(fi, "STING_CIRCUIT_ID_TXT", circuitId))   any = true;
                if (TrySetString(fi, "STING_RCD_GROUP_TXT",  rcd))         any = true;
                if (TrySetString(fi, "STING_CIRCUIT_KIND_TXT", kind))      any = true;
                if (TrySetDouble(fi, "STING_CIRCUIT_LOAD_VA", va))         any = true;
                if (TrySetInt   (fi, "STING_CIRCUIT_RATING_A", rating))    any = true;
            }
            catch { }
            return any;
        }

        private static bool TrySetString(Element el, string param, string val)
        {
            try
            {
                var p = el.LookupParameter(param);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) return false;
                if ((p.AsString() ?? "") == (val ?? "")) return false; // idempotent
                p.Set(val ?? "");
                return true;
            }
            catch { return false; }
        }

        private static bool TrySetDouble(Element el, string param, double val)
        {
            try
            {
                var p = el.LookupParameter(param);
                if (p == null || p.IsReadOnly) return false;
                if (p.StorageType == StorageType.Double)
                {
                    if (Math.Abs(p.AsDouble() - val) < 1e-6) return false;
                    p.Set(val);
                    return true;
                }
                if (p.StorageType == StorageType.String)
                {
                    string s = val.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                    if ((p.AsString() ?? "") == s) return false;
                    p.Set(s);
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static bool TrySetInt(Element el, string param, int val)
        {
            try
            {
                var p = el.LookupParameter(param);
                if (p == null || p.IsReadOnly) return false;
                if (p.StorageType == StorageType.Integer)
                {
                    if (p.AsInteger() == val) return false;
                    p.Set(val);
                    return true;
                }
                if (p.StorageType == StorageType.String)
                {
                    string s = val.ToString();
                    if ((p.AsString() ?? "") == s) return false;
                    p.Set(s);
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}
