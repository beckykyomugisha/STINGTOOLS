// ══════════════════════════════════════════════════════════════════════════
//  BoqProvisional.cs (QS gap G2) — provisional-sum reconciliation trail.
//
//  Until now a provisional sum (PS) was just a number you overwrote, so the
//  cost movement from estimate → actual was invisible. This records, per PS
//  row, the FROZEN original allowance, a dated adjustment trail, and the
//  reconciled final-account actual. Σ(actual − original) is the
//  "provisional-sum movement", surfaced in the panel and fed into the
//  Anticipated Final Cost. Keyed off BOQLineItem.Id (stable across rebuilds
//  because BOQLineItem.Clone preserves Id and PS rows come from the manual
//  store). Persisted per project at <project>/_BIM_COORD/boq_provisionals.json.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.BOQ
{
    public class BoqProvisionalAdjustment
    {
        public string Date { get; set; } = "";   // yyyy-MM-dd
        public double Amount { get; set; }        // signed delta UGX (new actual − prior basis)
        public string Note { get; set; } = "";
    }

    public class BoqProvisionalRecord
    {
        public string Id { get; set; } = "";                 // == BOQLineItem.Id of the PS row
        public string Description { get; set; } = "";
        public double OriginalSum { get; set; }              // frozen first-seen PS estimate
        public List<BoqProvisionalAdjustment> Adjustments { get; set; } = new List<BoqProvisionalAdjustment>();
        public double? ReconciledActual { get; set; }        // null = not yet reconciled
        public string Status { get; set; } = "Open";         // Open | PartlyReconciled | Closed
    }

    public class BoqProvisionalStore
    {
        public List<BoqProvisionalRecord> Records { get; set; } = new List<BoqProvisionalRecord>();
    }

    internal static class BoqProvisionalTrail
    {
        private static string PathFor(Document doc)
        {
            try
            {
                string parent = System.IO.Path.GetDirectoryName(doc?.PathName ?? "");
                if (string.IsNullOrEmpty(parent)) return null;
                return System.IO.Path.Combine(parent, "_BIM_COORD", "boq_provisionals.json");
            }
            catch { return null; }
        }

        public static BoqProvisionalStore Load(Document doc)
        {
            try
            {
                string path = PathFor(doc);
                if (path == null || !File.Exists(path)) return new BoqProvisionalStore();
                return JsonConvert.DeserializeObject<BoqProvisionalStore>(File.ReadAllText(path))
                       ?? new BoqProvisionalStore();
            }
            catch (Exception ex) { StingLog.Warn($"BoqProvisionalTrail.Load: {ex.Message}"); return new BoqProvisionalStore(); }
        }

        public static void Save(Document doc, BoqProvisionalStore store)
        {
            try
            {
                string path = PathFor(doc);
                if (path == null || store == null) return;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(store, Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"BoqProvisionalTrail.Save: {ex.Message}"); }
        }

        /// <summary>
        /// Σ(reconciledActual − originalSum) over reconciled records — the
        /// "provisional-sum movement". Positive = PS actuals overrun the
        /// allowances; negative = credit back.
        /// </summary>
        public static double MovementUGX(Document doc) => MovementUGX(Load(doc));

        public static double MovementUGX(BoqProvisionalStore store)
        {
            if (store?.Records == null) return 0;
            double mv = 0;
            foreach (var r in store.Records)
                if (r.ReconciledActual.HasValue)
                    mv += r.ReconciledActual.Value - r.OriginalSum;
            return Math.Round(mv, 0);
        }
    }
}
