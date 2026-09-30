// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccUploadLedger.cs
//
// What STING has already sent to ACC, per project, so an automatic upload after an export
// can tell three cases apart instead of blindly adding an ACC version every time:
//
//   1. the IDENTICAL file (same document number, revision, format and SHA-256) was already
//      sent                                   -> skip, and say so;
//   2. the same document number + revision + format was sent with DIFFERENT content
//                                              -> a re-issue without a revision change. ISO
//      19650 does not allow a changed deliverable under an unchanged revision, and ACC would
//      silently stack it as "version 2" of the same revision. Refused and reported, unless
//      the profile explicitly allows it;
//   3. anything else                           -> upload, then record it.
//
// The format (file extension) is part of the key: the PDF and the DWG of one sheet share a
// document number and a revision, and their bytes necessarily differ — that is two
// renditions, not a re-issue.
//
// Kept at <project>/_data/coord/acc/acc_upload_ledger.json (resolved by the caller through
// StingPaths, beside last_bundle.json). Revit-free and log-free: linked into
// StingTools.Acc.Tests.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;

namespace StingTools.V6
{
    /// <summary>One file STING sent to ACC.</summary>
    public sealed class AccLedgerEntry
    {
        [JsonProperty("documentNumber")] public string DocumentNumber { get; set; } = string.Empty;
        [JsonProperty("revision")] public string Revision { get; set; } = string.Empty;
        /// <summary>Upper-case extension without the dot ("PDF", "DWG").</summary>
        [JsonProperty("format")] public string Format { get; set; } = string.Empty;
        [JsonProperty("sha256")] public string Sha256 { get; set; } = string.Empty;
        [JsonProperty("fileName")] public string FileName { get; set; } = string.Empty;
        [JsonProperty("suitability")] public string Suitability { get; set; } = string.Empty;
        [JsonProperty("uploadedUtc")] public DateTime UploadedUtc { get; set; }
        [JsonProperty("itemUrn")] public string ItemUrn { get; set; } = string.Empty;
        [JsonProperty("versionUrn")] public string VersionUrn { get; set; } = string.Empty;
        /// <summary>The ACC folder the file went into — what a later retirement (supersede /
        /// replace → ARCHIVE) needs to stamp the original (R11).</summary>
        [JsonProperty("folderUrn")] public string FolderUrn { get; set; } = string.Empty;
        /// <summary>When STING retired this upload to the ARCHIVE folder; null while live. A
        /// retired entry is not retired again by a second supersede.</summary>
        [JsonProperty("retiredUtc")] public DateTime? RetiredUtc { get; set; }
        [JsonProperty("retiredAs")] public string RetiredAs { get; set; }
    }

    public enum AccLedgerDecision
    {
        /// <summary>Never sent under this document number + revision + format: upload.</summary>
        Upload,
        /// <summary>This exact content was already sent: do not upload again.</summary>
        SkipIdentical,
        /// <summary>Different content under a document number + revision already sent: refused.</summary>
        RefuseReissueWithoutRevisionChange,
        /// <summary>As above, but the profile allows it: upload as a new ACC version.</summary>
        UploadReissueAllowed,
        /// <summary>No document number or revision to key on: cannot be ledgered, so not sent.</summary>
        RefuseNoIdentity,
    }

    public sealed class AccLedgerVerdict
    {
        public AccLedgerDecision Decision { get; set; }
        /// <summary>The earlier upload this verdict is about, when there is one.</summary>
        public AccLedgerEntry Previous { get; set; }
        public string Reason { get; set; } = string.Empty;
        public bool ShouldUpload => Decision == AccLedgerDecision.Upload || Decision == AccLedgerDecision.UploadReissueAllowed;
    }

    public sealed class AccUploadLedger
    {
        public const string FileName = "acc_upload_ledger.json";

        [JsonProperty("entries")] public List<AccLedgerEntry> Entries { get; set; } = new List<AccLedgerEntry>();

        /// <summary>Upper-case extension without the dot.</summary>
        public static string FormatOf(string path)
        {
            try { return (Path.GetExtension(path ?? string.Empty) ?? string.Empty).TrimStart('.').ToUpperInvariant(); }
            catch (ArgumentException) { return string.Empty; }
        }

