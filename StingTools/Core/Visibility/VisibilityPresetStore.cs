// StingTools — Visibility Center · preset persistence
//
// Revit-free: every entry point takes a resolved path string. The Revit-bound caller
// (VisibilityCommands) resolves those through StingPaths.MetaFile — never by hand — which
// keeps tools/check_path_discipline.ps1 green and lets the round-trip be unit-tested.
//
// Layering matches DrawingTypeRegistry / MepSizingRegistry: a corporate baseline from
// Data/STING_VISIBILITY_PRESETS.json, with the per-project file layered on top, project
// entries winning by Name.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace StingTools.Core.Visibility
{
    /// <summary>Load/save <see cref="VisibilitySet"/> presets with corporate + project layering.</summary>
    public static class VisibilityPresetStore
    {
        /// <summary>File name of the per-project override, under the _BIM_COORD bucket.</summary>
        public const string ProjectFileName = "visibility_presets.json";

        /// <summary>File name of the corporate baseline shipped in the plugin's data folder.</summary>
        public const string BaselineFileName = "STING_VISIBILITY_PRESETS.json";

        /// <summary>
        /// Parse a preset library from JSON text. Returns an empty library (never null) on
        /// blank input; throws only on malformed JSON, which callers surface as a warning.
        /// </summary>
        public static VisibilityPresetLibrary Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new VisibilityPresetLibrary();

            var lib = JsonConvert.DeserializeObject<VisibilityPresetLibrary>(json)
                      ?? new VisibilityPresetLibrary();
            if (lib.Presets == null) lib.Presets = new List<VisibilitySet>();

            // Newtonsoft leaves a mistyped or misspelled field at its default rather than
            // failing, so normalise here: a preset with a null Rules list is the exact shape
            // a silent-default bug produces, and downstream code must never see it.
            foreach (var p in lib.Presets)
            {
                if (p == null) continue;
                if (p.Rules == null) p.Rules = new List<VisibilityRule>();
                foreach (var r in p.Rules)
                    if (r != null && r.Values == null) r.Values = new List<string>();
            }
            return lib;
        }

        public static string Serialise(VisibilityPresetLibrary lib) =>
            JsonConvert.SerializeObject(lib ?? new VisibilityPresetLibrary(), Formatting.Indented);

        /// <summary>
        /// Read a library from disk. A missing file yields an empty library — that is the
        /// normal state for a project that has never saved a preset, not an error.
        /// </summary>
        public static VisibilityPresetLibrary LoadFile(string path, IList<string> warnings = null)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new VisibilityPresetLibrary();
            try
            {
                return Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                warnings?.Add($"Could not read visibility presets from '{Path.GetFileName(path)}': {ex.Message}");
                StingLog.Warn($"VisibilityPresetStore.LoadFile({path}): {ex.Message}");
                return new VisibilityPresetLibrary();
            }
        }

        /// <summary>
        /// Corporate baseline with the project override layered on top; project entries win
        /// by <see cref="VisibilitySet.Name"/> (case-insensitive) and are stamped
        /// <c>Origin = "project"</c>. Either path may be null or missing.
        /// </summary>
        public static List<VisibilitySet> Load(
            string baselinePath, string projectPath, IList<string> warnings = null)
        {
            var merged = new List<VisibilitySet>();
            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in LoadFile(baselinePath, warnings).Presets)
            {
                if (p == null || string.IsNullOrWhiteSpace(p.Name)) continue;
                p.Origin = "corporate";
                index[p.Name] = merged.Count;
                merged.Add(p);
            }

            foreach (var p in LoadFile(projectPath, warnings).Presets)
            {
                if (p == null || string.IsNullOrWhiteSpace(p.Name)) continue;
                p.Origin = "project";
                int at;
                if (index.TryGetValue(p.Name, out at)) merged[at] = p;   // project wins
                else { index[p.Name] = merged.Count; merged.Add(p); }
            }
            return merged;
        }

        /// <summary>
        /// Write the project-scoped presets. Only project-origin entries are persisted, so the
        /// corporate baseline on disk stays pristine — the same rule the Drawing Type editor follows.
        /// Returns false (with a warning) rather than throwing when the path is unavailable,
        /// e.g. an unsaved document, where StingPaths.MetaFile returns null.
        /// </summary>
        public static bool Save(string projectPath, IEnumerable<VisibilitySet> sets, IList<string> warnings = null)
        {
            if (string.IsNullOrEmpty(projectPath))
            {
                warnings?.Add("This project has not been saved yet, so presets cannot be stored on disk.");
                return false;
            }

            var lib = new VisibilityPresetLibrary
            {
                Version = 1,
                Description = "Project-scoped STING Visibility Center presets.",
                // Carry over whatever this file already said about excludedCategories.
                // Saving a preset must not quietly delete a project's category exclusions —
                // they live in the same file and are edited by hand, not by this code path.
                ExcludedCategories = LoadFile(projectPath).ExcludedCategories
            };
            foreach (var s in sets ?? new List<VisibilitySet>())
            {
                if (s == null || string.IsNullOrWhiteSpace(s.Name)) continue;
                if (!string.Equals(s.Origin, "project", StringComparison.OrdinalIgnoreCase)) continue;
                lib.Presets.Add(s);
            }

            try
            {
                string dir = Path.GetDirectoryName(projectPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(projectPath, Serialise(lib));
                return true;
            }
            catch (Exception ex)
            {
                // This wraps a write, so it logs — bare catches are only for optional reads.
                warnings?.Add($"Could not save visibility presets: {ex.Message}");
                StingLog.Error($"VisibilityPresetStore.Save({projectPath})", ex);
                return false;
            }
        }

        /// <summary>
        /// The BuiltInCategory names to leave out of the category list. Resolution order, first
        /// non-null wins: project override → corporate baseline →
        /// <see cref="VisibilityCategoryTreeBuilder.DefaultExclusions"/>.
        /// <para>An explicit empty list in either file is honoured as "exclude nothing" — that
        /// is the difference between a project that overrides the key and one that omits it.</para>
        /// </summary>
        public static List<string> LoadExcludedCategories(
            string baselinePath, string projectPath, IList<string> warnings = null)
        {
            var project = LoadFile(projectPath, warnings).ExcludedCategories;
            if (project != null) return Clean(project);

            var baseline = LoadFile(baselinePath, warnings).ExcludedCategories;
            if (baseline != null) return Clean(baseline);

            return new List<string>(VisibilityCategoryTreeBuilder.DefaultExclusions);
        }

        private static List<string> Clean(IEnumerable<string> names)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            foreach (var n in names ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                string t = n.Trim();
                if (seen.Add(t)) list.Add(t);
            }
            return list;
        }

        /// <summary>Add or replace <paramref name="set"/> in <paramref name="existing"/> by name.</summary>
        public static void Upsert(List<VisibilitySet> existing, VisibilitySet set)
        {
            if (existing == null || set == null || string.IsNullOrWhiteSpace(set.Name)) return;
            set.Origin = "project";
            for (int i = 0; i < existing.Count; i++)
            {
                if (existing[i] != null &&
                    string.Equals(existing[i].Name, set.Name, StringComparison.OrdinalIgnoreCase))
                {
                    existing[i] = set;
                    return;
                }
            }
            existing.Add(set);
        }
    }
}
