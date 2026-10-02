#nullable enable
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Temp
{
    /// <summary>
    /// S8.5.1 — downloads the Planscape family library bundle (declared in
    /// <c>StingTools/Data/family-library/manifest.json</c>) to a per-user
    /// cache and loads each .rfa into the active project. Only runs once
    /// per (tenant, version) combination; later invocations resolve to the
    /// cached bundle and skip straight to load.
    ///
    /// Cache layout:
    ///   %APPDATA%/Planscape/Families/<version>/manifest.json
    ///   %APPDATA%/Planscape/Families/<version>/tags/*.rfa
    ///   %APPDATA%/Planscape/Families/<version>/titleblocks/*.rfa
    ///   ...
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FamilyLibraryLoaderCommand : IExternalCommand
    {
        // Fallbacks. project_config.json FAMILY_LIBRARY_URL / FAMILY_LIBRARY_SHA256 override
        // them, so a hotfix bundle can ship without a plugin redeploy; FAMILY_LIBRARY_LOCAL_ZIP
        // names a zip on disk (or a share) that is used instead of downloading.
        private const string DefaultCdnZipUrl = "https://cdn.planscape.app/families/PlanscapeStandard-v1.0.0.zip";
        private const string DefaultExpectedSha256 = ""; // empty = skip integrity check (dev / first ship)

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.Doc == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            try
            {
                string url = ConfigOr("FAMILY_LIBRARY_URL", TagConfig.GetConfigValue("FAMILY_LIBRARY_URL"), DefaultCdnZipUrl);
                string sha = ConfigOr("FAMILY_LIBRARY_SHA256", TagConfig.GetConfigValue("FAMILY_LIBRARY_SHA256"), DefaultExpectedSha256);
                string localZip = ConfigOr("FAMILY_LIBRARY_LOCAL_ZIP", TagConfig.GetConfigValue("FAMILY_LIBRARY_LOCAL_ZIP"), "");

                string? bundleDir;
                if (!string.IsNullOrEmpty(localZip) && File.Exists(localZip))
                {
                    bundleDir = EnsureBundleFromLocalZip(localZip, sha);
                }
                else
                {
                    if (!string.IsNullOrEmpty(localZip))
                        StingLog.Warn($"FAMILY_LIBRARY_LOCAL_ZIP '{localZip}' does not exist; downloading {url} instead.");
                    bundleDir = EnsureBundleAsync(url, sha).GetAwaiter().GetResult();
                }
                if (string.IsNullOrEmpty(bundleDir))
                {
                    TaskDialog.Show("Family Library",
                        "Could not obtain the family library bundle (download failed, or the SHA-256 did not match). " +
                        "See the STING log.\n\n" +
                        "In project_config.json you can set FAMILY_LIBRARY_LOCAL_ZIP to a local zip path, " +
                        "FAMILY_LIBRARY_URL to another download URL, and FAMILY_LIBRARY_SHA256 to its expected hash.");
                    return Result.Failed;
                }

                var manifestPath = Path.Combine(bundleDir, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    TaskDialog.Show("Family Library", $"manifest.json missing inside the bundle at {bundleDir}.");
                    return Result.Failed;
                }

                var loaded = LoadIntoProject(ctx.Doc, bundleDir, manifestPath);
                TaskDialog.Show("Family Library",
                    $"Loaded {loaded.loaded} families ({loaded.skipped} already present, {loaded.failed} failed).\n\nFamily cache: {bundleDir}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("FamilyLibraryLoaderCommand failed", ex);
                TaskDialog.Show("Family Library", $"Failed: {ex.Message}");
                return Result.Failed;
            }
        }

        // ── Bundle resolver ────────────────────────────────────────────

        private static string ConfigOr(string key, string? value, string fallback)
        {
            string v = (value ?? "").Trim();
            if (v.Length == 0) return fallback;
            StingLog.Info($"Family library: {key} from project_config.json");
            return v;
        }

        private static string CacheRootFor(string zipNameOrUrl)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Planscape", "Families", InferVersionFromUrl(zipNameOrUrl));
        }

        /// <summary>
        /// FAMILY_LIBRARY_LOCAL_ZIP: verify (when a SHA-256 is given) and extract a local zip into
        /// the same per-version cache a download would use. No network.
        /// </summary>
        private static string? EnsureBundleFromLocalZip(string zipPath, string expectedSha)
        {
            var cacheRoot = CacheRootFor(zipPath);
            if (Directory.Exists(cacheRoot) && File.Exists(Path.Combine(cacheRoot, "manifest.json")))
            {
                StingLog.Info($"Family bundle cached: {cacheRoot}");
                return cacheRoot;
            }
            if (!string.IsNullOrEmpty(expectedSha))
            {
                var sha = ComputeSha256(zipPath);
                if (!string.Equals(sha, expectedSha, StringComparison.OrdinalIgnoreCase))
                {
                    StingLog.Warn($"Family bundle SHA mismatch for {zipPath}. expected={expectedSha} actual={sha}");
                    return null;
                }
            }
            Directory.CreateDirectory(cacheRoot);
            ZipFile.ExtractToDirectory(zipPath, cacheRoot, overwriteFiles: true);
            StingLog.Info($"Family bundle extracted from local zip {zipPath}: {cacheRoot}");
            return cacheRoot;
        }

        private static async Task<string?> EnsureBundleAsync(string url, string expectedSha)
        {
            var cacheRoot = CacheRootFor(url);

            if (Directory.Exists(cacheRoot) && File.Exists(Path.Combine(cacheRoot, "manifest.json")))
            {
                StingLog.Info($"Family bundle cached: {cacheRoot}");
                return cacheRoot;
            }

            var tmpZip = Path.Combine(Path.GetTempPath(), $"planscape-families-{Guid.NewGuid():N}.zip");
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        StingLog.Warn($"Family bundle GET {url} → {(int)resp.StatusCode}");
                        return null;
                    }
                    await using var fs = File.Create(tmpZip);
                    await resp.Content.CopyToAsync(fs);
                }

                if (!string.IsNullOrEmpty(expectedSha))
                {
                    var sha = ComputeSha256(tmpZip);
                    if (!string.Equals(sha, expectedSha, StringComparison.OrdinalIgnoreCase))
                    {
                        StingLog.Warn($"Family bundle SHA mismatch. expected={expectedSha} actual={sha}");
                        return null;
                    }
                }

                Directory.CreateDirectory(cacheRoot);
                ZipFile.ExtractToDirectory(tmpZip, cacheRoot, overwriteFiles: true);
                StingLog.Info($"Family bundle extracted: {cacheRoot}");
                return cacheRoot;
            }
            finally
            {
                try { if (File.Exists(tmpZip)) File.Delete(tmpZip); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            }
        }

        private static string InferVersionFromUrl(string url)
        {
            // Convention: .../PlanscapeStandard-vX.Y.Z.zip
            var name = Path.GetFileNameWithoutExtension(url);
            var idx = name.IndexOf('v', StringComparison.OrdinalIgnoreCase);
            return idx > 0 ? name.Substring(idx) : "v0.0.0";
        }

        private static string ComputeSha256(string path)
        {
            using var fs = File.OpenRead(path);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        }

        // ── Family loader ──────────────────────────────────────────────

        private static (int loaded, int skipped, int failed) LoadIntoProject(Document doc, string bundleDir, string manifestPath)
        {
            // S8.2.2 — span the load loop so a stalled .rfa surfaces with a
            // duration outlier rather than a silent freeze.
            return StingTools.Core.PluginTelemetry.Run(
                "FamilyLibraryLoader.loadIntoProject",
                () => LoadIntoProjectImpl(doc, bundleDir, manifestPath));
        }

        private static (int loaded, int skipped, int failed) LoadIntoProjectImpl(Document doc, string bundleDir, string manifestPath)
        {
            int loaded = 0, skipped = 0, failed = 0;
            using var jdoc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!jdoc.RootElement.TryGetProperty("categories", out var categories)) return (0, 0, 0);

            using var t = new Transaction(doc, "STING — Load family library");
            t.Start();
            foreach (var cat in categories.EnumerateArray())
            {
                if (!cat.TryGetProperty("items", out var items)) continue;
                foreach (var item in items.EnumerateArray())
                {
                    var rel  = item.TryGetProperty("file", out var f) ? f.GetString() : null;
                    var label = item.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(rel)) continue;

                    var rfa = Path.Combine(bundleDir, rel.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(rfa))
                    {
                        StingLog.Warn($"Family bundle: missing {rel} (manifest references it but the file isn't in the zip)");
                        failed++;
                        continue;
                    }

                    try
                    {
                        if (doc.LoadFamily(rfa, out _))
                            loaded++;
                        else
                            skipped++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Family load failed for {label} ({rel}): {ex.Message}");
                        failed++;
                    }
                }
            }
            t.Commit();
            return (loaded, skipped, failed);
        }
    }
}
