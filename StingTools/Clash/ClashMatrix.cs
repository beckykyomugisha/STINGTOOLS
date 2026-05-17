using StingTools.Core;
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
        // C6: Auto-export critical/high clashes to BCF after a run finishes.
        // Defaulted to false so existing matrix files round-trip without
        // changing behaviour. When true, ClashRunCommand calls
        // ClashBcfExportCommand.ExportToBcf() inline and logs the path.
        public bool AutoBcfOnCritical { get; set; } = false;
        // B3: Per-project tick interval for the headless scheduler (minutes).
        // Falls back to ClashScheduler's hardcoded default when 0 / unset.
        public int SchedulerIntervalMinutes { get; set; } = 0;

        public static ClashMatrix LoadOrDefault(string path)
        {
            if (File.Exists(path))
            {
                try { return JsonConvert.DeserializeObject<ClashMatrix>(File.ReadAllText(path)); }
                // H9: Previously bare — corrupt or edited-wrong user matrix
                // silently reverted to Default, surprising anyone who edited
                // the JSON. Log so they see "your custom matrix didn't load".
                catch (Exception ex)
                {
                    StingTools.Core.StingLog.Warn($"ClashMatrix.LoadOrDefault({path}) failed: {ex.Message}. Using built-in default.");
                }
            }
            return Default();
        }

        public static ClashMatrix Default()
        {
            // rec-18: 40-cell default matrix covering MEP, MEP-vs-structure,
            // MEP-vs-arch, structure-vs-arch, fire protection, comms/IT, and
            // equipment access. Projects can override via
            // data/clash/default_clash_matrix.json.
            return new ClashMatrix
            {
                Cells =
                {
                    // ── MEP ↔ Structural ────────────────────────────────────
                    new ClashCell { PairId = "DUCT:STR_BEAM",             FilterA = "Category=OST_DuctCurves",             FilterB = "Category=OST_StructuralFraming",     Tolerance = "HARD",           Severity = "HIGH",     OwnerDiscipline = "MEP", StageGate = "DD" },
                    new ClashCell { PairId = "PIPE:STR_BEAM",             FilterA = "Category=OST_PipeCurves",             FilterB = "Category=OST_StructuralFraming",     Tolerance = "HARD",           Severity = "HIGH",     OwnerDiscipline = "MEP", StageGate = "DD" },
                    new ClashCell { PairId = "TRAY:STR_BEAM",             FilterA = "Category=OST_CableTray",              FilterB = "Category=OST_StructuralFraming",     Tolerance = "HARD",           Severity = "MED",      OwnerDiscipline = "ELE", StageGate = "DD" },
                    new ClashCell { PairId = "CONDUIT:STR_BEAM",          FilterA = "Category=OST_Conduit",                FilterB = "Category=OST_StructuralFraming",     Tolerance = "HARD",           Severity = "MED",      OwnerDiscipline = "ELE", StageGate = "DD" },
                    new ClashCell { PairId = "DUCT:STR_COLUMN",           FilterA = "Category=OST_DuctCurves",             FilterB = "Category=OST_StructuralColumns",     Tolerance = "HARD",           Severity = "HIGH",     OwnerDiscipline = "MEP", StageGate = "DD" },
                    new ClashCell { PairId = "PIPE:STR_COLUMN",           FilterA = "Category=OST_PipeCurves",             FilterB = "Category=OST_StructuralColumns",     Tolerance = "HARD",           Severity = "HIGH",     OwnerDiscipline = "MEP", StageGate = "DD" },
                    new ClashCell { PairId = "DUCT:STR_FLOOR",            FilterA = "Category=OST_DuctCurves",             FilterB = "Category=OST_Floors",                Tolerance = "HARD",           Severity = "HIGH",     OwnerDiscipline = "MEP", StageGate = "DD" },
                    new ClashCell { PairId = "PIPE:STR_FLOOR",            FilterA = "Category=OST_PipeCurves",             FilterB = "Category=OST_Floors",                Tolerance = "HARD",           Severity = "HIGH",     OwnerDiscipline = "MEP", StageGate = "DD" },
                    new ClashCell { PairId = "STR_FOUNDATION:WALL",       FilterA = "Category=OST_StructuralFoundation",   FilterB = "Category=OST_Walls",                 Tolerance = "HARD",           Severity = "HIGH",     OwnerDiscipline = "STR", StageGate = "DD" },

                    // ── MEP ↔ Architectural ────────────────────────────────
                    new ClashCell { PairId = "PIPE:WALL",                 FilterA = "Category=OST_PipeCurves",             FilterB = "Category=OST_Walls",                 Tolerance = "HARD",           Severity = "MED",      OwnerDiscipline = "MEP", StageGate = "CD" },
                    new ClashCell { PairId = "DUCT:WALL",                 FilterA = "Category=OST_DuctCurves",             FilterB = "Category=OST_Walls",                 Tolerance = "HARD",           Severity = "MED",      OwnerDiscipline = "MEP", StageGate = "CD" },
                    new ClashCell { PairId = "TRAY:WALL",                 FilterA = "Category=OST_CableTray",              FilterB = "Category=OST_Walls",                 Tolerance = "HARD",           Severity = "MED",      OwnerDiscipline = "ELE", StageGate = "CD" },
                    new ClashCell { PairId = "PIPE:FLOOR",                FilterA = "Category=OST_PipeCurves",             FilterB = "Category=OST_Floors",                Tolerance = "HARD",           Severity = "MED",      OwnerDiscipline = "MEP", StageGate = "CD" },
                    new ClashCell { PairId = "DUCT:FLOOR",                FilterA = "Category=OST_DuctCurves",             FilterB = "Category=OST_Floors",                Tolerance = "HARD",           Severity = "MED",      OwnerDiscipline = "MEP", StageGate = "CD" },
                    new ClashCell { PairId = "DUCT:CEILING",              FilterA = "Category=OST_DuctCurves",             FilterB = "Category=OST_Ceilings",              Tolerance = "HARD",           Severity = "LOW",      OwnerDiscipline = "MEP", StageGate = "CD" },
                    new ClashCell { PairId = "PIPE:CEILING",              FilterA = "Category=OST_PipeCurves",             FilterB = "Category=OST_Ceilings",              Tolerance = "HARD",           Severity = "LOW",      OwnerDiscipline = "MEP", StageGate = "CD" },

                    // ── Service ↔ Service ──────────────────────────────────
                    new ClashCell { PairId = "PIPE:PIPE",                 FilterA = "Category=OST_PipeCurves",             FilterB = "Category=OST_PipeCurves",            Tolerance = "CLEARANCE_50",   Severity = "MED",      OwnerDiscipline = "MEP", StageGate = "CD" },
                    new ClashCell { PairId = "DUCT:DUCT",                 FilterA = "Category=OST_DuctCurves",             FilterB = "Category=OST_DuctCurves",            Tolerance = "CLEARANCE_100",  Severity = "MED",      OwnerDiscipline = "MEP", StageGate = "CD" },
                    new ClashCell { PairId = "TRAY:DUCT",                 FilterA = "Category=OST_CableTray",              FilterB = "Category=OST_DuctCurves",            Tolerance = "CLEARANCE_100",  Severity = "MED",      OwnerDiscipline = "MEP", StageGate = "CD" },
                    new ClashCell { PairId = "TRAY:TRAY",                 FilterA = "Category=OST_CableTray",              FilterB = "Category=OST_CableTray",             Tolerance = "CLEARANCE_100",  Severity = "MED",      OwnerDiscipline = "ELE", StageGate = "CD" },
                    new ClashCell { PairId = "CONDUIT:DUCT",              FilterA = "Category=OST_Conduit",                FilterB = "Category=OST_DuctCurves",            Tolerance = "CLEARANCE_50",   Severity = "LOW",      OwnerDiscipline = "ELE", StageGate = "CD" },
                    new ClashCell { PairId = "CONDUIT:PIPE",              FilterA = "Category=OST_Conduit",                FilterB = "Category=OST_PipeCurves",            Tolerance = "CLEARANCE_50",   Severity = "LOW",      OwnerDiscipline = "ELE", StageGate = "CD" },

                    // ── Fire protection ────────────────────────────────────
                    new ClashCell { PairId = "SPRINKLER:CEILING",         FilterA = "Category=OST_Sprinklers",             FilterB = "Category=OST_Ceilings",              Tolerance = "CLEARANCE_50",   Severity = "LOW",      OwnerDiscipline = "MEP", StageGate = "CD" },
                    new ClashCell { PairId = "SPRINKLER:DUCT",            FilterA = "Category=OST_Sprinklers",             FilterB = "Category=OST_DuctCurves",            Tolerance = "CLEARANCE_100",  Severity = "HIGH",     OwnerDiscipline = "MEP", StageGate = "CD" },
                    new ClashCell { PairId = "FIREDEV:WALL",              FilterA = "Category=OST_FireAlarmDevices",       FilterB = "Category=OST_Walls",                 Tolerance = "HARD",           Severity = "MED",      OwnerDiscipline = "ELE", StageGate = "CD" },

                    // ── Equipment access & supports ────────────────────────
                    new ClashCell { PairId = "MEP_EQPT:DUCT",             FilterA = "Category=OST_MechanicalEquipment",    FilterB = "Category=OST_DuctCurves",            Tolerance = "HARD",           Severity = "MED",      OwnerDiscipline = "MEP", StageGate = "CD" },
                    new ClashCell { PairId = "MEP_EQPT:STR_BEAM",         FilterA = "Category=OST_MechanicalEquipment",    FilterB = "Category=OST_StructuralFraming",     Tolerance = "HARD",           Severity = "HIGH",     OwnerDiscipline = "MEP", StageGate = "DD" },
                    new ClashCell { PairId = "ELC_EQPT:STR_BEAM",         FilterA = "Category=OST_ElectricalEquipment",    FilterB = "Category=OST_StructuralFraming",     Tolerance = "HARD",           Severity = "HIGH",     OwnerDiscipline = "ELE", StageGate = "DD" },
                    new ClashCell { PairId = "ELC_EQPT:WALL",             FilterA = "Category=OST_ElectricalEquipment",    FilterB = "Category=OST_Walls",                 Tolerance = "CLEARANCE_100",  Severity = "MED",      OwnerDiscipline = "ELE", StageGate = "CD" },
                    new ClashCell { PairId = "PLUMB_FIX:WALL",            FilterA = "Category=OST_PlumbingFixtures",       FilterB = "Category=OST_Walls",                 Tolerance = "HARD",           Severity = "MED",      OwnerDiscipline = "MEP", StageGate = "CD" },
                    new ClashCell { PairId = "AIRTERM:CEILING",           FilterA = "Category=OST_DuctTerminal",           FilterB = "Category=OST_Ceilings",              Tolerance = "CLEARANCE_50",   Severity = "LOW",      OwnerDiscipline = "MEP", StageGate = "CD" },
                    new ClashCell { PairId = "LIGHT:CEILING",             FilterA = "Category=OST_LightingFixtures",       FilterB = "Category=OST_Ceilings",              Tolerance = "CLEARANCE_50",   Severity = "LOW",      OwnerDiscipline = "ELE", StageGate = "CD" },

                    // ── Structural ↔ Architectural ─────────────────────────
                    new ClashCell { PairId = "STR_COLUMN:ARCH_WALL",      FilterA = "Category=OST_StructuralColumns",      FilterB = "Category=OST_Walls",                 Tolerance = "HARD",           Severity = "HIGH",     OwnerDiscipline = "STR", StageGate = "DD" },
                    new ClashCell { PairId = "STR_BEAM:CEILING",          FilterA = "Category=OST_StructuralFraming",      FilterB = "Category=OST_Ceilings",              Tolerance = "HARD",           Severity = "MED",      OwnerDiscipline = "ARC", StageGate = "CD" },
                    new ClashCell { PairId = "STAIR:CEILING",             FilterA = "Category=OST_Stairs",                 FilterB = "Category=OST_Ceilings",              Tolerance = "CLEARANCE_100",  Severity = "MED",      OwnerDiscipline = "ARC", StageGate = "CD" },

                    // ── Comms / IT / security ──────────────────────────────
                    new ClashCell { PairId = "COMM_DEV:CEILING",          FilterA = "Category=OST_CommunicationDevices",   FilterB = "Category=OST_Ceilings",              Tolerance = "CLEARANCE_50",   Severity = "LOW",      OwnerDiscipline = "ELE", StageGate = "CD" },
                    new ClashCell { PairId = "DATA_DEV:CEILING",          FilterA = "Category=OST_DataDevices",            FilterB = "Category=OST_Ceilings",              Tolerance = "CLEARANCE_50",   Severity = "LOW",      OwnerDiscipline = "ELE", StageGate = "CD" },
                    new ClashCell { PairId = "SEC_DEV:WALL",              FilterA = "Category=OST_SecurityDevices",        FilterB = "Category=OST_Walls",                 Tolerance = "HARD",           Severity = "LOW",      OwnerDiscipline = "ELE", StageGate = "CD" },

                    // ── Flex services (fire-rating-critical penetrations) ──
                    new ClashCell { PairId = "FLEXDUCT:CEILING",          FilterA = "Category=OST_FlexDuctCurves",         FilterB = "Category=OST_Ceilings",              Tolerance = "HARD",           Severity = "LOW",      OwnerDiscipline = "MEP", StageGate = "CD" },
                    new ClashCell { PairId = "FLEXPIPE:WALL",             FilterA = "Category=OST_FlexPipeCurves",         FilterB = "Category=OST_Walls",                 Tolerance = "HARD",           Severity = "LOW",      OwnerDiscipline = "MEP", StageGate = "CD" },
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
