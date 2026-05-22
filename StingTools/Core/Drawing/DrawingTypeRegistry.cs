using StingTools.Core;
// StingTools — Drawing Template Manager
//
// DrawingTypeRegistry is the single access point for resolved Drawing
// Types. It:
//   1. Loads Data/STING_DRAWING_TYPES.json (shipped corporate baseline)
//   2. Layers project-scoped overrides from
//      <project>/_BIM_COORD/drawing_types.json, if present
//   3. Falls back to 15 hard-coded built-ins when neither file exists,
//      so a brand-new project still gets a sensible catalogue
//   4. Computes a SHA-256 checksum for every "corporate" origin entry
//      and flips the origin flag to "project" on first edit so the
//      shipped baseline cannot be silently mutated
//
// The registry caches the merged library per-document; callers use
// Get(doc, id), ResolveFor(doc, discipline, phase, docType), or
// ListAll(doc). Reload(doc) forces a re-read — wired to the
// DrawingTypesReload diagnostic command.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace StingTools.Core.Drawing
{
    public static class DrawingTypeRegistry
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<string, DrawingTypeLibrary> _cache
            = new Dictionary<string, DrawingTypeLibrary>(StringComparer.OrdinalIgnoreCase);

        // C-5: per-document memoization of ResolveExtends results so callers
        // (DrawingDriftDetector.Scan, DrawingTypePresentation.Apply, the
        // auto-tagger discipline filter) don't recompute the merged inheritance
        // chain on every Get() call. Cleared by Reload(doc).
        private static readonly Dictionary<string, Dictionary<string, DrawingType>> _resolvedCache
            = new Dictionary<string, Dictionary<string, DrawingType>>(StringComparer.OrdinalIgnoreCase);

        // Public surface -------------------------------------------------

        public static DrawingType Get(Document doc, string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            var lib = GetLibrary(doc);

            // C-5: cached resolved DrawingType. Lookup is O(1); first-call
            // miss runs ResolveExtends once and stores the result.
            string docKey = DocKey(doc);
            lock (_lock)
            {
                if (_resolvedCache.TryGetValue(docKey, out var docMap)
                    && docMap.TryGetValue(id, out var memo))
                {
                    StingTools.Core.StingLog.RecordHit(StingTools.Core.StingLog.CacheKind.DrawingTypeRegistry); // E-1
                    return memo;
                }
            }
            StingTools.Core.StingLog.RecordMiss(StingTools.Core.StingLog.CacheKind.DrawingTypeRegistry); // E-1

            var raw = lib.DrawingTypes.FirstOrDefault(
                t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
            var resolved = raw == null ? null : ResolveExtends(lib, raw);

            lock (_lock)
            {
                if (!_resolvedCache.TryGetValue(docKey, out var docMap))
                {
                    docMap = new Dictionary<string, DrawingType>(StringComparer.OrdinalIgnoreCase);
                    _resolvedCache[docKey] = docMap;
                }
                docMap[id] = resolved;
            }
            return resolved;
        }

        /// <summary>
        /// Phase 137 — convenience accessor that mirrors
        /// <see cref="ViewStylePackRegistry"/>.Get but lives next to the
        /// drawing-type registry so callers can reach packs without an
        /// extra using-statement. Returns null when the pack id is null,
        /// empty, or unresolved.
        /// </summary>
        public static ViewStylePack TryGetPack(Document doc, string packId)
        {
            if (string.IsNullOrWhiteSpace(packId)) return null;
            try { return ViewStylePackRegistry.Get(doc, packId); }
            catch { return null; }
        }

        /// <summary>
        /// Walk DrawingType.Extends and return a merged snapshot —
        /// child non-null / non-default fields override parent. Loop
        /// detection via visited-set; mirror of ViewStylePackRegistry.
        /// </summary>
        // FIX-5: dedupe ACC-01 drift warnings so they fire once per
        // (child, drifted parent) pair per session rather than on every
        // first cache miss. Cleared by Reload alongside the resolved cache.
        // Process-wide rather than per-doc on purpose — drift warnings are
        // about corporate-vs-project state, which carries across "Save As".
        private static readonly HashSet<string> _extendsDriftWarned
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _driftWarnedLock = new object();

        private static DrawingType ResolveExtends(DrawingTypeLibrary lib, DrawingType leaf)
        {
            if (string.IsNullOrWhiteSpace(leaf.Extends)) return leaf;

            var chain = new List<DrawingType>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cur = leaf;
            // GAP-P: hard depth limit guards against pathological JSON where
            // the visited-set doesn't catch a cycle (e.g. multiple null-id
            // entries chained together).
            int depthGuard = 0;
            while (cur != null && depthGuard++ < 32)
            {
                if (!visited.Add(cur.Id ?? Guid.NewGuid().ToString()))
                {
                    StingTools.Core.StingLog.Warn($"DrawingType extends cycle at '{cur.Id}'.");
                    break;
                }
                chain.Add(cur);
                // ACC-01: detect a drifted parent in the extends chain. If the
                // parent's origin has been flipped to "project" by checksum
                // drift, the child silently inherits the drifted fields. Log
                // a warning so the operator knows their resolved child is
                // non-canonical even when the JSON file is shipped pristine.
                if (cur != leaf
                    && string.Equals(cur.Origin, "project", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(cur.Checksum))
                {
                    string warnKey = (leaf.Id ?? string.Empty) + "→" + (cur.Id ?? string.Empty);
                    bool fire;
                    lock (_driftWarnedLock)
                    {
                        // GAP-G: soft-cap so a long-running session with many
                        // unique (child, parent) pairs cannot leak unbounded
                        // memory. 1024 is an order of magnitude above any
                        // realistic project's drawing-type catalogue.
                        if (_extendsDriftWarned.Count > 1024) _extendsDriftWarned.Clear();
                        fire = _extendsDriftWarned.Add(warnKey);
                    }
                    if (fire)
                        StingTools.Core.StingLog.Warn(
                            $"DrawingType '{leaf.Id}' extends '{cur.Id}' which has drifted from corporate baseline; " +
                            "merged snapshot reflects the project-edited parent values.");
                }
                if (string.IsNullOrWhiteSpace(cur.Extends)) break;
                cur = lib.DrawingTypes.FirstOrDefault(
                    t => string.Equals(t.Id, cur.Extends, StringComparison.OrdinalIgnoreCase));
            }

            chain.Reverse();
            var m = new DrawingType { Id = leaf.Id, Name = leaf.Name, Origin = leaf.Origin, Extends = leaf.Extends };
            foreach (var p in chain)
            {
                if (!string.IsNullOrEmpty(p.Description))      m.Description = p.Description;
                if (!string.IsNullOrEmpty(p.Purpose))          m.Purpose = p.Purpose;
                if (!string.IsNullOrEmpty(p.Discipline))       m.Discipline = p.Discipline;
                if (!string.IsNullOrEmpty(p.Phase))            m.Phase = p.Phase;
                if (!string.IsNullOrEmpty(p.PaperSize))        m.PaperSize = p.PaperSize;
                if (!string.IsNullOrEmpty(p.TitleBlockFamily)) m.TitleBlockFamily = p.TitleBlockFamily;
                if (!string.IsNullOrEmpty(p.Orientation))      m.Orientation = p.Orientation;
                if (p.Scale > 0)                               m.Scale = p.Scale;
                if (!string.IsNullOrEmpty(p.DetailLevel))      m.DetailLevel = p.DetailLevel;
                if (!string.IsNullOrEmpty(p.ViewTemplateName)) m.ViewTemplateName = p.ViewTemplateName;
                if (!string.IsNullOrEmpty(p.ViewportTypeName)) m.ViewportTypeName = p.ViewportTypeName;
                if (!string.IsNullOrEmpty(p.SheetNumberPattern)) m.SheetNumberPattern = p.SheetNumberPattern;
                if (!string.IsNullOrEmpty(p.SheetNamePattern))   m.SheetNamePattern = p.SheetNamePattern;
                if (!string.IsNullOrEmpty(p.ViewStylePackId))    m.ViewStylePackId = p.ViewStylePackId;
                if (p.Crop != null)          m.Crop = p.Crop;
                if (p.SectionMarker != null) m.SectionMarker = p.SectionMarker;
                if (p.Slots != null && p.Slots.Count > 0) m.Slots = p.Slots;
                if (p.Annotation != null)    m.Annotation = p.Annotation;
                if (p.TokenProfile != null)  m.TokenProfile = p.TokenProfile;
                if (p.Print != null)         m.Print = p.Print;
            }
            return m;
        }

        public static IReadOnlyList<DrawingType> ListAll(Document doc)
            => GetLibrary(doc).DrawingTypes;

        /// <summary>
        /// FG-05: invalidate every cached resolved DrawingType for a doc
        /// without re-loading the JSON library. Call after editing a
        /// single profile in-session (e.g. via the WPF editor) so children
        /// that extend the edited parent re-merge their snapshot on next
        /// <see cref="Get"/>.
        /// </summary>
        public static void InvalidateResolvedCache(Document doc)
        {
            string docKey = DocKey(doc);
            lock (_lock)
            {
                if (_resolvedCache.ContainsKey(docKey)) _resolvedCache.Remove(docKey);
            }
            try { DrawingTypePresentation.InvalidateViewTemplateCache(doc); } catch { }
            try { DrawingTypePresentation.InvalidatePackCache(doc); }       catch { }
            try { DrawingDriftDetector.InvalidateCache(doc); }              catch { }
        }

        public static IReadOnlyList<DrawingRoutingRule> ListRouting(Document doc)
            => GetLibrary(doc).Routing;

        /// <summary>
        /// Convenience method: look up a <see cref="ViewStylePack"/> by id via
        /// <see cref="ViewStylePackRegistry"/>.  Returns null when the pack is not
        /// found (no exception).  Used by <c>DrawingDriftDetector</c> and other
        /// consumers that want a null-check rather than a try/catch.
        /// </summary>
        public static ViewStylePack TryGetPack(Document doc, string packId)
        {
            if (string.IsNullOrWhiteSpace(packId)) return null;
            try { return ViewStylePackRegistry.Get(doc, packId); }
            catch { return null; }
        }

        public static void Reload(Document doc)
        {
            lock (_lock)
            {
                var key = DocKey(doc);
                if (_cache.ContainsKey(key)) _cache.Remove(key);
                if (_resolvedCache.ContainsKey(key)) _resolvedCache.Remove(key);
            }
            // Phase 183 — snapshot + diff the new library against the
            // previous load so Inspect / SyncStyles can surface "X
            // profiles changed since last reload" without the user
            // remembering what they edited. Safe outside the lock (it
            // reloads via GetLibrary internally).
            try { LiveProfileSync.OnRegistryReloaded(doc); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"DrawingTypeRegistry.Reload sync: {ex.Message}"); }
        }

        public static DrawingTypeLibrary GetLibrary(Document doc)
        {
            var key = DocKey(doc);
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var cached)) return cached;

                var corporate = LoadCorporate();
                var project   = LoadProjectOverride(doc);
                var merged    = Merge(corporate, project);
                ComputeChecksums(merged);
                // Phase 113 — fold legacy autoDim*/autoTag* booleans into
                // the new Rules collection so consumers only inspect Rules.
                foreach (var t in merged.DrawingTypes ?? new List<DrawingType>())
                    t.Annotation?.MigrateFromLegacy();
                _cache[key] = merged;
                return merged;
            }
        }

        // Loading --------------------------------------------------------

        private static DrawingTypeLibrary LoadCorporate()
        {
            try
            {
                var path = StingTools.Core.StingToolsApp.FindDataFile("STING_DRAWING_TYPES.json");
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var lib = JsonConvert.DeserializeObject<DrawingTypeLibrary>(json);
                    if (lib != null && lib.DrawingTypes != null && lib.DrawingTypes.Count > 0)
                    {
                        foreach (var t in lib.DrawingTypes)
                            if (string.IsNullOrEmpty(t.Origin)) t.Origin = "corporate";
                        return lib;
                    }
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"DrawingTypeRegistry: failed to load corporate JSON — falling back to built-ins. {ex.Message}");
            }
            return BuildDefaults();
        }

        private static DrawingTypeLibrary LoadProjectOverride(Document doc)
        {
            if (doc == null) return null;
            try
            {
                // Pack 122 / Gap C — Extensible Storage first. Survives "Save As"
                // and project renames; only falls back to the on-disk JSON for
                // pre-migration projects.
                var esJson = StingTools.Core.Storage.StingDrawingTypesSchema.Read(doc)?.OverridesJson;
                if (!string.IsNullOrEmpty(esJson))
                {
                    var lib = JsonConvert.DeserializeObject<DrawingTypeLibrary>(esJson);
                    if (lib != null)
                    {
                        foreach (var t in lib.DrawingTypes ?? new List<DrawingType>())
                            if (string.IsNullOrEmpty(t.Origin)) t.Origin = "project";
                    }
                    return lib;
                }

                var projPath = doc.PathName;
                if (string.IsNullOrEmpty(projPath)) return null;
                var dir = Path.GetDirectoryName(projPath);
                if (string.IsNullOrEmpty(dir)) return null;
                var path = Path.Combine(dir, "_BIM_COORD", "drawing_types.json");
                if (!File.Exists(path)) return null;
                var jsonOnDisk = File.ReadAllText(path);
                var libOnDisk = JsonConvert.DeserializeObject<DrawingTypeLibrary>(jsonOnDisk);
                if (libOnDisk != null)
                {
                    foreach (var t in libOnDisk.DrawingTypes ?? new List<DrawingType>())
                        if (string.IsNullOrEmpty(t.Origin)) t.Origin = "project";
                }
                return libOnDisk;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"DrawingTypeRegistry: project override load failed — {ex.Message}");
                return null;
            }
        }

        private static DrawingTypeLibrary Merge(DrawingTypeLibrary baseLib, DrawingTypeLibrary over)
        {
            if (over == null) return baseLib ?? new DrawingTypeLibrary();
            var merged = new DrawingTypeLibrary
            {
                Version = Math.Max(baseLib?.Version ?? 1, over.Version),
                DrawingTypes = new List<DrawingType>(baseLib?.DrawingTypes ?? new List<DrawingType>()),
                Routing = new List<DrawingRoutingRule>(baseLib?.Routing ?? new List<DrawingRoutingRule>()),
            };

            // Project types win over corporate types sharing the same id
            var byId = merged.DrawingTypes.ToDictionary(
                t => t.Id ?? "", StringComparer.OrdinalIgnoreCase);
            foreach (var t in over.DrawingTypes ?? new List<DrawingType>())
            {
                if (string.IsNullOrWhiteSpace(t.Id)) continue;
                byId[t.Id] = t;
            }
            merged.DrawingTypes = byId.Values.ToList();

            // Project routing rules are prepended (first-match-wins semantics).
            // GAP-J: dedupe by the (discipline, phase, docType) triple plus
            // any predicate fields that participate in matching, so a project
            // rule that overrides a corporate rule for the same key fully
            // suppresses the corporate entry rather than leaving it as a
            // never-reached trailing rule.
            if (over.Routing != null && over.Routing.Count > 0)
            {
                merged.Routing.InsertRange(0, over.Routing);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var deduped = new List<DrawingRoutingRule>();
                foreach (var rule in merged.Routing)
                {
                    if (rule == null) continue;
                    string sig = string.Join("|",
                        rule.Discipline ?? "*",
                        rule.Phase ?? "*",
                        rule.DocType ?? "*",
                        rule.DisciplineMatches ?? "",
                        rule.PhaseMatches ?? "",
                        rule.DocTypeMatches ?? "",
                        rule.LevelMatches ?? "",
                        rule.ProjectCodeMatches ?? "");
                    if (seen.Add(sig)) deduped.Add(rule);
                }
                merged.Routing = deduped;
            }

            return merged;
        }

        // Corporate-lock checksum ---------------------------------------
        //
        // The checksum lets us detect a corporate baseline that was
        // edited out-of-band (hand-tweaked on disk); downstream code can
        // warn the user rather than silently using the edited version.

        private static void ComputeChecksums(DrawingTypeLibrary lib)
        {
            if (lib?.DrawingTypes == null) return;
            foreach (var t in lib.DrawingTypes)
            {
                if (!string.Equals(t.Origin, "corporate", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var prior = t.Checksum;
                    t.Checksum = null;
                    var json = JsonConvert.SerializeObject(t, Formatting.None);
                    using (var sha = SHA256.Create())
                    {
                        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
                        // ACC-08: full 64-char hex hash. Previous 16-char Base64
                        // prefix had ~96 bits of collision space, which is
                        // unacceptable for an integrity check.
                        var sb = new StringBuilder(hash.Length * 2);
                        foreach (var b in hash) sb.Append(b.ToString("x2"));
                        var actual = sb.ToString();
                        // Migration safety: a project that opened with the old
                        // 16-char Base64 prefix should silently upgrade to the
                        // new 64-char hex form on first read. Treat any prior
                        // value that's not 64 hex chars as legacy and skip the
                        // drift warning.
                        bool priorIsLegacy = !string.IsNullOrEmpty(prior)
                            && prior.Length != 64;
                        if (!string.IsNullOrEmpty(prior) && !priorIsLegacy && prior != actual)
                        {
                            StingTools.Core.StingLog.Warn(
                                $"DrawingType '{t.Id}' checksum drift: shipped={prior} actual={actual}. " +
                                "Corporate baseline was edited on disk; origin flipped to 'project'.");
                            t.Origin = "project";
                        }
                        t.Checksum = actual;
                    }
                }
                catch (Exception ex)
                {
                    StingTools.Core.StingLog.Warn($"Checksum '{t.Id}': {ex.Message}");
                }
            }
        }

        // Built-in defaults ---------------------------------------------
        //
        // Minimal but comprehensive set covering every primary discipline
        // and document type, used when no JSON has been extracted yet.
        // Full rich defaults live in Data/STING_DRAWING_TYPES.json; these
        // C# built-ins are a last-resort fallback for test / unshipped
        // deployment scenarios.

        private static DrawingTypeLibrary BuildDefaults()
        {
            var lib = new DrawingTypeLibrary { Version = 1 };

            lib.DrawingTypes.AddRange(new[]
            {
                // Architectural
                MakeBasic("arch-plan-A1-1to100",       "Architectural Plan A1 1:100",     DrawingPurpose.Plan,      "A", 100),
                MakeBasic("arch-rcp-A1-1to100",        "Architectural RCP A1 1:100",      DrawingPurpose.Rcp,       "A", 100),
                MakeBasic("arch-section-A1-1to50",     "Architectural Section A1 1:50",   DrawingPurpose.Section,   "A",  50),
                MakeBasic("arch-elev-A1-1to100",       "Architectural Elevation A1 1:100",DrawingPurpose.Elevation, "A", 100),
                MakeBasic("arch-detail-A3-1to20",      "Architectural Detail A3 1:20",    DrawingPurpose.Detail,    "A",  20),
                // Structural
                MakeBasic("struct-plan-A1-1to100",     "Structural Plan A1 1:100",        DrawingPurpose.Plan,      "S", 100),
                MakeBasic("struct-section-A1-1to50",   "Structural Section A1 1:50",      DrawingPurpose.Section,   "S",  50),
                // MEP
                MakeBasic("mep-plan-A1-1to100",        "MEP Plan A1 1:100",               DrawingPurpose.Plan,      "M", 100),
                MakeBasic("mep-coord-A1-1to50",        "MEP Coordination A1 1:50",        DrawingPurpose.Coordination, "M", 50),
                // Fabrication / shop
                MakeFabSpool("pipe-spool-A1-1to50",    "Pipe Spool A1 1:50",              "P"),
                MakeFabSpool("duct-spool-A1-1to50",    "Duct Spool A1 1:50",              "M"),
                MakeBasic("elec-riser-A3-1to200",      "Electrical Riser A3 1:200",       DrawingPurpose.Plan,      "E", 200),
                // Schedules + handover
                MakeSchedule("door-schedule-A3",       "Door Schedule A3",                "A"),
                MakeBasic("handover-A1",               "Handover Sheet A1",               DrawingPurpose.Plan,      "A", 100),
                MakeBasic("legend-A3",                 "Legend Sheet A3",                 DrawingPurpose.Legend,    "G", 100),
            });

            // Routing table — first match wins
            lib.Routing.AddRange(new[]
            {
                new DrawingRoutingRule { Discipline = "P", DocType = "SPOOL",   DrawingTypeId = "pipe-spool-A1-1to50" },
                new DrawingRoutingRule { Discipline = "M", DocType = "SPOOL",   DrawingTypeId = "duct-spool-A1-1to50" },
                new DrawingRoutingRule { Discipline = "A", DocType = "PLAN",    DrawingTypeId = "arch-plan-A1-1to100" },
                new DrawingRoutingRule { Discipline = "A", DocType = "RCP",     DrawingTypeId = "arch-rcp-A1-1to100" },
                new DrawingRoutingRule { Discipline = "A", DocType = "SECTION", DrawingTypeId = "arch-section-A1-1to50" },
                new DrawingRoutingRule { Discipline = "A", DocType = "ELEVATION", DrawingTypeId = "arch-elev-A1-1to100" },
                new DrawingRoutingRule { Discipline = "A", DocType = "DETAIL",  DrawingTypeId = "arch-detail-A3-1to20" },
                new DrawingRoutingRule { Discipline = "S", DocType = "PLAN",    DrawingTypeId = "struct-plan-A1-1to100" },
                new DrawingRoutingRule { Discipline = "S", DocType = "SECTION", DrawingTypeId = "struct-section-A1-1to50" },
                new DrawingRoutingRule { Discipline = "M", DocType = "PLAN",    DrawingTypeId = "mep-plan-A1-1to100" },
                new DrawingRoutingRule { Discipline = "M", DocType = "COORD",   DrawingTypeId = "mep-coord-A1-1to50" },
                new DrawingRoutingRule { Discipline = "E", DocType = "PLAN",    DrawingTypeId = "elec-riser-A3-1to200" },
                new DrawingRoutingRule { Discipline = "*", DocType = "SCHEDULE",DrawingTypeId = "door-schedule-A3" },
                new DrawingRoutingRule { Discipline = "*", DocType = "LEGEND",  DrawingTypeId = "legend-A3" },
                new DrawingRoutingRule { Discipline = "*", DocType = "HANDOVER",DrawingTypeId = "handover-A1" },
            });

            return lib;
        }

        private static DrawingType MakeBasic(string id, string name, string purpose, string disc, int scale)
        {
            var dt = new DrawingType
            {
                Id = id, Name = name, Purpose = purpose, Discipline = disc,
                PaperSize = id.Contains("A2") ? "A2" : id.Contains("A3") ? "A3" : "A1",
                Scale = scale,
                DetailLevel = scale <= 50 ? "Fine" : "Medium",
                SheetNumberPattern = $"{disc}-{{lvl}}-{{seq:D3}}",
                SheetNamePattern   = $"{{discipline}} {purpose} - {{lvl}}",
                Annotation = AnnotationPackFor(purpose),
                Crop = new DrawingCropStrategy { Kind = "ScopeBoxOrBbox", MarginMm = 150 },
                SectionMarker = new SectionMarkerSpec
                {
                    MarkPrefix = purpose == DrawingPurpose.Section ? "S"
                               : purpose == DrawingPurpose.Elevation ? "E"
                               : purpose == DrawingPurpose.Detail ? "D" : null,
                },
            };
            dt.Slots.Add(new DrawingSlot
            {
                Label = "Main", ViewType = purpose,
                NormX = 0.05, NormY = 0.05, NormW = 0.70, NormH = 0.90,
                Required = true,
            });
            return dt;
        }

        private static DrawingType MakeFabSpool(string id, string name, string disc)
        {
            // Mirrors ShopDrawingComposer's current 1:50 A1 slot map so a
            // fabrication pipeline that resolves this profile produces the
            // same sheet the hard-coded path produces today.
            var dt = new DrawingType
            {
                Id = id, Name = name, Purpose = DrawingPurpose.Spool, Discipline = disc,
                PaperSize = "A1", Scale = 50, DetailLevel = "Fine",
                TitleBlockFamily = disc == "P" ? "STING_TB_ASSEMBLY_PIPE" : "STING_TB_ASSEMBLY_DUCT",
                SheetNumberPattern = "SP-{disc}-{sys}-{lvl}-{seq:D4}",
                SheetNamePattern   = "{discipline} spool {spool}",
                Crop = new DrawingCropStrategy { Kind = "TightBbox", MarginMm = 300 },
#pragma warning disable CS0618 // legacy AutoXxx setters; MigrateFromLegacy folds them into Rules at registry-load
                Annotation = new AnnotationRulePack
                {
                    AutoTagWelds = true, AutoTagBends = true, AutoTagSupports = true,
                    DimensionStrategy = "Ordinate",
                    DenseUntilScale = 100,
                },
#pragma warning restore CS0618
            };
            dt.Slots.AddRange(new[]
            {
                new DrawingSlot { Label = "Plan",   ViewType = "Plan",     NormX = 0.05, NormY = 0.55, NormW = 0.40, NormH = 0.40 },
                new DrawingSlot { Label = "ISO",    ViewType = "ISO",      NormX = 0.55, NormY = 0.55, NormW = 0.40, NormH = 0.40 },
                new DrawingSlot { Label = "Elev0",  ViewType = "Elevation",NormX = 0.05, NormY = 0.10, NormW = 0.28, NormH = 0.40 },
                new DrawingSlot { Label = "Elev90", ViewType = "Elevation",NormX = 0.36, NormY = 0.10, NormW = 0.28, NormH = 0.40 },
                new DrawingSlot { Label = "3D",     ViewType = "3D",       NormX = 0.67, NormY = 0.10, NormW = 0.28, NormH = 0.40 },
                new DrawingSlot { Label = "BOM",    ViewType = "Schedule", NormX = 0.78, NormY = 0.55, NormW = 0.20, NormH = 0.40 },
            });
            return dt;
        }

        private static DrawingType MakeSchedule(string id, string name, string disc)
        {
            var dt = new DrawingType
            {
                Id = id, Name = name, Purpose = DrawingPurpose.Schedule, Discipline = disc,
                PaperSize = "A2", Scale = 100, DetailLevel = "Medium",
                SheetNumberPattern = $"{disc}-SCH-{{seq:D3}}",
                SheetNamePattern   = "{discipline} Schedule",
                Crop = new DrawingCropStrategy { Kind = "None" },
            };
            dt.Slots.Add(new DrawingSlot
            {
                Label = "Main", ViewType = "Schedule",
                NormX = 0.05, NormY = 0.05, NormW = 0.90, NormH = 0.90,
                Required = true,
            });
            return dt;
        }

        private static AnnotationRulePack AnnotationPackFor(string purpose)
        {
#pragma warning disable CS0618 // legacy AutoXxx setters; MigrateFromLegacy folds them into Rules at registry-load
            switch (purpose)
            {
                case DrawingPurpose.Plan:
                case DrawingPurpose.Rcp:
                    return new AnnotationRulePack
                    {
                        AutoDimGrids = true, AutoTagRooms = true,
                        AutoTagDoors = true, AutoTagWindows = true,
                        DimensionStrategy = "Linear",
                        DenseUntilScale = 100,
                    };
                case DrawingPurpose.Section:
                case DrawingPurpose.Elevation:
                    return new AnnotationRulePack
                    {
                        AutoDimGrids = true, AutoDimLevels = true,
                        DimensionStrategy = "Linear",
                    };
                case DrawingPurpose.Detail:
                    return new AnnotationRulePack
                    {
                        AutoDimGrids = false, AutoDimLevels = true,
                        DimensionStrategy = "Chain",
                        DenseUntilScale = 50,
                    };
                default:
                    return new AnnotationRulePack();
            }
#pragma warning restore CS0618
        }

        private static string DocKey(Document doc)
        {
            if (doc == null) return "__null__";
            try { return string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName; }
            catch { return "__unknown__"; }
        }
    }
}
