// ══════════════════════════════════════════════════════════════════════════
//  BoqPrelims.cs (QS gap G3) — built-up preliminaries schedule.
//
//  Prelims/contingency/overhead were single flat percentages — fine for a quick
//  estimate, not a defensible prelims bill. This adds an OPTIONAL itemised
//  preliminaries schedule: a list of prelim line items (site set-up, management
//  staff, welfare, scaffolding, insurances, …), each priced as a fixed value OR
//  a %-of-works. The flat % stays the DEFAULT (Enabled = false) so nothing
//  regresses; when a project switches it on, the grand-total maths and the XLSX
//  export use the itemised total instead of the flat prelim %.
//
//  Corporate baseline ships at Data/STING_PRELIMS_TEMPLATE.json (disabled, a
//  starter line list); the per-project override lives at
//  <project>/_BIM_COORD/boq_prelims.json and wins entirely once present.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.BOQ
{
    public class BoqPrelimLine
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string Basis { get; set; } = "value";   // "value" (UGX) | "percent" (% of works subtotal)
        public double Value { get; set; }               // UGX when value-basis; % when percent-basis
        public string Note { get; set; } = "";

        /// <summary>Resolve this line to a UGX amount against the works subtotal.</summary>
        public double AmountFor(double worksSubtotalUGX)
            => string.Equals(Basis, "percent", StringComparison.OrdinalIgnoreCase)
                ? Math.Round(worksSubtotalUGX * Value / 100.0, 0)
                : Math.Round(Value, 0);

        public BoqPrelimLine Clone() => new BoqPrelimLine
        {
            Id = Id, Name = Name, Category = Category, Basis = Basis, Value = Value, Note = Note
        };
    }

    public class BoqPrelimsSchedule
    {
        public bool Enabled { get; set; } = false;       // flat % stays the default
        public List<BoqPrelimLine> Lines { get; set; } = new List<BoqPrelimLine>();

        public double TotalUGX(double worksSubtotalUGX)
            => Lines?.Sum(l => l.AmountFor(worksSubtotalUGX)) ?? 0;
    }

    internal static class BoqPrelimsStore
    {
        private const string CorporateFile = "STING_PRELIMS_TEMPLATE.json";

        private static string ProjectPath(Document doc)
        {
            try
            {
                string parent = System.IO.Path.GetDirectoryName(doc?.PathName ?? "");
                if (string.IsNullOrEmpty(parent)) return null;
                return System.IO.Path.Combine(parent, "_BIM_COORD", "boq_prelims.json");
            }
            catch { return null; }
        }

        /// <summary>
        /// Project override wins entirely if present; otherwise the corporate
        /// baseline template (Enabled = false). Always returns a non-null schedule.
        /// </summary>
        public static BoqPrelimsSchedule Load(Document doc)
        {
            try
            {
                string proj = ProjectPath(doc);
                if (proj != null && File.Exists(proj))
                    return JsonConvert.DeserializeObject<BoqPrelimsSchedule>(File.ReadAllText(proj))
                           ?? new BoqPrelimsSchedule();
            }
            catch (Exception ex) { StingLog.Warn($"BoqPrelimsStore.Load project: {ex.Message}"); }
            return LoadCorporate();
        }

        public static BoqPrelimsSchedule LoadCorporate()
        {
            try
            {
                string path = StingToolsApp.FindDataFile(CorporateFile);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return JsonConvert.DeserializeObject<BoqPrelimsSchedule>(File.ReadAllText(path))
                           ?? new BoqPrelimsSchedule();
            }
            catch (Exception ex) { StingLog.Warn($"BoqPrelimsStore.LoadCorporate: {ex.Message}"); }
            return new BoqPrelimsSchedule();
        }

        public static void Save(Document doc, BoqPrelimsSchedule schedule)
        {
            try
            {
                string proj = ProjectPath(doc);
                if (proj == null || schedule == null) return;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(proj));
                File.WriteAllText(proj, JsonConvert.SerializeObject(schedule, Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"BoqPrelimsStore.Save: {ex.Message}"); }
        }
    }
}
