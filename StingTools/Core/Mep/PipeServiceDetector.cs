// StingTools Phase 183 — pipe-service detector.
//
// Closes gap A2 from the post-Phase-181 review: MepAutoSizePipeCommand
// was defaulting every pipe to the "chw" service entry, so a domestic
// hot pipe got sized to 1.5 m/s instead of 1.0, and a refrigerant gas
// line got sized as chilled water.
//
// Strategy:
//   1. Read MEPSystem.SystemType abbreviation off the pipe (or fall back
//      to MEPSystem.Name / RBS_DUCT_PIPE_SYSTEM_ABBREVIATION_PARAM).
//   2. Match against patterns in STING_MEP_SERVICE_MAP.json (corporate
//      baseline + project override at <project>/_BIM_COORD/mep_service_map.json).
//   3. Return the matched PipeService.Id, or "chw" as the safe default
//      (lowest velocity ⇒ largest bore ⇒ no danger of under-sizing).
//
// Loader caches per-document path; Reload() invalidates.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Core.Mep
{
    public class PipeServicePattern
    {
        public string Match     { get; set; } = "";
        public string ServiceId { get; set; } = "";
        public string Notes     { get; set; } = "";
    }

    public static class PipeServiceDetector
    {
        public const string DataFileName = "STING_MEP_SERVICE_MAP.json";
        public const string ProjectOverrideRelPath = "_BIM_COORD/mep_service_map.json";
        public const string DefaultServiceId = "chw";

        private static readonly object _lock = new object();
        private static List<PipeServicePattern> _cached;
        private static string _cacheKey;

        public static string DetectServiceId(Document doc, Element pipe)
        {
            if (pipe == null) return DefaultServiceId;
            try
            {
                string abbr = ReadSystemAbbreviation(pipe);
                if (string.IsNullOrEmpty(abbr)) return DefaultServiceId;

                var patterns = Patterns(doc);
                foreach (var p in patterns)
                {
                    if (string.IsNullOrEmpty(p.Match)) continue;
                    if (abbr.StartsWith(p.Match, StringComparison.OrdinalIgnoreCase))
                        return string.IsNullOrEmpty(p.ServiceId) ? DefaultServiceId : p.ServiceId;
                }
            }
            catch (Exception ex) { StingLog.Warn($"PipeServiceDetector.DetectServiceId: {ex.Message}"); }
            return DefaultServiceId;
        }

        public static void Reload()
        {
            lock (_lock) { _cached = null; _cacheKey = null; }
        }

        // ── Internals ───────────────────────────────────────────────────

        private static string ReadSystemAbbreviation(Element pipe)
        {
            try
            {
                if (pipe is Pipe p && p.MEPSystem != null)
                {
                    var sysType = p.Document.GetElement(p.MEPSystem.GetTypeId()) as MEPSystemType;
                    if (sysType != null && !string.IsNullOrEmpty(sysType.Abbreviation))
                        return sysType.Abbreviation.Trim();
                    if (!string.IsNullOrEmpty(p.MEPSystem.Name))
                        return p.MEPSystem.Name.Trim();
                }
                // Generic parameter fallback (works on FabricationPart pipes too).
                var bip = pipe.get_Parameter(BuiltInParameter.RBS_DUCT_PIPE_SYSTEM_ABBREVIATION_PARAM);
                if (bip != null && bip.HasValue) return (bip.AsString() ?? "").Trim();
            }
            catch (Exception ex) { StingLog.Warn($"ReadSystemAbbreviation {pipe?.Id}: {ex.Message}"); }
            return "";
        }

        private static List<PipeServicePattern> Patterns(Document doc)
        {
            lock (_lock)
            {
                string key = doc?.PathName ?? "<no-doc>";
                if (_cached != null && _cacheKey == key) return _cached;

                var list = new List<PipeServicePattern>();
                try
                {
                    string basePath = StingTools.Core.StingToolsApp.FindDataFile(DataFileName);
                    if (!string.IsNullOrEmpty(basePath) && File.Exists(basePath))
                        ApplyOverlay(basePath, list);

                    if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                    {
                        string projDir = Path.GetDirectoryName(doc.PathName) ?? "";
                        string projPath = Path.Combine(projDir, ProjectOverrideRelPath);
                        if (File.Exists(projPath)) ApplyOverlay(projPath, list);
                    }
                }
                catch (Exception ex) { StingLog.Warn($"PipeServiceDetector.Patterns load: {ex.Message}"); }

                if (list.Count == 0) ApplyDefaults(list);
                _cached = list;
                _cacheKey = key;
                return _cached;
            }
        }

        private static void ApplyOverlay(string path, List<PipeServicePattern> list)
        {
            try
            {
                var jt = JObject.Parse(File.ReadAllText(path));
                var arr = jt["patterns"] as JArray;
                if (arr == null) return;
                foreach (var t in arr.OfType<JObject>())
                {
                    list.Add(new PipeServicePattern
                    {
                        Match     = (string)t["match"] ?? "",
                        ServiceId = (string)t["serviceId"] ?? DefaultServiceId,
                        Notes     = (string)t["notes"] ?? ""
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"PipeServiceDetector.ApplyOverlay {path}: {ex.Message}"); }
        }

        private static void ApplyDefaults(List<PipeServicePattern> list)
        {
            // Minimal fallback when JSON is missing — covers the common services.
            list.AddRange(new[]
            {
                new PipeServicePattern { Match = "CHW",  ServiceId = "chw" },
                new PipeServicePattern { Match = "HWS",  ServiceId = "hws" },
                new PipeServicePattern { Match = "HW",   ServiceId = "hws" },
                new PipeServicePattern { Match = "DCW",  ServiceId = "dcw" },
                new PipeServicePattern { Match = "DHW",  ServiceId = "dhw" },
                new PipeServicePattern { Match = "COND", ServiceId = "condensate" },
                new PipeServicePattern { Match = "RG",   ServiceId = "refrig_gas" },
                new PipeServicePattern { Match = "RL",   ServiceId = "refrig_liq" },
                new PipeServicePattern { Match = "STM",  ServiceId = "steam" },
                new PipeServicePattern { Match = "NG",   ServiceId = "natural_gas" }
            });
        }
    }
}
