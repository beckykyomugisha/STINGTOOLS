// LpsSldRules.cs — loader for STING_LPS_SLD_RULES.json.
//
// Layers a project override at
// <project>/_BIM_COORD/lps_sld_rules.json over the corporate baseline
// in Data/STING_LPS_SLD_RULES.json. Used by LpsSldEngine to look up
// row Y positions, column pitch, box dimensions, label prefixes
// instead of compile-time constants.

using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Core.Lightning
{
    public class LpsSldRules
    {
        // ── Rows (feet) ────────────────────────────────────────────────
        public double Y_AirTerminal    { get; set; } =  50.0;
        public double Y_DownConductor  { get; set; } =  20.0;
        public double Y_Spd            { get; set; } =   5.0;
        public double Y_MainEarthBar   { get; set; } =   0.0;
        public double Y_EarthElectrode { get; set; } = -25.0;

        // ── Spacing (feet) ─────────────────────────────────────────────
        public double ColumnPitch       { get; set; } = 6.0;
        public double BoxWidth          { get; set; } = 4.0;
        public double BoxHeight         { get; set; } = 2.0;
        public double MebPad            { get; set; } = 2.0;
        public double SpdHorizontalOffset { get; set; } = 6.0;
        public double SpdVerticalPitch  { get; set; } = 3.0;

        // ── Appearance ─────────────────────────────────────────────────
        public bool   DrawMainEarthBarDouble { get; set; } = true;
        public bool   ShowLegend             { get; set; } = true;
        public bool   ShowTitleBlock         { get; set; } = true;
        public double LegendOffset           { get; set; } = 4.0;

        // ── Labels ─────────────────────────────────────────────────────
        public string AirTerminalPrefix    { get; set; } = "★ AT-";
        public string DownConductorPrefix  { get; set; } = "DC-";
        public string EarthElectrodePrefix { get; set; } = "⏚ EE-";
        public string SpdPrefix            { get; set; } = "★ SPD-";
        public string Title    { get; set; } = "LIGHTNING PROTECTION — SINGLE LINE DIAGRAM";
        public string Subtitle { get; set; } = "BS EN 62305-3 / IEC 62305-3";

        // ── Cache ──────────────────────────────────────────────────────
        private static readonly object _lock = new object();
        private static readonly Dictionary<string, LpsSldRules> _cache
            = new Dictionary<string, LpsSldRules>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan _ttl = TimeSpan.FromMinutes(10);
        private static readonly Dictionary<string, DateTime> _ages
            = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        public static LpsSldRules Get(Document doc)
        {
            string key = doc?.PathName ?? "<no-doc>";
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var existing) &&
                    _ages.TryGetValue(key, out var built) &&
                    (DateTime.UtcNow - built) < _ttl)
                    return existing;
            }
            var rules = LoadFromDisk(doc);
            lock (_lock)
            {
                _cache[key] = rules;
                _ages[key]  = DateTime.UtcNow;
            }
            return rules;
        }

        public static void Reload()
        {
            lock (_lock) { _cache.Clear(); _ages.Clear(); }
        }

        private static LpsSldRules LoadFromDisk(Document doc)
        {
            var rules = new LpsSldRules();

            // Tier 1 — corporate baseline
            try
            {
                string corpPath = StingToolsApp.FindDataFile("STING_LPS_SLD_RULES.json");
                if (!string.IsNullOrEmpty(corpPath) && File.Exists(corpPath))
                {
                    MergeFrom(rules, JObject.Parse(File.ReadAllText(corpPath)));
                }
            }
            catch (Exception ex) { StingLog.Warn($"LpsSldRules corp: {ex.Message}"); }

            // Tier 2 — project override at <project>/_BIM_COORD/lps_sld_rules.json
            try
            {
                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string projDir = Path.GetDirectoryName(doc.PathName);
                    if (!string.IsNullOrEmpty(projDir))
                    {
                        string ovPath = Path.Combine(projDir, "_BIM_COORD", "lps_sld_rules.json");
                        if (File.Exists(ovPath))
                            MergeFrom(rules, JObject.Parse(File.ReadAllText(ovPath)));
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"LpsSldRules project override: {ex.Message}"); }

            return rules;
        }

        private static void MergeFrom(LpsSldRules dst, JObject root)
        {
            if (root == null) return;

            var rows = root["rows"] as JObject;
            if (rows != null)
            {
                if (rows["airTerminalBandY"]    != null) dst.Y_AirTerminal    = rows["airTerminalBandY"].Value<double>();
                if (rows["downConductorMidY"]   != null) dst.Y_DownConductor  = rows["downConductorMidY"].Value<double>();
                if (rows["spdBranchY"]          != null) dst.Y_Spd            = rows["spdBranchY"].Value<double>();
                if (rows["mainEarthBarY"]       != null) dst.Y_MainEarthBar   = rows["mainEarthBarY"].Value<double>();
                if (rows["earthElectrodeBandY"] != null) dst.Y_EarthElectrode = rows["earthElectrodeBandY"].Value<double>();
            }

            var sp = root["spacing"] as JObject;
            if (sp != null)
            {
                if (sp["columnPitchFt"]         != null) dst.ColumnPitch         = sp["columnPitchFt"].Value<double>();
                if (sp["boxWidthFt"]            != null) dst.BoxWidth            = sp["boxWidthFt"].Value<double>();
                if (sp["boxHeightFt"]           != null) dst.BoxHeight           = sp["boxHeightFt"].Value<double>();
                if (sp["mainEarthBarPadFt"]     != null) dst.MebPad              = sp["mainEarthBarPadFt"].Value<double>();
                if (sp["spdHorizontalOffsetFt"] != null) dst.SpdHorizontalOffset = sp["spdHorizontalOffsetFt"].Value<double>();
                if (sp["spdVerticalPitchFt"]    != null) dst.SpdVerticalPitch    = sp["spdVerticalPitchFt"].Value<double>();
            }

            var ap = root["appearance"] as JObject;
            if (ap != null)
            {
                if (ap["drawMainEarthBarDouble"] != null) dst.DrawMainEarthBarDouble = ap["drawMainEarthBarDouble"].Value<bool>();
                if (ap["showLegend"]             != null) dst.ShowLegend             = ap["showLegend"].Value<bool>();
                if (ap["showTitleBlock"]         != null) dst.ShowTitleBlock         = ap["showTitleBlock"].Value<bool>();
                if (ap["legendOffsetFt"]         != null) dst.LegendOffset           = ap["legendOffsetFt"].Value<double>();
            }

            var lt = root["labelTemplates"] as JObject;
            if (lt != null)
            {
                if (lt["airTerminalPrefix"]    != null) dst.AirTerminalPrefix    = lt["airTerminalPrefix"].ToString();
                if (lt["downConductorPrefix"]  != null) dst.DownConductorPrefix  = lt["downConductorPrefix"].ToString();
                if (lt["earthElectrodePrefix"] != null) dst.EarthElectrodePrefix = lt["earthElectrodePrefix"].ToString();
                if (lt["spdPrefix"]            != null) dst.SpdPrefix            = lt["spdPrefix"].ToString();
                if (lt["title"]                != null) dst.Title                = lt["title"].ToString();
                if (lt["subtitle"]             != null) dst.Subtitle             = lt["subtitle"].ToString();
            }
        }
    }
}
