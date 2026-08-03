// DocumentIdentity.cs — ISO 19650 consolidation (IM-13).
//
// ONE rule for "what is this document row's id". Three resolvers grew the same
// first-non-blank-candidate logic independently and then disagreed on trimming:
//
//   DocumentRegister.First(...)          returned the raw value (untrimmed)
//   CoordStores.RowId(...)               returned the raw value (untrimmed)
//   DeliverableLifecycle.DeliverableKey  trimmed
//
// So a DocNumber carrying a trailing space keyed as "PRJ-001" in deliverables.json
// but "PRJ-001 " in the register reader, the two stores landed in disjoint id
// namespaces, and DocumentRegister.BuildUnified showed the same deliverable twice.
//
// The candidate KEY LISTS legitimately differ per store (the register is snake_case,
// deliverables are PascalCase, CoordStores spans issues/meetings/transmittals too),
// so what is shared here is the rule — trim + first non-blank — not the key list.
//
// Deliberately Revit-free so the rule is unit-testable outside a Revit host.

using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    /// <summary>Canonical document-row identity rules (trim + first-non-blank candidate).</summary>
    public static class DocumentIdentity
    {
        /// <summary>
        /// Canonical form of a single id value: trimmed, with blank/whitespace-only
        /// collapsing to "". This is the ONLY place the trimming rule lives.
        /// </summary>
        public static string Normalize(string value)
            => string.IsNullOrWhiteSpace(value) ? "" : value.Trim();

        /// <summary>
        /// First non-blank value among <paramref name="keys"/>, normalised.
        /// Returns null when no candidate carries a value, so callers can pick their own
        /// empty sentinel ("" or null) without this helper changing their contract.
        /// </summary>
        public static string FirstNonBlank(JObject row, params string[] keys)
        {
            if (row == null || keys == null) return null;
            foreach (string k in keys)
            {
                // row[k]?.ToString() rather than a (string) cast: a doc number stored as a
                // JSON number casts to null but stringifies fine.
                string v = row[k]?.ToString();
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            }
            return null;
        }

        /// <summary>
        /// First non-blank among already-extracted <paramref name="values"/>, normalised.
        /// For POCO/dynamic rows that never pass through a JObject.
        /// Returns null when every candidate is blank.
        /// </summary>
        public static string FirstNonBlankValue(params string[] values)
        {
            if (values == null) return null;
            foreach (string v in values)
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            return null;
        }

        /// <summary>
        /// Candidate id keys for the deliverables store (PascalCase DeliverableRow).
        /// </summary>
        public static readonly string[] DeliverableKeys = { "DocNumber", "Code" };

        /// <summary>
        /// Candidate id keys for the broad document register (snake_case). doc_number
        /// leads because it is the ISO 19650 number deliverables.json also keys on —
        /// without it the two stores can never dedup.
        /// </summary>
        public static readonly string[] RegisterKeys = { "doc_number", "doc_id", "document_id" };

        /// <summary>
        /// Candidate id keys spanning every coordination store CoordStores merges
        /// (issues, documents, transmittals, meetings, revisions).
        /// </summary>
        public static readonly string[] CoordStoreKeys =
        {
            "id", "issue_id", "document_id", "doc_id", "transmittal_id",
            "meeting_id", "revision_id", "Id", "DocNumber", "number",
        };

        /// <summary>Comparer every id-keyed dictionary in the register path must use.</summary>
        public static readonly IEqualityComparer<string> Comparer = System.StringComparer.OrdinalIgnoreCase;
    }
}
