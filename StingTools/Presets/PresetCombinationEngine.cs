// ══════════════════════════════════════════════════════════════════════════
//  PresetCombinationEngine.cs — Phase 108m
//  Runs chained ModelCreate*/Place* commands from PRESET_COMBINATIONS.json
//  wrapped in a TransactionGroup so a preset either fully lands or fully
//  rolls back. Implements the preset-combinations feature — clicking one
//  ★ button replaces 8 manual placements + 3 tag ops.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Presets
{
    public class PresetParam
    {
        public string Name;
        public string Label;
        public string DefaultValue;
        public double? Min;
        public double? Max;
        public List<string> Choices = new List<string>();
    }

    public class PresetStep
    {
        public string CommandTag;
        public string Label;
        public JObject Params;        // raw params JObject — tokens resolved at runtime
        public bool Optional;
    }

    public class PresetDefinition
    {
        public string Id;
        public string Label;
        public string Description;
        public List<string> SectorTags = new List<string>();
        public List<PresetParam> SharedParams = new List<PresetParam>();
        public List<PresetStep> Steps = new List<PresetStep>();
        public List<PresetStep> OnComplete = new List<PresetStep>();
    }

    internal static class PresetCombinationEngine
    {
        private static List<PresetDefinition> _cache;
        private static string _cacheKey;

        public static List<PresetDefinition> LoadAll()
        {
            string path = Path.Combine(StingToolsApp.DataPath ?? "", "PRESET_COMBINATIONS.json");
            string key = path + "|" + (File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks.ToString() : "0");
            if (_cache != null && key == _cacheKey) return _cache;

            var list = new List<PresetDefinition>();
            if (!File.Exists(path)) { _cache = list; _cacheKey = key; return list; }

            try
            {
                var root = JObject.Parse(File.ReadAllText(path));
                var presets = root["presets"] as JArray;
                if (presets == null) { _cache = list; _cacheKey = key; return list; }

                foreach (var p in presets.OfType<JObject>())
                {
                    var def = new PresetDefinition
                    {
                        Id = p["id"]?.ToString() ?? "",
                        Label = p["label"]?.ToString() ?? "",
                        Description = p["description"]?.ToString() ?? ""
                    };
                    if (p["sectorTags"] is JArray tags)
                        foreach (var t in tags) def.SectorTags.Add(t.ToString());
                    if (p["sharedParams"] is JObject sp)
                        foreach (var kv in sp.Properties())
                        {
                            var pparam = new PresetParam { Name = kv.Name };
                            var body = kv.Value as JObject;
                            if (body != null)
                            {
                                pparam.Label = body["label"]?.ToString() ?? kv.Name;
                                pparam.DefaultValue = body["default"]?.ToString() ?? "";
                                if (body["min"] != null && double.TryParse(body["min"].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double mn)) pparam.Min = mn;
                                if (body["max"] != null && double.TryParse(body["max"].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double mx)) pparam.Max = mx;
                                if (body["choices"] is JArray ch)
                                    foreach (var c in ch) pparam.Choices.Add(c.ToString());
                            }
                            def.SharedParams.Add(pparam);
                        }
                    def.Steps      = ParseSteps(p["steps"] as JArray);
                    def.OnComplete = ParseSteps(p["onComplete"] as JArray);
                    list.Add(def);
                }
            }
            catch (Exception ex) { StingLog.Warn($"PresetCombinationEngine.LoadAll: {ex.Message}"); }

            _cache = list; _cacheKey = key;
            return list;
        }

        private static List<PresetStep> ParseSteps(JArray arr)
        {
            var list = new List<PresetStep>();
            if (arr == null) return list;
            foreach (var s in arr.OfType<JObject>())
            {
                list.Add(new PresetStep
                {
                    CommandTag = s["cmd"]?.ToString() ?? "",
                    Label = s["label"]?.ToString() ?? "",
                    Optional = s["optional"]?.Value<bool>() ?? false,
                    Params = s["params"] as JObject
                });
            }
            return list;
        }

        public static PresetDefinition GetById(string id)
            => LoadAll().FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Substitute @token placeholders from sharedValues into each step's params.</summary>
        public static Dictionary<string, string> ResolveParams(JObject rawParams, Dictionary<string, string> sharedValues)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (rawParams == null) return d;
            foreach (var kv in rawParams.Properties())
            {
                string v = kv.Value?.ToString() ?? "";
                if (v.StartsWith("@") && sharedValues != null && sharedValues.TryGetValue(v.Substring(1), out string sub))
                    v = sub;
                d[kv.Name] = v;
            }
            return d;
        }
    }
}
