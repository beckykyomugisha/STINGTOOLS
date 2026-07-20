using StingTools.Core;
// StingTools — Drawing Template Manager · Week 2
//
// ViewStylePackRegistry — mirrors DrawingTypeRegistry for style
// packs. Loads Data/STING_VIEW_STYLE_PACKS.json, layers the project
// override at <project>/_BIM_COORD/view_style_packs.json, and
// resolves the Extends chain at read-time so consumers never have
// to walk it themselves. Caches per-document like the Drawing Type
// registry does.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace StingTools.Core.Drawing
{
    public static class ViewStylePackRegistry
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<string, ViewStylePackLibrary> _cache
            = new Dictionary<string, ViewStylePackLibrary>(StringComparer.OrdinalIgnoreCase);
        // Per-document memo of the extends-folded pack, mirroring
        // DrawingTypeRegistry._resolvedCache. Get() previously re-walked the
        // whole Extends chain on every call — DrawingDriftDetector.Scan
        // resolves each view's pack up to 3× (managed / vg-filter / token),
        // so an N-view scan paid 3N chain-folds. Cleared in Reload().
        private static readonly Dictionary<string, Dictionary<string, ViewStylePack>> _resolvedCache
            = new Dictionary<string, Dictionary<string, ViewStylePack>>(StringComparer.OrdinalIgnoreCase);

        public static ViewStylePack Get(Document doc, string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            var lib = GetLibrary(doc);
            string docKey = DocKey(doc);
            lock (_lock)
            {
                if (_resolvedCache.TryGetValue(docKey, out var memoMap)
                    && memoMap.TryGetValue(id, out var memo))
                    return memo;
            }
            var raw = lib.Packs.FirstOrDefault(
                p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            var resolved = raw == null ? null : ResolveExtends(lib, raw);
            lock (_lock)
            {
                if (!_resolvedCache.TryGetValue(docKey, out var memoMap))
                    _resolvedCache[docKey] = memoMap = new Dictionary<string, ViewStylePack>(StringComparer.OrdinalIgnoreCase);
                memoMap[id] = resolved;
            }
            return resolved;
        }

        public static IReadOnlyList<ViewStylePack> ListAll(Document doc)
            => GetLibrary(doc).Packs;

        public static void Reload(Document doc)
        {
            lock (_lock)
            {
                var key = DocKey(doc);
                if (_cache.ContainsKey(key)) _cache.Remove(key);
                if (_resolvedCache.ContainsKey(key)) _resolvedCache.Remove(key);
            }
            // Phase 183 — snapshot + diff so Inspect / SyncStyles can
            // surface pack edits to the user. See LiveProfileSync.
            try { LiveProfileSync.OnRegistryReloaded(doc); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"ViewStylePackRegistry.Reload sync: {ex.Message}"); }
        }

        public static ViewStylePackLibrary GetLibrary(Document doc)
        {
            var key = DocKey(doc);
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var cached)) return cached;
                var corporate = LoadCorporate();
                var project   = LoadProjectOverride(doc);
                var merged    = Merge(corporate, project);
                _cache[key] = merged;
                return merged;
            }
        }

        private static ViewStylePackLibrary LoadCorporate()
        {
            try
            {
                var path = StingTools.Core.StingToolsApp.FindDataFile("STING_VIEW_STYLE_PACKS.json");
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var lib = JsonConvert.DeserializeObject<ViewStylePackLibrary>(File.ReadAllText(path));
                    if (lib?.Packs != null && lib.Packs.Count > 0)
                    {
                        foreach (var p in lib.Packs)
                        {
                            if (string.IsNullOrEmpty(p.Origin)) p.Origin = "corporate";
                            PromoteAppearance(p);
                        }
                        return lib;
                    }
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"ViewStylePackRegistry: corporate load failed — {ex.Message}");
            }
            return BuildDefaults();
        }

        private static ViewStylePackLibrary LoadProjectOverride(Document doc)
        {
            if (doc == null || string.IsNullOrEmpty(doc.PathName)) return null;
            try
            {
                var dir = Path.Combine(Path.GetDirectoryName(doc.PathName), "_BIM_COORD");
                var path = Path.Combine(dir, "view_style_packs.json");
                if (!File.Exists(path)) return null;
                var lib = JsonConvert.DeserializeObject<ViewStylePackLibrary>(File.ReadAllText(path));
                if (lib?.Packs != null)
                    foreach (var p in lib.Packs)
                    {
                        if (string.IsNullOrEmpty(p.Origin)) p.Origin = "project";
                        PromoteAppearance(p);
                    }
                return lib;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"ViewStylePackRegistry: project override load failed — {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Flatten the pack's nested "appearance" object onto the pack's
        /// flat fields. Flat fields already set on the pack win — the
        /// appearance block only fills gaps.
        /// </summary>
        private static void PromoteAppearance(ViewStylePack p)
        {
            if (p?.Appearance == null) return;
            var a = p.Appearance;
            if (a.LineWeightScale.HasValue && p.LineWeightScale == 1.0) p.LineWeightScale = a.LineWeightScale.Value;
            if (string.IsNullOrEmpty(p.TextStyle)      && !string.IsNullOrEmpty(a.TextStyleName))      p.TextStyle = a.TextStyleName;
            if (string.IsNullOrEmpty(p.DimensionStyle) && !string.IsNullOrEmpty(a.DimensionStyleName)) p.DimensionStyle = a.DimensionStyleName;
            if (string.IsNullOrEmpty(p.HatchPalette)   && !string.IsNullOrEmpty(a.HatchPalette))       p.HatchPalette = a.HatchPalette;
        }

        private static ViewStylePackLibrary Merge(ViewStylePackLibrary baseLib, ViewStylePackLibrary over)
        {
            if (over == null) return baseLib ?? new ViewStylePackLibrary();
            var merged = new ViewStylePackLibrary
            {
                Version = Math.Max(baseLib?.Version ?? 1, over.Version),
                Packs = new List<ViewStylePack>(baseLib?.Packs ?? new List<ViewStylePack>()),
            };
            // First-wins build (NOT ToDictionary, which throws on a duplicate
            // key) so a duplicate corporate pack id can never crash pack
            // resolution on projects carrying an override. Mirrors
            // DrawingTypeRegistry.Merge; null elements are dropped.
            var byId = new Dictionary<string, ViewStylePack>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (var p in merged.Packs)
            {
                if (p == null) continue;
                var key = p.Id ?? "";
                if (!byId.ContainsKey(key)) { byId[key] = p; order.Add(key); }
            }
            foreach (var p in over.Packs ?? new List<ViewStylePack>())
            {
                if (p == null || string.IsNullOrWhiteSpace(p.Id)) continue;
                if (!byId.ContainsKey(p.Id)) order.Add(p.Id);
                byId[p.Id] = p; // project overrides corporate on same id
            }
            merged.Packs = order.Select(k => byId[k]).ToList();
            return merged;
        }

        /// <summary>
        /// Walk the extends chain and return a fully-merged pack —
        /// child fields override parent where set. Loop-detection via
        /// a visited set so corrupt JSON with cycles fails safely
        /// rather than stack-overflowing.
        /// </summary>
        private static ViewStylePack ResolveExtends(ViewStylePackLibrary lib, ViewStylePack leaf)
        {
            var chain = new List<ViewStylePack>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cur = leaf;
            while (cur != null)
            {
                if (!visited.Add(cur.Id ?? Guid.NewGuid().ToString()))
                {
                    StingTools.Core.StingLog.Warn($"ViewStylePack extends cycle at '{cur.Id}'.");
                    break;
                }
                chain.Add(cur);
                if (string.IsNullOrWhiteSpace(cur.Extends)) break;
                cur = lib.Packs.FirstOrDefault(
                    p => string.Equals(p.Id, cur.Extends, StringComparison.OrdinalIgnoreCase));
            }

            // Fold parent → child. Later entries overwrite earlier.
            chain.Reverse();
            var merged = new ViewStylePack { Id = leaf.Id, Name = leaf.Name, Origin = leaf.Origin, Extends = leaf.Extends };
            foreach (var p in chain)
            {
                // Carry every field not explicitly merged below. Without
                // this the fold dropped templateMode / managedFields /
                // discipline / visualStyle / viewRange / worksetVisibility /
                // linkOverrides / colorFillSchemes / categoryDepths /
                // categoryTag7Sections / viewTemplate / detailLevel /
                // scaleHint / colorScheme — and since all 35 shipped packs
                // declare `extends`, that ran on every Get(), which is why
                // IsManaged never survived to DrawingTypePresentation.
                ExtendsMerge.Overlay(p, merged, _overlayProps);

                if (p.LineWeightScale != 0) merged.LineWeightScale = p.LineWeightScale;
                if (!string.IsNullOrEmpty(p.TextStyle))      merged.TextStyle      = p.TextStyle;
                if (!string.IsNullOrEmpty(p.DimensionStyle)) merged.DimensionStyle = p.DimensionStyle;
                if (!string.IsNullOrEmpty(p.HatchPalette))   merged.HatchPalette   = p.HatchPalette;
                if (p.Filters != null) foreach (var f in p.Filters) merged.Filters.Add(f);
                if (p.VgOverrides != null)
                    foreach (var kv in p.VgOverrides) merged.VgOverrides[kv.Key] = kv.Value;
                if (p.TagFamilies != null)
                    foreach (var kv in p.TagFamilies) merged.TagFamilies[kv.Key] = kv.Value;

                // Phase 135 — Tag Appearance pack-level defaults
                if (!string.IsNullOrEmpty(p.TagColorScheme))   merged.TagColorScheme = p.TagColorScheme;
                if (!string.IsNullOrEmpty(p.DefaultTagStyle))  merged.DefaultTagStyle = p.DefaultTagStyle;
                if (p.CategoryTagStyles != null)
                {
                    if (merged.CategoryTagStyles == null)
                        merged.CategoryTagStyles = new Dictionary<string, string>();
                    foreach (var kv in p.CategoryTagStyles) merged.CategoryTagStyles[kv.Key] = kv.Value;
                }

                // Phase 177 per-category maps — merge by key like their
                // siblings above rather than letting the child's whole
                // dictionary replace the parent's.
                if (p.CategoryDepths != null)
                {
                    if (merged.CategoryDepths == null)
                        merged.CategoryDepths = new Dictionary<string, int>();
                    foreach (var kv in p.CategoryDepths) merged.CategoryDepths[kv.Key] = kv.Value;
                }
                if (p.CategoryTag7Sections != null)
                {
                    if (merged.CategoryTag7Sections == null)
                        merged.CategoryTag7Sections = new Dictionary<string, bool>();
                    foreach (var kv in p.CategoryTag7Sections) merged.CategoryTag7Sections[kv.Key] = kv.Value;
                }
            }
            return merged;
        }

        // Fields the fold above merges by hand — collections with
        // accumulate / merge-by-key semantics, plus the identity fields
        // taken from the leaf and the scalars whose existing guards are
        // preserved verbatim. Everything else is carried by
        // ExtendsMerge.Overlay.
        private static readonly HashSet<string> _overlaySkip = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(ViewStylePack.Id),
            nameof(ViewStylePack.Name),
            nameof(ViewStylePack.Origin),
            nameof(ViewStylePack.Extends),
            nameof(ViewStylePack.LineWeightScale),
            nameof(ViewStylePack.TextStyle),
            nameof(ViewStylePack.DimensionStyle),
            nameof(ViewStylePack.HatchPalette),
            nameof(ViewStylePack.TagColorScheme),
            nameof(ViewStylePack.DefaultTagStyle),
            nameof(ViewStylePack.Filters),
            nameof(ViewStylePack.VgOverrides),
            nameof(ViewStylePack.TagFamilies),
            nameof(ViewStylePack.CategoryTagStyles),
            nameof(ViewStylePack.CategoryDepths),
            nameof(ViewStylePack.CategoryTag7Sections),
        };

        private static readonly System.Reflection.PropertyInfo[] _overlayProps
            = ExtendsMerge.OverlayProps(typeof(ViewStylePack), _overlaySkip);

        private static string DocKey(Document doc)
        {
            if (doc == null) return "__null__";
            try { return string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName; }
            catch { return "__unknown__"; }
        }

        // Built-in defaults ------------------------------------------------
        private static ViewStylePackLibrary BuildDefaults()
        {
            var lib = new ViewStylePackLibrary { Version = 1 };
            lib.Packs.Add(new ViewStylePack {
                Id = "corp-standard-plan", Name = "Corporate Standard Plan",
                Origin = "corporate", LineWeightScale = 1.0,
                TextStyle = "STING - 2.5mm", DimensionStyle = "STING - Linear",
            });
            lib.Packs.Add(new ViewStylePack {
                Id = "corp-presentation", Name = "Corporate Presentation",
                Origin = "corporate", LineWeightScale = 0.8,
                TextStyle = "STING - 3.0mm Presentation", HatchPalette = "Rich",
            });
            lib.Packs.Add(new ViewStylePack {
                Id = "corp-fabrication", Name = "Corporate Fabrication Shop",
                Origin = "corporate", LineWeightScale = 1.1,
                TextStyle = "STING - 2.0mm Shop", DimensionStyle = "STING - Ordinate",
            });
            return lib;
        }
    }
}
