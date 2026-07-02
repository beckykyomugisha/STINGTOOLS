using StingTools.Core.Validation;
using System;
using Autodesk.Revit.DB;
using StingTools.Standards.HBN;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Validation.Healthcare
{
    /// <summary>HBN-derived adjacency targets — flags when mandatory adjacencies
    /// are violated or forbidden adjacencies appear. Targets are loaded live from
    /// HEALTHCARE_ADJACENCY_HBN.csv (HC-DEF-08) and rooms are matched against them
    /// by BOTH canonical room-class code and RoomClassCodes department, so a room
    /// tagged e.g. IMG-CT satisfies a rule keyed on the IMAGING department.
    /// Distance heuristic uses room centroids (door-graph BFS is HC-DEF-04).</summary>
    public class AdjacencyValidator : HealthcareValidatorBase
    {
        public override string Name => "AdjacencyValidator";
        private const string Tag = "AdjacencyValidator";

        // Threshold (m) under which two rooms are considered "directly accessible".
        // Hc.AdjacencyDepth (1..4) maps to ×10 m here as a stand-in until door-graph
        // BFS lands in Phase H-10. Wrapping command sets these from HcOptions.
        public double MaxMandatoryDistanceM { get; set; } = 30.0;
        // Threshold (m) over which forbidden adjacency is satisfied.
        public double MinForbiddenDistanceM { get; set; } = 30.0;

        // Forward-prep for Phase H-10. The wrapping command always sets this
        // from HcOptions.AdjacencyDepth so that when door-graph BFS lands the
        // calling sites need no further changes — only the implementation of
        // Validate() switches over from centroid distance to BFS hop count.
        public int BfsDepth { get; set; } = 3;

        public override List<ValidationResult> Validate(Document doc)
        {
            var res = new List<ValidationResult>();
            if (doc == null) return res;

            // Group clinical rooms under BOTH their canonical room-class code and
            // their RoomClassCodes department, so adjacency keys expressed either
            // way (IMAGING department vs HSDU-P canonical code) both resolve.
            var table = RoomClassCodes.Get(doc);
            var byKey = new Dictionary<string, List<Element>>(System.StringComparer.OrdinalIgnoreCase);
            void AddKey(string key, Element r)
            {
                if (string.IsNullOrEmpty(key)) return;
                if (!byKey.TryGetValue(key, out var list)) { list = new List<Element>(); byKey[key] = list; }
                if (!list.Contains(r)) list.Add(r);
            }
            foreach (var r in GetClinicalRoomsCached(doc))
            {
                var canon = GetRoomClassCached(r);          // canonical code
                if (string.IsNullOrEmpty(canon)) continue;
                AddKey(canon, r);
                AddKey(table.DepartmentOf(canon), r);       // department (deduped inside AddKey)
            }
            if (byKey.Count < 2) return res;

            foreach (var t in AdjacencyTargetsRegistry.Get(doc))
            {
                var a = t.A; var b = t.B;
                var target = t.Target;
                if (!byKey.TryGetValue(a, out var aRooms)) continue;
                if (!byKey.TryGetValue(b, out var bRooms)) continue;

                foreach (var ar in aRooms)
                {
                    var ap = (ar.Location as LocationPoint)?.Point;
                    if (ap == null) continue;
                    double minDist = double.MaxValue;
                    foreach (var br in bRooms)
                    {
                        // A room can sit in both buckets (its canonical code and its
                        // department); never match it against itself.
                        if (ReferenceEquals(ar, br)) continue;
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
