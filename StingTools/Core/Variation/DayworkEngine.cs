// ══════════════════════════════════════════════════════════════════════════
//  DayworkEngine.cs — persistence for the instructed-daywork register. PM-3.
//
//  Mirrors VariationEngine's Save/Load/List shape. The register is a SINGLE
//  document (unlike variations, which are file-per-VO) because dayworks are read
//  as a set by the Final Account / AFC waterfalls and by Daywork_Register.
//
//  Sidecar root: <project>\STING_BIM_MANAGER\dayworks.json — deliberately the
//  SIBLING of variations\ and star_rates\, which DayworkEngine's callers read in
//  the same breath (a priced sheet attaches to a VariationItem). The PM-3 brief
//  named _BIM_COORD; that would put commercially-coupled data in a third sidecar
//  root while "sidecar-root unification" is still an open PM-7 hygiene item, so
//  this follows the VariationEngine pattern instead. If PM-7 later unifies the
//  roots, this moves with variations\ and star_rates\ as one change.
//
//  Revit-coupled (Document + I/O) — the pure math lives in DayworkModels.cs and
//  is unit-tested headlessly.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.BIMManager;

namespace StingTools.Core.Variation
{
    internal static class DayworkEngine
    {
        private static readonly JsonSerializerSettings _json = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DateFormatString = "yyyy-MM-dd HH:mm:ss",
            Culture = CultureInfo.InvariantCulture
        };

        private static string PathFor(Document doc)
            => Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), "dayworks.json");

        /// <summary>Load the register; never null (an absent/corrupt file yields
        /// an empty register so callers can append without a null dance).</summary>
        public static DayworkRegister Load(Document doc)
        {
            try
            {
                string p = PathFor(doc);
                if (!File.Exists(p)) return new DayworkRegister();
                var reg = JsonConvert.DeserializeObject<DayworkRegister>(File.ReadAllText(p), _json);
                return reg ?? new DayworkRegister();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DayworkEngine.Load: {ex.Message}");
                return new DayworkRegister();
            }
        }

        public static string Save(Document doc, DayworkRegister reg)
        {
            if (reg == null) return null;
            try
            {
                string p = PathFor(doc);
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllText(p, JsonConvert.SerializeObject(reg, _json));
                StingLog.Info($"Daywork register saved — {reg.Records.Count} sheet(s), "
                    + $"priced {reg.Currency} {reg.PricedGrossTotal:N2} "
                    + $"(unattached {reg.UnattachedPricedGrossTotal:N2}).");
                return p;
            }
            catch (Exception ex) { StingLog.Warn($"DayworkEngine.Save: {ex.Message}"); return null; }
        }

        /// <summary>Append (or replace by Id) one sheet and persist.</summary>
        public static string Upsert(Document doc, DayworkRecord rec)
        {
            if (rec == null) return null;
            var reg = Load(doc);
            int i = reg.Records.FindIndex(r => r != null
                && string.Equals(r.Id, rec.Id, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) reg.Records[i] = rec; else reg.Records.Add(rec);
            if (string.IsNullOrEmpty(reg.Currency) && !string.IsNullOrEmpty(rec.Currency))
                reg.Currency = rec.Currency;
            return Save(doc, reg);
        }

        /// <summary>
        /// The daywork figure the Final Account / AFC waterfalls add: priced
        /// sheets NOT already carried by an agreed variation. 0 when the register
        /// is absent, so the waterfalls are unchanged on projects with no dayworks.
        /// </summary>
        public static double UnattachedPricedTotal(Document doc)
        {
            try { return Load(doc).UnattachedPricedGrossTotal; }
            catch (Exception ex) { StingLog.Warn($"DayworkEngine.UnattachedPricedTotal: {ex.Message}"); return 0; }
        }

        /// <summary>Sheets available to attach to a variation — priced and not
        /// yet attached.</summary>
        public static List<DayworkRecord> AttachableSheets(Document doc)
            => Load(doc).WithStatus(DayworkStatus.Priced).Where(r => !r.IsAttached).ToList();
    }
}