        /// <summary>Decide what to do with one file. Pure: reads the ledger, changes nothing.</summary>
        public AccLedgerVerdict Check(string documentNumber, string revision, string format, string sha256, bool allowReissue)
        {
            string doc = (documentNumber ?? string.Empty).Trim();
            string rev = (revision ?? string.Empty).Trim();
            string fmt = (format ?? string.Empty).Trim().ToUpperInvariant();
            string sha = (sha256 ?? string.Empty).Trim().ToLowerInvariant();
            if (doc.Length == 0 || rev.Length == 0 || sha.Length == 0)
                return new AccLedgerVerdict
                {
                    Decision = AccLedgerDecision.RefuseNoIdentity,
                    Reason = "no document number, revision or content hash to record the upload under",
                };

            var same = Entries.Where(e => e != null &&
                    string.Equals(e.DocumentNumber, doc, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Revision, rev, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Format, fmt, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.UploadedUtc)
                .ToList();
            if (same.Count == 0)
                return new AccLedgerVerdict { Decision = AccLedgerDecision.Upload, Reason = "not sent before" };

            var identical = same.FirstOrDefault(e => string.Equals(e.Sha256, sha, StringComparison.OrdinalIgnoreCase));
            if (identical != null)
                return new AccLedgerVerdict
                {
                    Decision = AccLedgerDecision.SkipIdentical,
                    Previous = identical,
                    Reason = $"already in ACC — the identical file was sent {identical.UploadedUtc:yyyy-MM-dd HH:mm}Z",
                };

            var last = same[0];
            return allowReissue
                ? new AccLedgerVerdict
                {
                    Decision = AccLedgerDecision.UploadReissueAllowed,
                    Previous = last,
                    Reason = $"content changed since {last.UploadedUtc:yyyy-MM-dd HH:mm}Z under the same revision {rev}; " +
                             "sent as a new ACC version because the profile allows a re-issue without a revision change",
                }
                : new AccLedgerVerdict
                {
                    Decision = AccLedgerDecision.RefuseReissueWithoutRevisionChange,
                    Previous = last,
                    Reason = $"re-issue without a revision change: {doc} revision {rev} ({fmt}) was sent " +
                             $"{last.UploadedUtc:yyyy-MM-dd HH:mm}Z with different content. Revise the sheet " +
                             "(or allow re-issues in the export profile) — nothing was sent",
                };
        }

        /// <summary>
        /// The ACC documents a deliverable is live as: for each format, the newest recorded
        /// upload of <paramref name="documentNumber"/> that has a version URN, unless that
        /// newest one is already retired (R11 — Supersede / Replace find the deliverable here;
        /// the document register never carried its ACC URNs).
        /// </summary>
        public List<AccLedgerEntry> LiveRenditions(string documentNumber)
        {
            string doc = (documentNumber ?? string.Empty).Trim();
            if (doc.Length == 0) return new List<AccLedgerEntry>();
            return Entries
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.VersionUrn) &&
                            string.Equals(e.DocumentNumber, doc, StringComparison.OrdinalIgnoreCase))
                .GroupBy(e => (e.Format ?? string.Empty).ToUpperInvariant())
                .Select(g => g.OrderByDescending(e => e.UploadedUtc).First())
                .Where(e => e.RetiredUtc == null)
                .OrderBy(e => e.Format, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Record a completed upload.</summary>
        public void Record(AccLedgerEntry entry)
        {
            if (entry == null) return;
            entry.Format = (entry.Format ?? string.Empty).ToUpperInvariant();
            entry.Sha256 = (entry.Sha256 ?? string.Empty).ToLowerInvariant();
            Entries.Add(entry);
        }

        /// <summary>SHA-256 of a file's bytes, lower-case hex. Null when it cannot be read.</summary>
        public static string Sha256OfFile(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                using var sha = SHA256.Create();
                return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
            }
            catch (Exception) { return null; }
        }

        /// <summary>Read the ledger. A missing file is an empty ledger. An unreadable one is
        /// NOT treated as empty — that would make every file look "never sent" and re-send
        /// the lot — so it returns null with the reason.</summary>
        public static AccUploadLedger Load(string path, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path)) { error = "no ledger path"; return null; }
            try
            {
                if (!File.Exists(path)) return new AccUploadLedger();
                var l = JsonConvert.DeserializeObject<AccUploadLedger>(File.ReadAllText(path));
                if (l == null) { error = "the ledger file is empty or not a ledger"; return null; }
                l.Entries ??= new List<AccLedgerEntry>();
                return l;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        public bool TrySave(string path, out string error)
        {
            error = null;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(this, Formatting.Indented));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }
    }
}
