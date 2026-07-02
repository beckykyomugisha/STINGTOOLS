// Healthcare Pack HC-DEF-10 — clinical-equipment COBie / SFG20 consumer.
//
// The COBIE_*.csv pack already ships the clinical-equipment rows the design
// promised (43 CEQ-* type rows, 26 CEQ-* job templates, 17 CEQ-* spare
// templates) and the CEQ_* shared-parameter cluster is registered — but until
// now NOTHING consumed either, so the cluster was orphaned and clinical
// equipment never reached the COBie handover.
//
// This bridge is the consumer. It collects clinical-equipment elements
// (Medical / Specialty / Nurse-Call), resolves each to its COBie TypeCode via
// COBIE_TYPE_MAP.csv ("{RevitCategory}|{StingProdCode}" like COBieAutoMatch),
// and appends COBie Attribute / Job / Spare rows driven by the CSV pack plus
// the element's own CEQ_* values. All domain values are data-supplied (the CSV
// pack + per-element CEQ_* params); no codes are hardcoded here. It is invoked
// from COBieHandoverExportCommand so clinical rows flow through the existing
// exporter rather than a parallel one.

using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Temp;   // COBieDataHelper + COBie*Record (internal, same assembly)
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Docs
{
    internal static class ClinicalCobieBridge
    {
        // CEQ_* params surfaced as COBie Attribute rows (display name, param, unit, description).
        private static readonly (string Name, string Param, string Unit, string Desc)[] CeqAttributes =
        {
            ("ClinicalCategory",      "CEQ_CATEGORY_TXT",        "", "Clinical equipment category"),
            ("GMDN",                  "CEQ_GMDN_CODE_TXT",       "", "GMDN nomenclature code"),
            ("UMDNS",                 "CEQ_UMDNS_CODE_TXT",      "", "ECRI UMDNS nomenclature code"),
            ("SFG20Schedule",         "CEQ_SFG20_REF_TXT",       "", "SFG20 healthcare maintenance schedule reference"),
            ("InfectionTier",         "CEQ_INFECT_TIER_TXT",     "", "Spaulding infection classification"),
            ("DecontaminationMethod", "CEQ_DECON_METHOD_TXT",    "", "Decontamination route"),
            ("EndoscopeId",           "CEQ_ENDO_SCOPE_ID_TXT",   "", "HTM 01-06 endoscope traceability id"),
            ("EndoscopeAER",          "CEQ_ENDO_AER_REF_TXT",    "", "Owning automated endoscope reprocessor"),
            ("EndoReprocessCycles",   "CEQ_ENDO_CYCLE_COUNT_INT","", "Endoscope reprocessing cycle count"),
            ("EndoLastReprocessed",   "CEQ_ENDO_LAST_REPRO_DT",  "", "Last reprocessing date (ISO 8601)"),
            ("ImagingStructuralLoad", "CEQ_IMAGING_STRUCT_LOAD", "", "Imaging equipment structural load"),
        };

        internal static bool IsClinical(Element el) =>
            Gb(el, "CEQ_CLINICAL_BOOL") || !string.IsNullOrEmpty(Gs(el, "CEQ_CATEGORY_TXT"));

        /// <summary>Appends clinical Attribute / Job / Spare rows to the COBie sheet
        /// line-lists. Returns the number of clinical elements emitted.</summary>
        public static int Emit(Document doc, List<string> attrLines, List<string> jobLines,
                               List<string> resLines, List<string> spareLines)
        {
            if (doc == null) return 0;

            List<Element> clinical;
            try
            {
                var cats = new ElementMulticategoryFilter(new[] {
                    BuiltInCategory.OST_MedicalEquipment,
                    BuiltInCategory.OST_SpecialityEquipment,
                    BuiltInCategory.OST_NurseCallDevices });
                clinical = new FilteredElementCollector(doc).WherePasses(cats)
                    .WhereElementIsNotElementType().ToElements()
                    .Where(IsClinical).ToList();
            }
            catch (Exception ex) { StingLog.Warn($"ClinicalCobieBridge collect: {ex.Message}"); return 0; }
            if (clinical.Count == 0) return 0;

            var types = COBieDataHelper.LoadTypeMap();
            var jobs = COBieDataHelper.LoadJobTemplates();
            var spares = COBieDataHelper.LoadSpareParts();
            var attrTpl = COBieDataHelper.LoadAttributeTemplates();

            // TypeCode resolution: RevitCategory|StingProdCode, then category-only fallback
            // (identical to COBieAutoMatchCommand so clinical elements land on the same TypeCode).
            var lookup = new Dictionary<string, COBieTypeRecord>(StringComparer.OrdinalIgnoreCase);
            var catFallback = new Dictionary<string, COBieTypeRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in types)
            {
                var key = $"{t.RevitCategory}|{t.StingProdCode}";
                if (!lookup.ContainsKey(key)) lookup[key] = t;
                if (!catFallback.ContainsKey(t.RevitCategory)) catFallback[t.RevitCategory] = t;
            }

            string now = DateTime.Now.ToString("yyyy-MM-dd");
            var jobsSeen = new HashSet<string>();
            var resourcesSeen = new HashSet<string>();   // Resource sheet is a name-keyed catalog
            var sparesSeen = new HashSet<string>();       // Spare sheet links Type→spare (per-type)
            int emitted = 0;

            foreach (var el in clinical)
            {
                string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                if (string.IsNullOrEmpty(tag1)) tag1 = $"CEQ-{el.Id.Value}";   // still emit even if untagged
                emitted++;

                // ── Attribute rows from the element's CEQ_* values (project data) ──
                foreach (var a in CeqAttributes)
                {
                    string v = GsAny(el, a.Param);
                    if (!string.IsNullOrEmpty(v))
                        attrLines.Add($"{Esc(a.Name)},STING Tools,{now},Attribute,Component,{Esc(tag1)},{Esc(v)},{a.Unit},{Esc(a.Desc)},");
                }
                // Data-driven extension: any attribute template mapped to a CEQ_ param.
                foreach (var t in attrTpl)
                {
                    if (string.IsNullOrEmpty(t.StingParamKey) ||
                        !t.StingParamKey.StartsWith("CEQ_", StringComparison.OrdinalIgnoreCase)) continue;
                    if (CeqAttributes.Any(c => string.Equals(c.Param, t.StingParamKey, StringComparison.OrdinalIgnoreCase))) continue;
                    string v = GsAny(el, t.StingParamKey);
                    if (!string.IsNullOrEmpty(v))
                        attrLines.Add($"{Esc(t.AttributeName)},STING Tools,{now},Attribute,Component,{Esc(tag1)},{Esc(v)},{Esc(t.Unit)},{Esc(t.Description)},{Esc(t.AllowedValues)}");
                }

                // Resolve the clinical TypeCode for job/spare template matching.
                string cat = ParameterHelpers.GetCategoryName(el);
                string prod = ParameterHelpers.GetString(el, ParamRegistry.PROD);
                COBieTypeRecord typeRec = null;
                if (!string.IsNullOrEmpty(prod)) lookup.TryGetValue($"{cat}|{prod}", out typeRec);
                if (typeRec == null) catFallback.TryGetValue(cat, out typeRec);
                string typeCode = typeRec?.TypeCode ?? "";
                string typeKey = $"{ParameterHelpers.GetFamilyName(el)}:{ParameterHelpers.GetFamilySymbolName(el)}";
                string elSfg20 = Gs(el, "CEQ_SFG20_REF_TXT");
                if (string.IsNullOrEmpty(typeCode)) continue;   // no clinical type → attributes only

                // ── Job rows: templates whose pattern matches the resolved TypeCode ──
                foreach (var j in jobs)
                {
                    if (!PatternMatches(j.TypeCodePattern, typeCode)) continue;
                    if (!jobsSeen.Add($"{tag1}|{j.JobName}")) continue;
                    string sfg = !string.IsNullOrEmpty(elSfg20) ? elSfg20 : j.SFG20Code;
                    string desc = j.Description + (string.IsNullOrEmpty(sfg) ? "" : $" [SFG20 {sfg}]");
                    string freqUnit = string.IsNullOrEmpty(j.FrequencyUnit) ? "month" : j.FrequencyUnit;
                    // Job columns: Name,CreatedBy,CreatedOn,Category,Status,TypeName,Description,
                    //              Duration,DurationUnit,Start,TaskStartUnit,Frequency,FrequencyUnit,Priors
                    jobLines.Add($"{Esc($"PPM-{tag1}-{j.JobName}")},STING Tools,{now}," +
                                 $"{Esc(string.IsNullOrEmpty(j.JobType) ? "Preventive" : j.JobType)},Required," +
                                 $"{Esc(typeKey)},{Esc(desc)},{j.Duration:0.#},{Esc(j.DurationUnit)}," +
                                 $"{j.Start:0.#},{Esc(j.TaskStartUnit)},{j.Frequency:0.#},{Esc(freqUnit)},");
                }

                // ── Spare rows: templates whose pattern matches the resolved TypeCode ──
                foreach (var s in spares)
                {
                    if (!PatternMatches(s.TypeCodePattern, typeCode)) continue;

                    // Resource sheet is a flat catalog keyed by Name (no Type/PartNumber
                    // column) — dedup globally by name.
                    if (resourcesSeen.Add(s.SpareName))
                        // Resource sheet: Name,CreatedBy,CreatedOn,Category,Description
                        resLines.Add($"{Esc(s.SpareName)},STING Tools,{now},Spare Part,{Esc(s.Description)}");

                    // Spare sheet links each Type to its spare (TypeName + PartNumber), so
                    // the same spare name under a different type is a distinct, valid row.
                    // Dedup per {typeCode|SpareName|PartNumber}: genuine duplicates within a
                    // type still collapse; distinct types / part numbers both survive.
                    if (sparesSeen.Add($"{typeCode}|{s.SpareName}|{s.PartNumber}"))
                        // Spare sheet: Name,CreatedBy,CreatedOn,Category,TypeName,Suppliers,Description
                        spareLines.Add($"{Esc(s.SpareName)},STING Tools,{now},Spare Part,{Esc(typeCode)},{Esc(s.Supplier)},{Esc(s.Description)}");
                }
            }

            return emitted;
        }

        // "CEQ-PEND-*" → prefix; "CEQ-CT-" → prefix; "CEQ-AED" → exact. Case-insensitive.
        private static bool PatternMatches(string pattern, string typeCode)
        {
            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(typeCode)) return false;
            var p = pattern.Trim();
            if (p.EndsWith("*")) return typeCode.StartsWith(p.Substring(0, p.Length - 1), StringComparison.OrdinalIgnoreCase);
            if (p.EndsWith("-")) return typeCode.StartsWith(p, StringComparison.OrdinalIgnoreCase);
            return string.Equals(p, typeCode, StringComparison.OrdinalIgnoreCase);
        }

        private static string Esc(string s) => HandoverHelper.Esc(s);

        private static string Gs(Element el, string p)
        {
            var pr = el?.LookupParameter(p);
            return (pr != null && pr.HasValue && pr.StorageType == StorageType.String) ? (pr.AsString() ?? "") : "";
        }

        private static string GsAny(Element el, string p)
        {
            var pr = el?.LookupParameter(p);
            if (pr == null || !pr.HasValue) return "";
            if (pr.StorageType == StorageType.String) return pr.AsString() ?? "";
            if (pr.StorageType == StorageType.Integer) return pr.AsInteger().ToString();
            return pr.AsValueString() ?? "";
        }

        private static bool Gb(Element el, string p)
        {
            var pr = el?.LookupParameter(p);
            return pr != null && pr.HasValue && pr.StorageType == StorageType.Integer && pr.AsInteger() != 0;
        }
    }
}
