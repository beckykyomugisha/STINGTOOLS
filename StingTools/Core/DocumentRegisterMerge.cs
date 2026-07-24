// DocumentRegisterMerge.cs — ISO 19650 consolidation (IM-13).
//
// The Revit-free core of DocumentRegister: the RegisterEntry shape, the two
// per-store row mappings, and the id-keyed merge. DocumentRegister keeps the
// Document-facing half (path resolution + file IO) and calls in here.
//
// Split out so the dedup rule is provable without a Revit host — the trailing-space
// dedup regression this file exists to fix (IM-13) is asserted in
// StingTools.Tags.Tests.DocumentRegisterMergeTests.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    /// <summary>One normalised row of the unified document register.</summary>
    public class RegisterEntry
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Type { get; set; } = "";
        public string Discipline { get; set; } = "";
        public string Suitability { get; set; } = "";
        public string CdeStatus { get; set; } = "";
        public string Revision { get; set; } = "";
        public string Direction { get; set; } = "";
        public string Status { get; set; } = "";
        public string ReviewedBy { get; set; } = "";
        public string ApprovedBy { get; set; } = "";
        public string DateCreated { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string FileFormat { get; set; } = "";
        public string CreatedBy { get; set; } = "";
        /// <summary>Which store(s) this row came from: "register", "deliverable", or "both".</summary>
        public string Source { get; set; } = "";
    }

    /// <summary>Pure row-mapping + merge logic behind <see cref="DocumentRegister"/>.</summary>
    public static class DocumentRegisterMerge
    {
        /// <summary>Map one row of the broad document register (snake_case schema).</summary>
        public static RegisterEntry MapRegisterRow(JObject t)
        {
            if (t == null) return null;
            string source = Str(t, "source");
            string direction = Str(t, "direction");
            if (string.IsNullOrEmpty(direction))
                direction = source.IndexOf("Auto-Export", StringComparison.OrdinalIgnoreCase) >= 0 ? "OUT" : "";

            return new RegisterEntry
            {
                // doc_number first: deliverable-sourced rows carry the ISO 19650 number,
                // which is the SAME key deliverables.json uses — without it the two
                // stores live in disjoint id namespaces and dedup could never fire
                // ("both" was always 0 and every deliverable appeared twice).
                Id          = First(t, DocumentIdentity.RegisterKeys),
                Title       = First(t, "title", "file_name", "description"),
                Type        = First(t, "type", "document_type"),
                Discipline  = Str(t, "discipline"),
                Suitability = Str(t, "suitability"),
                CdeStatus   = First(t, "cde_status", "status"),
                Revision    = Str(t, "revision"),
                Direction   = direction,
                Status      = First(t, "status", "review_status"),
                ReviewedBy  = Str(t, "reviewed_by"),
                DateCreated = Str(t, "date_created"),
                FilePath    = First(t, "file_reference", "file_path"),
                FileFormat  = First(t, "file_format", "format"),
                CreatedBy   = First(t, "created_by", "author"),
                Source      = "register"
            };
        }

        /// <summary>Map one row of the lifecycle deliverables store (PascalCase schema).</summary>
        public static RegisterEntry MapDeliverableRow(JObject t)
        {
            if (t == null) return null;
            return new RegisterEntry
            {
                Id          = First(t, DocumentIdentity.DeliverableKeys),
                Title       = First(t, "Name", "Title"),
                Type        = Str(t, "Type"),
                Discipline  = Str(t, "Discipline"),
                Suitability = Str(t, "Suitability"),
                CdeStatus   = Str(t, "CDE"),
                Revision    = Str(t, "Revision"),
                Direction   = "OUT",
                Status      = Str(t, "Status"),
                ReviewedBy  = Str(t, "ReviewedBy"),
                ApprovedBy  = Str(t, "ApprovedBy"),
                DateCreated = LatestRevisionTs(t),
                FilePath    = Str(t, "SignedFilePath"),
                FileFormat  = First(t, "FileFormat", "Format"),
                CreatedBy   = First(t, "CreatedBy", "Author", "Originator"),
                Source      = "deliverable"
            };
        }

        /// <summary>
        /// Merge both stores into one id-keyed list. Rows present in both collapse to a
        /// single entry sourced "both", preferring the deliverable row's lifecycle fields
        /// and filling gaps from the register row — so deliverables MUST be passed first.
        ///
        /// Ids are normalised (trimmed) before keying: a register row "PRJ-001 " and a
        /// deliverable "PRJ-001" are the same document and must collapse to one row.
        /// </summary>
        public static List<RegisterEntry> Merge(
            IEnumerable<RegisterEntry> deliverables, IEnumerable<RegisterEntry> register)
        {
            var byId = new Dictionary<string, RegisterEntry>(StringComparer.OrdinalIgnoreCase);
            var noId = new List<RegisterEntry>();

            void Add(RegisterEntry e)
            {
                if (e == null) return;
                // Normalise here too, not only at map time: callers building entries by
                // hand must not be able to reintroduce the disjoint-namespace bug.
                string key = DocumentIdentity.Normalize(e.Id);
                if (key.Length == 0) { noId.Add(e); return; }
                e.Id = key;
                if (byId.TryGetValue(key, out var existing))
                {
                    Fill(existing, e);
                    existing.Source = "both";
                }
                else byId[key] = e;
            }

            // Deliverables first, so their richer lifecycle fields win on collision.
            foreach (var e in deliverables ?? Enumerable.Empty<RegisterEntry>()) Add(e);
            foreach (var e in register ?? Enumerable.Empty<RegisterEntry>()) Add(e);

            return byId.Values.Concat(noId)
                .OrderBy(e => e.Direction).ThenBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Fill empty fields of <paramref name="keep"/> from <paramref name="other"/>.</summary>
        private static void Fill(RegisterEntry keep, RegisterEntry other)
        {
            if (string.IsNullOrEmpty(keep.Title))       keep.Title       = other.Title;
            if (string.IsNullOrEmpty(keep.Type))        keep.Type        = other.Type;
            if (string.IsNullOrEmpty(keep.Discipline))  keep.Discipline  = other.Discipline;
            if (string.IsNullOrEmpty(keep.Suitability)) keep.Suitability = other.Suitability;
            if (string.IsNullOrEmpty(keep.CdeStatus))   keep.CdeStatus   = other.CdeStatus;
            if (string.IsNullOrEmpty(keep.Revision))    keep.Revision    = other.Revision;
            if (string.IsNullOrEmpty(keep.Direction))   keep.Direction   = other.Direction;
            if (string.IsNullOrEmpty(keep.Status))      keep.Status      = other.Status;
            if (string.IsNullOrEmpty(keep.ReviewedBy))  keep.ReviewedBy  = other.ReviewedBy;
            if (string.IsNullOrEmpty(keep.ApprovedBy))  keep.ApprovedBy  = other.ApprovedBy;
            if (string.IsNullOrEmpty(keep.DateCreated)) keep.DateCreated = other.DateCreated;
            if (string.IsNullOrEmpty(keep.FilePath))    keep.FilePath    = other.FilePath;
            if (string.IsNullOrEmpty(keep.FileFormat))  keep.FileFormat  = other.FileFormat;
            if (string.IsNullOrEmpty(keep.CreatedBy))   keep.CreatedBy   = other.CreatedBy;
        }

        private static string LatestRevisionTs(JObject row)
        {
            var hist = row["RevisionHistory"] as JArray ?? row["revision_history"] as JArray;
            if (hist == null) return "";
            string best = "";
            DateTime bestDt = DateTime.MinValue;
            foreach (var h in hist.OfType<JObject>())
            {
                string ts = First(h, "Timestamp", "timestamp");
                if (DateTime.TryParse(ts, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) && dt > bestDt)
                { bestDt = dt; best = ts; }
            }
            return best;
        }

        private static string Str(JObject o, string key) => o[key]?.ToString() ?? "";

        /// <summary>First non-blank candidate, trimmed — the shared IM-13 identity rule.</summary>
        private static string First(JObject o, params string[] keys)
            => DocumentIdentity.FirstNonBlank(o, keys) ?? "";
    }
}
