// §5.5 — reader for the five identity / classification parameters.
//
// Wired into BOQ grouping, COBie export, handover manual, and the issue
// tracker. Missing Uniclass values fall back to the existing STING_OMNICLASS_23
// (already injected by InjectAutomationPresentationPack) so hybrid projects
// that only populated OmniClass remain fully functional.

using System;
using System.Collections.Concurrent;
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

        // Phase 196 — per-project classification order overlay, cached per
        // project directory. Default (no file) reproduces the historic order
        // exactly (Uniclass.Pr → Ss → Ef → OmniClass23 → native).
        private static readonly ConcurrentDictionary<string, ClassificationPolicy> _policyCache
            = new ConcurrentDictionary<string, ClassificationPolicy>(StringComparer.OrdinalIgnoreCase);

        private static ClassificationPolicy PolicyFor(Document doc)
        {
            string dir = "";
            try { dir = Path.GetDirectoryName(doc?.PathName ?? "") ?? ""; } catch { }
            string key = string.IsNullOrEmpty(dir) ? "default" : dir;
            return _policyCache.GetOrAdd(key, _ =>
            {
                var p = ClassificationPolicy.Load(dir);
                // Only the opt-in case is worth a log line.
                try
                {
                    if (!string.IsNullOrEmpty(dir) &&
                        File.Exists(Path.Combine(dir, "_BIM_COORD", "classification_policy.json")))
                        StingLog.Info($"ClassificationReader: applied classification_policy.json ({p.Order.Count} rung(s)).");
                }
                catch { }
                return p;
            });
        }

        /// <summary>Drop the cached policy so an edited classification_policy.json is re-read.</summary>
        public static void InvalidatePolicy() => _policyCache.Clear();

        /// <summary>
        /// Pack 126 / Gap J — single canonical fallback chain used by BOQ /
        /// COBie / handover / IFC export. Phase 196: the order is now driven by
        /// the project's classification_policy.json (see <see cref="ClassificationPolicy"/>);
        /// the compiled-in default reproduces the historic order
        /// (Uniclass.Pr → Ss → Ef → OmniClass23 → native), so an American project
        /// can put CSI MasterFormat or OmniClass first by dropping an overlay.
        ///
        /// Returns (key, source, value). Source is the rung that won so
        /// downstream reports can show "via: Uniclass.Pr" / "via: CSI.MasterFormat".
        /// Guarantees a non-empty key so BOQ rows never collide on blank
        /// classification.
        /// </summary>
        public static (string key, string source, string value) ResolveFallback(Element el)
        {
            var policy = PolicyFor(el?.Document);
            Element type = null;
            try { type = el?.Document?.GetElement(el.GetTypeId()); } catch { }

            foreach (var src in policy.Order)
            {
                if (src.IsNative) return NativeKey(type);
                string v = TypeFirst(el, type, src.Param);
                if (!string.IsNullOrEmpty(v))
                    return (src.Prefix + ":" + v, src.Label, v);
            }
            // Policy with no terminal native rung — guarantee a key.
            return NativeKey(type);
        }

        private static (string key, string source, string value) NativeKey(Element type)
        {
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
