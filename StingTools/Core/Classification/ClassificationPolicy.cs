// ══════════════════════════════════════════════════════════════════════════
//  ClassificationPolicy.cs — Phase 196. Per-project classification switch.
//
//  STING coexists with five classification axes (Uniclass 2015, OmniClass, CSI
//  MasterFormat, NBS, native family/type). Which one is AUTHORITATIVE for BOQ
//  grouping / COBie / IFC / handover used to be hard-coded in
//  ClassificationReader.ResolveFallback (Uniclass.Pr → Ss → Ef → OmniClass23 →
//  native). British projects want Uniclass-first; American projects want
//  CSI MasterFormat or OmniClass-first; an owner with a bespoke table wants its
//  own parameter first. This overlay makes that order a per-project data file
//  instead of a recompile:
//
//    <project>/_BIM_COORD/classification_policy.json
//    {
//      "order": [
//        { "id": "csi",         "param": "CSI_SECTION_TXT",    "prefix": "CSI",  "label": "CSI.MasterFormat" },
//        { "id": "omniclass23", "param": "STING_OMNICLASS_23", "prefix": "OMNI", "label": "OmniClass23" },
//        { "id": "uniclass.pr", "param": "UNICLASS_PR_TXT",    "prefix": "PR",   "label": "Uniclass.Pr" },
//        { "id": "native" }
//      ]
//    }
//
//  Each entry names a shared parameter to read (type-first), a key prefix, and a
//  provenance label. An entry with no `param` (or id "native") is the terminal
//  family/type fallback. Point `param` at ANY text parameter to introduce a
//  classification the code has never seen — no new code required.
//
//  A missing or malformed file yields the DEFAULT policy, which reproduces the
//  pre-Phase-196 hard-coded order exactly — zero behavioural change until a
//  project opts in. HOST-FREE (no Autodesk.Revit references) so it unit-tests
//  independently of Revit; ClassificationReader does the parameter reads.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace StingTools.Core.Classification
{
    /// <summary>One rung of the classification fallback ladder.</summary>
    public sealed class ClassificationSource
    {
        [JsonProperty("id")]    public string Id { get; set; } = "";
        [JsonProperty("param")] public string Param { get; set; } = "";
        [JsonProperty("prefix")] public string Prefix { get; set; } = "";
        [JsonProperty("label")] public string Label { get; set; } = "";

        /// <summary>Terminal native family/type fallback (no parameter to read).</summary>
        [JsonIgnore]
        public bool IsNative =>
            string.IsNullOrWhiteSpace(Param) ||
            string.Equals(Id, "native", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class ClassificationPolicy
    {
        [JsonProperty("order")]
        public List<ClassificationSource> Order { get; set; } = new List<ClassificationSource>();

        /// <summary>
        /// Phase 199 — the active OmniClass TABLE the OmniClass column / OmniClass_Assign
        /// classifies by. "21" = Elements (default), "13" = Spaces by Function, "23" =
        /// Products. Accepts "Table 21" / "T21" / "21" (normalised by <see cref="OmniClassTableNumber"/>).
        /// Switching is a one-line change here; the assigner loads the matching corporate
        /// map (STING_OMNICLASS_&lt;table&gt;_MAP.csv) and the BOQ column names the table.
        /// </summary>
        [JsonProperty("omniClassTable")]
        public string OmniClassTable { get; set; } = "21";

        /// <summary>Normalised active table number — "Table 21"/"T21"/"21" → "21". Default "21".</summary>
        [JsonIgnore]
        public string OmniClassTableNumber => NormalizeTable(OmniClassTable);

        private static string NormalizeTable(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "21";
            var digits = new string(raw.Where(char.IsDigit).ToArray());
            return digits.Length > 0 ? digits : "21";
        }

        /// <summary>
        /// The compiled-in default: the exact order ResolveFallback used before
        /// Phase 196 (Uniclass.Pr → Ss → Ef → OmniClass23 → native). Returned
        /// whenever no project policy is present, so existing projects are
        /// untouched.
        /// </summary>
        public static ClassificationPolicy Default => new ClassificationPolicy
        {
            Order = new List<ClassificationSource>
            {
                new ClassificationSource { Id = "uniclass.pr", Param = "UNICLASS_PR_TXT",    Prefix = "PR",   Label = "Uniclass.Pr" },
                new ClassificationSource { Id = "uniclass.ss", Param = "UNICLASS_SS_TXT",    Prefix = "SS",   Label = "Uniclass.Ss" },
                new ClassificationSource { Id = "uniclass.ef", Param = "UNICLASS_EF_TXT",    Prefix = "EF",   Label = "Uniclass.Ef" },
                new ClassificationSource { Id = "omniclass23", Param = "STING_OMNICLASS_23", Prefix = "OMNI", Label = "OmniClass23" },
                new ClassificationSource { Id = "native" }
            }
        };

        /// <summary>
        /// Parse a policy from raw JSON. Returns the Default policy on null/blank
        /// input, malformed JSON, or an empty order — a broken policy file must
        /// never break classification, it just falls back to the baseline order.
        /// Always normalised so a terminal native rung exists and every rung
        /// carries a prefix + label (synthesised from the id when omitted).
        /// </summary>
        public static ClassificationPolicy Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return Default;
            try
            {
                var p = JsonConvert.DeserializeObject<ClassificationPolicy>(json);
                if (p == null) return Default;
                // A policy may set only omniClassTable (no order) — keep it, use the
                // default classification order.
                if (p.Order == null || p.Order.Count == 0) { p.Order = Default.Order; return p; }
                return Normalize(p);
            }
            catch
            {
                return Default;
            }
        }

        /// <summary>
        /// Load from <c>&lt;projectDir&gt;/_BIM_COORD/classification_policy.json</c>.
        /// Returns the Default policy when the directory is unknown or the file
        /// is absent. Never throws.
        /// </summary>
        public static ClassificationPolicy Load(string projectDir)
        {
            if (string.IsNullOrWhiteSpace(projectDir)) return Default;
            try
            {
                string path = Path.Combine(projectDir, "_BIM_COORD", "classification_policy.json");
                if (!File.Exists(path)) return Default;
                return Parse(File.ReadAllText(path));
            }
            catch
            {
                return Default;
            }
        }

        private static ClassificationPolicy Normalize(ClassificationPolicy p)
        {
            var cleaned = new List<ClassificationSource>();
            bool hasNative = false;
            foreach (var s in p.Order)
            {
                if (s == null) continue;
                if (hasNative) break;              // native is terminal — ignore any trailing rungs
                if (s.IsNative)
                {
                    hasNative = true;
                    cleaned.Add(new ClassificationSource { Id = "native" });
                    continue;
                }
                cleaned.Add(new ClassificationSource
                {
                    Id = string.IsNullOrWhiteSpace(s.Id) ? s.Param : s.Id.Trim(),
                    Param = s.Param.Trim(),
                    Prefix = string.IsNullOrWhiteSpace(s.Prefix) ? PrefixFromId(s) : s.Prefix.Trim(),
                    Label = string.IsNullOrWhiteSpace(s.Label) ? (string.IsNullOrWhiteSpace(s.Id) ? s.Param : s.Id.Trim()) : s.Label.Trim()
                });
            }
            // Guarantee a terminal native rung so ResolveFallback always yields a key.
            if (!hasNative) cleaned.Add(new ClassificationSource { Id = "native" });
            return new ClassificationPolicy { Order = cleaned, OmniClassTable = p.OmniClassTable };
        }

        private static string PrefixFromId(ClassificationSource s)
        {
            string baseId = string.IsNullOrWhiteSpace(s.Id) ? s.Param : s.Id;
            baseId = (baseId ?? "").Trim();
            int dot = baseId.IndexOf('.');
            string seg = dot > 0 ? baseId.Substring(dot + 1) : baseId;
            return string.IsNullOrEmpty(seg) ? "CLS" : seg.ToUpperInvariant();
        }
    }
}
