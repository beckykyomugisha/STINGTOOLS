// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccUploadGate.cs
//
// ONE pre-upload discipline for every path that puts a deliverable into ACC. Before this
// file the two upload paths each had half of it:
//
//   * the Export Centre auto-upload used the upload ledger (skip an identical file, refuse a
//     changed file under a revision already sent) but never checked that the revision and
//     suitability agree (ISO 19650: P with S0-S7, C with A/B/CR);
//   * ACC_UploadModel / ACC_UploadLastBundle checked the pairing but had no ledger, so the
//     same bundle uploaded twice stacked a second ACC version, and a changed file under an
//     unchanged revision went through silently.
//
// Both now call Check before sending and Record after ACC confirmed. Order of the checks:
//
//   1. pairing   - the recorded revision against the suitability (Iso19650RevisionRules.Check),
//                  and the revision a file NAME carries against the suitability (CheckFileName).
//                  An inconsistent pair is refused: it is a deliverable whose status cannot
//                  be read by whoever receives it.
//   2. content   - SHA-256 of the file. An unreadable file is refused, never "sent blind".
//   3. ledger    - with a revision: AccUploadLedger.Check (identical -> skip; changed under the
//                  same revision -> refused unless allowed). Without a revision (a model or a
//                  file the register does not know) the re-issue rule cannot apply, so only the
//                  identical-content skip does; the upload goes ahead and is recorded.
//
// Revit-free and log-free: linked into StingTools.Acc.Tests.

using System;
using System.IO;
using System.Linq;
using StingTools.Core.Drawing;

namespace StingTools.V6
{
    public enum AccUploadGateDecision
    {
        /// <summary>Send it.</summary>
        Upload,
        /// <summary>The identical file is already in ACC: do not send it again. Not a failure.</summary>
        SkipIdentical,
        /// <summary>Must not be sent; <see cref="AccUploadGateResult.Reason"/> says why.</summary>
        Refuse,
    }

    public sealed class AccUploadGateResult
    {
        public AccUploadGateDecision Decision { get; set; }
        public string Reason { get; set; } = string.Empty;
        /// <summary>The ledger's verdict, when the ledger was consulted.</summary>
        public AccLedgerVerdict Ledger { get; set; }
        public string Sha256 { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public bool ShouldUpload => Decision == AccUploadGateDecision.Upload;
        /// <summary>Sent as a new ACC version under an unchanged revision because it was allowed.</summary>
        public bool ReissueAllowed => Ledger?.Decision == AccLedgerDecision.UploadReissueAllowed;
        /// <summary>Refused only because this document + revision + format was already sent with
        /// other bytes. A whole-set re-export of unchanged sheets lands here when the renderer is
        /// not byte-stable (C6), so callers report it apart from a real refusal.</summary>
        public bool HeldAsReissue => Ledger?.Decision == AccLedgerDecision.RefuseReissueWithoutRevisionChange;
        /// <summary>Sent because its suitability changed under an unchanged revision (C5).</summary>
        public bool StatusChange => Ledger?.Decision == AccLedgerDecision.UploadStatusChange;
    }

    public static class AccUploadGate
    {
        /// <summary>Decide whether one file may be uploaded. Pure apart from reading the file to
        /// hash it (pass <paramref name="sha256"/> to skip that). Changes nothing.</summary>
        public static AccUploadGateResult Check(AccUploadLedger ledger, string filePath, string documentNumber,
            string revision, string suitability, bool allowReissue, string sha256 = null)
        {
            string name = Path.GetFileName(filePath ?? string.Empty);
            if (ledger == null)
                return Refuse("the upload ledger is not available, so STING cannot tell whether this file was already sent");

            string rev = (revision ?? string.Empty).Trim();
            string suit = (suitability ?? string.Empty).Trim();
            if (rev.Length > 0 && suit.Length > 0)
            {
                var pair = Iso19650RevisionRules.Check(rev, suit);
                if (pair.IsInconsistent) return Refuse($"revision {rev} with suitability {suit}: {pair.Reason}");
            }
            if (suit.Length > 0)
            {
                var byName = Iso19650RevisionRules.CheckFileName(name, suit);
                if (byName.IsInconsistent) return Refuse($"the name {name}: {byName.Reason}");
            }

            string sha = string.IsNullOrWhiteSpace(sha256) ? AccUploadLedger.Sha256OfFile(filePath) : sha256.Trim();
            if (string.IsNullOrEmpty(sha)) return Refuse($"{name} could not be read to fingerprint it");
            string fmt = AccUploadLedger.FormatOf(filePath);
            string doc = (documentNumber ?? string.Empty).Trim();
            if (doc.Length == 0) doc = Path.GetFileNameWithoutExtension(name);

            if (rev.Length == 0)
            {
                var same = ledger.Entries.Where(e => e != null &&
                        string.Equals(e.DocumentNumber, doc, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(e.Format, fmt, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(e.Sha256, sha, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(e => e.UploadedUtc).FirstOrDefault();
                if (same != null)
                    return new AccUploadGateResult
                    {
                        Decision = AccUploadGateDecision.SkipIdentical, Sha256 = sha, Format = fmt,
                        Reason = $"already in ACC — the identical file was sent {same.UploadedUtc:yyyy-MM-dd HH:mm}Z",
                    };
                return new AccUploadGateResult
                {
                    Decision = AccUploadGateDecision.Upload, Sha256 = sha, Format = fmt,
                    Reason = "no revision is recorded for this file, so only an identical re-send can be detected",
                };
            }

            var verdict = ledger.Check(doc, rev, fmt, sha, allowReissue, suit);
            var result = new AccUploadGateResult { Ledger = verdict, Sha256 = sha, Format = fmt, Reason = verdict.Reason };
            result.Decision = verdict.ShouldUpload ? AccUploadGateDecision.Upload
                : verdict.Decision == AccLedgerDecision.SkipIdentical ? AccUploadGateDecision.SkipIdentical
                : AccUploadGateDecision.Refuse;
            return result;
        }

        /// <summary>Record an upload ACC confirmed. Call ONLY after a successful upload.</summary>
        public static void Record(AccUploadLedger ledger, AccUploadGateResult gate, string filePath, string documentNumber,
            string revision, string suitability, string itemUrn, string versionUrn, DateTime utcNow,
            string folderUrn = null)
        {
            if (ledger == null || gate == null) return;
            string name = Path.GetFileName(filePath ?? string.Empty);
            string doc = (documentNumber ?? string.Empty).Trim();
            ledger.Record(new AccLedgerEntry
            {
                DocumentNumber = doc.Length > 0 ? doc : Path.GetFileNameWithoutExtension(name),
                Revision = (revision ?? string.Empty).Trim(),
                Format = gate.Format,
                Sha256 = gate.Sha256,
                FileName = name,
                Suitability = suitability ?? string.Empty,
                UploadedUtc = utcNow,
                ItemUrn = itemUrn ?? string.Empty,
                VersionUrn = versionUrn ?? string.Empty,
                FolderUrn = folderUrn ?? string.Empty,
            });
        }

        private static AccUploadGateResult Refuse(string why) =>
            new AccUploadGateResult { Decision = AccUploadGateDecision.Refuse, Reason = why };
    }
}
