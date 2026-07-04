// StingTools — GreenPhysicsRegistry (SUS-5).
//
// Loads the last in-code magic numbers of the sustainability engine from data:
//  - vertical-solar transposition coefficients (base / range),
//  - the DHW delivery target (degC),
//  - the indicative per-material-class A1-A3 carbon factors + default.
// Corporate baseline STING_GREEN_PHYSICS.json + a project override at
// <project>/_BIM_COORD/sustainability/green_physics.json (additive merge). Every value
// keeps an in-code default, so a missing/partial file reproduces the prior behaviour;
// a malformed override is surfaced via SustainOverrideHealth (never silently swallowed).
//
// Cached per document; reuses the SustainabilityRegistries path helpers.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Sustainability
{
    public class GreenPhysicsRegistry
    {
        public double SolarBase  = 0.27;
        public double SolarRange = 0.35;
        public double DhwTargetC = 45.0;
        public double IndicativeDefaultKgCo2ePerKg = 0.50;
        public readonly Dictionary<string, double> IndicativeClassFactors =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        public const string DataFileName = "STING_GREEN_PHYSICS.json";
        public const string ProjectOverrideFile = "green_physics.json";

        private static readonly ConcurrentDictionary<string, GreenPhysicsRegistry> _cache =
            new ConcurrentDictionary<string, GreenPhysicsRegistry>();

        public static GreenPhysicsRegistry Active(Document doc)
            => _cache.GetOrAdd(doc?.PathName ?? "<no-doc>", _ => Load(doc));

        public static void Invalidate(Document doc)
            => _cache.TryRemove(doc?.PathName ?? "<no-doc>", out _);

        private static GreenPhysicsRegistry Load(Document doc)
        {
            var reg = new GreenPhysicsRegistry();
            string corp = null;
            try { corp = StingTools.Core.StingToolsApp.FindDataFile(DataFileName); } catch { }
            reg.Apply(SafeRead(corp));
            reg.Apply(SafeRead(SustainabilityRegistries.Proj(doc, ProjectOverrideFile)));
            return reg;
        }

        private static string SafeRead(string path)
        {
            try { return !string.IsNullOrEmpty(path) && File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (Exception ex) { SustainOverrideHealth.Report("GreenPhysics", $"read failed for {path}: {ex.Message}"); return null; }
        }

        private void Apply(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception ex) { SustainOverrideHealth.Report("GreenPhysics", $"malformed override/data JSON: {ex.Message}"); return; }
            SustainOverrideHealth.CheckSchema("GreenPhysics", (string)root["schema"], "sting.green.physics/");   // SUS-7

            if (root["verticalSolar"] is JObject vs)
            {
                if (vs["base"]  != null) SolarBase  = (double)vs["base"];
                if (vs["range"] != null) SolarRange = (double)vs["range"];
            }
            if (root["dhw"] is JObject dhw && dhw["targetC"] != null)
                DhwTargetC = (double)dhw["targetC"];

            if (root["indicativeCarbon"] is JObject ic)
            {
                if (ic["defaultKgCo2ePerKg"] != null) IndicativeDefaultKgCo2ePerKg = (double)ic["defaultKgCo2ePerKg"];
                if (ic["classFactorsKgCo2ePerKg"] is JObject cf)
                    foreach (var p in cf.Properties())          // additive merge (project adds/overrides keys)
                        IndicativeClassFactors[p.Name] = (double)p.Value;
            }
        }
    }
}
