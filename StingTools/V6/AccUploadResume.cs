// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccUploadResume.cs  (ROADMAP ACC-HARD-2)
//
// A multi-GB model over a site connection should not restart from the first byte because
// Revit crashed, the laptop slept or the link dropped at part 180 of 200. The signed-S3
// upload is resumable by design: the storage object, the uploadKey and the parts already
// PUT stay valid for a while, and new URLs for the remaining parts can be requested with
// the same uploadKey. This file remembers that state between attempts.
//
// KEYED ON EVERYTHING THAT MAKES IT THE SAME UPLOAD: the file's full path, size and
// last-write time, and the target project + folder. Change any of them and it is a new
// upload - resuming someone else's half-sent bytes into a different file would corrupt it.
//
// EXPIRY. Resume state older than MaxAge is discarded. APS does not state a single lifetime
// for an uploadKey in the reference we could read (UNCONFIRMED); the direct-to-S3 guidance
// is to complete uploads promptly. 20 h is a conservative ceiling; if Autodesk has already
// forgotten the uploadKey the sign request fails, the state is dropped and the upload starts
// again from the first part - never a silent partial file.
//
// Per-user, machine-local (%LOCALAPPDATA%\Planscape\acc_upload_resume). Revit-free.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace StingTools.V6
{
    public sealed class AccUploadResumeState
    {
        public string Key { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime FileWriteUtc { get; set; }
        public string ProjectId { get; set; } = string.Empty;
        public string FolderUrn { get; set; } = string.Empty;
        public string ObjectId { get; set; } = string.Empty;
        public string BucketKey { get; set; } = string.Empty;
        public string ObjectKey { get; set; } = string.Empty;
        public string UploadKey { get; set; } = string.Empty;
        public long PartSize { get; set; }
        public int TotalParts { get; set; }
        /// <summary>Parts 1..PartsCompleted are confirmed uploaded (sequential upload).</summary>
        public int PartsCompleted { get; set; }
        /// <summary>The S3 upload was completed (POST signeds3upload succeeded); only the ACC item/version is left.</summary>
        public bool Finalised { get; set; }
        public DateTime StartedUtc { get; set; }
    }

    public static class AccUploadResume
    {
        public static readonly TimeSpan MaxAge = TimeSpan.FromHours(20);

        /// <summary>Test seam. Production never sets it.</summary>
        internal static string DirOverride;

        private static string Dir => DirOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Planscape", "acc_upload_resume");

        public static string KeyFor(string filePath, long size, DateTime writeUtc, string projectId, string folderUrn)
        {
            string raw = string.Join("|", Path.GetFullPath(filePath ?? "").ToUpperInvariant(), size,
                writeUtc.ToUniversalTime().Ticks, AccIds.ForAcc(projectId).ToLowerInvariant(), folderUrn ?? "");
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).Substring(0, 32).ToLowerInvariant();
        }

        private static string PathFor(string key) => Path.Combine(Dir, key + ".json");

        /// <summary>The state to resume, or null (none, expired, unreadable, or made with a
        /// different part size). Anything unusable is deleted so it cannot be tried twice.</summary>
        public static AccUploadResumeState Load(string key, long partSize, DateTime utcNow)
        {
            string p = PathFor(key);
            try
            {
                if (!File.Exists(p)) return null;
                var s = JsonConvert.DeserializeObject<AccUploadResumeState>(File.ReadAllText(p));
                bool usable = s != null && s.Key == key && s.PartSize == partSize
                              && utcNow - s.StartedUtc < MaxAge
                              && !string.IsNullOrEmpty(s.ObjectId) && s.PartsCompleted >= 0;
                if (usable) return s;
            }
            catch (Exception) { /* unreadable: treated as absent and removed below */ }
            Delete(key);
            return null;
        }

        /// <summary>Best-effort: a state that cannot be saved only costs the ability to resume.</summary>
        public static bool Save(AccUploadResumeState s)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string p = PathFor(s.Key), tmp = p + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(s, Formatting.Indented));
                if (File.Exists(p)) File.Replace(tmp, p, null); else File.Move(tmp, p);
                return true;
            }
            catch (Exception) { return false; }
        }

        public static void Delete(string key)
        {
            try { File.Delete(PathFor(key)); } catch (Exception) { /* nothing to resume either way */ }
        }
    }
}
