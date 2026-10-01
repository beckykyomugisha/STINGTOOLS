// StingTools — reading and replacing a JSON store without losing it (C4).
//
// BIMManagerEngine.LoadJsonArray read an unreadable store as an EMPTY array, and SaveJsonFile
// then wrote the caller's rows over it: a truncated document_register.json lost every manual
// and deliverable row the next time an export registered one file. The loader fell back to a
// ".bak" that nothing ever wrote, and a failed save was swallowed, so "Transmittal TX-… is now
// recorded as SENT" was reported after a save that did not happen.
//
// Two rules, both here so they cannot drift:
//   * Replace keeps a ".bak" of the last READABLE content, and moves an UNREADABLE target aside
//     (".unreadable.<utc>") instead of overwriting it — the bytes stay recoverable.
//   * TryLoadArray tells "missing" (empty, fine) from "unreadable" (refuse, or the .bak), so a
//     caller that must not guess can refuse.
//
// Revit-free and StingLog-free: linked into StingTools.Tags.Tests.

using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    public static class JsonStoreFile
    {
        /// <summary>
        /// Read a JSON-array store. A missing file is an empty array (true). An unreadable one
        /// falls back to its ".bak" (true, with <paramref name="note"/> saying so); with no
        /// readable backup it returns false and <paramref name="error"/> — never an empty
        /// array standing in for data that exists.
        /// </summary>
        public static bool TryLoadArray(string path, out JArray array, out string error, out string note)
        {
            array = null; error = null; note = null;
            if (string.IsNullOrEmpty(path)) { error = "no path"; return false; }
            string bak = path + ".bak";
            if (!File.Exists(path))
            {
                if (File.Exists(bak) && TryParseArray(bak, out array, out _))
                {
                    note = $"{Path.GetFileName(path)} is missing; its backup was read.";
                    return true;
                }
                array = new JArray();
                return true;
            }
            if (TryParseArray(path, out array, out string why)) return true;
            if (File.Exists(bak) && TryParseArray(bak, out array, out _))
            {
                note = $"{Path.GetFileName(path)} could not be read ({why}); its backup was read instead.";
                return true;
            }
            error = $"{Path.GetFileName(path)} could not be read ({why}) and has no readable backup";
            array = null;
            return false;
        }

        /// <summary>
        /// Replace <paramref name="path"/> with <paramref name="text"/>. The previous content
        /// becomes ".bak" when it was readable JSON; an unreadable previous file is moved to
        /// ".unreadable.&lt;utc&gt;" so nothing is destroyed. False with the reason on failure.
        /// </summary>
        public static bool Replace(string path, string text, out string error)
        {
            error = null;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, text ?? "");
                if (File.Exists(path))
                {
                    if (IsReadableJson(path))
                        File.Copy(path, path + ".bak", true);
                    else
                        File.Move(path, path + ".unreadable." + DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
                }
                File.Move(tmp, path, true);
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private static bool TryParseArray(string path, out JArray array, out string why)
        {
            array = null; why = null;
            try
            {
                var tok = JToken.Parse(File.ReadAllText(path));
                if (tok is JArray a) { array = a; return true; }
                why = $"the file holds a JSON {tok.Type}, not an array";
                return false;
            }
            catch (Exception ex) { why = ex.Message; return false; }
        }

        private static bool IsReadableJson(string path)
        {
            try { JToken.Parse(File.ReadAllText(path)); return true; }
            catch (Exception) { return false; }
        }
    }
}
