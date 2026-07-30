// StingTools — Per-space-type load profile registry.
//
// Closes the "schedules + densities are hardcoded to office" gap:
// loads now come from a corporate-baseline JSON keyed by space type,
// with per-project overrides at <project>/_BIM_COORD/load_profiles.json.
//
// HvacBlockLoadCommand.ZoneFromSpace looks up the profile via
// HVC_SPACE_TYPE_TXT (preferred) or the Revit SpaceType.Name (fallback)
// and seeds LoadZone with the matching schedules, densities, OA and
// setpoints. When no profile matches, the default "Office" profile is
// returned so legacy projects still get a valid number.
//
// Sources cited in STING_LOAD_PROFILES.json header.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Hvac.Loads
{
    // LoadProfile + LoadProfileLibrary moved to LoadProfileModels.cs (pure, unit-tested
    // resolution). This file keeps the Document-facing loader only. WS K2.

    public static class LoadProfileRegistry
    {
        public const string DataFileName = "STING_LOAD_PROFILES.json";
        public const string ProjectOverrideRelPath = "_BIM_COORD/load_profiles.json";

        private static readonly ConcurrentDictionary<string, LoadProfileLibrary> _cache
            = new ConcurrentDictionary<string, LoadProfileLibrary>(StringComparer.OrdinalIgnoreCase);

        public static LoadProfileLibrary Get(Document doc)
        {
            string key = doc?.PathName ?? "<no-doc>";
            return _cache.GetOrAdd(key, _ => Load(doc));
        }

        public static void Reload()             => _cache.Clear();
        public static void Reload(Document doc) => _cache.TryRemove(doc?.PathName ?? "<no-doc>", out _);

        private static LoadProfileLibrary Load(Document doc)
        {
            string corp = null, proj = null;
            try
            {
                string basePath = StingTools.Core.StingToolsApp.FindDataFile(DataFileName);
                if (!string.IsNullOrEmpty(basePath) && File.Exists(basePath)) corp = File.ReadAllText(basePath);

                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string projPath = ProjectFolderEngine.ResolveProjectOverridePath(doc, ProjectOverrideRelPath);
                    if (File.Exists(projPath)) proj = File.ReadAllText(projPath);
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Error("LoadProfileRegistry.Load", ex); }

            // Parse (incl. WS K3 subtype overlay) + Office guarantee live in the pure
            // LoadProfileLibrary so they are unit-tested.
            return LoadProfileLibrary.FromJson(corp, proj);
        }
    }
}
