using StingTools.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StingTools.Photometrics
{
    /// <summary>
    /// Directory-scoped photometric file library. Lazy-loads + caches every
    /// .ies / .ldt / .gldf file under one or more configured root paths so
    /// the AssignPhotometricCommand can pick a fixture's data without
    /// re-parsing on every Revit click.
    ///
    /// Threading: cache is concurrent; safe to read from any thread but
    /// all Revit transactions must happen on the API thread (commands
    /// already enforce this).
    /// </summary>
    public class PhotometricLibrary
    {
        private static readonly ConcurrentDictionary<string, PhotometricFile> _cache =
            new ConcurrentDictionary<string, PhotometricFile>(StringComparer.OrdinalIgnoreCase);

        private readonly List<string> _rootPaths = new List<string>();

        public PhotometricLibrary(IEnumerable<string> roots)
        {
            if (roots == null) return;
            foreach (var r in roots) AddRoot(r);
        }

        public IReadOnlyList<string> RootPaths => _rootPaths.AsReadOnly();

        public void AddRoot(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (!Directory.Exists(path)) return;
                string norm = Path.GetFullPath(path);
                if (!_rootPaths.Any(r => string.Equals(r, norm, StringComparison.OrdinalIgnoreCase)))
                    _rootPaths.Add(norm);
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"PhotometricLibrary.AddRoot: {ex.Message}"); }
        }

        public IEnumerable<string> Enumerate()
        {
            foreach (var root in _rootPaths)
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                        .Where(IsPhotometric);
                }
                catch (Exception ex)
                {
                    StingTools.Core.StingLog.Warn($"Enumerate({root}): {ex.Message}");
                    continue;
                }
                foreach (var f in files) yield return f;
            }
        }

        /// <summary>
        /// Return cached metadata for a file path (parses on first request,
        /// re-parses only when the file's last-write timestamp changes).
        /// </summary>
        public PhotometricFile Read(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists) return null;
                string key = $"{fi.FullName}|{fi.LastWriteTimeUtc.Ticks}";
                if (_cache.TryGetValue(key, out var cached)) return cached;
                var parsed = ParseByExtension(fi);
                _cache[key] = parsed;
                return parsed;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"PhotometricLibrary.Read({path}): {ex.Message}");
                return null;
            }
        }

        /// <summary>Parse every file under every root and return the populated DTOs.</summary>
        public List<PhotometricFile> LoadAll()
        {
            var all = new List<PhotometricFile>();
            foreach (var path in Enumerate())
            {
                var f = Read(path);
                if (f != null) all.Add(f);
            }
            return all;
        }

        public static void InvalidateCache() => _cache.Clear();

        // ── helpers ─────────────────────────────────────────────────────

        private static bool IsPhotometric(string path)
        {
            string ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext == ".ies" || ext == ".ldt" || ext == ".eul" || ext == ".gldf";
        }

        private static PhotometricFile ParseByExtension(FileInfo fi)
        {
            string ext = fi.Extension?.ToLowerInvariant();
            switch (ext)
            {
                case ".ies":  return IesParser.ParseFile(fi.FullName);
                case ".ldt":
                case ".eul":  return LdtParser.ParseFile(fi.FullName);
                case ".gldf":
                    // Phase 180 ships IES + LDT only — GLDF is reserved for
                    // a later phase to avoid the GLDF.Net NuGet transitive-dep risk.
                    var stub = new PhotometricFile
                    {
                        FileFormat = "GLDF",
                        FilePath = fi.FullName,
                        LuminaireName = Path.GetFileNameWithoutExtension(fi.Name)
                    };
                    stub.Warnings.Add("GLDF parser not yet implemented (Phase 182). File metadata only.");
                    return stub;
                default:
                    var unknown = new PhotometricFile { FilePath = fi.FullName };
                    unknown.Warnings.Add($"Unknown extension: {ext}");
                    return unknown;
            }
        }
    }
}
