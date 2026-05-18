using StingTools.Core.Validation;
using System;
using Autodesk.Revit.DB;
using StingTools.Standards.HBN;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Validation.Healthcare
{
    /// <summary>HBN-derived adjacency targets — flags when mandatory adjacencies
    /// are violated or forbidden adjacencies appear. Distance heuristic uses room
    /// centroids since door-graph traversal is part of Phase H-10.</summary>
    public class AdjacencyValidator : HealthcareValidatorBase
    {
        public override string Name => "AdjacencyValidator";
        private const string Tag = "AdjacencyValidator";

        // Threshold (m) under which two rooms are considered "directly accessible".
        private const double MaxMandatoryDistanceM = 30.0;
        // Threshold (m) over which forbidden adjacency is satisfied.
        private const double MinForbiddenDistanceM = 30.0;

        public override List<ValidationResult> Validate(Document doc)
        {
            var res = new List<ValidationResult>();
            if (doc == null) return res;

            // Cache-aware: when running inside RunAllHealthcareValidators the
            // RoomsByClass map is pre-computed (one collector pass instead of N).
            var ctx = HealthcareValidatorContext.Active;
            Dictionary<string, List<Element>> byClass;
            if (ctx != null && ctx.Document == doc)
            {
                byClass = ctx.RoomsByClass;
                if (byClass.Count < 2) return res;
            }
            else
            {
                var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType().ToElements()
                    .Where(r => !string.IsNullOrEmpty(GetParam(r, "CLN_ROOM_CLASS_TXT")))
                    .ToList();
                if (rooms.Count < 2) return res;
                byClass = rooms.GroupBy(r => GetParam(r, "CLN_ROOM_CLASS_TXT"))
                               .ToDictionary(g => g.Key, g => g.ToList(),
                                             System.StringComparer.OrdinalIgnoreCase);
            }

            foreach (var kv in HBNStandards.AdjacencyTargets)
            {
                var (a, b) = kv.Key;
                var target = kv.Value;
                if (!byClass.TryGetValue(a, out var aRooms)) continue;
                if (!byClass.TryGetValue(b, out var bRooms)) continue;

                foreach (var ar in aRooms)
                {
                    var ap = (ar.Location as LocationPoint)?.Point;
                    if (ap == null) continue;
                    double minDist = double.MaxValue;
                    foreach (var br in bRooms)
                    {
                        var bp = (br.Location as LocationPoint)?.Point;
                        if (bp == null) continue;
                        var d = (bp - ap).GetLength() * 0.3048; // ft → m
                        if (d < minDist) minDist = d;
                    }
                    if (minDist == double.MaxValue) continue;

                    if (target == 2 && minDist > MaxMandatoryDistanceM)
                    {
                        res.Add(new ValidationResult(ar.Id, ValidationSeverity.Warning,
                            "ADJ.MANDATORY_FAR",
                            $"{a} (room {ar.Name}) is {minDist:F0} m from nearest {b} — HBN expects direct adjacency",
                            Tag));
                    }
                    else if (target == 0 && minDist < MinForbiddenDistanceM)
                    {
                        res.Add(new ValidationResult(ar.Id, ValidationSeverity.Warning,
                            "ADJ.FORBIDDEN_NEAR",
                            $"{a} (room {ar.Name}) is only {minDist:F0} m from {b} — HBN forbids close adjacency",
                            Tag));
                    }
                }
            }
            return res;
        }
    }
}
