// Phase 139.2 — Manufacturer catalogue registry.
//
// Loads STING_MANUFACTURER_CATALOGUE.json (one ship-with-plug-in default
// alongside any project override placed under <project>/_BIM_COORD/) and
// caches entries keyed by ManufacturerCode + ":" + CatalogueRef.
//
// AutoPopulateFromFamilies(doc) walks every loaded FamilySymbol, reads
// the MK_* shared parameters that ship on NBS / BIMobject content, and
// upserts entries on disk so designers never hand-edit the JSON.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace StingTools.Core.Placement
{
    public static class ManufacturerCatalogueRegistry
    {
        private const string FileName = "Placement/STING_MANUFACTURER_CATALOGUE.json";

        private static readonly object _lock = new object();
        private static Dictionary<string, ManufacturerCatalogueEntry> _byKey;
        private static string _loadedPath = "";

        // ── Public API ──────────────────────────────────────────────

        /// <summary>Resolve a single entry by manufacturer + catalogue ref. Null when not found.</summary>
        public static ManufacturerCatalogueEntry Resolve(string manufacturerCode, string catalogueRef)
        {
            if (string.IsNullOrEmpty(catalogueRef)) return null;
            EnsureLoaded();
            string key = MakeKey(manufacturerCode, catalogueRef);
            lock (_lock)
                return _byKey.TryGetValue(key, out var e) ? e : null;
        }

        /// <summary>Convenience: resolve for a placement rule's ManufacturerCode + CatalogueRef.</summary>
        public static ManufacturerCatalogueEntry GetForRule(PlacementRule rule)
        {
            if (rule == null) return null;
            return Resolve(rule.ManufacturerCode, rule.CatalogueRef);
        }

        /// <summary>All currently-cached entries (read-only snapshot).</summary>
        public static IReadOnlyDictionary<string, ManufacturerCatalogueEntry> All
        {
            get { EnsureLoaded(); lock (_lock) return new Dictionary<string, ManufacturerCatalogueEntry>(_byKey); }
        }

        /// <summary>Force re-read from disk — used after AutoPopulate or external edits.</summary>
        public static void Reload()
        {
            lock (_lock) { _byKey = null; }
        }

        /// <summary>
        /// Walk every FamilySymbol in the document; if it carries the MK
        /// shared parameter pack, upsert a corresponding catalogue entry
        /// and write the JSON back to disk. Returns (newCount, updatedCount, contributingFamilies).
        /// Caller does NOT need a Transaction — this is read-only on the
        /// Revit side.
        /// </summary>
        public static (int newCount, int updatedCount, List<string> contributingFamilies)
            AutoPopulateFromFamilies(Document doc)
        {
            int created = 0, updated = 0;
            var contributing = new List<string>();
            if (doc == null) return (0, 0, contributing);

            EnsureLoaded();

            try
            {
                var collector = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol));
                foreach (var el in collector)
                {
                    if (!(el is FamilySymbol fs)) continue;
                    string catalogueRef = ReadString(fs, "MK_CATALOGUE_REF");
                    if (string.IsNullOrEmpty(catalogueRef)) continue;

                    string manufacturer = ReadString(fs, "MK_MANUFACTURER");
                    if (string.IsNullOrEmpty(manufacturer)) manufacturer = "MK";

                    var entry = new ManufacturerCatalogueEntry
                    {
                        ManufacturerCode  = manufacturer,
                        CatalogueRef      = catalogueRef,
                        Description       = ReadString(fs, "MK_DESCRIPTION"),
                        GangCount         = (int)ReadDouble(fs, "MK_GANG_COUNT"),
                        BoxDepthMm        = (int)ReadDouble(fs, "MK_BOX_DEPTH_MM"),
                        BoxExternalLMm    = ReadDouble(fs, "MK_BOX_EXT_L_MM"),
                        BoxExternalWMm    = ReadDouble(fs, "MK_BOX_EXT_W_MM"),
                        FixingCentresMm   = ReadDouble(fs, "MK_FIXING_CENTRES_MM"),
                        ModulePitchMm     = ReadDouble(fs, "MK_MODULE_PITCH_MM"),
                        IpRating          = ReadString(fs, "MK_IP_RATING"),
                        MountType         = ReadString(fs, "MK_MOUNT_TYPE"),
                        RevitFamilyName   = fs.Family?.Name ?? "",
                        RevitTypeName     = fs.Name ?? "",
                        InsertionOrigin   = ReadString(fs, "MK_INSERTION_ORIGIN"),
                        FaceplateStandard = ReadString(fs, "MK_FACEPLATE_STD"),
                    };

                    string key = MakeKey(entry.ManufacturerCode, entry.CatalogueRef);
                    bool isNew;
                    lock (_lock)
                    {
                        isNew = !_byKey.ContainsKey(key);
                        _byKey[key] = entry;
                    }
                    if (isNew) created++; else updated++;
                    string famLabel = $"{entry.RevitFamilyName} : {entry.RevitTypeName}";
                    if (!contributing.Contains(famLabel)) contributing.Add(famLabel);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ManufacturerCatalogueRegistry.AutoPopulateFromFamilies: {ex.Message}");
            }

            if (created + updated > 0) TrySaveToDisk();
            return (created, updated, contributing);
        }

        // ── Internal ────────────────────────────────────────────────

        private static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (_byKey != null) return;
                _byKey = new Dictionary<string, ManufacturerCatalogueEntry>(StringComparer.OrdinalIgnoreCase);
                string path = ResolvePath();
                _loadedPath = path ?? "";
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    StingLog.Info("ManufacturerCatalogueRegistry: no catalogue file on disk yet — starting empty.");
                    return;
                }
                try
                {
                    var json = File.ReadAllText(path);
                    var root = JsonConvert.DeserializeObject<RootDoc>(json);
                    if (root?.Entries == null) return;
                    foreach (var e in root.Entries)
                    {
                        if (e == null || string.IsNullOrEmpty(e.CatalogueRef)) continue;
                        _byKey[MakeKey(e.ManufacturerCode, e.CatalogueRef)] = e;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ManufacturerCatalogueRegistry: load '{path}': {ex.Message}");
                }
            }
        }

        private static void TrySaveToDisk()
        {
            try
            {
                string path = string.IsNullOrEmpty(_loadedPath) ? ResolvePath() : _loadedPath;
                if (string.IsNullOrEmpty(path)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                List<ManufacturerCatalogueEntry> entries;
                lock (_lock)
                    entries = _byKey.Values
                        .OrderBy(e => e.ManufacturerCode, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(e => e.CatalogueRef, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                var root = new RootDoc { Version = "v1", Entries = entries };
                File.WriteAllText(path, JsonConvert.SerializeObject(root, Formatting.Indented));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ManufacturerCatalogueRegistry: save: {ex.Message}");
            }
        }

        private static string ResolvePath()
        {
            try
            {
                string fromFinder = StingToolsApp.FindDataFile(FileName);
                if (!string.IsNullOrEmpty(fromFinder)) return fromFinder;
            }
            catch { }
            try
            {
                string root = StingToolsApp.DataPath;
                if (!string.IsNullOrEmpty(root))
                    return Path.Combine(root, FileName.Replace('/', Path.DirectorySeparatorChar));
            }
            catch { }
            return null;
        }

        private static string MakeKey(string manufacturer, string catalogueRef)
        {
            string m = (manufacturer ?? "").Trim();
            string c = (catalogueRef ?? "").Trim();
            return (m.Length == 0 ? "?" : m) + ":" + c;
        }

        private static string ReadString(Element el, string paramName)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || !p.HasValue) return "";
                if (p.StorageType == StorageType.String) return p.AsString() ?? "";
                if (p.StorageType == StorageType.Integer) return p.AsInteger().ToString();
                return p.AsValueString() ?? "";
            }
            catch { return ""; }
        }

        private static double ReadDouble(Element el, string paramName)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || !p.HasValue) return 0.0;
                switch (p.StorageType)
                {
                    case StorageType.Double:
                        // Length params in Revit are stored in feet; convert to mm.
                        // Heuristic: if value < 1000 we assume already mm or unit-less.
                        double raw = p.AsDouble();
                        return raw < 50.0 ? raw * 304.8 : raw;
                    case StorageType.Integer: return p.AsInteger();
                    case StorageType.String:
                        if (double.TryParse(p.AsString(), System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double d)) return d;
                        return 0.0;
                }
            }
            catch { }
            return 0.0;
        }

        private class RootDoc
        {
            public string Version { get; set; } = "v1";
            public List<ManufacturerCatalogueEntry> Entries { get; set; } = new List<ManufacturerCatalogueEntry>();
        }
    }
}
