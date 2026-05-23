// StingTools — SpdCoordinator.cs
//
// Surge Protective Device coordination engine for the STING Lightning
// Protection panel. Pure C# utility: loads STING_LPS_SPD_CATALOGUE.json,
// scores a user-supplied SPD layout against IEC 62305-4 / IEC 61643-11
// rules (per-LPZ presence, Up ≤ Uw, Iimp ≥ class minimum, cascade
// energy coordination by ≥10 m cable separation), and recommends a
// product when a slot is empty.
//
// Engine logic only — no IExternalCommand types. Commands wrap writes
// in their own "STING …" transaction. Mirrors the LpsEngine shape so
// the panel can switch between them transparently.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Core.Lightning
{
    public static class SpdCoordinator
    {
        private static readonly object _lock = new object();
        private static JObject _catalogue;
        // Per-document project-override cache, keyed by the override file path.
        // Reload() clears both maps so a manual reload picks up disk edits.
        private static readonly Dictionary<string, JObject> _overrides
            = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        // Wave C #12 — FileSystemWatcher per override file so disk edits
        // out-of-band (text editor, CDE sync, git pull) invalidate the
        // cache automatically. Without this, the panel kept serving a
        // stale catalogue until "Reload catalogue" was clicked manually.
        private static readonly Dictionary<string, System.IO.FileSystemWatcher> _watchers
            = new Dictionary<string, System.IO.FileSystemWatcher>(StringComparer.OrdinalIgnoreCase);

        // ── Loaders ───────────────────────────────────────────────────

        private static JObject Load()
        {
            if (_catalogue != null) return _catalogue;
            lock (_lock)
            {
                if (_catalogue != null) return _catalogue;
                try
                {
                    string path = StingToolsApp.FindDataFile("STING_LPS_SPD_CATALOGUE.json");
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        _catalogue = JObject.Parse(File.ReadAllText(path));
                }
                catch (Exception ex) { StingLog.Warn($"SpdCoordinator.Load: {ex.Message}"); }
                if (_catalogue == null) _catalogue = new JObject();
            }
            return _catalogue;
        }

        /// <summary>
        /// Effective catalogue for a document: layers
        /// <c>&lt;project&gt;/_BIM_COORD/lps_spd_catalogue.json</c> over the
        /// corporate baseline. Override entries with the same <c>id</c>
        /// replace the corporate row; new entries are appended; new
        /// locations are appended (corporate entries always win on
        /// duplicate <c>id</c> when override absent).
        /// </summary>
        public static JObject Load(Document doc)
        {
            var baseLib = Load();
            string overridePath = ResolveOverridePath(doc);
            if (string.IsNullOrEmpty(overridePath) || !File.Exists(overridePath))
                return baseLib;
            try
            {
                JObject ov;
                lock (_lock)
                {
                    if (!_overrides.TryGetValue(overridePath, out ov))
                    {
                        ov = JObject.Parse(File.ReadAllText(overridePath));
                        _overrides[overridePath] = ov;
                        EnsureWatcher(overridePath);
                    }
                }
                // Deep-clone the corporate baseline so the merge doesn't mutate it.
                var merged = (JObject)baseLib.DeepClone();
                MergeArray(merged, ov, "products");
                MergeArray(merged, ov, "locations");
                return merged;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SpdCoordinator project-override merge: {ex.Message}");
                return baseLib;
            }
        }

        private static void MergeArray(JObject baseLib, JObject overrideLib, string arrayName)
        {
            var baseArr = baseLib[arrayName] as JArray;
            var ovArr   = overrideLib[arrayName] as JArray;
            if (baseArr == null || ovArr == null) return;
            foreach (var ovItem in ovArr)
            {
                string id = ovItem["id"]?.ToString();
                if (string.IsNullOrEmpty(id)) continue;
                JToken existing = baseArr.FirstOrDefault(b =>
                    string.Equals(b["id"]?.ToString(), id, StringComparison.OrdinalIgnoreCase));
                if (existing != null) existing.Replace(ovItem);
                else baseArr.Add(ovItem);
            }
        }

        /// <summary>
        /// Write the user's SPD grid back to
        /// <c>&lt;project&gt;/_BIM_COORD/lps_spd_catalogue.json</c> as the
        /// project's installed-products array. Returns the path written,
        /// or null on failure.
        /// </summary>
        public static string SaveProjectOverride(Document doc, IList<SpdInstance> rows)
        {
            try
            {
                string path = ResolveOverridePath(doc);
                if (string.IsNullOrEmpty(path)) return null;
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                JObject root = File.Exists(path)
                    ? JObject.Parse(File.ReadAllText(path))
                    : new JObject();

                var arr = new JArray();
                foreach (var r in rows ?? Enumerable.Empty<SpdInstance>())
                {
                    arr.Add(new JObject
                    {
                        ["id"]            = string.IsNullOrEmpty(r.Tag) ? Guid.NewGuid().ToString("N") : r.Tag,
                        ["manufacturer"]  = r.Manufacturer ?? "",
                        ["model"]         = r.Model ?? "",
                        ["type"]          = r.Type,
                        ["iimpKa"]        = r.IimpKa,
                        ["inKa"]          = r.InKa,
                        ["upKv"]          = r.UpKv,
                        ["polesCfg"]      = "",
                        ["location"]      = r.LocationId ?? "",
                        ["datasheet"]     = ""
                    });
                }
                root["products"] = arr;
                root["_lastSavedUtc"] = DateTime.UtcNow.ToString("o");

                File.WriteAllText(path, root.ToString(Newtonsoft.Json.Formatting.Indented));
                lock (_lock) { _overrides.Remove(path); }
                StingLog.Info($"SpdCoordinator.SaveProjectOverride wrote {path}");
                return path;
            }
            catch (Exception ex) { StingLog.Error("SpdCoordinator.SaveProjectOverride", ex); return null; }
        }

        private static string ResolveOverridePath(Document doc)
        {
            try
            {
                if (doc == null) return null;
                string rvt = doc.PathName;
                if (string.IsNullOrEmpty(rvt)) return null;
                string dir = Path.GetDirectoryName(rvt);
                if (string.IsNullOrEmpty(dir)) return null;
                return Path.Combine(dir, "_BIM_COORD", "lps_spd_catalogue.json");
            }
            catch (Exception ex) { StingLog.Warn($"ResolveOverridePath: {ex.Message}"); return null; }
        }

        public static void Reload()
        {
            lock (_lock)
            {
                _catalogue = null;
                _overrides.Clear();
                foreach (var w in _watchers.Values)
                {
                    try { w.EnableRaisingEvents = false; w.Dispose(); }
                    catch (Exception ex) { StingLog.Warn($"Watcher dispose: {ex.Message}"); }
                }
                _watchers.Clear();
            }
        }

        /// <summary>
        /// Wave C #12 — register a FileSystemWatcher on the override
        /// path so out-of-band edits invalidate the cache. Caller must
        /// hold _lock. Idempotent: re-registering for the same path
        /// is a no-op.
        /// </summary>
        private static void EnsureWatcher(string overridePath)
        {
            if (_watchers.ContainsKey(overridePath)) return;
            try
            {
                string dir = Path.GetDirectoryName(overridePath);
                string file = Path.GetFileName(overridePath);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file)) return;
                if (!Directory.Exists(dir)) return;

                var w = new System.IO.FileSystemWatcher(dir, file)
                {
                    NotifyFilter = System.IO.NotifyFilters.LastWrite |
                                   System.IO.NotifyFilters.CreationTime |
                                   System.IO.NotifyFilters.Size,
                    EnableRaisingEvents = true
                };
                System.IO.FileSystemEventHandler invalidate = (sender, args) =>
                {
                    try
                    {
                        lock (_lock) { _overrides.Remove(overridePath); }
                        StingLog.Info($"SpdCoordinator: override invalidated by file change: {overridePath}");
                    }
                    catch (Exception ex) { StingLog.Warn($"Watcher cb: {ex.Message}"); }
                };
                w.Changed += invalidate;
                w.Created += invalidate;
                w.Deleted += invalidate;
                w.Renamed += (sender, args) => invalidate(sender, args);
                _watchers[overridePath] = w;
                StingLog.Info($"SpdCoordinator: watching {overridePath}");
            }
            catch (Exception ex) { StingLog.Warn($"EnsureWatcher: {ex.Message}"); }
        }

        // ── Public API ────────────────────────────────────────────────

        public static double GetMinIimpKaForClass(string lpsClass)
        {
            try
            {
                var map = Load()["lpsClassToIimpKA"] as JObject;
                if (map == null) return 12.5;
                string norm = (lpsClass ?? "II").Trim().ToUpperInvariant();
                return map[norm]?.Value<double>() ?? 12.5;
            }
            catch (Exception ex) { StingLog.Warn($"GetMinIimpKaForClass: {ex.Message}"); return 12.5; }
        }

        public static double GetMinCascadeSeparationM()
        {
            try { return Load()["minSeparationCableLengthM"]?.Value<double>() ?? 10.0; }
            catch (Exception ex) { StingLog.Warn($"GetMinCascadeSeparationM: {ex.Message}"); return 10.0; }
        }

        public static IReadOnlyList<SpdProduct> AllProducts(Document doc = null)
        {
            var list = new List<SpdProduct>();
            try
            {
                var lib = doc != null ? Load(doc) : Load();
                var arr = lib["products"] as JArray;
                if (arr == null) return list;
                foreach (var p in arr)
                {
                    list.Add(new SpdProduct
                    {
                        Id            = p["id"]?.ToString() ?? "",
                        Manufacturer  = p["manufacturer"]?.ToString() ?? "",
                        Model         = p["model"]?.ToString() ?? "",
                        Type          = p["type"]?.Value<int>() ?? 0,
                        IimpKa        = p["iimpKa"]?.Value<double>() ?? 0,
                        InKa          = p["inKa"]?.Value<double>() ?? 0,
                        UpKv          = p["upKv"]?.Value<double>() ?? 0,
                        PolesCfg      = p["polesCfg"]?.ToString() ?? "",
                        Location      = p["location"]?.ToString() ?? "",
                        Datasheet     = p["datasheet"]?.ToString() ?? ""
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"AllProducts: {ex.Message}"); }
            return list;
        }

        public static IReadOnlyList<SpdLocation> AllLocations(Document doc = null)
        {
            var list = new List<SpdLocation>();
            try
            {
                var lib = doc != null ? Load(doc) : Load();
                var arr = lib["locations"] as JArray;
                if (arr == null) return list;
                foreach (var l in arr)
                {
                    list.Add(new SpdLocation
                    {
                        Id              = l["id"]?.ToString() ?? "",
                        Label           = l["label"]?.ToString() ?? "",
                        RequiredType    = l["requiredType"]?.Value<int>() ?? 0,
                        Iec62305Section = l["iec62305Section"]?.ToString() ?? ""
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"AllLocations: {ex.Message}"); }
            return list;
        }

        /// <summary>
        /// Recommend the cheapest matching product for a (locationId, lpsClass).
        /// Filters by required type per location + Iimp ≥ class minimum.
        /// Returns null when nothing in the catalogue matches.
        /// </summary>
        public static SpdProduct Recommend(string locationId, string lpsClass, Document doc = null)
        {
            try
            {
                var loc = AllLocations(doc).FirstOrDefault(l =>
                    string.Equals(l.Id, locationId, StringComparison.OrdinalIgnoreCase));
                if (loc == null) return null;
                double minIimp = GetMinIimpKaForClass(lpsClass);

                return AllProducts(doc)
                    .Where(p =>
                        (p.Type == loc.RequiredType || p.Type == 12) &&
                        string.Equals(p.Location, loc.Id, StringComparison.OrdinalIgnoreCase) &&
                        (loc.RequiredType != 1 || p.IimpKa >= minIimp))
                    .OrderBy(p => p.UpKv)
                    .FirstOrDefault();
            }
            catch (Exception ex) { StingLog.Warn($"Recommend: {ex.Message}"); return null; }
        }

        /// <summary>
        /// Coordinate a user-supplied SPD layout against IEC 62305-4 rules.
        /// Returns one item per rule fired, severity Pass/Warn/Fail.
        /// </summary>
        public static IReadOnlyList<LpsComplianceItem> Coordinate(
            IList<SpdInstance> installed,
            string lpsClass,
            double equipmentWithstandKv = 1.5)
        {
            var items = new List<LpsComplianceItem>();
            if (installed == null) installed = new List<SpdInstance>();

            // RULE_TYPE_PRESENT — at least one Type 1 at MAIN_INCOMER, Type 2 at SUB_DB
            foreach (var loc in AllLocations().Where(l => l.RequiredType != 3))
            {
                bool present = installed.Any(s =>
                    string.Equals(s.LocationId, loc.Id, StringComparison.OrdinalIgnoreCase) &&
                    (s.Type == loc.RequiredType || s.Type == 12));
                items.Add(present
                    ? new LpsComplianceItem { Severity = LpsSeverity.Pass, CheckName = "SPD_PRESENT_" + loc.Id,
                        Message = $"Type {loc.RequiredType} SPD installed at {loc.Label}." }
                    : new LpsComplianceItem { Severity = LpsSeverity.Fail, CheckName = "SPD_PRESENT_" + loc.Id,
                        Message = $"No Type {loc.RequiredType} SPD installed at {loc.Label} ({loc.Iec62305Section})." });
            }

            // RULE_UP_LE_UW — every Up must be ≤ Uw
            foreach (var s in installed)
            {
                if (s.UpKv <= 0) continue;
                if (s.UpKv > equipmentWithstandKv)
                    items.Add(new LpsComplianceItem { Severity = LpsSeverity.Fail, CheckName = "SPD_UP_GT_UW",
                        Message = $"SPD '{s.Tag}' Up = {s.UpKv:F2} kV exceeds equipment withstand Uw = {equipmentWithstandKv:F2} kV (IEC 61643)." });
            }

            // RULE_IIMP_GE_CLASS — any Type 1 must satisfy class minimum
            double minIimp = GetMinIimpKaForClass(lpsClass);
            foreach (var s in installed.Where(x => x.Type == 1 || x.Type == 12))
            {
                if (s.IimpKa < minIimp)
                    items.Add(new LpsComplianceItem { Severity = LpsSeverity.Fail, CheckName = "SPD_IIMP_BELOW_CLASS",
                        Message = $"SPD '{s.Tag}' Iimp = {s.IimpKa:F1} kA below class {lpsClass} minimum {minIimp:F1} kA." });
            }
            if (installed.Any(s => s.Type == 1 || s.Type == 12) &&
                !items.Any(i => i.CheckName == "SPD_IIMP_BELOW_CLASS"))
            {
                items.Add(new LpsComplianceItem { Severity = LpsSeverity.Pass, CheckName = "SPD_IIMP_OK",
                    Message = $"All Type-1 SPDs meet class {lpsClass} Iimp ≥ {minIimp:F1} kA." });
            }

            // RULE_CASCADE_ENERGY — Type 1 → Type 2 must be ≥10 m apart OR manufacturer-paired
            var t1s = installed.Where(s => s.Type == 1 || s.Type == 12).ToList();
            var t2s = installed.Where(s => s.Type == 2 || s.Type == 12).ToList();
            double minSep = GetMinCascadeSeparationM();
            int cascadeWarn = 0;
            foreach (var a in t1s)
            {
                foreach (var b in t2s)
                {
                    if (a == b) continue;
                    bool sameManu = !string.IsNullOrEmpty(a.Manufacturer) &&
                                    string.Equals(a.Manufacturer, b.Manufacturer, StringComparison.OrdinalIgnoreCase);
                    if (sameManu) continue;
                    if (b.CableSeparationFromUpstreamM > 0 && b.CableSeparationFromUpstreamM >= minSep) continue;
                    cascadeWarn++;
                }
            }
            if (cascadeWarn > 0)
                items.Add(new LpsComplianceItem { Severity = LpsSeverity.Warn, CheckName = "SPD_CASCADE_ENERGY",
                    Message = $"{cascadeWarn} Type 1↔Type 2 pair(s) lack manufacturer pairing and < {minSep:F0} m cable separation (IEC 62305-4 §6.2.4)." });
            else if (t1s.Count > 0 && t2s.Count > 0)
                items.Add(new LpsComplianceItem { Severity = LpsSeverity.Pass, CheckName = "SPD_CASCADE_ENERGY",
                    Message = $"Type 1 → Type 2 cascade energy-coordinated." });

            return items;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  POCOs
    // ══════════════════════════════════════════════════════════════════

    public class SpdProduct
    {
        public string Id           { get; set; }
        public string Manufacturer { get; set; }
        public string Model        { get; set; }
        public int    Type         { get; set; } // 1 / 2 / 3 / 12 (combined 1+2)
        public double IimpKa       { get; set; }
        public double InKa         { get; set; }
        public double UpKv         { get; set; }
        public string PolesCfg     { get; set; }
        public string Location     { get; set; }
        public string Datasheet    { get; set; }
    }

    public class SpdLocation
    {
        public string Id              { get; set; }
        public string Label           { get; set; }
        public int    RequiredType    { get; set; }
        public string Iec62305Section { get; set; }
    }

    /// <summary>
    /// One installed SPD slot — fed in from the panel grid. Tag / Type /
    /// IimpKa / UpKv are the parameters that the coordinator checks.
    /// CableSeparationFromUpstreamM lets the user record the physical
    /// install distance from the upstream Type 1 (used by the cascade
    /// energy-coordination rule).
    /// </summary>
    public class SpdInstance
    {
        public string Tag          { get; set; } = "";
        public string LocationId   { get; set; } = "";
        public int    Type         { get; set; }
        public double IimpKa       { get; set; }
        public double InKa         { get; set; }
        public double UpKv         { get; set; }
        public string Manufacturer { get; set; } = "";
        public string Model        { get; set; } = "";
        public double CableSeparationFromUpstreamM { get; set; }
    }
}
