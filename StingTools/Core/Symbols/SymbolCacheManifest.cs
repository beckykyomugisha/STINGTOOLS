// StingTools — Symbol library cache manifest (W-1).
//
// Problem this solves
// ───────────────────
// SymbolLibraryCreator used to treat File.Exists as the entire staleness
// test for a built .rfa. Existence is not freshness: once a family had been
// generated it was never rebuilt, so a corrected catalogue — or a corrected
// GENERATOR — could not reach any project that had already run the build.
// Symbol fixes were unshippable by construction.
//
// The sidecar
// ───────────
// A .sting_library.json written beside the built families records, per
// catalogue, the SHA-256 of the JSON it was built from, plus the generator
// version and content-library version in force at build time:
//
//   {
//     "generatorVersion": "2",
//     "libraryVersion":   "2026.6.1",
//     "builtUtc":         "2026-07-29T18:00:00Z",
//     "catalogues": { "STING_SLD_SYMBOLS.json": "<sha256>" }
//   }
//
// Two triggers, not one
// ─────────────────────
// Hashing the catalogue alone is NOT sufficient, and the distinction matters:
//
//   * catalogue hash  — catches data edits (a changed symbolSize, new glyph)
//   * generatorVersion — catches CODE changes that alter output while every
//                        catalogue byte stays identical
//
// The second case is not hypothetical. The fix that motivated this file
// repaired 206 filled regions and 115 arc sweeps purely by correcting JSON
// key binding in the POCO — the catalogues those symbols live in were never
// touched, so a hash-only check would have declared them fresh and shipped
// the repair to nobody. Bump GeneratorVersion whenever emitted geometry
// changes.
//
// Failure posture: any read/parse problem yields a manifest that reports
// everything stale. Rebuilding unnecessarily costs time; skipping a needed
// rebuild silently ships broken geometry, which is the bug this exists to
// prevent.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace StingTools.Core.Symbols
{
    public sealed class SymbolCacheManifest
    {
        /// <summary>File name of the sidecar, written into the build output folder.</summary>
        public const string FileName = ".sting_library.json";

        /// <summary>
        /// Bumped whenever SymbolLibraryCreator's emitted geometry changes in a way
        /// that is invisible to the catalogue hashes.
        ///
        /// <para>History:
        /// 1 — pre-invalidation baseline (no sidecar was written).
        /// 2 — filled-region "vertices" and arc "startAngle"/"endAngle" key aliases;
        ///     model-category families sized from realSizeMm instead of symbolSize.</para>
        /// </summary>
        public const string GeneratorVersion = "2";

        [JsonProperty("generatorVersion")] public string BuiltGeneratorVersion { get; set; } = "";
        [JsonProperty("libraryVersion")]   public string LibraryVersion { get; set; } = "";
        [JsonProperty("builtUtc")]         public string BuiltUtc { get; set; } = "";

        /// <summary>Catalogue file name → SHA-256 of the bytes it was built from.</summary>
        [JsonProperty("catalogues")]
        public Dictionary<string, string> Catalogues { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // ── Load / save ──────────────────────────────────────────────────

        /// <summary>
        /// Reads the sidecar from <paramref name="outputFolder"/>. Returns an empty
        /// manifest — which marks everything stale — when absent or unreadable.
        /// </summary>
        public static SymbolCacheManifest Load(string outputFolder)
        {
            try
            {
                if (string.IsNullOrEmpty(outputFolder)) return new SymbolCacheManifest();
                var path = Path.Combine(outputFolder, FileName);
                if (!File.Exists(path)) return new SymbolCacheManifest();
                return JsonConvert.DeserializeObject<SymbolCacheManifest>(File.ReadAllText(path))
                       ?? new SymbolCacheManifest();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SymbolCacheManifest.Load: {ex.Message} — treating cache as stale.");
                return new SymbolCacheManifest();
            }
        }

        /// <summary>
        /// Persists the sidecar. Never throws: a library that built correctly but
        /// could not record the fact is merely rebuilt next time.
        /// </summary>
        public void Save(string outputFolder)
        {
            try
            {
                if (string.IsNullOrEmpty(outputFolder)) return;
                Directory.CreateDirectory(outputFolder);
                BuiltGeneratorVersion = GeneratorVersion;
                BuiltUtc = DateTime.UtcNow.ToString("o");
                File.WriteAllText(Path.Combine(outputFolder, FileName),
                    JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SymbolCacheManifest.Save: {ex.Message} — " +
                              "next build will rebuild this catalogue.");
            }
        }

        // ── Staleness ────────────────────────────────────────────────────

        /// <summary>
        /// True when families built from <paramref name="cataloguePath"/> must be
        /// regenerated: the generator changed, or the catalogue's bytes changed, or
        /// nothing was ever recorded for it.
        /// </summary>
        public bool IsCatalogueStale(string cataloguePath, out string reason)
        {
            reason = null;
            if (!string.Equals(BuiltGeneratorVersion, GeneratorVersion, StringComparison.Ordinal))
            {
                reason = string.IsNullOrEmpty(BuiltGeneratorVersion)
                    ? "no cache manifest — built before cache invalidation existed"
                    : $"generator {BuiltGeneratorVersion} → {GeneratorVersion}";
                return true;
            }

            string key = SafeFileName(cataloguePath);
            if (string.IsNullOrEmpty(key)) { reason = "catalogue path unresolved"; return true; }

            if (!Catalogues.TryGetValue(key, out var recorded) || string.IsNullOrEmpty(recorded))
            {
                reason = "catalogue not recorded in cache manifest";
                return true;
            }

            string current = HashFile(cataloguePath);
            if (string.IsNullOrEmpty(current)) { reason = "catalogue unreadable"; return true; }

            if (!string.Equals(recorded, current, StringComparison.OrdinalIgnoreCase))
            {
                reason = "catalogue content changed";
                return true;
            }
            return false;
        }

        /// <summary>Records the current hash of <paramref name="cataloguePath"/> as freshly built.</summary>
        public void RecordCatalogue(string cataloguePath)
        {
            string key = SafeFileName(cataloguePath);
            if (string.IsNullOrEmpty(key)) return;
            string hash = HashFile(cataloguePath);
            if (!string.IsNullOrEmpty(hash)) Catalogues[key] = hash;
        }

        // ── Helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// SHA-256 of a file's bytes, or null on any failure. Matches the hashing
        /// convention already used by DrawingTypeRegistry.ComputeChecksums rather
        /// than introducing a second one.
        /// </summary>
        public static string HashFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                using (var sha = SHA256.Create())
                using (var fs = File.OpenRead(path))
                {
                    var sb = new StringBuilder();
                    foreach (var b in sha.ComputeHash(fs)) sb.Append(b.ToString("x2"));
                    return sb.ToString();
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SymbolCacheManifest.HashFile('{path}'): {ex.Message}");
                return null;
            }
        }

        private static string SafeFileName(string path)
        {
            try { return string.IsNullOrEmpty(path) ? null : Path.GetFileName(path); }
            catch { return null; }
        }
    }
}
