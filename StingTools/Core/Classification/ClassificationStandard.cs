// StingTools — Classification Standard selector (Phase G).
//
// Lets a project choose which classification standard is AUTHORITATIVE for the
// BOQ / COBie / handover / IFC grouping cascade. Until now the fallback order was
// hardcoded Uniclass → OmniClass → Native; CSI MasterFormat was a parallel,
// never-prioritised layer.
//
// The active standard is a per-project setting at
//   <project>/_BIM_COORD/sting_classification.json  →  { "standard": "Uniclass" }
// (file-based, like every other STING project override — no shared-param binding
// needed). Default is Uniclass when the file is absent.

using System;
using System.Collections.Concurrent;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Classification
{
    public enum ClassStandard { Uniclass, Csi, OmniClass, Native }

    public static class ClassificationStandard
    {
        public const string FileRel = "_BIM_COORD/sting_classification.json";

        private static readonly ConcurrentDictionary<string, ClassStandard> _cache
            = new ConcurrentDictionary<string, ClassStandard>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Active standard for the document (cached). Defaults to Uniclass.</summary>
        public static ClassStandard Active(Document doc)
            => _cache.GetOrAdd(DocKey(doc), _ => Load(doc));

        public static void Reload() => _cache.Clear();
        public static void Reload(Document doc)
        {
            if (doc != null) _cache.TryRemove(DocKey(doc), out _);
        }

        private static string DocKey(Document doc)
            => !string.IsNullOrEmpty(doc?.PathName) ? doc.PathName : $"<unsaved:{doc?.GetHashCode() ?? 0}>";

        /// <summary>Persist the choice to the project config file. Returns the path written.</summary>
        public static string Set(Document doc, ClassStandard std)
        {
            if (doc == null || string.IsNullOrEmpty(doc.PathName))
                throw new InvalidOperationException("Save the project before setting its classification standard.");
            string dir = Path.Combine(Path.GetDirectoryName(doc.PathName) ?? "", "_BIM_COORD");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "sting_classification.json");
            var j = new JObject { ["standard"] = std.ToString(), ["_note"] = "STING classification standard (Phase G). Values: Uniclass | CSI | OmniClass | Native." };
            // Atomic write — a crash mid-write must not leave a corrupt config.
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, j.ToString());
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            _cache[doc.PathName] = std;
            return path;
        }

        public static string Label(ClassStandard s) => s switch
        {
            ClassStandard.Uniclass  => "Uniclass 2015",
            ClassStandard.Csi       => "CSI MasterFormat",
            ClassStandard.OmniClass => "OmniClass 2.3",
            ClassStandard.Native    => "Native (Category / Family)",
            _ => s.ToString()
        };

        private static ClassStandard Load(Document doc)
        {
            try
            {
                if (doc == null || string.IsNullOrEmpty(doc.PathName)) return ClassStandard.Uniclass;
                string path = Path.Combine(Path.GetDirectoryName(doc.PathName) ?? "", FileRel);
                if (!File.Exists(path)) return ClassStandard.Uniclass;
                string raw = (string)JObject.Parse(File.ReadAllText(path))["standard"] ?? "";
                if (Enum.TryParse<ClassStandard>(raw, true, out var s)) return s;
                if (raw.IndexOf("uniclass", StringComparison.OrdinalIgnoreCase) >= 0) return ClassStandard.Uniclass;
                if (raw.IndexOf("csi", StringComparison.OrdinalIgnoreCase) >= 0) return ClassStandard.Csi;
                if (raw.IndexOf("omni", StringComparison.OrdinalIgnoreCase) >= 0) return ClassStandard.OmniClass;
                if (raw.IndexOf("native", StringComparison.OrdinalIgnoreCase) >= 0) return ClassStandard.Native;
            }
            catch (Exception ex) { StingLog.Warn($"ClassificationStandard.Load: {ex.Message}"); }
            return ClassStandard.Uniclass;
        }
    }
}
