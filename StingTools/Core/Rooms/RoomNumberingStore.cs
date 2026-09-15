// StingTools — Room numbering · scheme persistence
//
// Revit-free: every entry point takes a resolved path string. The Revit-bound caller
// (RoomNumberingCommands) resolves those through StingPaths.MetaFile — never by hand —
// which keeps tools/check_path_discipline.ps1 green and lets the round-trip be unit-tested.
//
// Layering matches VisibilityPresetStore / DrawingTypeRegistry / MepSizingRegistry: a
// corporate baseline from Data/STING_ROOM_NUMBERING.json, with the per-project file
// layered on top, project entries winning by Id.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace StingTools.Core.Rooms
{
    /// <summary>Root JSON document: a list of named schemes plus the default id.</summary>
    public class RoomNumberingLibrary
    {
        public string DefaultSchemeId { get; set; }
        public List<RoomNumberingScheme> Schemes { get; set; } = new List<RoomNumberingScheme>();
    }

    public static class RoomNumberingStore
    {
        /// <summary>File name of the per-project override, under the _BIM_COORD bucket.</summary>
        public const string ProjectFileName = "room_numbering.json";

        /// <summary>File name of the corporate baseline shipped in the plugin's data folder.</summary>
        public const string BaselineFileName = "STING_ROOM_NUMBERING.json";

        /// <summary>
        /// Parse a library from JSON text. Returns an empty library (never null) on blank
        /// input; throws only on malformed JSON, which callers surface as a warning.
        /// </summary>
        public static RoomNumberingLibrary Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new RoomNumberingLibrary();

            var lib = JsonConvert.DeserializeObject<RoomNumberingLibrary>(json)
                      ?? new RoomNumberingLibrary();
            if (lib.Schemes == null) lib.Schemes = new List<RoomNumberingScheme>();

            // Newtonsoft leaves a mistyped or misspelled field at its default rather than
            // failing. For this file that is not cosmetic: a scheme whose Pattern came back
            // null renders every room as the empty string, and a RowBandMm that silently
            // defaulted to 0 makes the walk order meaningless. Normalise to values the
            // planner can reject loudly rather than values it would quietly act on.
            foreach (var s in lib.Schemes)
            {
                if (s == null) continue;
                if (string.IsNullOrWhiteSpace(s.Id)) s.Id = "unnamed";
                if (string.IsNullOrWhiteSpace(s.Name)) s.Name = s.Id;
                if (s.Pattern == null) s.Pattern = "";
            }
            lib.Schemes.RemoveAll(s => s == null);
            return lib;
        }

        public static string Serialise(RoomNumberingLibrary lib)
        {
            return JsonConvert.SerializeObject(lib ?? new RoomNumberingLibrary(), Formatting.Indented);
        }

        /// <summary>
        /// Read a library from disk. A missing file yields an empty library — that is the
        /// normal state for a project that has never saved a scheme, not an error.
        /// </summary>
        public static RoomNumberingLibrary LoadFile(string path, IList<string> warnings = null)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new RoomNumberingLibrary();
            try
            {
                return Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                if (warnings != null)
                    warnings.Add("Could not read room numbering schemes from '" +
                                 Path.GetFileName(path) + "': " + ex.Message);
                StingLog.Warn("RoomNumberingStore.LoadFile(" + path + "): " + ex.Message);
                return new RoomNumberingLibrary();
            }
        }

        /// <summary>
        /// Corporate baseline with the project file layered on top, project schemes winning
        /// by Id. A project DefaultSchemeId wins too when it names a scheme that survives
        /// the merge — naming one that does not is a warning, not a silent fall-through,
        /// because the user would otherwise renumber with a scheme they did not choose.
        /// </summary>
        public static RoomNumberingLibrary Merge(
            RoomNumberingLibrary baseline, RoomNumberingLibrary project, IList<string> warnings = null)
        {
            var merged = new RoomNumberingLibrary();
            var byId = new Dictionary<string, RoomNumberingScheme>(StringComparer.OrdinalIgnoreCase);

            foreach (var s in (baseline ?? new RoomNumberingLibrary()).Schemes ?? new List<RoomNumberingScheme>())
                if (s != null && !string.IsNullOrEmpty(s.Id)) byId[s.Id] = s;

            foreach (var s in (project ?? new RoomNumberingLibrary()).Schemes ?? new List<RoomNumberingScheme>())
                if (s != null && !string.IsNullOrEmpty(s.Id)) byId[s.Id] = s;

            merged.Schemes = byId.Values.OrderBy(s => s.Id, StringComparer.Ordinal).ToList();

            string wanted = project != null && !string.IsNullOrWhiteSpace(project.DefaultSchemeId)
                ? project.DefaultSchemeId
                : (baseline != null ? baseline.DefaultSchemeId : null);

            if (!string.IsNullOrWhiteSpace(wanted) && !byId.ContainsKey(wanted))
            {
                if (warnings != null)
                    warnings.Add("Default scheme '" + wanted + "' is not defined by either the " +
                                 "corporate baseline or the project override. Falling back to '" +
                                 (merged.Schemes.Count > 0 ? merged.Schemes[0].Id : "(none)") + "'.");
                wanted = merged.Schemes.Count > 0 ? merged.Schemes[0].Id : null;
            }
            merged.DefaultSchemeId = wanted;
            return merged;
        }

        /// <summary>Resolve one scheme by id, falling back to the library default.</summary>
        public static RoomNumberingScheme Resolve(RoomNumberingLibrary lib, string schemeId)
        {
            if (lib == null || lib.Schemes == null || lib.Schemes.Count == 0) return null;

            string id = !string.IsNullOrWhiteSpace(schemeId) ? schemeId : lib.DefaultSchemeId;
            if (string.IsNullOrWhiteSpace(id)) return lib.Schemes[0];

            return lib.Schemes.FirstOrDefault(s =>
                       string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))
                   ?? lib.Schemes[0];
        }

        public static void SaveFile(string path, RoomNumberingLibrary lib)
        {
            if (string.IsNullOrEmpty(path)) return;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, Serialise(lib));
        }
    }
}
