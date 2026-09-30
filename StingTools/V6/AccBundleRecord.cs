// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccBundleRecord.cs
//
// What ACCPublish just produced, written down so a later step can upload it.
//
// PR #927 added ACC_UploadModel behind a file picker and deliberately refused to put
// it in the fortnightly Coordination Cycle: a workflow step cannot answer "upload
// which file?", and guessing the model path — or silently uploading the active
// document — would put an unintended file into an issued CDE container.
//
// That question is answerable now. ACCPublish already builds a ZIP at a deterministic
// path and registers it in the export register; this records that path where the
// uploader can read it. "Upload the bundle step 6 just produced" is a file choice, not
// a guess.
//
// Revit-free and log-free: linked into StingTools.Acc.Tests, and it builds no project
// paths of its own — the caller holds the Document and resolves through StingPaths, the
// same contract CommissioningSource works under.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StingTools.V6
{
    /// <summary>The last ACC-ready bundle ACCPublish produced.</summary>
    public sealed class AccBundleRecord
    {
        /// <summary>Absolute path to the ZIP.</summary>
        [JsonProperty("path")] public string Path { get; set; } = string.Empty;
        [JsonProperty("createdUtc")] public DateTime CreatedUtc { get; set; }
        [JsonProperty("suitability")] public string Suitability { get; set; } = string.Empty;
        [JsonProperty("deliverableCount")] public int DeliverableCount { get; set; }
        [JsonProperty("sizeBytes")] public long SizeBytes { get; set; }
        /// <summary>The PREPARED transmittal ACCPublish recorded for this bundle. A
        /// successful upload of this exact file marks that row SENT (IM-17).</summary>
        [JsonProperty("transmittalId")] public string TransmittalId { get; set; } = string.Empty;
        /// <summary>The ONE revision every deliverable in the bundle carries, per the document
        /// register; empty when they carry none or several (see <see cref="RevisionNote"/>).
        /// Sent to ACC as the ISO Revision attribute. Never defaulted.</summary>
        [JsonProperty("revision")] public string Revision { get; set; } = string.Empty;
        /// <summary>Why <see cref="Revision"/> is empty, in a sentence; empty when it is set.</summary>
        [JsonProperty("revisionNote")] public string RevisionNote { get; set; } = string.Empty;

        /// <summary>
        /// The bundle's revision from its deliverables' revisions (one entry per deliverable,
        /// blank where the register records none). A bundle is one revision only when EVERY
        /// deliverable carries the same one; otherwise no revision is recorded and the note
        /// says why — a bundle spanning P02 and P03 is not "P03", and a register that records
        /// no revision is not "P01".
        /// </summary>
        public static (string revision, string note) CommonRevision(IEnumerable<string> perDeliverable)
        {
            var all = (perDeliverable ?? Enumerable.Empty<string>()).Select(r => (r ?? string.Empty).Trim()).ToList();
            if (all.Count == 0) return (string.Empty, "the bundle has no deliverables to take a revision from");
            int blank = all.Count(r => r.Length == 0);
            var distinct = all.Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (distinct.Count == 0)
                return (string.Empty, "none of the bundle's deliverables carries a revision in the document register");
            if (distinct.Count > 1)
                return (string.Empty, $"the bundle spans {distinct.Count} revisions ({string.Join(", ", distinct)}), so it carries none");
            if (blank > 0)
                return (string.Empty, $"{blank} of {all.Count} deliverables carry no revision, so the bundle's revision is not known");
            return (distinct[0], string.Empty);
        }

        /// <summary>One line for a dialog: what would be uploaded, and how old it is.</summary>
        public string Describe() =>
            $"{System.IO.Path.GetFileName(Path)}  ({Suitability}, " +
            $"{(string.IsNullOrWhiteSpace(Revision) ? "no revision" : "revision " + Revision)}, {DeliverableCount} deliverable(s), " +
            $"{SizeBytes / 1024.0 / 1024.0:F1} MB, built {CreatedUtc:yyyy-MM-dd HH:mm}Z)";

        /// <summary>Write the record. Failures are returned, not thrown: recording the
        /// bundle is a convenience, and it must never fail the publish that produced it.</summary>
        public static bool TryWrite(string recordPath, AccBundleRecord rec, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(recordPath) || rec == null || string.IsNullOrEmpty(rec.Path))
            {
                error = "no record path or no bundle path";
                return false;
            }
            try
            {
                string dir = System.IO.Path.GetDirectoryName(recordPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(recordPath, JsonConvert.SerializeObject(rec, Formatting.Indented));
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        /// <summary>Read the record as written. Null when absent or unreadable.</summary>
        public static AccBundleRecord Read(string recordPath)
        {
            if (string.IsNullOrEmpty(recordPath)) return null;
            try
            {
                if (!File.Exists(recordPath)) return null;
                var rec = JsonConvert.DeserializeObject<AccBundleRecord>(File.ReadAllText(recordPath));
                return string.IsNullOrEmpty(rec?.Path) ? null : rec;
            }
            catch (Exception) { return null; }
        }

        /// <summary>The recorded bundle IF THE FILE IS STILL THERE. Null otherwise.
        ///
        /// This is the difference between a file choice and a guess. A record pointing at a
        /// ZIP somebody has since deleted, moved or cleaned out is not a file to upload —
        /// acting on it either fails at the last moment or, worse, matches something else
        /// that has taken the path. A caller must never treat Read() as "there is a bundle".</summary>
        public static AccBundleRecord ReadExisting(string recordPath)
        {
            var rec = Read(recordPath);
            if (rec == null) return null;
            try { return File.Exists(rec.Path) ? rec : null; }
            catch (Exception) { return null; }
        }

        /// <summary>Build a record for a bundle that exists on disk. Null when it does not —
        /// there is no such thing as a record of a file that was never written.</summary>
        public static AccBundleRecord ForFile(string zipPath, string suitability, int deliverableCount)
        {
            if (string.IsNullOrEmpty(zipPath)) return null;
            long size = 0;
            try { if (!File.Exists(zipPath)) return null; size = new FileInfo(zipPath).Length; }
            catch (Exception) { return null; }
            return new AccBundleRecord
            {
                Path = zipPath,
                CreatedUtc = DateTime.UtcNow,
                Suitability = suitability ?? string.Empty,
                DeliverableCount = deliverableCount,
                SizeBytes = size,
            };
        }

        /// <summary>The record file name, so the writer and the reader cannot disagree on it.</summary>
        public const string FileName = "last_bundle.json";
    }
}
