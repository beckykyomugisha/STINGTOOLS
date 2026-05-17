using StingTools.Core;
// PlumbingSystemConfig — Phase 179a foundation.
//
// Project-scoped configuration that every other plumbing engine reads:
// building type → K factor, drainage / supply standard, pipe materials
// per service, velocity limits, slope rules. Persisted to
// <project>/_BIM_COORD/plumbing_system_config.json. Idempotent: missing
// file → returns Defaults() so engines never hard-fail.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Autodesk.Revit.DB;

namespace StingTools.Core.Plumbing
{
    public class PlumbingSystemConfig
    {
        public string Version       { get; set; } = "179a";
        public string BuildingType  { get; set; } = "Office";
        public string DrainStandard { get; set; } = "BS-EN-12056";   // BS-EN-12056 | IPC-2021 | MANUAL
        public string SupplyStandard{ get; set; } = "BS-EN-806";     // BS-EN-806   | HUNTER-WSFU | MANUAL
        public double KFactor       { get; set; } = 0.5;
        public bool   FlushValveMajority { get; set; } = false;
        public int    OccupancyCount     { get; set; } = 0;
        public int    BedsOrWorkstations { get; set; } = 0;

        public Dictionary<string, string> Materials { get; set; } = new Dictionary<string, string>
        {
            { "DCW",      "COPPER_R250"  },
            { "DHW",      "COPPER_R250"  },
            { "Drainage", "UPVC_DRAIN"   },
            { "Storm",    "UPVC_DRAIN"   },
            { "Vent",     "UPVC_DRAIN"   }
        };

        public Dictionary<string, double> VelocityMps { get; set; } = new Dictionary<string, double>
        {
            { "DCW_Max",            2.0 },
            { "DHW_Max",            1.5 },
            { "Drain_SelfCleansing",0.7 },
            { "Drain_Max",          3.5 }
        };

        public Dictionary<string, double> SlopePctMin { get; set; } = new Dictionary<string, double>
        {
            { "DN32_50",  2.0  },
            { "DN75_100", 1.0  },
            { "DN150",    0.67 },
            { "Target",   1.25 }
        };

        public double MaxFillRatio  { get; set; } = 0.50;
        public double SupplyPressureBarAtEntry { get; set; } = 3.0;
        public double MaxPressureDropPaPerM     { get; set; } = 300.0;
        public double FittingsEquivLengthFactor { get; set; } = 1.30;
        public string LastSavedUtc  { get; set; } = "";

        // ── Defaults ──
        public static PlumbingSystemConfig Defaults() => new PlumbingSystemConfig();

        public static PlumbingSystemConfig DefaultsForBuildingType(string buildingType)
        {
            var c = Defaults();
            c.BuildingType = buildingType ?? "Office";
            switch ((buildingType ?? "").ToUpperInvariant())
            {
                case "DWELLING":
                case "OFFICE":      c.KFactor = 0.5; break;
                case "HOSPITAL":
                case "SCHOOL":
                case "HOTEL":       c.KFactor = 0.7; break;
                case "RESTAURANT":
                case "FACTORY":
                case "SPORTS":      c.KFactor = 1.0; break;
                case "PUBLICWC":
                case "PUBLIC WC":   c.KFactor = 1.2; break;
                default:            c.KFactor = 0.7; break;
            }
            return c;
        }

