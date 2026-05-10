// ══════════════════════════════════════════════════════════════════════
//  SitePhotoOfflineCache — Phase 179 thin disk cache for the BCC Review
//  queue. When the desktop is offline (or the server is down), the BCC
//  reads the last successfully-loaded page from disk so the panel
//  doesn't appear empty. Cache files live under
//
//      %LOCALAPPDATA%\StingTools\photo-cache\{projectId}\photos.json
//
//  Cache hit policy: any cache < 14 days old is considered fresh enough
//  to display with an "offline — last sync …" pill. Older caches still
//  load but flag a warning.
// ══════════════════════════════════════════════════════════════════════

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.BIMManager
{
    internal static class SitePhotoOfflineCache
    {
        private static readonly object _gate = new();

        private static string CacheDir(Guid projectId)
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StingTools", "photo-cache", projectId.ToString());
            Directory.CreateDirectory(root);
            return root;
        }

        public sealed class CachedPage
        {
            public DateTime           SavedAtUtc { get; set; } = DateTime.UtcNow;
            public List<SitePhotoDto> Photos     { get; set; } = new();
        }

        // Phase 180 — bound the JSON file size by trimming to the most
        // recent N photos. With 1000+ photos a project the on-disk
        // file grew to multi-MB and degraded BCC start time.
        private const int MaxCachedPhotos = 200;

        public static void Save(Guid projectId, IEnumerable<SitePhotoDto> photos)
        {
            try
            {
                lock (_gate)
                {
                    var path = Path.Combine(CacheDir(projectId), "photos.json");
                    // Newest first — the offline reader uses CapturedAt
                    // descending anyway. Trim to MaxCachedPhotos before
                    // serialising.
                    var trimmed = photos
                        .OrderByDescending(p => p.CapturedAt)
                        .Take(MaxCachedPhotos)
                        .ToList();
                    var payload = new CachedPage
                    {
                        SavedAtUtc = DateTime.UtcNow,
                        Photos     = trimmed,
                    };
                    File.WriteAllText(path, JsonConvert.SerializeObject(payload));
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SitePhotoOfflineCache.Save: {ex.Message}");
            }
        }

        public static CachedPage? Load(Guid projectId)
        {
            try
            {
                var path = Path.Combine(CacheDir(projectId), "photos.json");
                if (!File.Exists(path)) return null;
                var text = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<CachedPage>(text);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SitePhotoOfflineCache.Load: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Cache the JPEG bytes for a single photo. Limit on disk to ≈
        /// 50 MB per project — once the directory exceeds the cap, we
        /// drop the oldest files. Best-effort; failure is silent.
        /// </summary>
        public static void SaveThumbBytes(Guid projectId, Guid photoId, byte[] bytes)
        {
            try
            {
                var dir = Path.Combine(CacheDir(projectId), "thumbs");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, photoId.ToString("N") + ".jpg");
                File.WriteAllBytes(path, bytes);
                EnforceCap(dir, capBytes: 50L * 1024 * 1024);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SitePhotoOfflineCache.SaveThumb: {ex.Message}");
            }
        }

        public static byte[]? LoadThumbBytes(Guid projectId, Guid photoId)
        {
            try
            {
                var path = Path.Combine(CacheDir(projectId), "thumbs", photoId.ToString("N") + ".jpg");
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch { return null; }
        }

        private static void EnforceCap(string dir, long capBytes)
        {
            try
            {
                var files = new DirectoryInfo(dir).GetFiles().OrderBy(f => f.LastWriteTimeUtc).ToList();
                long total = files.Sum(f => f.Length);
                int i = 0;
                while (total > capBytes && i < files.Count)
                {
                    total -= files[i].Length;
                    files[i].Delete();
                    i++;
                }
            }
            catch { /* best-effort */ }
        }
    }
}
