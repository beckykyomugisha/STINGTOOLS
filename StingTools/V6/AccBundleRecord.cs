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
using System.IO;
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

        /// <summary>One line for a dialog: what would be uploaded, and how old it is.</summary>
        public string Describe() =>
            $"{System.IO.Path.GetFileName(Path)}  ({Suitability}, {DeliverableCount} deliverable(s), " +
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
