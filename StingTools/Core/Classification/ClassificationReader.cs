// §5.5 — reader for the five identity / classification parameters.
//
// Wired into BOQ grouping, COBie export, handover manual, and the issue
// tracker. Missing Uniclass values fall back to the existing STING_OMNICLASS_23
// (already injected by InjectAutomationPresentationPack) so hybrid projects
// that only populated OmniClass remain fully functional.

using Autodesk.Revit.DB;

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
            if (!string.IsNullOrEmpty(c.UniclassProduct)) return ("PR:"   + c.UniclassProduct, "Uniclass.Pr",  c.UniclassProduct);
            if (!string.IsNullOrEmpty(c.UniclassSystem))  return ("SS:"   + c.UniclassSystem,  "Uniclass.Ss",  c.UniclassSystem);
            if (!string.IsNullOrEmpty(c.UniclassElement)) return ("EF:"   + c.UniclassElement, "Uniclass.Ef",  c.UniclassElement);

            Element type = null;
            try { type = el?.Document?.GetElement(el.GetTypeId()); } catch { }
            string omni = type?.LookupParameter("STING_OMNICLASS_23")?.AsString() ?? "";
            if (!string.IsNullOrEmpty(omni)) return ("OMNI:" + omni, "OmniClass23", omni);

            string famTypeKey = (type?.Category?.Name ?? "") + "/" +
                                ((type as ElementType)?.FamilyName ?? "") + "/" +
                                (type?.Name ?? "");
            return ("NATIVE:" + famTypeKey, "Native.Family", famTypeKey);
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
