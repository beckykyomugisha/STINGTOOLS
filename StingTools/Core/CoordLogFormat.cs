// CoordLogFormat.cs — the coordination-log wire format, in one place.
//
// The coordination log had a three-way contract split. WarningsManager wrote
// line-delimited JSON to coord_log.jsonl; MaterialAuditLogger wrote line-delimited
// JSON into coord_log.json; and every reader opened coord_log.json and handed the
// whole file to DeserializeObject<List<CoordLogEntry>>. So the entries one writer
// produced lived in a file nobody read, and the file the readers did open threw on
// the second line and was swallowed by a catch — the BCC coordination timeline
// silently showed nothing at all.
//
// The contract is now: ONE file, coord_log.jsonl, ONE object per line. This type
// owns parsing and formatting and carries no Autodesk.Revit dependency, so it is
// linked into StingTools.Clash.Tests and unit-tested directly.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    /// <summary>
    /// Line-delimited JSON (JSONL) codec for the coordination log. Pure — no Revit,
    /// no filesystem. See <see cref="CoordLog"/> for the document-aware wrapper.
    /// </summary>
    public static class CoordLogFormat
    {
        /// <summary>Canonical file name. The only spelling any caller should use.</summary>
        public const string FileName = "coord_log.jsonl";

        /// <summary>Canonical sidecar fallback used when no project root resolves.</summary>
        public const string SidecarFileName = ".sting_coord_log.jsonl";

        /// <summary>
        /// Render one entry as a single JSONL line. Formatting.None is not cosmetic —
        /// an indented object would span lines and break the one-object-per-line
        /// contract for every subsequent reader.
        /// </summary>
        public static string FormatLine(JObject entry)
        {
            if (entry == null) return "";
            return entry.ToString(Formatting.None);
        }

        /// <summary>
        /// Parse line-delimited JSON. Tolerant by design: a corrupt or partially
        /// written line is skipped rather than discarding the whole log, which is
        /// what the previous whole-file DeserializeObject&lt;List&gt; did.
        ///
        /// Also accepts a legacy whole-file JSON array, so logs written before the
        /// contract was unified still load instead of silently reading as empty.
        /// </summary>
        public static List<JObject> ParseLines(string content)
        {
            var rows = new List<JObject>();
            if (string.IsNullOrWhiteSpace(content)) return rows;

            // Legacy shape: the entire file is a single JSON array.
            string trimmed = content.TrimStart();
            if (trimmed.StartsWith("["))
            {
                try
                {
                    var arr = JArray.Parse(content);
                    foreach (var t in arr)
                        if (t is JObject o) rows.Add(o);
                    return rows;
                }
                catch
                {
                    // Not a valid array after all — fall through to line parsing.
                    rows.Clear();
                }
            }

            using (var sr = new StringReader(content))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string s = line.Trim();

                    // Tolerate a trailing comma from hand-edited logs.
                    if (s.EndsWith(",")) s = s.Substring(0, s.Length - 1);
                    if (s.Length == 0) continue;

                    try
                    {
                        var o = JObject.Parse(s);
                        rows.Add(o);
                    }
                    catch
                    {
                        // Skip this line only.
                    }
                }
            }

            return rows;
        }

        /// <summary>
        /// Keep only the most recent <paramref name="maxLines"/> entries. The previous
        /// cap operated on raw lines of a file that was not reliably line-delimited.
        /// </summary>
        public static List<string> Cap(List<string> lines, int maxLines)
        {
            if (lines == null) return new List<string>();
            if (maxLines <= 0 || lines.Count <= maxLines) return lines;
            return lines.GetRange(lines.Count - maxLines, maxLines);
        }
    }
}
