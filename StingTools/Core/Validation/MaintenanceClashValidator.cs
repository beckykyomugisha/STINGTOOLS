// Pack 2 — MaintenanceClashValidator.
//
// Reads the MNT_ENV_{W,D,H}_MM + MNT_ACCESS_DIR_TXT envelope declared by
// maintenance-aware families and flags elements whose withdrawal/service
// volume is obstructed by other BIM geometry.
//
// A maintenance envelope is the swept volume an operator needs free to
// replace filters, swing chiller doors, rod heat exchangers, etc.
// Revit geometry captures the manufactured shell — not the access volume —
// so clash detection without this validator silently routes MEP through
// the envelope operators need to access.
//
// First-pass: projects the envelope from the element's bounding-box face
// indicated by MNT_ACCESS_DIR_TXT (Front/Back/Left/Right/Top) and AABB-
// checks it against every other clearance-bearing or physically-modelled
// element. Uses Document.get_BoundingBox for cheap spatial check — not
// solid-solid Boolean. Good enough for a first-pass audit.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Validation
{
    public class MaintenanceClashValidator
    {
        public string Name => "MaintenanceClashValidator";
        private const string ValidatorTag = "MaintenanceClashValidator";

        public List<ValidationResult> Validate(Document doc)
        {
            var results = new List<ValidationResult>();
            if (doc == null) return results;

            try
            {
                var scanCats = new[]
                {
                    BuiltInCategory.OST_MechanicalEquipment,
                    BuiltInCategory.OST_ElectricalEquipment,
                    BuiltInCategory.OST_PlumbingFixtures,
                    BuiltInCategory.OST_FireProtection,
                    BuiltInCategory.OST_SpecialityEquipment,
                };
                var scanFilter = new ElementMulticategoryFilter(scanCats);
                var envCol = new FilteredElementCollector(doc).WherePasses(scanFilter)
                    .WhereElementIsNotElementType();

                // Phase 1: collect envelope-bearing elements.
                var envelopes = new List<(ElementId id, BoundingBoxXYZ env, string dir)>();
                foreach (var el in envCol)
                {
                    double w = ReadLengthMm(el, "MNT_ENV_W_MM");
                    double d = ReadLengthMm(el, "MNT_ENV_D_MM");
                    double h = ReadLengthMm(el, "MNT_ENV_H_MM");
                    if (w <= 0 && d <= 0 && h <= 0) continue;

                    BoundingBoxXYZ bb = null;
                    try { bb = el.get_BoundingBox(null); } catch { }
                    if (bb == null) continue;

                    string dir = ReadString(el, "MNT_ACCESS_DIR_TXT");
                    BoundingBoxXYZ env = ProjectEnvelope(bb, w, d, h, dir);
                    envelopes.Add((el.Id, env, dir));
                }

                if (envelopes.Count == 0) return results;

                // Phase 2: collect all potential obstructions (broad category sweep).
                // TODO-VERIFY-API: a larger-than-bbox overlap filter (BoundingBoxIntersects)
                // would be faster but requires a per-envelope outline — left for a future
                // performance pass.
                var obstructions = new List<(ElementId id, BoundingBoxXYZ bb)>();
                var obsCats = new[]
                {
                    BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Ceilings,
                    BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_DuctCurves,
                    BuiltInCategory.OST_Conduit, BuiltInCategory.OST_CableTray,
                    BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming,
                };
                var obsFilter = new ElementMulticategoryFilter(obsCats);
                var obsCol = new FilteredElementCollector(doc).WherePasses(obsFilter)
                    .WhereElementIsNotElementType();
                foreach (var el in obsCol)
                {
                    BoundingBoxXYZ b = null;
                    try { b = el.get_BoundingBox(null); } catch { }
                    if (b != null) obstructions.Add((el.Id, b));
                }

                int hits = 0;
                foreach (var e in envelopes)
                {
                    foreach (var o in obstructions)
                    {
                        if (o.id == e.id) continue;
                        if (AabbOverlap(e.env, o.bb))
                        {
                            hits++;
                            results.Add(new ValidationResult(
                                e.id, ValidationSeverity.Warning,
                                "MNT.CLASH",
                                $"Maintenance envelope (dir={e.dir}) obstructed by element {o.id.Value}",
                                ValidatorTag));
                            break; // one report per envelope is enough
                        }
                    }
                }

                results.Add(new ValidationResult(
                    ElementId.InvalidElementId, ValidationSeverity.Info,
                    "MNT.SCAN",
                    $"Scanned {envelopes.Count} maintenance envelope(s) — {hits} obstruction(s)",
                    ValidatorTag));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MaintenanceClashValidator: scan failed: {ex.Message}");
            }
            return results;
        }

        /// <summary>
        /// Project an envelope from one face of the element's bounding box.
        /// dir interpretation: FRONT/BACK → ±Y, LEFT/RIGHT → ±X, TOP/BOTTOM → ±Z.
        /// Empty / unrecognised direction extrudes from the centre axially.
        /// </summary>
        private static BoundingBoxXYZ ProjectEnvelope(BoundingBoxXYZ bb, double wMm, double dMm, double hMm, string dir)
        {
            double wFt = MmToFeet(Math.Max(wMm, 0));
            double dFt = MmToFeet(Math.Max(dMm, 0));
            double hFt = MmToFeet(Math.Max(hMm, 0));
            string D = (dir ?? "").Trim().ToUpperInvariant();

            XYZ c = 0.5 * (bb.Min + bb.Max);

            // Default: envelope covers the whole access volume extending "front"
            // (positive Y) of the element's face. Authors who supply a direction
            // override it.
            double minX = c.X - wFt * 0.5;
            double maxX = c.X + wFt * 0.5;
            double minY = bb.Max.Y;
            double maxY = bb.Max.Y + dFt;
            double minZ = bb.Min.Z;
            double maxZ = bb.Min.Z + hFt;

            switch (D)
            {
                case "BACK":
                    maxY = bb.Min.Y;
                    minY = bb.Min.Y - dFt;
                    break;
                case "LEFT":
                    maxX = bb.Min.X; minX = bb.Min.X - wFt;
                    minY = c.Y - dFt * 0.5; maxY = c.Y + dFt * 0.5;
                    break;
                case "RIGHT":
                    minX = bb.Max.X; maxX = bb.Max.X + wFt;
                    minY = c.Y - dFt * 0.5; maxY = c.Y + dFt * 0.5;
                    break;
                case "TOP":
                    minX = c.X - wFt * 0.5; maxX = c.X + wFt * 0.5;
                    minY = c.Y - dFt * 0.5; maxY = c.Y + dFt * 0.5;
                    minZ = bb.Max.Z; maxZ = bb.Max.Z + hFt;
                    break;
                case "BOTTOM":
                    minX = c.X - wFt * 0.5; maxX = c.X + wFt * 0.5;
                    minY = c.Y - dFt * 0.5; maxY = c.Y + dFt * 0.5;
                    maxZ = bb.Min.Z; minZ = bb.Min.Z - hFt;
                    break;
            }

            var env = new BoundingBoxXYZ
            {
                Min = new XYZ(Math.Min(minX, maxX), Math.Min(minY, maxY), Math.Min(minZ, maxZ)),
                Max = new XYZ(Math.Max(minX, maxX), Math.Max(minY, maxY), Math.Max(minZ, maxZ)),
            };
            return env;
        }

        private static bool AabbOverlap(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            return !(a.Max.X < b.Min.X || a.Min.X > b.Max.X ||
                     a.Max.Y < b.Min.Y || a.Min.Y > b.Max.Y ||
                     a.Max.Z < b.Min.Z || a.Min.Z > b.Max.Z);
        }

        private static double ReadLengthMm(Element el, string paramName)
        {
            try
            {
                Element type = null;
                try { type = el.Document.GetElement(el.GetTypeId()); } catch { }
                double fromType = ReadLengthOnOne(type, paramName);
                if (fromType > 0) return fromType;
                return ReadLengthOnOne(el, paramName);
            }
            catch { return 0; }
        }

        private static double ReadLengthOnOne(Element el, string paramName)
        {
            if (el == null) return 0;
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || !p.HasValue) return 0;
                if (p.StorageType == StorageType.Double) return FeetToMm(p.AsDouble());
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
            }
            catch { }
            return 0;
        }

        private static string ReadString(Element el, string paramName)
        {
            try
            {
                Element type = null;
                try { type = el.Document.GetElement(el.GetTypeId()); } catch { }
                string fromType = type?.LookupParameter(paramName)?.AsString();
                if (!string.IsNullOrEmpty(fromType)) return fromType;
                return el.LookupParameter(paramName)?.AsString() ?? "";
            }
            catch { return ""; }
        }

        private const double MM_PER_FOOT = 304.8;
        private static double MmToFeet(double mm) => mm / MM_PER_FOOT;
        private static double FeetToMm(double ft) => ft * MM_PER_FOOT;
    }
}
