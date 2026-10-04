using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace StingTools.V6
{
    /// <summary>
    /// The record of which clash (by <see cref="AccClashRecord.StableSignature"/>) became which
    /// ACC issue — what makes escalation idempotent. Revit-free so StingTools.Acc.Tests can
    /// prove it.
    /// <para>
    /// KUT deep review INT-14. The old reader caught any read or parse failure and returned an
    /// EMPTY map, so a corrupt or locked file meant every clash was escalated again as a new ACC
    /// issue, assigned to real people. And the file was written once, after the whole loop, so a
    /// crash part-way lost every issue id created so far — the next run duplicated them.
    /// </para>
    /// <para>
    /// Now: a missing file is an empty ledger; a file that exists but cannot be read is an
    /// error the caller must stop on; and each write goes to a temporary file that replaces the
    /// ledger, so a crash leaves either the old ledger or the new one, never half of one.
    /// </para>
    /// </summary>
    public static class AccEscalationLedger
    {
        /// <summary>Read the ledger. Returns null and sets <paramref name="error"/> when the
        /// file exists but cannot be read — the caller must not escalate on a null.</summary>
        public static Dictionary<string, string> Load(string path, out string error)
        {
            error = null;
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return map;
            try
            {
                string text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text)) return map;
                foreach (var p in JObject.Parse(text).Properties())
                    map[p.Name] = (string)p.Value ?? string.Empty;
                return map;
            }
            catch (Exception ex)
            {
                error = $"{path} could not be read ({ex.Message}). Escalating without it would create " +
                        "a duplicate ACC issue for every clash already raised. Fix or restore the file, " +
                        "or delete it only if no issues were ever raised from this project.";
                return null;
            }
        }

        /// <summary>Write the whole ledger atomically. Returns false (with the reason) on failure.</summary>
        public static bool Save(string path, IReadOnlyDictionary<string, string> map, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path)) { error = "no ledger path (unsaved model?)"; return false; }
            try
            {
                var o = new JObject();
                foreach (var kv in map) o[kv.Key] = kv.Value;
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, o.ToString());
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
                return true;
            }
            catch (Exception ex)
            {
                error = $"{path} could not be written ({ex.Message})";
                return false;
            }
        }
    }
}
