// ClashMatrix.cs — pair-wise clash rules keyed by filter expressions.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace StingTools.Core.Clash
{
    public sealed class ClashCell
    {
        public string PairId;                  // e.g. "DUCT_SA:STR_BEAM"
        public string FilterA;                 // e.g. "Category=OST_DuctCurves"
        public string FilterB;                 // e.g. "Category=OST_StructuralFraming"
        public string Tolerance;               // "HARD" | "CLEARANCE_50" | "CLEARANCE_100"
        public string Severity;                // "LOW" | "MED" | "HIGH" | "CRITICAL"
        public string OwnerDiscipline;         // "MEP" | "STR" | "ARCH" | ...
        public string StageGate;               // "DD" | "CD" | "IFC" | "PREFAB"
        public string ParamCondition;          // optional: "A.FireRating>0 AND B.SealType=NONE"
        public bool Enabled = true;
    }

    public sealed class ClashMatrix
    {
        public List<ClashCell> Cells { get; set; } = new List<ClashCell>();

        public static ClashMatrix LoadOrDefault(string path)
        {
            if (File.Exists(path))
            {
                try { return JsonConvert.DeserializeObject<ClashMatrix>(File.ReadAllText(path)); }
                catch { }
            }
            return Default();
        }

        public static ClashMatrix Default()
        {
            return new ClashMatrix
            {
                Cells =
                {
                    new ClashCell { PairId = "DUCT:STR_BEAM", FilterA = "Category=OST_DuctCurves", FilterB = "Category=OST_StructuralFraming", Tolerance = "HARD", Severity = "HIGH", OwnerDiscipline = "MEP", StageGate = "DD" },
                    new ClashCell { PairId = "PIPE:STR_BEAM", FilterA = "Category=OST_PipeCurves", FilterB = "Category=OST_StructuralFraming", Tolerance = "HARD", Severity = "HIGH", OwnerDiscipline = "MEP", StageGate = "DD" },
                    new ClashCell { PairId = "PIPE:WALL", FilterA = "Category=OST_PipeCurves", FilterB = "Category=OST_Walls", Tolerance = "HARD", Severity = "MED", OwnerDiscipline = "MEP", StageGate = "CD" },
                    new ClashCell { PairId = "DUCT:WALL", FilterA = "Category=OST_DuctCurves", FilterB = "Category=OST_Walls", Tolerance = "HARD", Severity = "MED", OwnerDiscipline = "MEP", StageGate = "CD" },
                    new ClashCell { PairId = "TRAY:DUCT", FilterA = "Category=OST_CableTray", FilterB = "Category=OST_DuctCurves", Tolerance = "CLEARANCE_100", Severity = "MED", OwnerDiscipline = "MEP", StageGate = "CD" },
                    new ClashCell { PairId = "SPRINKLER:CEILING", FilterA = "Category=OST_Sprinklers", FilterB = "Category=OST_Ceilings", Tolerance = "CLEARANCE_50", Severity = "LOW", OwnerDiscipline = "MEP", StageGate = "CD" },
                    new ClashCell { PairId = "MEP_HANGER:DUCT", FilterA = "Category=OST_MechanicalEquipment", FilterB = "Category=OST_DuctCurves", Tolerance = "HARD", Severity = "MED", OwnerDiscipline = "MEP", StageGate = "CD" },
                    new ClashCell { PairId = "STR_COLUMN:ARCH_WALL", FilterA = "Category=OST_StructuralColumns", FilterB = "Category=OST_Walls", Tolerance = "HARD", Severity = "HIGH", OwnerDiscipline = "STR", StageGate = "DD" },
                }
            };
        }

        public ClashCell Match(ElementFacts a, ElementFacts b)
        {
            foreach (var cell in Cells.Where(c => c.Enabled))
            {
                if (FilterMatches(cell.FilterA, a) && FilterMatches(cell.FilterB, b)) return cell;
                if (FilterMatches(cell.FilterA, b) && FilterMatches(cell.FilterB, a)) return cell;
            }
            return null;
        }

        private static bool FilterMatches(string filter, ElementFacts f)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            foreach (var clause in filter.Split(new[] { " AND " }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = clause.Split(new[] { '=' }, 2);
                if (kv.Length != 2) return false;
                string key = kv[0].Trim();
                string val = kv[1].Trim();
                string actual = f.Get(key);
                if (val.EndsWith("*"))
                {
                    if (!actual.StartsWith(val.Substring(0, val.Length - 1), StringComparison.OrdinalIgnoreCase)) return false;
                }
                else
                {
                    if (!string.Equals(actual, val, StringComparison.OrdinalIgnoreCase)) return false;
                }
            }
            return true;
        }
    }

    public sealed class ElementFacts
    {
        public string Category;
        public string System;
        public string Classification;
        public string Workset;
        public Dictionary<string, string> Params { get; } = new Dictionary<string, string>();

        public string Get(string key)
        {
            switch (key)
            {
                case "Category": return Category ?? "";
                case "System": return System ?? "";
                case "Classification": return Classification ?? "";
                case "Workset": return Workset ?? "";
                default:
                    return Params.TryGetValue(key, out var v) ? v : "";
            }
        }
    }
}
