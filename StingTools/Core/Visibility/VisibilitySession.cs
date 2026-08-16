// StingTools — Visibility Center · session state + path resolution
//
// The dropdown runs on the WPF thread; the commands run on the Revit API thread behind an
// IExternalEvent. This is the handoff between them — the same static-snapshot pattern
// StingHvacPanel uses for CurrentRegion / CurrentStandard.
//
// It is also the ONLY place the feature touches the filesystem, and it does so exclusively
// through StingPaths / StingToolsApp.FindDataFile. No path is ever built by hand;
// tools/check_path_discipline.ps1 is a zero-tolerance build gate.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core.Visibility
{
    /// <summary>Shared state between the visibility dropdown and the commands it dispatches.</summary>
    public static class VisibilitySession
    {
        private static readonly object _lock = new object();
        private static VisibilitySet _current;

        /// <summary>
        /// The set the dropdown last snapshotted. Commands read this on the Revit API thread.
        /// Never null — an empty set is a valid "nothing selected" state.
        /// </summary>
        public static VisibilitySet Current
        {
            get
            {
                lock (_lock)
                {
                    if (_current == null)
                        _current = new VisibilitySet { Name = "(current)", Origin = "project" };
                    return _current;
                }
            }
            set { lock (_lock) { _current = value; } }
        }

        /// <summary>Replace the working set's rules and mode in one atomic swap.</summary>
        public static void Snapshot(VisibilityMode mode, VisibilityTarget target, List<VisibilityRule> rules)
        {
            lock (_lock)
            {
                _current = new VisibilitySet
                {
                    Name = "(current)",
                    Origin = "project",
                    Mode = mode,
                    Target = target,
                    Rules = rules ?? new List<VisibilityRule>()
                };
            }
        }

        // ── Paths — StingPaths only ─────────────────────────────────────

        /// <summary>Per-project preset override. Null for an unsaved document.</summary>
        public static string ProjectPresetPath(Document doc) =>
            StingPaths.MetaFile(doc, "_BIM_COORD", VisibilityPresetStore.ProjectFileName);

        /// <summary>Corporate baseline shipped in the plugin's data folder.</summary>
        public static string BaselinePresetPath() =>
            StingToolsApp.FindDataFile(VisibilityPresetStore.BaselineFileName);

        // ── Presets ─────────────────────────────────────────────────────

        /// <summary>
        /// BuiltInCategory names the category list leaves out, resolved through the same
        /// corporate-baseline + project-override pair the presets use. Cached per document
        /// path because <c>TokenValueHarvester</c> asks for it on every scan.
        /// </summary>
        public static List<string> ExcludedCategories(Document doc)
        {
            string key = ProjectPresetPath(doc) ?? "(no project)";
            lock (_lock)
            {
                if (_excluded != null && string.Equals(_excludedKey, key, StringComparison.Ordinal))
                    return _excluded;
            }

            var list = VisibilityPresetStore.LoadExcludedCategories(BaselinePresetPath(),
                                                                    ProjectPresetPath(doc));
            lock (_lock) { _excluded = list; _excludedKey = key; }
            return list;
        }

        private static List<string> _excluded;
        private static string _excludedKey;

        /// <summary>Drop the cached exclusion list — call after a preset file is written.</summary>
        public static void InvalidateExclusions()
        {
            lock (_lock) { _excluded = null; _excludedKey = null; }
        }

        /// <summary>Corporate baseline with the project override layered on top.</summary>
        public static List<VisibilitySet> LoadPresets(Document doc, IList<string> warnings = null) =>
            VisibilityPresetStore.Load(BaselinePresetPath(), ProjectPresetPath(doc), warnings);

        /// <summary>
        /// Persist <paramref name="set"/> to the project override file. Corporate baseline
        /// entries are never rewritten — the saved file holds project-origin entries only.
        /// </summary>
        public static bool SavePreset(Document doc, VisibilitySet set, IList<string> warnings = null)
        {
            if (set == null || string.IsNullOrWhiteSpace(set.Name))
            {
                warnings?.Add("A preset needs a name.");
                return false;
            }

            string path = ProjectPresetPath(doc);
            if (string.IsNullOrEmpty(path))
            {
                warnings?.Add("Save the Revit project first — presets are stored alongside it.");
                return false;
            }

            var existing = LoadPresets(doc, warnings);
            VisibilityPresetStore.Upsert(existing, set);
            bool ok = VisibilityPresetStore.Save(path, existing, warnings);
            // The project file we just rewrote is also where excludedCategories lives.
            if (ok) InvalidateExclusions();
            return ok;
        }
    }
}
