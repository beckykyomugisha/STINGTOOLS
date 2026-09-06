// ══════════════════════════════════════════════════════════════════════════
//  SharedParameterNames.cs — the names in a Revit shared-parameter file.
//
//  Layer 3 adds SHARED parameters. A name that is not in the file does not
//  fail loudly: the ExternalDefinition lookup simply returns nothing, and the
//  honest outcomes are then either "skipped without a word" or — far worse —
//  "fell back to a LOCAL parameter", which has no GUID, cannot be scheduled
//  with its namesake in another family, and looks completely normal in the
//  properties palette.
//
//  So the check happens in ProjectBaseline.Validate(), BEFORE any family is
//  opened. EditFamily + LoadFamily across every door and window family is a
//  heavy, model-mutating operation, and a misspelled name found halfway
//  through it is the worst possible moment to find it.
//
//  Deliberately a text parse and not a Revit call: it makes the check
//  available to Validate(), which is Revit-free by design, and it lets the
//  shipped-data test read the SAME file the plugin ships.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;

namespace StingTools.Core.Baseline
{
    public static class SharedParameterNames
    {
        /// <summary>
        /// Parameter names from the tab-separated shared-parameter file.
        ///
        /// Format, from the file's own header:
        /// <code>*PARAM  GUID  NAME  DATATYPE  DATACATEGORY  GROUP  VISIBLE …</code>
        /// so a PARAM row's name is field index 2. Rows that are too short, and
        /// every non-PARAM row, are skipped rather than guessed at.
        /// </summary>
        public static ISet<string> Parse(IEnumerable<string> lines)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (lines == null) return names;

            foreach (string raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string line = raw.TrimEnd('\r', '\n');
                if (line.StartsWith("#", StringComparison.Ordinal)) continue;
                if (!line.StartsWith("PARAM\t", StringComparison.OrdinalIgnoreCase)) continue;

                var f = line.Split('\t');
                if (f.Length < 3) continue;
                string name = (f[2] ?? "").Trim();
                if (name.Length > 0) names.Add(name);
            }
            return names;
        }

        /// <summary>Parse a file, or an EMPTY set when it cannot be read.
        ///
        /// Empty is the safe failure here: <see cref="ProjectBaseline.Validate"/>
        /// skips the existence check when it is given no names, so an unreadable
        /// file degrades to the behaviour that existed before this check — it
        /// never reports every parameter as missing, which would be a wall of
        /// confident nonsense.</summary>
        /// <remarks><paramref name="onWarn"/> exists so this file stays free of
        /// StingTools.Core — it is compiled into the Revit-free test project,
        /// which has no logger. The Revit caller passes StingLog.Warn, so the
        /// catch is reported rather than silent.</remarks>
        public static ISet<string> ParseFile(string path, Action<string> onWarn = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    onWarn?.Invoke($"shared-parameter file not found at '{path}' — layer 3's "
                                 + "name check is SKIPPED, not failed");
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                return Parse(File.ReadAllLines(path));
            }
            catch (Exception ex)
            {
                onWarn?.Invoke($"SharedParameterNames.ParseFile '{path}': {ex.Message}");
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
