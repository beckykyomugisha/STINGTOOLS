// StingTools — how an exported file becomes a row in the document register
//
// BIMManagerEngine.AutoRegisterExport used to load document_register.json, add or
// update one row and save it again, once per file. The Export Centre called it for
// every file of an issue, so a 400-sheet PDF + DWG issue read and rewrote the whole
// register 800 times, each rewrite larger than the last (DOCX-13). The row rule
// lives here now, over an in-memory register, so a batch loads once and saves once
// and a single export goes through exactly the same rule.
//
// Revit-free; unit-tested (StingTools.Tags.Tests).

using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.BIMManager
{
    /// <summary>One exported file to record in the document register.</summary>
    internal sealed class ExportRegistration
    {
        public string FilePath;
        public string DocType;
        public string Description;
        public string Suitability;
        public string Revision;
        public string CdeStatus;
        public string DocNumber;
        /// <summary>ISO fields the exporter could NOT resolve for this file ("suitability",
        /// "revision"), written as the row's iso_unset. Null leaves an existing row's flag
        /// untouched; an empty list clears it (the file now carries both). A flagged row is
        /// never uploaded to ACC (AccFileIso).</summary>
        public System.Collections.Generic.IReadOnlyList<string> IsoUnset;
    }

    internal static class ExportRegisterUpsert
    {
        /// <summary>Add or update the register row for <paramref name="e"/>.
        ///
        /// Updates in place rather than skipping: a deliverable re-rendered on the
        /// same day keeps its file name but moves CDE state, so an early return would
        /// leave the row pointing at a purged file, and on a later day it would append
        /// a duplicate. Matches on the deliverable number first (stable across
        /// renders), then the file name. False only when there is no file to record;
        /// <paramref name="id"/> is the row's id (null on a legacy row that has none)
        /// and <paramref name="added"/> says whether the row is new.</summary>
        public static bool Apply(JArray register, ExportRegistration e, DateTime now,
                                 string userName, out string id, out bool added)
        {
            added = false;
            id = null;
            if (register == null) throw new ArgumentNullException(nameof(register));
            if (e == null || string.IsNullOrWhiteSpace(e.FilePath)) return false;

            string fileName = Path.GetFileName(e.FilePath);
            string fileFormat = Path.GetExtension(e.FilePath).ToUpperInvariant().TrimStart('.');
            // A row with no suitability keeps the register's documented WIP/S0 convention, but
            // says so (suitability_defaulted): the ACC upload must not read that S0 as a code
            // somebody chose (R14).
            bool suitDefaulted = string.IsNullOrWhiteSpace(e.Suitability);
            string suit = suitDefaulted ? "S0" : e.Suitability;
            // No revision is recorded as none. This used to default to "P01", which put a
            // revision nobody issued on every model, report and bundle row — and, once the
            // ACC upload read the register, would have stamped it into ACC as ISO Revision.
            string rev  = string.IsNullOrWhiteSpace(e.Revision)    ? ""    : e.Revision.Trim();
            string cde  = string.IsNullOrWhiteSpace(e.CdeStatus)   ? "WIP" : e.CdeStatus;
            string stamp = now.ToString("yyyy-MM-dd HH:mm");

            JObject existing = null;
            if (!string.IsNullOrWhiteSpace(e.DocNumber))
                existing = register.OfType<JObject>().FirstOrDefault(r =>
                    string.Equals(r["doc_number"]?.ToString(), e.DocNumber, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
                existing = register.OfType<JObject>().FirstOrDefault(r =>
                    string.Equals(r["file_name"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing["file_name"]     = fileName;
                existing["file_path"]     = e.FilePath;
                existing["file_format"]   = fileFormat;
                existing["suitability"]   = suit;
                SetDefaultedFlag(existing, suitDefaulted);
                existing["revision"]      = rev;
                existing["status"]        = cde;
                existing["cde_status"]    = cde;
                existing["date_modified"] = stamp;
                if (!string.IsNullOrWhiteSpace(e.DocNumber)) existing["doc_number"] = e.DocNumber;
                ApplyIsoUnset(existing, e.IsoUnset);
                id = existing["document_id"]?.ToString() ?? existing["doc_id"]?.ToString();
                return true;
            }

            string nextId = NextId(register, "DOC", "document_id");
            var entry = new JObject
            {
                // doc_id too: the Document Manager's edit paths and most readers use it.
                ["doc_id"]        = nextId,
                ["document_id"]   = nextId,
                ["title"]         = e.Description,
                ["file_name"]     = fileName,
                ["file_path"]     = e.FilePath,
                ["document_type"] = e.DocType,
                ["description"]   = e.Description,
                ["originator"]    = userName ?? "",
                ["date_created"]  = stamp,
                ["suitability"]   = suit,
                ["revision"]      = rev,
                ["status"]        = cde,
                ["cde_status"]    = cde,
                ["file_format"]   = fileFormat,
                ["source"]        = "STING Auto-Export",
            };
            // Deliverable-sourced rows carry the ISO 19650 number so the unified
            // register can match them to their deliverables.json row.
            if (!string.IsNullOrWhiteSpace(e.DocNumber)) entry["doc_number"] = e.DocNumber;
            SetDefaultedFlag(entry, suitDefaulted);
            ApplyIsoUnset(entry, e.IsoUnset);
            register.Add(entry);
            added = true;
            id = nextId;
            return true;
        }

        /// <summary>The register row's suitability is the S0 convention, not a recorded code.</summary>
        public const string SuitabilityDefaultedKey = "suitability_defaulted";

        private static void SetDefaultedFlag(JObject row, bool defaulted)
        {
            if (defaulted) row[SuitabilityDefaultedKey] = true;
            else row.Remove(SuitabilityDefaultedKey);
        }

        /// <summary>Write (non-empty), clear (empty) or leave (null) the row's iso_unset flag.</summary>
        private static void ApplyIsoUnset(JObject row, System.Collections.Generic.IReadOnlyList<string> unset)
        {
            if (unset == null) return;
            var names = unset.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (names.Count == 0) row.Remove("iso_unset");
            else row["iso_unset"] = new JArray(names);
        }

        /// <summary>Next "PREFIX-NNNN" id in an array. The document register is
        /// written with doc_id by some paths and document_id by others; counting only
        /// the requested spelling minted ids that already existed under the other.</summary>
        public static string NextId(JArray arr, string prefix, string idField)
        {
            string[] fields = idField == "doc_id" || idField == "document_id"
                ? new[] { "doc_id", "document_id" } : new[] { idField };
            int max = 0;
            foreach (var item in arr ?? new JArray())
            {
                if (!(item is JObject)) continue;
                foreach (string f in fields)
                {
                    string id = item[f]?.ToString() ?? "";
                    if (id.StartsWith(prefix + "-") &&
                        int.TryParse(id.Substring(prefix.Length + 1), out int n) && n > max)
                        max = n;
                }
            }
            return $"{prefix}-{(max + 1):D4}";
        }
    }
}
