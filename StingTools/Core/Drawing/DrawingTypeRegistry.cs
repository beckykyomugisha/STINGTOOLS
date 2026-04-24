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

        // Public surface -------------------------------------------------

        public static DrawingType Get(Document doc, string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            var lib = GetLibrary(doc);
            return lib.DrawingTypes.FirstOrDefault(
                t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static IReadOnlyList<DrawingType> ListAll(Document doc)
            => GetLibrary(doc).DrawingTypes;

        public static IReadOnlyList<DrawingRoutingRule> ListRouting(Document doc)
            => GetLibrary(doc).Routing;

        public static void Reload(Document doc)
        {
            lock (_lock)
            {
                var key = DocKey(doc);
                if (_cache.ContainsKey(key)) _cache.Remove(key);
            }
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
                var projPath = doc.PathName;
                if (string.IsNullOrEmpty(projPath)) return null;
                var dir = Path.GetDirectoryName(projPath);
                if (string.IsNullOrEmpty(dir)) return null;
                var path = Path.Combine(dir, "_BIM_COORD", "drawing_types.json");
                if (!File.Exists(path)) return null;
                var json = File.ReadAllText(path);
                var lib = JsonConvert.DeserializeObject<DrawingTypeLibrary>(json);
                if (lib != null)
                {
                    foreach (var t in lib.DrawingTypes ?? new List<DrawingType>())
                        if (string.IsNullOrEmpty(t.Origin)) t.Origin = "project";
                }
                return lib;
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

            // Project routing rules are prepended (first-match-wins semantics)
            if (over.Routing != null && over.Routing.Count > 0)
                merged.Routing.InsertRange(0, over.Routing);

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
                        var actual = Convert.ToBase64String(hash).Substring(0, 16);
                        if (!string.IsNullOrEmpty(prior) && prior != actual)
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
                MakeBasic("elec-riser-A2-1to100",      "Electrical Riser A2 1:100",       DrawingPurpose.Plan,      "E", 100),
                // Schedules + handover
                MakeSchedule("door-schedule-A2",       "Door Schedule A2",                "A"),
                MakeBasic("handover-A1",               "Handover Sheet A1",               DrawingPurpose.Plan,      "A", 100),
                MakeBasic("legend-A2",                 "Legend Sheet A2",                 DrawingPurpose.Legend,    "G", 100),
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
                new DrawingRoutingRule { Discipline = "E", DocType = "PLAN",    DrawingTypeId = "elec-riser-A2-1to100" },
                new DrawingRoutingRule { Discipline = "*", DocType = "SCHEDULE",DrawingTypeId = "door-schedule-A2" },
                new DrawingRoutingRule { Discipline = "*", DocType = "LEGEND",  DrawingTypeId = "legend-A2" },
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
                Annotation = new AnnotationRulePack
                {
                    AutoTagWelds = true, AutoTagBends = true, AutoTagSupports = true,
                    DimensionStrategy = "Ordinate",
                    DenseUntilScale = 100,
                },
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
        }

        private static string DocKey(Document doc)
        {
            if (doc == null) return "__null__";
            try { return string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName; }
            catch { return "__unknown__"; }
        }
    }
}
