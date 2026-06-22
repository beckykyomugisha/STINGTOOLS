// StingTools — MEP System Type Registry (Phase A: System Type Materializer).
//
// Single source of truth for the Revit MEP *system types* STING creates and
// maintains in a project (duct -> MechanicalSystemType, pipe -> PipingSystemType).
// Each definition carries the Revit system CLASSIFICATION (which the 19 MEP AEC
// filters key off via RBS_SYSTEM_CLASSIFICATION_PARAM) plus the STING tag tokens,
// so materializing them is what makes the system-colour filters + System Browser
// light up.
//
// Layered baseline + project override, mirroring MepSizingRegistry /
// DrawingTypeRegistry / AecFilterRegistry / ViewStylePackRegistry:
//   corporate baseline -> Data/STING_MEP_SYSTEM_TYPES.json
//   project override   -> <project>/_BIM_COORD/mep_system_types.json  (merged by id, project wins)

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Mep
{
    /// <summary>
    /// One MEP system-type definition (duct or pipe) loaded from
    /// STING_MEP_SYSTEM_TYPES.json + project override.
    /// </summary>
    public class MepSystemTypeDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>"duct" (MechanicalSystemType) | "pipe" (PipingSystemType).</summary>
        public string Discipline { get; set; } = "";
        /// <summary>MEPSystemClassification enum name (e.g. SupplyAir, DomesticColdWater).</summary>
        public string Classification { get; set; } = "";
        public string Abbreviation { get; set; } = "";
        public string StingSysCode { get; set; } = "";
        public string StingFuncCode { get; set; } = "";

        /// <summary>[r,g,b] 0..255 → MEPSystemType.LineColor. Null = leave default.</summary>
        public int[] LineColor { get; set; }
        /// <summary>1..16 → MEPSystemType.LineWeight. 0 = leave default.</summary>
        public int LineWeight { get; set; }
        /// <summary>Line-pattern name → MEPSystemType.LinePatternId. Empty = solid/default.</summary>
        public string LinePattern { get; set; } = "";
        /// <summary>Material name → MEPSystemType.MaterialId. Empty = leave default.</summary>
        public string Material { get; set; } = "";

        public string Uniclass { get; set; } = "";
        public string Cibse { get; set; } = "";
        public bool Enabled { get; set; } = true;

        public bool IsDuct => string.Equals(Discipline, "duct", StringComparison.OrdinalIgnoreCase);
        public bool IsPipe => string.Equals(Discipline, "pipe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Loaded view of STING_MEP_SYSTEM_TYPES.json + project override.</summary>
    public class MepSystemTypeRules
    {
        /// <summary>Definitions keyed by id (project override merges by id, project wins).</summary>
        public Dictionary<string, MepSystemTypeDef> ById { get; }
            = new Dictionary<string, MepSystemTypeDef>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Insertion-ordered list for deterministic materialization + reporting.</summary>
        public List<MepSystemTypeDef> All { get; } = new List<MepSystemTypeDef>();

        public IEnumerable<MepSystemTypeDef> Enabled => All.Where(d => d.Enabled);
        public IEnumerable<MepSystemTypeDef> Ducts   => Enabled.Where(d => d.IsDuct);
        public IEnumerable<MepSystemTypeDef> Pipes   => Enabled.Where(d => d.IsPipe);
    }

    /// <summary>
    /// Loader / cache for MepSystemTypeRules. Same layered baseline + project
    /// override + per-document cache pattern as MepSizingRegistry.
    /// </summary>
    public static class MepSystemTypeRegistry
    {
        private static readonly ConcurrentDictionary<string, MepSystemTypeRules> _cache
            = new ConcurrentDictionary<string, MepSystemTypeRules>(StringComparer.OrdinalIgnoreCase);

        public const string DataFileName = "STING_MEP_SYSTEM_TYPES.json";
        public const string ProjectOverrideRelPath = "_BIM_COORD/mep_system_types.json";

        /// <summary>Resolve the active definitions for a Revit document (cached by project file path).</summary>
        public static MepSystemTypeRules Get(Document doc)
        {
            string key = doc?.PathName ?? "<no-doc>";
            return _cache.GetOrAdd(key, _ => Load(doc));
        }

        /// <summary>Force a reload from disk for every cached project.</summary>
        public static void Reload() => _cache.Clear();

        /// <summary>Force a reload for a single document (e.g. after Save As).</summary>
        public static void Reload(Document doc)
        {
            string key = doc?.PathName ?? "<no-doc>";
            _cache.TryRemove(key, out _);
        }

        private static MepSystemTypeRules Load(Document doc)
        {
            var rules = new MepSystemTypeRules();
            try
            {
                // 1. Corporate baseline.
                string basePath = StingTools.Core.StingToolsApp.FindDataFile(DataFileName);
                if (!string.IsNullOrEmpty(basePath) && File.Exists(basePath))
                    Apply(JObject.Parse(File.ReadAllText(basePath)), rules);

                // 2. Project override (merge by id, project wins).
                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string projDir = Path.GetDirectoryName(doc.PathName) ?? "";
                    string projPath = Path.Combine(projDir, ProjectOverrideRelPath);
                    if (File.Exists(projPath))
                        Apply(JObject.Parse(File.ReadAllText(projPath)), rules);
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("MepSystemTypeRegistry.Load failed; using whatever loaded", ex);
            }
            return rules;
        }

        private static void Apply(JObject j, MepSystemTypeRules rules)
        {
            if (!(j["systemTypes"] is JArray arr)) return;
            foreach (var t in arr.OfType<JObject>())
            {
                string id = (string)t["id"] ?? "";
                if (string.IsNullOrWhiteSpace(id)) continue;

                // Merge onto an existing def by id (project override wins field-by-field
                // only for fields present in the override JSON), else create fresh.
                if (!rules.ById.TryGetValue(id, out var def))
                {
                    def = new MepSystemTypeDef { Id = id };
                    rules.ById[id] = def;
                    rules.All.Add(def);
                }

                def.Name          = (string)t["name"]          ?? def.Name;
                def.Discipline    = (string)t["discipline"]     ?? def.Discipline;
                def.Classification= (string)t["classification"] ?? def.Classification;
                def.Abbreviation  = (string)t["abbreviation"]   ?? def.Abbreviation;
                def.StingSysCode  = (string)t["stingSysCode"]   ?? def.StingSysCode;
                def.StingFuncCode = (string)t["stingFuncCode"]  ?? def.StingFuncCode;
                def.LinePattern   = (string)t["linePattern"]    ?? def.LinePattern;
                def.Material      = (string)t["material"]       ?? def.Material;
                def.Uniclass      = (string)t["uniclass"]       ?? def.Uniclass;
                def.Cibse         = (string)t["cibse"]          ?? def.Cibse;
                if (t["lineWeight"] != null) def.LineWeight = (int?)t["lineWeight"] ?? def.LineWeight;
                if (t["enabled"]    != null) def.Enabled    = (bool?)t["enabled"]  ?? def.Enabled;

                if (t["lineColor"] is JArray c && c.Count >= 3)
                {
                    try { def.LineColor = new[] { (int)c[0], (int)c[1], (int)c[2] }; }
                    catch { /* leave previous / null */ }
                }
            }
        }
    }
}
