// StingTools — Content Library (runtime resolution)
//
// ContentResolver is the single FamilySymbol lookup the content engines share.
// It replaces the per-builder category scans (MepFixtureBuilder's private index,
// FixturePlacementEngine's PC-16 auto-load) with one 4-step chain:
//
//   1. In-project  — a FamilySymbol of the requested category already loaded
//                    (hint-matched on Family/Type name; else first-for-category).
//   2. Disk        — load the first matching .rfa found across ContentRoots
//                    (AllowLoad; caller must own a Transaction).
//   3. Build       — mint from a seed spec (AllowBuild; OFF in this slice — the
//                    guarded hook is left for the explicit Library_LoadAll path).
//   4. Not found   — record a MissingContent (never silent) and return null.
//
// CONTRACT: the caller MUST own an open Transaction when AllowLoad/AllowBuild is
// true (LoadFamily writes to the document). Step 1 is read-only and always safe.
// In-project selection reproduces the legacy "hint match first, else first-for-
// category" behaviour so wiring an existing builder through the resolver does not
// change which symbol it picks.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace StingTools.Core.Content
{
    public enum ContentSource { Project, Disk, Built, NotFound }

    public sealed class ContentRequest
    {
        public string Category;          // Revit category name (required)
        public string FamilyHint;        // regex on Family.Name (optional)
        public string TypeHint;          // regex on Symbol.Name (optional)
        public string TypeCatalogKey;    // Phase-185 selective type load (optional)
        public bool AllowLoad = true;    // step 2 — load from disk roots
        public bool AllowBuild = false;  // step 3 — build from seed spec (off in slice 1)
    }

    public sealed class ContentResolution
    {
        public FamilySymbol Symbol;      // null ⇒ NotFound
        public ContentSource Source;
        public string ResolvedFrom;      // "project" | <path> | "built:<spec>"
        public bool IsFallback;          // matched first-for-category, not the hint
        public string Detail;

        public static ContentResolution Miss(string detail)
            => new ContentResolution { Source = ContentSource.NotFound, Detail = detail };
    }

    /// <summary>One unmatched request — the shared vocabulary that replaces the
    /// divergent SkippedNoSymbol / MissingFamilies / Unmatched counters.</summary>
    public sealed class MissingContent
    {
        public string Category;
        public string FamilyHint;
        public string TypeHint;
        public string Reason;
    }

    public sealed class ContentResolver
    {
        private readonly Document _doc;
        private readonly ContentManifest _manifest;
        private readonly IReadOnlyList<string> _roots;
        private readonly Dictionary<string, FamilySymbol> _cache =
            new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
        private readonly List<MissingContent> _missing = new List<MissingContent>();
        private Dictionary<string, List<FamilySymbol>> _byCategory;

        public IReadOnlyList<MissingContent> Missing => _missing;
        public IReadOnlyList<string> Roots => _roots;

        public ContentResolver(Document doc, ContentManifest manifest)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _manifest = manifest;
            _roots = ContentRoots.Resolve(doc, manifest?.RootPrecedence ?? "projectFirst");
        }

        public ContentResolution Resolve(Document doc, ContentRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Category))
                return ContentResolution.Miss("no category");

            string key = $"{req.Category}|{req.FamilyHint}|{req.TypeHint}";
            if (_cache.TryGetValue(key, out var cached))
                return new ContentResolution
                {
                    Symbol = cached,
                    Source = cached != null ? ContentSource.Project : ContentSource.NotFound
                };

            // 1) In-project.
            var inproj = ResolveInProject(req, out bool fallback);
            if (inproj != null)
            {
                _cache[key] = inproj;
                return new ContentResolution
                {
                    Symbol = inproj, Source = ContentSource.Project,
                    ResolvedFrom = "project", IsFallback = fallback
                };
            }

            // 2) Disk load (needs a transaction).
            if (req.AllowLoad && IsModifiable())
            {
                var loaded = TryLoadFromRoots(req, out string from);
                if (loaded != null)
                {
                    _cache[key] = loaded;
                    return new ContentResolution { Symbol = loaded, Source = ContentSource.Disk, ResolvedFrom = from };
                }
            }

            // 3) Build from spec — guarded hook, intentionally not wired in slice 1.
            //    (Reserved for the explicit Library_LoadAll path.)
            // if (req.AllowBuild && IsModifiable()) { /* SymbolLibraryCreator.BuildOne → load */ }

            // 4) Not found — record, never silent.
            _missing.Add(new MissingContent
            {
                Category = req.Category, FamilyHint = req.FamilyHint,
                TypeHint = req.TypeHint, Reason = "no in-project or library family"
            });
            _cache[key] = null;
            return ContentResolution.Miss($"no family for category '{req.Category}'");
        }

        // ── Step 1: in-project ──────────────────────────────────────────
        private FamilySymbol ResolveInProject(ContentRequest req, out bool fallback)
        {
            fallback = false;
            EnsureIndex();
            Regex famRx = SafeRx(req.FamilyHint), typeRx = SafeRx(req.TypeHint);
            FamilySymbol picked = null, first = null;
            if (_byCategory.TryGetValue(req.Category, out var list))
                foreach (var fs in list)   // collector order preserved → identical selection
                {
                    if (first == null) first = fs;
                    if (famRx != null && !famRx.IsMatch(fs.Family?.Name ?? "")) continue;
                    if (typeRx != null && !typeRx.IsMatch(fs.Name ?? "")) continue;
                    picked = fs; break;
                }
            var sym = picked ?? first;
            fallback = sym != null && picked == null;
            return sym;
        }

        private void EnsureIndex()
        {
            if (_byCategory != null) return;
            _byCategory = new Dictionary<string, List<FamilySymbol>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var fs in new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>())
                {
                    var c = fs.Category?.Name;
                    if (string.IsNullOrEmpty(c)) continue;
                    if (!_byCategory.TryGetValue(c, out var l)) _byCategory[c] = l = new List<FamilySymbol>();
                    l.Add(fs);
                }
            }
            catch (Exception ex) { StingLog.Warn($"ContentResolver index: {ex.Message}"); }
        }

        // ── Step 2: disk load across roots ──────────────────────────────
        private FamilySymbol TryLoadFromRoots(ContentRequest req, out string from)
        {
            from = null;
            string token = req.Category.Replace(" ", "").Replace("/", "").Replace("\\", "");
            foreach (var root in _roots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                List<string> rfas;
                try
                {
                    rfas = Directory.EnumerateFiles(root, "*.rfa", SearchOption.AllDirectories)
                        .Where(p =>
                        {
                            var n = Path.GetFileNameWithoutExtension(p) ?? "";
                            return n.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0
                                || n.IndexOf(req.Category, StringComparison.OrdinalIgnoreCase) >= 0;
                        })
                        .ToList();
                }
                catch { continue; }

                foreach (var path in rfas)
                {
                    try
                    {
                        if (_doc.LoadFamily(path, out var fam) && fam != null)
                        {
                            var sym = FirstSymbolOfCategory(fam, req.Category) ?? FirstSymbol(fam);
                            if (sym != null) { AddToIndex(sym); from = path; return sym; }
                        }
                        else
                        {
                            // Already loaded — resolve its symbol by family name.
                            var existing = FindLoadedSymbol(path, req.Category);
                            if (existing != null) { from = path + " (already loaded)"; return existing; }
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"ContentResolver load '{path}': {ex.Message}"); }
                }
            }
            return null;
        }

        private FamilySymbol FirstSymbolOfCategory(Family fam, string cat)
        {
            foreach (var id in fam.GetFamilySymbolIds())
                if (_doc.GetElement(id) is FamilySymbol fs &&
                    string.Equals(fs.Category?.Name, cat, StringComparison.OrdinalIgnoreCase))
                    return fs;
            return null;
        }

        private FamilySymbol FirstSymbol(Family fam)
        {
            foreach (var id in fam.GetFamilySymbolIds())
                if (_doc.GetElement(id) is FamilySymbol fs) return fs;
            return null;
        }

        private FamilySymbol FindLoadedSymbol(string path, string cat)
        {
            var fn = Path.GetFileNameWithoutExtension(path);
            foreach (var fs in new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>())
                if (string.Equals(fs.FamilyName, fn, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrEmpty(cat)
                        || string.Equals(fs.Category?.Name, cat, StringComparison.OrdinalIgnoreCase)))
                    return fs;
            return null;
        }

        private void AddToIndex(FamilySymbol fs)
        {
            if (_byCategory == null) EnsureIndex();
            var c = fs.Category?.Name;
            if (string.IsNullOrEmpty(c)) return;
            if (!_byCategory.TryGetValue(c, out var l)) _byCategory[c] = l = new List<FamilySymbol>();
            l.Add(fs);
        }

        private bool IsModifiable()
        {
            try { return _doc.IsModifiable; } catch { return false; }
        }

        private static Regex SafeRx(string p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            try { return new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
            catch { return null; }
        }
    }
}
