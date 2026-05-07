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

        public static ViewStylePack Get(Document doc, string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            var lib = GetLibrary(doc);
            var raw = lib.Packs.FirstOrDefault(
                p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            return raw == null ? null : ResolveExtends(lib, raw);
        }

        public static IReadOnlyList<ViewStylePack> ListAll(Document doc)
            => GetLibrary(doc).Packs;

        public static void Reload(Document doc)
        {
            lock (_lock)
            {
                var key = DocKey(doc);
                if (_cache.ContainsKey(key)) _cache.Remove(key);
            }
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
                            if (string.IsNullOrEmpty(p.Origin)) p.Origin = "corporate";
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
                        if (string.IsNullOrEmpty(p.Origin)) p.Origin = "project";
                return lib;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"ViewStylePackRegistry: project override load failed — {ex.Message}");
                return null;
            }
        }

        private static ViewStylePackLibrary Merge(ViewStylePackLibrary baseLib, ViewStylePackLibrary over)
        {
            if (over == null) return baseLib ?? new ViewStylePackLibrary();
            var merged = new ViewStylePackLibrary
            {
                Version = Math.Max(baseLib?.Version ?? 1, over.Version),
                Packs = new List<ViewStylePack>(baseLib?.Packs ?? new List<ViewStylePack>()),
            };
            var byId = merged.Packs.ToDictionary(p => p.Id ?? "", StringComparer.OrdinalIgnoreCase);
            foreach (var p in over.Packs ?? new List<ViewStylePack>())
            {
                if (string.IsNullOrWhiteSpace(p.Id)) continue;
                byId[p.Id] = p;
            }
            merged.Packs = byId.Values.ToList();
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
            var merged = new ViewStylePack { Id = leaf.Id, Name = leaf.Name, Origin = leaf.Origin };
            foreach (var p in chain)
            {
                if (p.LineWeightScale != 0) merged.LineWeightScale = p.LineWeightScale;
                if (!string.IsNullOrEmpty(p.TextStyle))      merged.TextStyle      = p.TextStyle;
                if (!string.IsNullOrEmpty(p.DimensionStyle)) merged.DimensionStyle = p.DimensionStyle;
                if (!string.IsNullOrEmpty(p.HatchPalette))   merged.HatchPalette   = p.HatchPalette;

                // Phase 136 — pack-level view template / detail level / scale / colour scheme
                if (!string.IsNullOrEmpty(p.ViewTemplate))   merged.ViewTemplate   = p.ViewTemplate;
                if (!string.IsNullOrEmpty(p.DetailLevel))    merged.DetailLevel    = p.DetailLevel;
                if (!string.IsNullOrEmpty(p.ScaleHint))      merged.ScaleHint      = p.ScaleHint;
                if (!string.IsNullOrEmpty(p.ColorScheme))    merged.ColorScheme    = p.ColorScheme;

                // Phase 137 — managed mode + view-template controlled fields
                if (!string.IsNullOrEmpty(p.TemplateMode))   merged.TemplateMode   = p.TemplateMode;
                if (p.ManagedFields != null && p.ManagedFields.Count > 0) merged.ManagedFields = new List<string>(p.ManagedFields);
                if (!string.IsNullOrEmpty(p.Discipline))     merged.Discipline     = p.Discipline;
                if (!string.IsNullOrEmpty(p.VisualStyle))    merged.VisualStyle    = p.VisualStyle;
                if (!string.IsNullOrEmpty(p.PhaseFilter))    merged.PhaseFilter    = p.PhaseFilter;
                if (!string.IsNullOrEmpty(p.Phase))          merged.Phase          = p.Phase;
                if (p.AnnotationCrop.HasValue)               merged.AnnotationCrop = p.AnnotationCrop;
                if (p.FarClipMm.HasValue)                    merged.FarClipMm      = p.FarClipMm;
                if (p.ViewRange != null)                     merged.ViewRange      = p.ViewRange;
                if (p.Underlay != null)                      merged.Underlay       = p.Underlay;
                if (!string.IsNullOrEmpty(p.Background))     merged.Background     = p.Background;
                if (p.WorksetVisibility != null) merged.WorksetVisibility = MergeStringStringDict(merged.WorksetVisibility, p.WorksetVisibility);
                if (p.LinkOverrides != null)
                {
                    merged.LinkOverrides = merged.LinkOverrides ?? new Dictionary<string, PackLinkOverride>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in p.LinkOverrides) merged.LinkOverrides[kv.Key] = kv.Value;
                }
                if (p.ColorFillSchemes != null) merged.ColorFillSchemes = MergeStringStringDict(merged.ColorFillSchemes, p.ColorFillSchemes);
                if (p.FilterEnabled != null)
                {
                    merged.FilterEnabled = merged.FilterEnabled ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in p.FilterEnabled) merged.FilterEnabled[kv.Key] = kv.Value;
                }
                if (!string.IsNullOrEmpty(p.ManagedChecksum)) merged.ManagedChecksum = p.ManagedChecksum;

                // Filters — append. Same-name filters from the child win
                // because we re-add them after the parent's copy and the
                // applier uses last-write semantics inside Revit's
                // OverrideGraphicSettings.
                if (p.Filters != null) foreach (var f in p.Filters) merged.Filters.Add(f);

                // VG overrides — Phase 177: PER-FIELD merge instead of
                // whole-object replace. Previously a child saying
                // {"Walls": {"halftone": true}} would wipe the parent's
                // projColor / projWeight / cutColor / cutWeight on Walls.
                // Now child fields land on top of parent fields and only
                // the explicitly-set fields override.
                if (p.VgOverrides != null)
                    foreach (var kv in p.VgOverrides) merged.VgOverrides[kv.Key] = MergeVgOverride(
                        merged.VgOverrides.TryGetValue(kv.Key, out var existing) ? existing : null, kv.Value);

                if (p.TagFamilies != null)
                    foreach (var kv in p.TagFamilies) merged.TagFamilies[kv.Key] = kv.Value;

                // Phase 135 — Tag Appearance pack-level defaults
                if (!string.IsNullOrEmpty(p.TagColorScheme))   merged.TagColorScheme = p.TagColorScheme;
                if (!string.IsNullOrEmpty(p.DefaultTagStyle))  merged.DefaultTagStyle = p.DefaultTagStyle;
                if (p.CategoryTagStyles != null) merged.CategoryTagStyles = MergeStringStringDict(merged.CategoryTagStyles, p.CategoryTagStyles);

                // Phase 177 — per-category depth + TAG7 narrative sections
                if (p.CategoryDepths != null)
                {
                    merged.CategoryDepths = merged.CategoryDepths ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in p.CategoryDepths) merged.CategoryDepths[kv.Key] = kv.Value;
                }
                if (p.CategoryTag7Sections != null) merged.CategoryTag7Sections = MergeStringStringDict(merged.CategoryTag7Sections, p.CategoryTag7Sections);
            }
            return merged;
        }

        // Phase 177 — per-field merge of two StyleVgOverride values so a
        // child's halftone-only override doesn't blow away the parent's
        // colour and line weight. Child fields win on conflict.
        private static StyleVgOverride MergeVgOverride(StyleVgOverride parent, StyleVgOverride child)
        {
            if (parent == null) return child;
            if (child == null) return parent;
            return new StyleVgOverride
            {
                Visible              = child.Visible              ?? parent.Visible,
                Halftone             = child.Halftone             ?? parent.Halftone,
                ProjectionLineWeight = child.ProjectionLineWeight ?? parent.ProjectionLineWeight,
                ProjectionLineColor  = string.IsNullOrEmpty(child.ProjectionLineColor)  ? parent.ProjectionLineColor  : child.ProjectionLineColor,
                ProjectionLinePattern= string.IsNullOrEmpty(child.ProjectionLinePattern)? parent.ProjectionLinePattern: child.ProjectionLinePattern,
                CutLineWeight        = child.CutLineWeight        ?? parent.CutLineWeight,
                CutLineColor         = string.IsNullOrEmpty(child.CutLineColor)         ? parent.CutLineColor         : child.CutLineColor,
                CutLinePattern       = string.IsNullOrEmpty(child.CutLinePattern)       ? parent.CutLinePattern       : child.CutLinePattern,
                SurfaceFgColor       = string.IsNullOrEmpty(child.SurfaceFgColor)       ? parent.SurfaceFgColor       : child.SurfaceFgColor,
                SurfaceFgPattern     = string.IsNullOrEmpty(child.SurfaceFgPattern)     ? parent.SurfaceFgPattern     : child.SurfaceFgPattern,
                SurfaceFgVisible     = child.SurfaceFgVisible     ?? parent.SurfaceFgVisible,
                SurfaceBgColor       = string.IsNullOrEmpty(child.SurfaceBgColor)       ? parent.SurfaceBgColor       : child.SurfaceBgColor,
                SurfaceBgPattern     = string.IsNullOrEmpty(child.SurfaceBgPattern)     ? parent.SurfaceBgPattern     : child.SurfaceBgPattern,
                SurfaceBgVisible     = child.SurfaceBgVisible     ?? parent.SurfaceBgVisible,
                CutFgColor           = string.IsNullOrEmpty(child.CutFgColor)           ? parent.CutFgColor           : child.CutFgColor,
                CutFgPattern         = string.IsNullOrEmpty(child.CutFgPattern)         ? parent.CutFgPattern         : child.CutFgPattern,
                CutFgVisible         = child.CutFgVisible         ?? parent.CutFgVisible,
                CutBgColor           = string.IsNullOrEmpty(child.CutBgColor)           ? parent.CutBgColor           : child.CutBgColor,
                CutBgPattern         = string.IsNullOrEmpty(child.CutBgPattern)         ? parent.CutBgPattern         : child.CutBgPattern,
                Transparency         = child.Transparency         ?? parent.Transparency,
                DetailLevel          = string.IsNullOrEmpty(child.DetailLevel)          ? parent.DetailLevel          : child.DetailLevel,
            };
        }

        // Helper for any Dictionary<string,string> field that wants
        // case-insensitive child-wins merge semantics.
        private static Dictionary<string, string> MergeStringStringDict(Dictionary<string, string> parent, Dictionary<string, string> child)
        {
            var merged = parent != null
                ? new Dictionary<string, string>(parent, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (child != null) foreach (var kv in child) merged[kv.Key] = kv.Value;
            return merged;
        }

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
