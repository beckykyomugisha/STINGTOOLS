// §5.5 — reader for the five identity / classification parameters.
//
// Wired into BOQ grouping, COBie export, handover manual, and the issue
// tracker. Missing Uniclass values fall back to the existing STING_OMNICLASS_23
// (already injected by InjectAutomationPresentationPack) so hybrid projects
// that only populated OmniClass remain fully functional.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Classification
{
    public class ClassificationInfo
    {
        public string UniclassProduct  { get; set; } = "";   // UNICLASS_PR_TXT (Pr)
        public string UniclassSystem   { get; set; } = "";   // UNICLASS_SS_TXT (Ss)
        public string UniclassElement  { get; set; } = "";   // UNICLASS_EF_TXT (EF)
        public string NbsCode          { get; set; } = "";   // NBS specification clause
        public string AssetRfiUrl      { get; set; } = "";   // Instance — per-element RFI target

        public bool HasAnyUniclass =>
            !string.IsNullOrEmpty(UniclassProduct) ||
            !string.IsNullOrEmpty(UniclassSystem) ||
            !string.IsNullOrEmpty(UniclassElement);
    }

    public static class ClassificationReader
    {
        /// <summary>
        /// Read the five classification parameters. Uniclass / NBS read from
        /// the type first (family-authored classification); RFI URL reads
        /// from the instance first (per-element override then type default).
        /// </summary>
        public static ClassificationInfo Read(Element el)
        {
            var c = new ClassificationInfo();
            if (el == null) return c;
            Element type = null;
            try { type = el.Document.GetElement(el.GetTypeId()); } catch { }

            c.UniclassProduct = TypeFirst(el, type, "UNICLASS_PR_TXT");
            c.UniclassSystem  = TypeFirst(el, type, "UNICLASS_SS_TXT");
            c.UniclassElement = TypeFirst(el, type, "UNICLASS_EF_TXT");
            c.NbsCode         = TypeFirst(el, type, "NBS_CODE_TXT");
            c.AssetRfiUrl     = InstanceFirst(el, type, "ASSET_RFI_URL_TXT");
            return c;
        }

        // ---- per-project classification policy (classification_policy.json) --------
        // Cached per open document. A missing / malformed file yields the DEFAULT
        // policy, whose Order is the historic ladder and whose HasExplicitOrder is false,
        // so nothing below changes behaviour until a project opts in.
        private static readonly ConcurrentDictionary<string, ClassificationPolicy> _policyCache
            = new ConcurrentDictionary<string, ClassificationPolicy>(StringComparer.OrdinalIgnoreCase);

        private static ClassificationPolicy PolicyFor(Document doc)
        {
            // Key on the MODEL PATH, not the resolved policy path. ResolveFallback runs
            // per element across a whole-project BOQ, and StingPaths.MetaFile probes the
            // consolidated and legacy locations on every call - doing that per element
            // would put thousands of File.Exists calls in the take-off loop. The model
            // path is free, and InvalidatePolicy() is what makes an edit visible.
            string key = string.IsNullOrEmpty(doc?.PathName) ? "<unsaved>" : doc.PathName;
            return _policyCache.GetOrAdd(key, _ =>
            {
                string path = null;
                try { path = StingPaths.MetaFile(doc, "_BIM_COORD", "classification_policy.json"); }
                catch (Exception ex) { StingLog.Warn($"ClassificationReader policy path: {ex.Message}"); }
                if (string.IsNullOrEmpty(path)) return ClassificationPolicy.Default;
                try
                {
                    if (!File.Exists(path)) return ClassificationPolicy.Default;
                    var p = ClassificationPolicy.Parse(File.ReadAllText(path));
                    StingLog.Info($"ClassificationReader: applied classification_policy.json " +
                                  $"(table {p.OmniClassTableNumber}, explicit order: {p.HasExplicitOrder}).");
                    return p;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ClassificationReader: classification_policy.json unreadable ({ex.Message}) - using defaults.");
                    return ClassificationPolicy.Default;
                }
            });
        }

        /// <summary>Drop the cached policy so an edited classification_policy.json is re-read.</summary>
        public static void InvalidatePolicy() => _policyCache.Clear();

        /// <summary>True when the project authored an explicit <c>order</c> in
        /// classification_policy.json, which takes precedence over
        /// <see cref="ClassificationStandard"/>. Surfaced so the standard picker can say
        /// its choice will be overridden instead of appearing to work and doing nothing.</summary>
        public static bool HasExplicitPolicyOrder(Document doc) => PolicyFor(doc).HasExplicitOrder;

        /// <summary>The project's active OmniClass table number ("21" / "13" / ...), from
        /// classification_policy.json (default "21"). Read by OmniClass_Assign / _Audit.</summary>
        public static string OmniClassTable(Document doc) => PolicyFor(doc).OmniClassTableNumber;

        /// <summary>The classification parameter(s) the project stamps into the TAG7
        /// narrative (so they show on drawings). Empty list =&gt; none. Read by the tag
        /// narrative builder.</summary>
        public static IReadOnlyList<string> TagClassifications(Document doc)
            => PolicyFor(doc).TagClassifications ?? new List<string>();

        /// <summary>Human label for a classification parameter, for the tag narrative
        /// ("CSI_SECTION_TXT" -&gt; "MasterFormat"). Unknown params fall back to a tidied
        /// name so a new axis still stamps with something readable.</summary>
        public static string ClassificationLabel(string paramName)
        {
            switch ((paramName ?? "").ToUpperInvariant())
            {
                case "CSI_SECTION_TXT":
                case "CSI_TITLE_TXT":           return "MasterFormat";
                case "ASS_OMNICLASS_TXT":
                case "CLS_OMNICLASS_TITLE_TXT": return "OmniClass";
                case "ASS_UNIFORMAT_TXT":
                case "ASS_UNIFORMAT_DESC_TXT":  return "Uniformat";
                case "UNICLASS_PR_TXT":         return "Uniclass Pr";
                case "UNICLASS_SS_TXT":         return "Uniclass Ss";
                case "UNICLASS_EF_TXT":         return "Uniclass EF";
                default:
                    string n = (paramName ?? "").Trim();
                    if (n.EndsWith("_TXT", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
                    return n.Replace('_', ' ');
            }
        }

        /// <summary>Read a classification value type-first (classification is usually authored
        /// on the type) then instance - used by the tag narrative stamp.</summary>
        public static string ReadClassificationValue(Element el, string paramName)
        {
            if (el == null || string.IsNullOrEmpty(paramName)) return "";
            Element type = null;
            try { type = el.Document.GetElement(el.GetTypeId()); } catch { }
            return TypeFirst(el, type, paramName);
        }

        /// <summary>
        /// Pack 126 / Gap J — single canonical fallback chain used by BOQ /
        /// COBie / handover / IFC export. Ordered by specificity:
        ///   1. Uniclass.Pr  (Product)
        ///   2. Uniclass.Ss  (Systems)
        ///   3. Uniclass.Ef  (Elements / Functions)
        ///   4. STING_OMNICLASS_23
        ///   5. Category / FamilyName / TypeName
        ///
        /// Returns (key, source, value). Source is the bucket that won so
        /// downstream reports can show "via: Uniclass.Pr". Guarantees a
        /// non-empty key so BOQ rows never collide on blank classification.
        /// </summary>
        public static (string key, string source, string value) ResolveFallback(Element el)
        {
            var c = Read(el);
            Element type = null;
            try { type = el?.Document?.GetElement(el.GetTypeId()); } catch { }

            string csi  = TypeFirst(el, type, "CSI_SECTION_TXT");
            string omni = type?.LookupParameter("STING_OMNICLASS_23")?.AsString() ?? "";
            string famTypeKey = (type?.Category?.Name ?? "") + "/" +
                                ((type as ElementType)?.FamilyName ?? "") + "/" +
                                (type?.Name ?? "");

            // Candidate buckets in Uniclass-default order.
            var uniPr = ("PR:" + c.UniclassProduct, "Uniclass.Pr", c.UniclassProduct);
            var uniSs = ("SS:" + c.UniclassSystem,  "Uniclass.Ss", c.UniclassSystem);
            var uniEf = ("EF:" + c.UniclassElement, "Uniclass.Ef", c.UniclassElement);
            var csiB  = ("CSI:" + csi,   "CSI.MasterFormat", csi);
            var omniB = ("OMNI:" + omni, "OmniClass23", omni);
            var natB  = ("NATIVE:" + famTypeKey, "Native.Family", famTypeKey);

            // An EXPLICIT classification_policy.json "order" wins: it can name parameters
            // (a bespoke owner table) that the four-way standard selector cannot express.
            // Absent one - the overwhelmingly common case - fall through to the standard
            // selector below, unchanged.
            var policy = PolicyFor(el?.Document);
            if (policy.HasExplicitOrder)
            {
                foreach (var src in policy.Order)
                {
                    if (src.IsNative) return natB;
                    string pv = TypeFirst(el, type, src.Param);
                    if (!string.IsNullOrEmpty(pv)) return (src.Prefix + ":" + pv, src.Label, pv);
                }
                return natB;   // policy with no terminal native rung - guarantee a key
            }

            // Order the cascade by the project's chosen standard (Phase G). Uniclass
            // is the default and preserves the historic order exactly; the others
            // promote their bucket to the front, then fall through the rest.
            (string, string, string)[] order;
            switch (ClassificationStandard.Active(el?.Document))
            {
                case ClassStandard.Csi:      order = new[] { csiB, uniPr, uniSs, uniEf, omniB, natB }; break;
                case ClassStandard.OmniClass:order = new[] { omniB, uniPr, uniSs, uniEf, csiB, natB }; break;
                case ClassStandard.Native:   order = new[] { natB }; break;
                default:                     order = new[] { uniPr, uniSs, uniEf, csiB, omniB, natB }; break;
            }
            foreach (var (key, source, value) in order)
                if (!string.IsNullOrEmpty(value)) return (key, source, value);

            return natB; // famTypeKey is always non-empty-ish, but guarantee a return
        }

        /// <summary>
        /// BOQ grouping key — back-compat shim around <see cref="ResolveFallback"/>.
        /// Returns just the key string for callers that don't need provenance.
        /// </summary>
        public static string BoqGroupKey(Element el) => ResolveFallback(el).key;

        private static string TypeFirst(Element instance, Element type, string name)
        {
            try
            {
                string t = type?.LookupParameter(name)?.AsString();
                if (!string.IsNullOrEmpty(t)) return t;
                return instance?.LookupParameter(name)?.AsString() ?? "";
            }
            catch { return ""; }
        }

        private static string InstanceFirst(Element instance, Element type, string name)
        {
            try
            {
                string i = instance?.LookupParameter(name)?.AsString();
                if (!string.IsNullOrEmpty(i)) return i;
                return type?.LookupParameter(name)?.AsString() ?? "";
            }
            catch { return ""; }
        }
    }
}
