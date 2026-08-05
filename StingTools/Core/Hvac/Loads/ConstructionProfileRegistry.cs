// StingTools — Construction profile registry.
//
// Replaces hardcoded U-values (0.30 wall, 0.20 roof, 1.4 window) in
// HvacBlockLoadCommand.AddPerimeterEnvelope with a project-tunable
// lookup keyed by PRJ_CONSTRUCTION_PROFILE_TXT on ProjectInformation.
//
// Layered:
//   corporate baseline → Data/STING_CONSTRUCTION_PROFILES.json
//   project override   → <project>/_BIM_COORD/construction_profiles.json
//
// Default profile: "PartL2021" — closest to STING's design-engineer
// audience baseline.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Hvac.Loads
{
    public class ConstructionProfile
    {
        public string Id    { get; set; } = "";
        public string Label { get; set; } = "";
        public double WallUvalue   { get; set; } = 0.30;
        public double RoofUvalue   { get; set; } = 0.20;
        public double FloorUvalue  { get; set; } = 0.25;
        public double WindowUvalue { get; set; } = 1.40;
        public double DoorUvalue   { get; set; } = 1.80;
        public double WindowSHGC          { get; set; } = 0.40;
        public double WindowShadingFactor { get; set; } = 0.90;
    }

    public class ConstructionProfileLibrary
    {
        public Dictionary<string, ConstructionProfile> ById { get; }
            = new Dictionary<string, ConstructionProfile>(StringComparer.OrdinalIgnoreCase);

        public ConstructionProfile Get(string id)
        {
            if (!string.IsNullOrWhiteSpace(id) && ById.TryGetValue(id, out var hit)) return hit;
            return ById.TryGetValue("PartL2021", out var def)
                ? def
                : new ConstructionProfile { Id = "default" };
        }
    }

    public static class ConstructionProfileRegistry
    {
        public const string DataFileName = "STING_CONSTRUCTION_PROFILES.json";
        public const string ProjectOverrideRelPath = "_BIM_COORD/construction_profiles.json";
        public const string ProjectInfoParam = "PRJ_CONSTRUCTION_PROFILE_TXT";

        private static readonly ConcurrentDictionary<string, ConstructionProfileLibrary> _cache
            = new ConcurrentDictionary<string, ConstructionProfileLibrary>(StringComparer.OrdinalIgnoreCase);

        public static ConstructionProfileLibrary Get(Document doc)
        {
            string key = doc?.PathName ?? "<no-doc>";
            return _cache.GetOrAdd(key, _ => Load(doc));
        }

        public static void Reload()              => _cache.Clear();
        public static void Reload(Document doc)  => _cache.TryRemove(doc?.PathName ?? "<no-doc>", out _);

        /// <summary>
        /// Resolve the active profile for a document via the
        /// <see cref="ProjectInfoParam"/> stamp; falls back to PartL2021.
        /// </summary>
        public static ConstructionProfile Active(Document doc)
        {
            var lib = Get(doc);
            string id = null;
            try { id = doc?.ProjectInformation?.LookupParameter(ProjectInfoParam)?.AsString(); } catch { }
            return lib.Get(id);
        }

        private static ConstructionProfileLibrary Load(Document doc)
        {
            var lib = new ConstructionProfileLibrary();
            try
            {
                string basePath = StingTools.Core.StingToolsApp.FindDataFile(DataFileName);
                if (!string.IsNullOrEmpty(basePath) && File.Exists(basePath))
                    Apply(JObject.Parse(File.ReadAllText(basePath)), lib);
                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string projDir = Path.GetDirectoryName(doc.PathName) ?? "";
                    string projPath = Path.Combine(projDir, ProjectOverrideRelPath);
                    if (File.Exists(projPath))
                        Apply(JObject.Parse(File.ReadAllText(projPath)), lib);
                }
            }
            catch (Exception ex)
            { StingTools.Core.StingLog.Error("ConstructionProfileRegistry.Load", ex); }

            if (!lib.ById.ContainsKey("PartL2021"))
                lib.ById["PartL2021"] = new ConstructionProfile { Id = "PartL2021", Label = "fallback" };
            return lib;
        }

        private static void Apply(JObject j, ConstructionProfileLibrary lib)
        {
            var profiles = j["profiles"] as JArray;
            if (profiles == null) return;
            foreach (var p in profiles.OfType<JObject>())
            {
                var cp = new ConstructionProfile
                {
                    Id    = (string)p["id"] ?? "",
                    Label = (string)p["label"] ?? "",
                    WallUvalue          = (double?)p["wallUvalue"]          ?? 0.30,
                    RoofUvalue          = (double?)p["roofUvalue"]          ?? 0.20,
                    FloorUvalue         = (double?)p["floorUvalue"]         ?? 0.25,
                    WindowUvalue        = (double?)p["windowUvalue"]        ?? 1.40,
                    DoorUvalue          = (double?)p["doorUvalue"]          ?? 1.80,
                    WindowSHGC          = (double?)p["windowSHGC"]          ?? 0.40,
                    WindowShadingFactor = (double?)p["windowShadingFactor"] ?? 0.90
                };
                if (!string.IsNullOrEmpty(cp.Id)) lib.ById[cp.Id] = cp;
            }
        }
    }
}
