// ══════════════════════════════════════════════════════════════════════════
//  BoqPrintProfile.cs — P2.3 — named print / export column profiles.
//
//  A print profile is a named set of VISIBLE BOQ columns. It drives which
//  columns the on-screen item grid shows and is remembered (via the config
//  key COST_ACTIVE_PRINT_PROFILE) so exports + the next session pick it up.
//
//  Corporate baseline ships in Data/STING_BOQ_PRINT_PROFILES.json; project
//  overrides at <project>/_BIM_COORD/boq_print_profiles.json take precedence
//  by id (project profiles prepended). Mirrors TakeoffRuleRegistry.
//
//  Canonical column keys (case-insensitive):
//    Ref · Item · Qty · Unit · Rate · Total      — core bill (always visible)
//    Source · Confidence · Carbon · Level · Location · Note  — toggleable
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.BIMManager;
using StingTools.Core;

namespace StingTools.BOQ
{
    public class BoqPrintProfile
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>Ordered canonical column keys that should be VISIBLE.</summary>
        public List<string> Columns { get; set; } = new List<string>();
        public string Origin { get; set; } = "corporate"; // "corporate" | "project"

        public bool ShowsColumn(string key)
            => Columns != null && Columns.Any(c => string.Equals(c, key, StringComparison.OrdinalIgnoreCase));
    }

    public class BoqPrintProfileLibrary
    {
        public string Version { get; set; } = "1.0";
        public List<BoqPrintProfile> Profiles { get; set; } = new List<BoqPrintProfile>();
    }

    public sealed class BoqPrintProfileRegistry
    {
        // The six toggleable columns the grid + profiles control. Core columns
        // (Ref/Item/Qty/Unit/Rate/Total) are always shown and never in this set.
        public static readonly string[] ToggleableColumns =
            { "Level", "Location", "Source", "Confidence", "Carbon", "Note" };

        private static readonly ConcurrentDictionary<string, BoqPrintProfileRegistry> _cache
            = new ConcurrentDictionary<string, BoqPrintProfileRegistry>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<BoqPrintProfile> Profiles { get; }

        private BoqPrintProfileRegistry(IReadOnlyList<BoqPrintProfile> profiles) { Profiles = profiles; }

        public static BoqPrintProfileRegistry Get(Document doc)
            => _cache.GetOrAdd(doc?.PathName ?? "default", _ => Load(doc));

        public static void Invalidate() => _cache.Clear();

        public BoqPrintProfile GetById(string id)
        {
            if (string.IsNullOrEmpty(id) || Profiles == null) return null;
            return Profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The hidden toggleable columns implied by a profile (every
        /// toggleable column the profile does NOT list as visible).</summary>
        public static HashSet<string> HiddenColumnsFor(BoqPrintProfile profile)
        {
            var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (profile == null) return hidden;   // null profile = show everything
            foreach (var col in ToggleableColumns)
                if (!profile.ShowsColumn(col)) hidden.Add(col);
            return hidden;
        }

        private static BoqPrintProfileRegistry Load(Document doc)
        {
            string corpPath = StingToolsApp.FindDataFile("STING_BOQ_PRINT_PROFILES.json");
            string projectPath = ResolveProjectOverridePath(doc);

            var corporate = LoadFile(corpPath, "corporate");
            var project = LoadFile(projectPath, "project");

            var projectIds = new HashSet<string>(project.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
            var merged = new List<BoqPrintProfile>(project.Count + corporate.Count);
            merged.AddRange(project);
            merged.AddRange(corporate.Where(p => !projectIds.Contains(p.Id)));

            if (merged.Count == 0) merged = DefaultProfiles();
            StingLog.Info($"BoqPrintProfileRegistry: loaded {merged.Count} profile(s) " +
                          $"({project.Count} project + {corporate.Count} corporate)");
            return new BoqPrintProfileRegistry(merged);
        }

        private static List<BoqPrintProfile> LoadFile(string path, string origin)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new List<BoqPrintProfile>();
            try
            {
                var lib = JsonConvert.DeserializeObject<BoqPrintProfileLibrary>(File.ReadAllText(path));
                if (lib?.Profiles == null) return new List<BoqPrintProfile>();
                foreach (var p in lib.Profiles) p.Origin = origin;
                return lib.Profiles;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"BoqPrintProfileRegistry.LoadFile({Path.GetFileName(path)}): {ex.Message}");
                return new List<BoqPrintProfile>();
            }
        }

        // Safety net if the corporate JSON is missing — keeps the dropdown usable.
        private static List<BoqPrintProfile> DefaultProfiles() => new List<BoqPrintProfile>
        {
            new BoqPrintProfile { Id = "internal", Name = "Internal (all columns)",
                Columns = { "Ref", "Item", "Qty", "Unit", "Rate", "Total",
                            "Source", "Confidence", "Carbon", "Level", "Location", "Note" } },
            new BoqPrintProfile { Id = "tender", Name = "Tender (price-only)",
                Columns = { "Ref", "Item", "Unit", "Qty", "Rate", "Total" } },
            new BoqPrintProfile { Id = "locational", Name = "Locational (with level/location)",
                Columns = { "Ref", "Item", "Unit", "Qty", "Rate", "Total", "Level", "Location" } },
        };

        private static string ResolveProjectOverridePath(Document doc)
        {
            try
            {
                string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
                if (string.IsNullOrEmpty(bimDir)) return null;
                string parent = Path.GetDirectoryName(bimDir);
                if (string.IsNullOrEmpty(parent)) return null;
                return Path.Combine(parent, "_BIM_COORD", "boq_print_profiles.json");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"BoqPrintProfileRegistry.ResolveProjectOverridePath: {ex.Message}");
                return null;
            }
        }
    }
}
