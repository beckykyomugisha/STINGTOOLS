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
        // Configurable so a hotfix bundle can ship without a plugin redeploy.
        private const string DefaultCdnZipUrl = "https://cdn.planscape.app/families/PlanscapeStandard-v1.0.0.zip";
        private const string DefaultExpectedSha256 = ""; // empty = skip integrity check (dev / first ship)

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.Doc == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            try
            {
                var bundleDir = EnsureBundleAsync(DefaultCdnZipUrl, DefaultExpectedSha256).GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(bundleDir))
                {
                    TaskDialog.Show("Family Library", "Bundle download failed. Check log + network. You can paste a local zip path in project_config.json under family_library.local_zip.");
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

        private static async Task<string?> EnsureBundleAsync(string url, string expectedSha)
        {
            var version = InferVersionFromUrl(url);
            var cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Planscape", "Families", version);

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