        // ── Load / Save ──
        public static string ProjectConfigPath(Document doc)
        {
            try
            {
                if (doc == null || string.IsNullOrEmpty(doc.PathName)) return null;
                var dir = Path.GetDirectoryName(doc.PathName);
                if (string.IsNullOrEmpty(dir)) return null;
                var coord = Path.Combine(dir, "_BIM_COORD");
                return Path.Combine(coord, "plumbing_system_config.json");
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        public static PlumbingSystemConfig Load(Document doc)
        {
            try
            {
                var path = ProjectConfigPath(doc);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return ReadFromProjectInfo(doc) ?? Defaults();
                var text = File.ReadAllText(path);
                var c = JsonConvert.DeserializeObject<PlumbingSystemConfig>(text);
                if (c == null) return Defaults();
                if (c.Materials   == null) c.Materials   = Defaults().Materials;
                if (c.VelocityMps == null) c.VelocityMps = Defaults().VelocityMps;
                if (c.SlopePctMin == null) c.SlopePctMin = Defaults().SlopePctMin;
                return c;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlumbingSystemConfig.Load: {ex.Message}");
                return Defaults();
            }
        }

        public static string Save(Document doc, PlumbingSystemConfig cfg)
        {
            if (cfg == null) return null;
            try
            {
                cfg.LastSavedUtc = DateTime.UtcNow.ToString("o");
                var path = ProjectConfigPath(doc);
                if (string.IsNullOrEmpty(path))
                {
                    StingLog.Warn("PlumbingSystemConfig.Save: project not saved — config skipped");
                    return null;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(cfg, Formatting.Indented));
                StampProjectInfo(doc, cfg);
                StingLog.Info($"PlumbingSystemConfig saved: {path}");
                return path;
            }
            catch (Exception ex)
            {
                StingLog.Error("PlumbingSystemConfig.Save", ex);
                return null;
            }
        }

        // ── ProjectInformation stamping (so the config travels with the .rvt) ──
        private static void StampProjectInfo(Document doc, PlumbingSystemConfig cfg)
        {
            try
            {
                var pi = doc?.ProjectInformation;
                if (pi == null) return;
                TrySetParam(pi, ParamRegistry.PLM_BLDG_TYPE, cfg.BuildingType);
                TrySetParam(pi, ParamRegistry.PLM_K_FACTOR,  cfg.KFactor.ToString("F2"));
                TrySetParam(pi, ParamRegistry.PLM_STD_DRAIN, cfg.DrainStandard);
                TrySetParam(pi, ParamRegistry.PLM_STD_SUPPLY,cfg.SupplyStandard);
                TrySetParam(pi, ParamRegistry.PLM_MAT_DCW,   GetOrDefault(cfg.Materials, "DCW",      "COPPER_R250"));
                TrySetParam(pi, ParamRegistry.PLM_MAT_DHW,   GetOrDefault(cfg.Materials, "DHW",      "COPPER_R250"));
                TrySetParam(pi, ParamRegistry.PLM_MAT_DRN,   GetOrDefault(cfg.Materials, "Drainage", "UPVC_DRAIN"));
                TrySetParam(pi, ParamRegistry.PLM_MAT_VNT,   GetOrDefault(cfg.Materials, "Vent",     "UPVC_DRAIN"));
                // Mirror to legacy plumbing-code switch so DrainageSizer / VentDesigner picks up.
                TrySetParam(pi, ParamRegistry.PRJ_PLUMBING_CODE,
                    cfg.DrainStandard.StartsWith("IPC", StringComparison.OrdinalIgnoreCase) ? "IPC-US" : "BS-UK");
            }
            catch (Exception ex) { StingLog.Warn($"PlumbingSystemConfig.StampProjectInfo: {ex.Message}"); }
        }

        private static PlumbingSystemConfig ReadFromProjectInfo(Document doc)
        {
            try
            {
                var pi = doc?.ProjectInformation;
                if (pi == null) return null;
                var c = Defaults();
                var bt = ReadString(pi, ParamRegistry.PLM_BLDG_TYPE);
                if (!string.IsNullOrEmpty(bt)) c.BuildingType = bt;
                var k  = ReadString(pi, ParamRegistry.PLM_K_FACTOR);
                if (!string.IsNullOrEmpty(k) && double.TryParse(k, out var kv) && kv > 0) c.KFactor = kv;
                var sd = ReadString(pi, ParamRegistry.PLM_STD_DRAIN);
                if (!string.IsNullOrEmpty(sd)) c.DrainStandard  = sd;
                var ss = ReadString(pi, ParamRegistry.PLM_STD_SUPPLY);
                if (!string.IsNullOrEmpty(ss)) c.SupplyStandard = ss;
                var m = ReadString(pi, ParamRegistry.PLM_MAT_DCW); if (!string.IsNullOrEmpty(m)) c.Materials["DCW"] = m;
                m     = ReadString(pi, ParamRegistry.PLM_MAT_DHW); if (!string.IsNullOrEmpty(m)) c.Materials["DHW"] = m;
                m     = ReadString(pi, ParamRegistry.PLM_MAT_DRN); if (!string.IsNullOrEmpty(m)) c.Materials["Drainage"] = m;
                m     = ReadString(pi, ParamRegistry.PLM_MAT_VNT); if (!string.IsNullOrEmpty(m)) c.Materials["Vent"]     = m;
                return c;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        private static void TrySetParam(Element el, string name, string value)
        {
            try
            {
                var p = el?.LookupParameter(name);
                if (p == null || p.IsReadOnly) return;
                if (p.StorageType == StorageType.String) p.Set(value ?? "");
                else if (p.StorageType == StorageType.Double && double.TryParse(value, out var dv)) p.Set(dv);
                else if (p.StorageType == StorageType.Integer && int.TryParse(value, out var iv)) p.Set(iv);
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }

        private static string ReadString(Element el, string name)
        {
            try
            {
                var p = el?.LookupParameter(name);
                if (p == null || !p.HasValue) return "";
                if (p.StorageType == StorageType.String)  return p.AsString() ?? "";
                if (p.StorageType == StorageType.Double)  return p.AsDouble().ToString("F2");
                if (p.StorageType == StorageType.Integer) return p.AsInteger().ToString();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return "";
        }

        private static string GetOrDefault(IDictionary<string, string> d, string k, string fallback)
            => (d != null && d.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v)) ? v : fallback;

        // ── Convenience accessors that fall back to defaults ──
        public string MaterialFor(string service)
            => GetOrDefault(Materials, service, "COPPER_R250");

        public double VelocityMaxFor(string key)
            => (VelocityMps != null && VelocityMps.TryGetValue(key, out var v)) ? v : 2.0;

        public double SlopeMinFor(int dnMm)
        {
            if (SlopePctMin == null) return 1.0;
            if (dnMm <= 50)  return SlopePctMin.TryGetValue("DN32_50",  out var a) ? a : 2.0;
            if (dnMm <= 100) return SlopePctMin.TryGetValue("DN75_100", out var b) ? b : 1.0;
            return SlopePctMin.TryGetValue("DN150", out var c) ? c : 0.67;
        }
    }
}
