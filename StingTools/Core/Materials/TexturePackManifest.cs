using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace StingTools.Core.Materials
{
    /// <summary>
    /// On-disk descriptor for a single PBR texture pack. One pack = one folder
    /// containing 1..N map files (base color, normal, roughness, etc.) + a
    /// sidecar manifest.json. Written by <see cref="TexturePackIngester"/>
    /// the first time a pack is discovered; read on subsequent applies.
    /// </summary>
    public sealed class TexturePackManifest
    {
        public string SchemaVersion { get; set; } = "1.0";
        public string PackId { get; set; }            // folder name, sanitised
        public string DisplayName { get; set; }
        public string ProviderId { get; set; }        // polyhaven | ambientcg | architextures | user-folder
        public string SourceUrl { get; set; }         // origin URL (optional)
        public string License { get; set; }           // CC0, royalty-free, …
        public string Category { get; set; }          // free-text
        public int ResolutionPx { get; set; }         // longest side, e.g. 2048
        public string FormatHint { get; set; }        // png|jpg|exr
        public DateTime IngestedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Absolute paths to each map. Missing slots are null/empty.</summary>
        public TexturePackMaps Maps { get; set; } = new TexturePackMaps();

        /// <summary>Suggested UV + render defaults. Applied on first apply;
        /// per-material overrides live on the Revit material itself, not here.</summary>
        public TexturePackDefaults Defaults { get; set; } = new TexturePackDefaults();

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);

        public static TexturePackManifest FromJson(string json)
        {
            try { return JsonConvert.DeserializeObject<TexturePackManifest>(json); }
            catch { return null; }
        }
    }

    public sealed class TexturePackMaps
    {
        public string BaseColor { get; set; }
        public string Normal { get; set; }
        public string Roughness { get; set; }
        public string Metalness { get; set; }
        public string Ao { get; set; }
        public string Bump { get; set; }
        public string Displacement { get; set; }
        public string Opacity { get; set; }
        public string Emission { get; set; }
        public string Anisotropy { get; set; }

        public IEnumerable<(string Slot, string Path)> Enumerate()
        {
            yield return ("baseColor", BaseColor);
            yield return ("normal", Normal);
            yield return ("roughness", Roughness);
            yield return ("metalness", Metalness);
            yield return ("ao", Ao);
            yield return ("bump", Bump);
            yield return ("displacement", Displacement);
            yield return ("opacity", Opacity);
            yield return ("emission", Emission);
            yield return ("anisotropy", Anisotropy);
        }

        public int FilledSlotCount => Enumerate().Count(t => !string.IsNullOrEmpty(t.Path));
    }

    public sealed class TexturePackDefaults
    {
        public double RealWorldScaleXMm { get; set; } = 1000.0;
        public double RealWorldScaleYMm { get; set; } = 1000.0;
        public double UvOffsetX { get; set; } = 0.0;
        public double UvOffsetY { get; set; } = 0.0;
        public double UvRotationDeg { get; set; } = 0.0;
        public double BumpAmount { get; set; } = 1.0;
        public double NormalIntensity { get; set; } = 1.0;
        public bool DisplacementEnabled { get; set; } = false;
        public double DisplacementMinMm { get; set; } = -2.0;
        public double DisplacementMaxMm { get; set; } = 2.0;
        public double DisplacementScale { get; set; } = 1.0;
        public double EmissionLuminanceCdM2 { get; set; } = 0.0;
    }

    /// <summary>
    /// Suffix-detection ingester. Given a folder containing PBR map files
    /// named with the usual conventions ("_basecolor", "_normal", "_disp"
    /// etc.), produces a <see cref="TexturePackManifest"/> describing which
    /// file goes in which slot. Suffix rules are loaded from
    /// STING_TEXTURE_PROVIDERS.json so projects can extend them without code.
    /// </summary>
    public static class TexturePackIngester
    {
        private const string ManifestFileName = "manifest.json";

        private static readonly string[] ImageExtensions =
            { ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".exr", ".hdr", ".tga", ".bmp" };

        /// <summary>Load existing manifest if present, otherwise scan the folder,
        /// auto-detect maps via suffix rules, write a new manifest.json, return it.</summary>
        public static TexturePackManifest LoadOrIngest(string packFolder, string providerId = null, string sourceUrl = null, string license = null, IDictionary<string, IList<string>> suffixRules = null)
        {
            if (string.IsNullOrEmpty(packFolder) || !Directory.Exists(packFolder)) return null;

            string mfp = Path.Combine(packFolder, ManifestFileName);
            if (File.Exists(mfp))
            {
                try
                {
                    var m = TexturePackManifest.FromJson(File.ReadAllText(mfp));
                    if (m != null)
                    {
                        ReanchorPaths(m, packFolder);
                        return m;
                    }
                }
                catch { /* fall through to rebuild */ }
            }

            var manifest = Scan(packFolder, providerId, sourceUrl, license, suffixRules);
            if (manifest != null) WriteManifestAtomic(mfp, manifest);
            return manifest;
        }

        /// <summary>Force a fresh scan, overwriting any existing manifest.</summary>
        public static TexturePackManifest ReIngest(string packFolder, string providerId = null, string sourceUrl = null, string license = null, IDictionary<string, IList<string>> suffixRules = null)
        {
            if (string.IsNullOrEmpty(packFolder) || !Directory.Exists(packFolder)) return null;
            var manifest = Scan(packFolder, providerId, sourceUrl, license, suffixRules);
            if (manifest != null) WriteManifestAtomic(Path.Combine(packFolder, ManifestFileName), manifest);
            return manifest;
        }

        // Per-folder lock so two threads writing to the same pack folder
        // serialise. Keyed by full path so independent packs don't block
        // each other.
        private static readonly Dictionary<string, object> _folderLocks = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        private static object GetFolderLock(string path)
        {
            string key = (path ?? "").TrimEnd('\\', '/').ToLowerInvariant();
            lock (_folderLocks)
            {
                if (!_folderLocks.TryGetValue(key, out var l))
                    _folderLocks[key] = l = new object();
                return l;
            }
        }

        /// <summary>Atomic write: serialize to a temp file alongside the
        /// destination, then <see cref="File.Replace(string,string,string)"/>
        /// (or move-overwrite on platforms without Replace) so a concurrent
        /// reader never sees a half-written manifest.</summary>
        private static void WriteManifestAtomic(string destPath, TexturePackManifest manifest)
        {
            if (string.IsNullOrEmpty(destPath) || manifest == null) return;
            string dir = Path.GetDirectoryName(destPath);
            lock (GetFolderLock(dir))
            {
                string tmp = destPath + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                try
                {
                    File.WriteAllText(tmp, manifest.ToJson());
                    if (File.Exists(destPath))
                    {
                        // Replace is atomic on NTFS; backup-path null means no backup.
                        try { File.Replace(tmp, destPath, null, ignoreMetadataErrors: true); return; }
                        catch { /* falls through to move-overwrite */ }
                    }
                    File.Move(tmp, destPath);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"WriteManifestAtomic '{destPath}': {ex.Message}");
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* non-fatal */ }
                }
            }
        }

        private static TexturePackManifest Scan(string packFolder, string providerId, string sourceUrl, string license, IDictionary<string, IList<string>> suffixRules)
        {
            var rules = suffixRules ?? BuiltInSuffixRules();
            var manifest = new TexturePackManifest
            {
                PackId = SanitiseId(Path.GetFileName(packFolder)),
                DisplayName = Path.GetFileName(packFolder),
                ProviderId = providerId ?? "user-folder",
                SourceUrl = sourceUrl,
                License = license,
            };

            var images = Directory.GetFiles(packFolder, "*", SearchOption.TopDirectoryOnly)
                .Where(p => ImageExtensions.Contains(Path.GetExtension(p).ToLowerInvariant()))
                .ToList();

            int maxRes = 0;
            string fmtHint = null;
            foreach (var img in images)
            {
                string stem = Path.GetFileNameWithoutExtension(img).ToLowerInvariant();
                string slot = MatchSlot(stem, rules);
                if (string.IsNullOrEmpty(slot)) continue;
                AssignIfEmpty(manifest.Maps, slot, img);
                if (fmtHint == null) fmtHint = Path.GetExtension(img).TrimStart('.');

                if (maxRes == 0)
                {
                    foreach (var token in stem.Split('_', '-'))
                    {
                        if (token.EndsWith("k", StringComparison.OrdinalIgnoreCase) &&
                            int.TryParse(token.TrimEnd('k', 'K'), out int kVal))
                        {
                            maxRes = kVal * 1024;
                            break;
                        }
                    }
                }
            }

            manifest.ResolutionPx = maxRes;
            manifest.FormatHint = fmtHint;

            return manifest.Maps.FilledSlotCount == 0 ? null : manifest;
        }

        /// <summary>Match a file stem to a PBR slot by suffix. Uses
        /// <c>EndsWith</c> ONLY (no <c>Contains</c>) so file names that
        /// happen to embed a suffix word as a substring (e.g.
        /// <c>bumper_diffuse</c> embedding <c>_bump</c>) don't get
        /// misrouted. Suffix candidates are sorted by length descending so
        /// the most specific match wins (<c>_normalgl</c> beats
        /// <c>_n</c>).</summary>
        private static string MatchSlot(string stem, IDictionary<string, IList<string>> rules)
        {
            // Flatten + sort: (slotKey, suffix) tuples ordered by suffix
            // length desc → "_displacement" tried before "_disp"; "_normalgl"
            // before "_n"; etc. Sorting once per call is cheap (≤ 100 rules).
            var candidates = new List<(string slot, string suffix)>(64);
            foreach (var kv in rules)
                foreach (var suf in kv.Value)
                    if (!string.IsNullOrEmpty(suf))
                        candidates.Add((kv.Key, suf));
            candidates.Sort((a, b) => b.suffix.Length.CompareTo(a.suffix.Length));

            foreach (var (slot, suf) in candidates)
                if (stem.EndsWith(suf, StringComparison.OrdinalIgnoreCase))
                    return slot;
            return null;
        }

        private static void AssignIfEmpty(TexturePackMaps maps, string slot, string path)
        {
            switch (slot)
            {
                case "baseColor":    if (string.IsNullOrEmpty(maps.BaseColor))    maps.BaseColor = path; break;
                case "normal":       if (string.IsNullOrEmpty(maps.Normal))       maps.Normal = path; break;
                case "roughness":    if (string.IsNullOrEmpty(maps.Roughness))    maps.Roughness = path; break;
                case "metalness":    if (string.IsNullOrEmpty(maps.Metalness))    maps.Metalness = path; break;
                case "ao":           if (string.IsNullOrEmpty(maps.Ao))           maps.Ao = path; break;
                case "bump":         if (string.IsNullOrEmpty(maps.Bump))         maps.Bump = path; break;
                case "displacement": if (string.IsNullOrEmpty(maps.Displacement)) maps.Displacement = path; break;
                case "opacity":      if (string.IsNullOrEmpty(maps.Opacity))      maps.Opacity = path; break;
                case "emission":     if (string.IsNullOrEmpty(maps.Emission))     maps.Emission = path; break;
                case "anisotropy":   if (string.IsNullOrEmpty(maps.Anisotropy))   maps.Anisotropy = path; break;
            }
        }

        /// <summary>Rewrite map paths to be absolute against the current folder
        /// — manifests committed at a different mount point still resolve.</summary>
        private static void ReanchorPaths(TexturePackManifest m, string packFolder)
        {
            if (m?.Maps == null) return;
            m.Maps.BaseColor    = Reanchor(m.Maps.BaseColor,    packFolder);
            m.Maps.Normal       = Reanchor(m.Maps.Normal,       packFolder);
            m.Maps.Roughness    = Reanchor(m.Maps.Roughness,    packFolder);
            m.Maps.Metalness    = Reanchor(m.Maps.Metalness,    packFolder);
            m.Maps.Ao           = Reanchor(m.Maps.Ao,           packFolder);
            m.Maps.Bump         = Reanchor(m.Maps.Bump,         packFolder);
            m.Maps.Displacement = Reanchor(m.Maps.Displacement, packFolder);
            m.Maps.Opacity      = Reanchor(m.Maps.Opacity,      packFolder);
            m.Maps.Emission     = Reanchor(m.Maps.Emission,     packFolder);
            m.Maps.Anisotropy   = Reanchor(m.Maps.Anisotropy,   packFolder);
        }

        private static string Reanchor(string p, string folder)
        {
            if (string.IsNullOrEmpty(p)) return p;
            if (File.Exists(p)) return p;
            string local = Path.Combine(folder, Path.GetFileName(p));
            return File.Exists(local) ? local : p;
        }

        private static string SanitiseId(string s)
        {
            if (string.IsNullOrEmpty(s)) return "pack";
            var chars = s.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray();
            return chars.Length == 0 ? "pack" : new string(chars);
        }

        /// <summary>Fallback suffix rules when the providers JSON hasn't been loaded.
        /// Mirrors the corporate STING_TEXTURE_PROVIDERS.json defaults.</summary>
        public static IDictionary<string, IList<string>> BuiltInSuffixRules() => new Dictionary<string, IList<string>>
        {
            ["baseColor"]    = new List<string> { "_basecolor", "_albedo", "_diffuse", "_diff", "_col", "_color", "_basemap", "_base" },
            ["normal"]       = new List<string> { "_normalgl", "_normaldx", "_normal", "_norm", "_nrm", "_nor", "_n" },
            ["roughness"]    = new List<string> { "_roughness", "_rough", "_rgh", "_r" },
            ["metalness"]    = new List<string> { "_metalness", "_metallic", "_metal", "_met", "_m" },
            ["ao"]           = new List<string> { "_ambientocclusion", "_occlusion", "_ao", "_occ" },
            ["bump"]         = new List<string> { "_bump", "_bmp", "_height", "_h" },
            ["displacement"] = new List<string> { "_displacement", "_displace", "_disp", "_dsp" },
            ["opacity"]      = new List<string> { "_transparency", "_opacity", "_alpha", "_cutout", "_mask" },
            ["emission"]     = new List<string> { "_emission", "_emissive", "_emit", "_glow", "_self_illum" },
            ["anisotropy"]   = new List<string> { "_anisotropy", "_aniso", "_anis" },
        };
    }
}
