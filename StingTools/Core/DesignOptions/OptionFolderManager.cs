// StingTools — per-option CDE folder mint.
//
// Phase 175 — generates `_BIM_COORD/options/<set>/<option>/` under the
// WIP container so per-option transmittals, schedule exports, and
// briefcase backups have a stable home. Idempotent.

using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;

namespace StingTools.Core.DesignOptions
{
    public static class OptionFolderManager
    {
        public static string GetOptionsRoot(Document doc)
        {
            string proj = OutputLocationHelper.GetOutputDirectory(doc);
            return Path.Combine(proj, "_BIM_COORD", "options");
        }

        public static string GetOptionFolder(Document doc, string setName, string optionName)
        {
            return Path.Combine(GetOptionsRoot(doc),
                Sanitise(setName), Sanitise(optionName));
        }

        public static List<string> EnsureFoldersForAllOptions(Document doc)
        {
            var made = new List<string>();
            if (doc == null) return made;
            try
            {
                var sets = DesignOptionRegistry.Snapshot(doc);
                foreach (var s in sets)
                foreach (var o in s.Options)
                {
                    string p = GetOptionFolder(doc, s.Name, o.Name);
                    if (!Directory.Exists(p))
                    {
                        Directory.CreateDirectory(p);
                        // Standard sub-shape mirroring the corporate WIP layout
                        Directory.CreateDirectory(Path.Combine(p, "drawings"));
                        Directory.CreateDirectory(Path.Combine(p, "schedules"));
                        Directory.CreateDirectory(Path.Combine(p, "boq"));
                        Directory.CreateDirectory(Path.Combine(p, "transmittals"));
                        Directory.CreateDirectory(Path.Combine(p, "renders"));
                        made.Add(p);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"OptionFolderManager: {ex.Message}"); }
            return made;
        }

        private static string Sanitise(string s)
        {
            if (string.IsNullOrEmpty(s)) return "_";
            var bad = Path.GetInvalidFileNameChars();
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (Array.IndexOf(bad, chars[i]) >= 0) chars[i] = '_';
            return new string(chars).Trim();
        }
    }
}
