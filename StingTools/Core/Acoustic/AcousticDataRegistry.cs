// StingTools — Acoustic data registry.
//
// Loads fan sound-power and silencer insertion-loss octave-band spectra
// from corporate baseline + project override JSON. Used by
// NcPredictionEngine + HvacNcPredictionCommand so the synthetic-Lw
// fallback can be replaced with real manufacturer data per family name.
//
// Lookup is a case-insensitive substring match against the supplied
// element-name string (typically `Element.Name`). Returns null when no
// match — caller falls back to its own default.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Acoustic
{
    public static class AcousticDataRegistry
    {
        public const string FanFileName       = "STING_FAN_SPECTRA.json";
        public const string SilencerFileName  = "STING_SILENCER_DATA.json";
        public const string FanOverrideRel    = "_BIM_COORD/fan_spectra.json";
        public const string SilencerOverrideRel = "_BIM_COORD/silencer_data.json";

        public class FanEntry      { public string Match = ""; public string Label = ""; public OctaveBand Lw; }
        public class SilencerEntry { public string Match = ""; public string Label = ""; public OctaveBand Il; }

        public class AcousticData
        {
            public List<FanEntry>      Fans      { get; } = new List<FanEntry>();
            public List<SilencerEntry> Silencers { get; } = new List<SilencerEntry>();

            /// <summary>Match by substring (case-insensitive) against the supplied name.
            /// Longest-match wins so "Trox AT 600 Y" hits "Trox AT" before a generic
            /// "AHU" partial.</summary>
            public FanEntry FindFan(string name)
            {
                if (string.IsNullOrWhiteSpace(name)) return null;
                string lo = name.ToLowerInvariant();
                return Fans
                    .Where(f => !string.IsNullOrEmpty(f.Match) && lo.Contains(f.Match.ToLowerInvariant()))
                    .OrderByDescending(f => f.Match.Length)
                    .FirstOrDefault();
            }
            public SilencerEntry FindSilencer(string name)
            {
                if (string.IsNullOrWhiteSpace(name)) return null;
                string lo = name.ToLowerInvariant();
                return Silencers
                    .Where(s => !string.IsNullOrEmpty(s.Match) && lo.Contains(s.Match.ToLowerInvariant()))
                    .OrderByDescending(s => s.Match.Length)
                    .FirstOrDefault();
            }
        }

        private static readonly ConcurrentDictionary<string, AcousticData> _cache
            = new ConcurrentDictionary<string, AcousticData>(StringComparer.OrdinalIgnoreCase);

        public static AcousticData Get(Document doc)
        {
            string key = doc?.PathName ?? "<no-doc>";
            return _cache.GetOrAdd(key, _ => Load(doc));
        }

        public static void Reload()              => _cache.Clear();
        public static void Reload(Document doc)  => _cache.TryRemove(doc?.PathName ?? "<no-doc>", out _);

        private static AcousticData Load(Document doc)
        {
            var data = new AcousticData();
            try
            {
                LoadFans(data, StingTools.Core.StingToolsApp.FindDataFile(FanFileName));
                LoadSilencers(data, StingTools.Core.StingToolsApp.FindDataFile(SilencerFileName));
                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string projDir = Path.GetDirectoryName(doc.PathName) ?? "";
                    LoadFans(data, Path.Combine(projDir, FanOverrideRel));
                    LoadSilencers(data, Path.Combine(projDir, SilencerOverrideRel));
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Error("AcousticDataRegistry.Load", ex); }
            return data;
        }

        private static void LoadFans(AcousticData data, string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                var j = JObject.Parse(File.ReadAllText(path));
                var arr = j["fans"] as JArray; if (arr == null) return;
                foreach (var t in arr.OfType<JObject>())
                {
                    var lw = (t["lw"] as JArray)?.Select(v => (double)v).ToArray();
                    if (lw == null || lw.Length != 8) continue;
                    data.Fans.Add(new FanEntry
                    {
                        Match = (string)t["match"] ?? "",
                        Label = (string)t["label"] ?? "",
                        Lw    = OctaveBand.FromArray(lw)
                    });
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"LoadFans {path}: {ex.Message}"); }
        }

        private static void LoadSilencers(AcousticData data, string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                var j = JObject.Parse(File.ReadAllText(path));
                var arr = j["silencers"] as JArray; if (arr == null) return;
                foreach (var t in arr.OfType<JObject>())
                {
                    var il = (t["il"] as JArray)?.Select(v => (double)v).ToArray();
                    if (il == null || il.Length != 8) continue;
                    data.Silencers.Add(new SilencerEntry
                    {
                        Match = (string)t["match"] ?? "",
                        Label = (string)t["label"] ?? "",
                        Il    = OctaveBand.FromArray(il)
                    });
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"LoadSilencers {path}: {ex.Message}"); }
        }
    }
}
