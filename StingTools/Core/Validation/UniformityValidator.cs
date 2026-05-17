// Phase 139 D4 — BS EN 12464-1 illuminance & uniformity validator.
//
// Simplified zonal-cavity / inverse-square check for placed lighting
// fixtures.  Reads STING_LUMEN_OUTPUT (per fixture) and STING_LUX_TARGET
// (per room) when available, falls back to defaults.  Reports per-room
// pass/fail vs target Em and minimum uniformity Uo = E_min / E_avg ≥ 0.60.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace StingTools.Core.Validation
{
    public class UniformityValidator
    {
        public string Name => "UniformityValidator";
        private const string ValidatorTag = "UniformityValidator";

        private const double DefaultLumenOutput   = 3000.0;
        private const double DefaultTargetLux     = 300.0;
        private const double DefaultMfMaintenance = 0.80;
        private const double MinUniformity        = 0.60;
        private const double FtPerMm = 1.0 / 304.8;

        public List<ValidationResult> Validate(Document doc)
        {
            var results = new List<ValidationResult>();
            if (doc == null) return results;

            try
            {
                // 1. Collect rooms.
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r != null && r.Area > 0)
                    .ToList();

                // 2. Collect lighting fixtures keyed by room id.
                var fixturesByRoom = new Dictionary<long, List<FamilyInstance>>();
                foreach (var fi in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_LightingFixtures)
                    .WhereElementIsNotElementType()
                    .OfType<FamilyInstance>())
                {
                    var pt = (fi.Location as LocationPoint)?.Point;
                    if (pt == null) continue;
                    Room hostRoom = FindRoomContaining(rooms, pt);
                    if (hostRoom == null) continue;
                    long key = hostRoom.Id.Value;
                    if (!fixturesByRoom.ContainsKey(key)) fixturesByRoom[key] = new List<FamilyInstance>();
                    fixturesByRoom[key].Add(fi);
                }

                // 3. Per-room: compute Em + Uo.
                foreach (var room in rooms)
                {
                    if (!fixturesByRoom.TryGetValue(room.Id.Value, out var fixtures) || fixtures.Count == 0)
                        continue;

                    double targetLux = ReadDouble(room, "STING_LUX_TARGET", DefaultTargetLux);
                    double mf        = ReadDouble(room, "STING_MAINTENANCE_FACTOR", DefaultMfMaintenance);

                    var (em, uo) = ComputePointByPoint(room, fixtures, mf);

                    if (em < targetLux * 0.95)
                    {
                        results.Add(new ValidationResult(
                            room.Id, ValidationSeverity.Warning,
                            "ILLUMINANCE_LOW",
                            $"Room '{room.Name}' Ē={em:F0}lx < target {targetLux:F0}lx (BS EN 12464-1)",
                            ValidatorTag));
                    }

                    if (uo < MinUniformity)
                    {
                        results.Add(new ValidationResult(
                            room.Id, ValidationSeverity.Warning,
                            "UNIFORMITY_LOW",
                            $"Room '{room.Name}' Uo={uo:F2} < 0.60 (BS EN 12464-1)",
                            ValidatorTag));
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"UniformityValidator: scan failed: {ex.Message}");
            }
            return results;
        }

        private static Room FindRoomContaining(List<Room> rooms, XYZ pt)
        {
            foreach (var r in rooms)
            {
                try { if (r.IsPointInRoom(pt)) return r; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            }
            return null;
        }

        private (double em, double uo) ComputePointByPoint(Room room,
            List<FamilyInstance> fixtures, double mf)
        {
            try
            {
                var bb = room.get_BoundingBox(null);
                if (bb == null || fixtures == null || fixtures.Count == 0) return (0.0, 0.0);
                // 3×3 sample grid on the floor at room.Level elevation.
                double zSample = room.Level?.Elevation ?? bb.Min.Z;
                double xMin = bb.Min.X, yMin = bb.Min.Y;
                double xMax = bb.Max.X, yMax = bb.Max.Y;
                var samples = new List<XYZ>();
                for (int i = 1; i <= 3; i++)
                for (int j = 1; j <= 3; j++)
                {
                    double sx = xMin + (xMax - xMin) * i / 4.0;
                    double sy = yMin + (yMax - yMin) * j / 4.0;
                    samples.Add(new XYZ(sx, sy, zSample));
                }
                double total = 0.0;
                double minE = double.MaxValue, maxE = double.MinValue;
                foreach (var s in samples)
                {
                    double e = 0.0;
                    foreach (var fi in fixtures)
                    {
                        var pt = (fi.Location as LocationPoint)?.Point;
                        if (pt == null) continue;
                        double lumen = ReadDouble(fi, "STING_LUMEN_OUTPUT", DefaultLumenOutput);
                        // Rough downlight intensity I = lumen / pi (lumens to candela rough)
                        double iCandela = lumen / Math.PI;
                        XYZ d = s - pt;
                        double dist = d.GetLength();
                        if (dist < 0.1) dist = 0.1;
                        // E = I cos(θ) / d²; cos(θ) ≈ |dz| / d for downlights
                        double cosTheta = Math.Abs(d.Z) / dist;
                        if (cosTheta < 0.05) cosTheta = 0.05;
                        // Convert to lux (intensity in cd, distance in m)
                        double dM = dist * 0.3048;
                        e += (iCandela * cosTheta) / (dM * dM);
                    }
                    e *= mf; // maintenance factor
                    total += e;
                    if (e < minE) minE = e;
                    if (e > maxE) maxE = e;
                }
                double avg = total / samples.Count;
                double uo  = avg > 0 ? minE / avg : 0.0;
                return (avg, uo);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"UniformityValidator point-by-point: {ex.Message}");
                return (0.0, 0.0);
            }
        }

        private static double ReadDouble(Element el, string paramName, double fallback)
        {
            try
            {
                var p = el?.LookupParameter(paramName);
                if (p == null || !p.HasValue) return fallback;
                if (p.StorageType == StorageType.Double)  return p.AsDouble();
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                if (p.StorageType == StorageType.String && double.TryParse(p.AsString(), out var d)) return d;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return fallback;
        }
    }
}
