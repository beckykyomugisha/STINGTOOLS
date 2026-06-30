// StingTools — Green scheme registry (Phase 195).
//
// Loads STING_GREEN_SCHEMES.json (corporate baseline) + an optional project
// override merged by id. Pure POCO — no Revit dependency. The Revit command
// layer resolves the two file paths and hands them to LoadFromJson; the
// per-document cache lives in SustainabilityRegistries (Revit-facing).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Sustainability
{
    public class SchemeGate
    {
        public string Id        { get; set; } = "";
        public string Label     { get; set; } = "";
        public string Metric    { get; set; } = "";
        public string Provider  { get; set; } = "";
        public string Operator  { get; set; } = ">=";
        public bool   Required    { get; set; }
        public string Unit        { get; set; } = "";
        public string Delegated   { get; set; } = "";   // e.g. EDGE_APP
        public bool   ThresholdBool { get; set; }        // for "==" against a bool
        public bool   HasThresholdBool { get; set; }
        /// <summary>Step function (pct -> pts), ascending by pct. Empty for gate-style.</summary>
        public List<PointStep> Points { get; } = new List<PointStep>();
    }

    public class PointStep
    {
        public double Pct { get; set; }
        public int    Pts { get; set; }
    }

    public class GreenScheme
    {
        public string Id          { get; set; } = "";
        public string Name        { get; set; } = "";
        public string Version     { get; set; } = "";
        public string DefaultLevel { get; set; } = "";
        public string Aggregation { get; set; } = "all_required";  // all_required | pointSum
        public bool   Phase2        { get; set; }
        /// <summary>Per-level gate thresholds: level -> (gateId -> threshold).</summary>
        public Dictionary<string, Dictionary<string, double>> Levels { get; }
            = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        /// <summary>pointSum band thresholds: band -> min points.</summary>
        public Dictionary<string, int> CertificationBands { get; }
            = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public List<SchemeGate> Gates { get; } = new List<SchemeGate>();
    }

    public class GreenSchemeRegistry
    {
        private readonly List<GreenScheme> _schemes = new List<GreenScheme>();

        public IReadOnlyList<GreenScheme> All => _schemes;

        public GreenScheme Get(string id)
            => _schemes.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Build a registry from a corporate JSON + an optional project
        /// override JSON (both raw strings; project wins by id).</summary>
        public static GreenSchemeRegistry LoadFromJson(string corporateJson, string projectJson = null)
        {
            var reg = new GreenSchemeRegistry();
            if (!string.IsNullOrWhiteSpace(corporateJson)) reg.Apply(corporateJson);
            if (!string.IsNullOrWhiteSpace(projectJson))   reg.Apply(projectJson);
            return reg;
        }

        /// <summary>Convenience loader from file paths (used by the Revit layer).</summary>
        public static GreenSchemeRegistry LoadFromFiles(string corporatePath, string projectPath)
        {
            string corp = SafeRead(corporatePath);
            string proj = SafeRead(projectPath);
            return LoadFromJson(corp, proj);
        }

        private static string SafeRead(string path)
        {
            try { return !string.IsNullOrEmpty(path) && File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (Exception ex) { SustainOverrideHealth.Report("GreenScheme", $"read failed for {path}: {ex.Message}"); return null; }
        }

        private void Apply(string json)
        {
            JObject root;
            try { root = JObject.Parse(json); } catch (Exception ex) { SustainOverrideHealth.Report("GreenScheme", $"malformed override/data JSON: {ex.Message}"); return; }
            var arr = root["schemes"] as JArray;
            if (arr == null) return;
            foreach (var s in arr.OfType<JObject>())
            {
                var scheme = ParseScheme(s);
                int existing = _schemes.FindIndex(x => string.Equals(x.Id, scheme.Id, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0) _schemes[existing] = scheme;   // project override wins by id
                else _schemes.Add(scheme);
            }
        }

        private static GreenScheme ParseScheme(JObject s)
        {
            var scheme = new GreenScheme
            {
                Id          = (string)s["id"] ?? "",
                Name        = (string)s["name"] ?? (string)s["id"] ?? "",
                Version     = (string)s["version"] ?? "",
                DefaultLevel = (string)s["level"] ?? "",
                Aggregation = (string)s["aggregation"] ?? "all_required",
                Phase2       = (bool?)s["phase2"] ?? false
            };

            if (s["levels"] is JObject levels)
                foreach (var lvl in levels.Properties())
                {
                    var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    if (lvl.Value is JObject lo)
                        foreach (var g in lo.Properties())
                            map[g.Name] = (double?)g.Value ?? 0;
                    scheme.Levels[lvl.Name] = map;
                }

            if (s["certificationBands"] is JObject bands)
                foreach (var b in bands.Properties())
                    scheme.CertificationBands[b.Name] = (int?)b.Value ?? 0;

            if (s["gates"] is JArray gates)
                foreach (var g in gates.OfType<JObject>())
                {
                    var gate = new SchemeGate
                    {
                        Id        = (string)g["id"] ?? "",
                        Label     = (string)g["label"] ?? (string)g["id"] ?? "",
                        Metric    = (string)g["metric"] ?? "",
                        Provider  = (string)g["provider"] ?? "",
                        Operator  = (string)g["operator"] ?? ">=",
                        Required   = (bool?)g["required"] ?? false,
                        Unit       = (string)g["unit"] ?? "",
                        Delegated  = (string)g["delegated"] ?? ""
                    };
                    var thr = g["threshold"];
                    if (thr != null && thr.Type == JTokenType.Boolean)
                    {
                        gate.HasThresholdBool = true;
                        gate.ThresholdBool = (bool)thr;
                    }
                    if (g["points"] is JArray pts)
                        foreach (var p in pts.OfType<JObject>())
                            gate.Points.Add(new PointStep
                            {
                                Pct = (double?)p["pct"] ?? 0,
                                Pts = (int?)p["pts"] ?? 0
                            });
                    scheme.Gates.Add(gate);
                }

            return scheme;
        }
    }
}
