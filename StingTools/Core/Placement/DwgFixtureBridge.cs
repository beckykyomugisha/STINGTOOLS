// StingTools — DwgFixtureBridge.
//
// The flagship synthesis: turn DWG MEP fixture SYMBOLS (blocks) into real,
// parameterised, swap-ready STING seed instances by REUSING the foundation —
// not a parallel pipeline. Pipeline:
//
//   DWG import  --(CADToModelEngine.PreviewImport)-->  fixture blocks
//               (point + layer + blockName + coarse category)
//     --(DwgSymbolMapRegistry)-->  STING category + variant hint + host anchor
//     --(CategoryToSeedRegistry)-->  seedId
//     --(SeedEnsurer.EnsureSeedsForCategories, OUTSIDE a transaction)-->  built/loaded seed family
//     --(PlacementHostPreflight.Place at the block point)-->  hosted, oriented instance
//     --(StingProvenanceSchema.Stamp)-->  provenance + source DWG layer/block (audit)
//
// The placed instances are STING seeds, so the user can then run Swap to
// Manufacturer (SwapToManufacturerCommand) to get real product geometry — the
// bridge output is swap-ready by construction. No duplication of the DWG geometry
// engine (CADToModelEngine) or the placement host (PlacementHostPreflight).

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core.Storage;
using StingTools.Model;

namespace StingTools.Core.Placement
{
    /// <summary>Outcome of a DWG fixture bridge run.</summary>
    public sealed class DwgFixtureBridgeResult
    {
        public int TotalBlocks { get; set; }         // block inserts detected (DetectedBlock path)
        public int TotalLayerPoints { get; set; }    // points captured from mapped layers (point/cluster)
        public int Placed { get; set; }
        public int SkippedNoMapping { get; set; }    // capture didn't map to a STING category
        public int SkippedSeedless { get; set; }     // category has no seed (runs/structure)
        public int SkippedNoSymbol { get; set; }     // seed family/variant not resolvable
        public int SkippedNotHosted { get; set; }    // PlacementHostPreflight returned Skipped
        public int SkippedExplodedNoPoint { get; set; } // layer mapped but nothing capturable
        public bool DryRun { get; set; }
        public bool IncludedLineClusters { get; set; }  // the experimental cluster pass ran
        public Dictionary<string, int> PlacedByCategory { get; } =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> CapturedByMode { get; } =   // block / point / cluster
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public List<string> Messages { get; } = new List<string>();
        public List<ElementId> PlacedIds { get; } = new List<ElementId>();
    }

    public static class DwgFixtureBridge
    {
        private const string EngineName = "DwgFixtureBridge";

        // A captured fixture (block insert, DWG point, or line cluster) resolved to its
        // STING target (read-only pre-pass).
        private sealed class Captured
        {
            public XYZ Point;
            public string BlockName = "";
            public string LayerName = "";
            public string Category = "";
            public string SeedId = "";
            public string Variant = "";
            public string Anchor = "WALL_MIDPOINT";
            public string Mode = "block";   // "block" | "point" | "cluster"
        }

        /// <summary>Pick the (first / only, else selected) DWG import and run the bridge.
        /// Returns a result even when nothing is found (with a message).</summary>
        public static DwgFixtureBridgeResult PlaceFromFirstImport(Document doc, bool dryRun)
            => PlaceFromFirstImport(doc, dryRun, includeLineClusters: false);

        /// <summary>Pick the (first) DWG import and run the bridge. <paramref name="includeLineClusters"/>
        /// turns on the EXPERIMENTAL line-cluster capture for exploded layers (dry-run gated by callers).</summary>
        public static DwgFixtureBridgeResult PlaceFromFirstImport(Document doc, bool dryRun, bool includeLineClusters)
        {
            var res = new DwgFixtureBridgeResult { DryRun = dryRun, IncludedLineClusters = includeLineClusters };
            if (doc == null) { res.Messages.Add("No document."); return res; }
            ImportInstance import = null;
            try
            {
                var imports = CADToModelEngine.FindImportInstances(doc);
                import = imports?.FirstOrDefault();
            }
            catch (Exception ex) { StingLog.Warn($"DwgFixtureBridge.FindImports: {ex.Message}"); }
            if (import == null)
            {
                res.Messages.Add("No DWG/DXF import found in the project. Link or import a DWG first, then re-run.");
                return res;
            }
            return PlaceFromImport(doc, import, dryRun, includeLineClusters);
        }

        public static DwgFixtureBridgeResult PlaceFromImport(Document doc, ImportInstance import, bool dryRun)
            => PlaceFromImport(doc, import, dryRun, includeLineClusters: false);

        /// <summary>Capture fixtures from <paramref name="import"/> (block inserts, plus
        /// points on mapped fixture layers when the DWG is exploded; plus EXPERIMENTAL
        /// line-clusters when <paramref name="includeLineClusters"/> is set), map each to a
        /// STING seed, ensure the seeds are built, then place + stamp them. Caller must NOT
        /// have an open transaction (seed build opens its own).</summary>
        public static DwgFixtureBridgeResult PlaceFromImport(Document doc, ImportInstance import, bool dryRun, bool includeLineClusters)
        {
            var res = new DwgFixtureBridgeResult { DryRun = dryRun, IncludedLineClusters = includeLineClusters };
            if (doc == null || import == null) { res.Messages.Add("No document / import."); return res; }

            // ── 1) Capture blocks (read-only) — REUSE the DWG geometry engine. ──
            CADExtractionResult extraction;
            List<DetectedBlock> blocks;
            try
            {
                extraction = new CADToModelEngine(doc).PreviewImport(import);
                blocks = extraction?.Blocks ?? new List<DetectedBlock>();
            }
            catch (Exception ex)
            {
                StingLog.Error("DwgFixtureBridge.PreviewImport", ex);
                res.Messages.Add($"Could not read the DWG import: {ex.Message}");
                return res;
            }
            res.TotalBlocks = blocks.Count;

            // ── 2) Resolve captures → category → seed (read-only pre-pass). ──
            var captured = new List<Captured>();

            // 2a) Block inserts (the proven path — captured regardless of layer).
            foreach (var b in blocks)
            {
                if (b?.InsertionPoint == null) continue;
                var map = DwgSymbolMapRegistry.Resolve(doc, b.BlockName, b.LayerName, b.InferredCategory);
                if (map == null || string.IsNullOrWhiteSpace(map.Category)) { res.SkippedNoMapping++; continue; }
                string seedId = CategoryToSeedRegistry.Resolve(doc, map.Category);
                if (string.IsNullOrWhiteSpace(seedId)) { res.SkippedSeedless++; continue; }
                captured.Add(new Captured
                {
                    Point = b.InsertionPoint, BlockName = b.BlockName ?? "", LayerName = b.LayerName ?? "",
                    Category = map.Category, SeedId = seedId, Variant = map.VariantHint ?? "", Anchor = map.Anchor,
                    Mode = "block"
                });
            }

            // 2b) Layer capture for EXPLODED geometry — points on mapped fixture layers,
            //     plus (experimental, opt-in) line-cluster centroids. Fixes the "0 blocks"
            //     case. Only layers that resolve to a STING category are considered.
            var fixtureLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var layer in (extraction?.LayerCounts?.Keys ?? Enumerable.Empty<string>()))
                {
                    if (string.IsNullOrWhiteSpace(layer) || layer == "(unnamed)") continue;
                    var lm = DwgSymbolMapRegistry.ResolveLayer(doc, layer);
                    if (lm != null && !string.IsNullOrWhiteSpace(lm.Category)) fixtureLayers.Add(layer);
                }
            }
            catch (Exception ex) { StingLog.Warn($"DwgFixtureBridge.fixtureLayers: {ex.Message}"); }

            var layerHitCounts = fixtureLayers.ToDictionary(l => l, _ => 0, StringComparer.OrdinalIgnoreCase);
            if (fixtureLayers.Count > 0)
            {
                List<DwgFixturePoint> layerPts = new List<DwgFixturePoint>();
                try { layerPts = new CADToModelEngine(doc).CaptureFixturePoints(import, fixtureLayers, includeLineClusters); }
                catch (Exception ex) { StingLog.Warn($"DwgFixtureBridge.CaptureFixturePoints: {ex.Message}"); }
                res.TotalLayerPoints = layerPts.Count;

                // Dedup against block insertions on the same layer (a fixture that is BOTH a
                // block and a point/cluster is the same symbol — the block wins).
                double dedupFt = UnitUtils.ConvertToInternalUnits(300.0, UnitTypeId.Millimeters);
                double dedup2 = dedupFt * dedupFt;
                var blockPts = captured.Where(c => c.Mode == "block")
                    .Select(c => (c.LayerName, c.Point)).ToList();

                foreach (var fp in layerPts)
                {
                    if (fp?.Point == null) continue;
                    string layer = fp.LayerName ?? "";
                    if (layerHitCounts.ContainsKey(layer)) layerHitCounts[layer]++;
                    bool dupOfBlock = blockPts.Any(bp =>
                        string.Equals(bp.LayerName, layer, StringComparison.OrdinalIgnoreCase)
                        && bp.Point != null && bp.Point.DistanceTo(fp.Point) * bp.Point.DistanceTo(fp.Point) <= dedup2);
                    if (dupOfBlock) continue;

                    var map = DwgSymbolMapRegistry.Resolve(doc, fp.BlockName, layer, fp.InferredCategory);
                    if (map == null || string.IsNullOrWhiteSpace(map.Category)) { res.SkippedNoMapping++; continue; }
                    string seedId = CategoryToSeedRegistry.Resolve(doc, map.Category);
                    if (string.IsNullOrWhiteSpace(seedId)) { res.SkippedSeedless++; continue; }
                    captured.Add(new Captured
                    {
                        Point = fp.Point, BlockName = fp.BlockName ?? "", LayerName = layer,
                        Category = map.Category, SeedId = seedId, Variant = map.VariantHint ?? "",
                        Anchor = map.Anchor, Mode = string.IsNullOrWhiteSpace(fp.CaptureMode) ? "point" : fp.CaptureMode
                    });
                }

                // Honest accounting: a mapped layer that produced nothing capturable.
                foreach (var kv in layerHitCounts.Where(k => k.Value == 0))
                {
                    res.SkippedExplodedNoPoint++;
                    res.Messages.Add($"Layer '{kv.Key}' is mapped but has no blocks/points to place from " +
                                     (includeLineClusters ? "(no Points and no clusterable lines)."
                                                          : "(exploded — enable experimental line-cluster capture, or add DWG Points)."));
                }
            }

            // Capture-mode histogram for the report.
            foreach (var grp in captured.GroupBy(c => c.Mode))
                res.CapturedByMode[grp.Key] = grp.Count();

            if (captured.Count == 0)
            {
                res.Messages.Add($"Nothing placeable: {res.TotalBlocks} block(s) + {res.TotalLayerPoints} layer point(s) " +
                                 $"({res.SkippedNoMapping} unmapped, {res.SkippedSeedless} seedless, {res.SkippedExplodedNoPoint} mapped-but-empty). " +
                                 "Use Library -> Map DWG layers to assign categories, or extend the DWG symbol map.");
                return res;
            }

            // ── 3) Ensure the mapped seeds are built/loaded — OUTSIDE any transaction. ──
            var categories = captured.Select(c => c.Category).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            try
            {
                var seedRes = SeedEnsurer.EnsureSeedsForCategories(doc, categories);
                res.Messages.Add($"Seeds ensured: {seedRes.SeedsBuiltOrLoaded} built/loaded for {categories.Count} categor(ies).");
            }
            catch (Exception ex)
            {
                StingLog.Error("DwgFixtureBridge.EnsureSeeds", ex);
                res.Messages.Add($"Seed build failed: {ex.Message}");
                return res;
            }

            if (dryRun)
            {
                foreach (var grp in captured.GroupBy(c => c.Category, StringComparer.OrdinalIgnoreCase))
                    res.PlacedByCategory[grp.Key] = grp.Count();
                res.Messages.Add($"DRY RUN — {captured.Count} fixture(s) would be placed across {categories.Count} categor(ies). No model changes made.");
                return res;
            }

            // ── 4) Place + stamp inside one transaction. ──
            var roomCache = CollectRooms(doc);
            using (var t = new Transaction(doc, "STING Place DWG Fixtures"))
            {
                t.Start();
                foreach (var c in captured)
                {
                    try
                    {
                        var symbol = ResolveSeedSymbol(doc, c.Category, c.SeedId, c.Variant, out string symNote);
                        if (symbol == null) { res.SkippedNoSymbol++; if (!string.IsNullOrEmpty(symNote)) res.Messages.Add(symNote); continue; }
                        if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }

                        var room = FindRoom(roomCache, c.Point);
                        var rule = new PlacementRule
                        {
                            RuleId = $"dwg:{c.SeedId}",
                            CategoryFilter = c.Category,
                            VariantHint = c.Variant,
                            AnchorType = string.IsNullOrWhiteSpace(c.Anchor) ? "WALL_MIDPOINT" : c.Anchor
                        };

                        var placed = PlacementHostPreflight.Place(doc, symbol, room, c.Point, rule);
                        if (placed?.Placed != null)
                        {
                            res.Placed++;
                            res.PlacedIds.Add(placed.Placed.Id);
                            res.PlacedByCategory[c.Category] =
                                (res.PlacedByCategory.TryGetValue(c.Category, out var n) ? n : 0) + 1;
                            // Provenance + the source DWG block/layer + capture mode (audit) — caller owns the tx.
                            try { StingProvenanceSchema.Stamp(placed.Placed, EngineName,
                                $"DWG:{c.BlockName}|{c.LayerName}|seed:{c.SeedId}|var:{c.Variant}|mode:{c.Mode}"); }
                            catch (Exception ex) { StingLog.Warn($"DwgFixtureBridge.Stamp: {ex.Message}"); }
                        }
                        else
                        {
                            res.SkippedNotHosted++;
                            if (!string.IsNullOrEmpty(placed?.Reason))
                                res.Messages.Add($"{c.BlockName} ({c.Category}): {placed.Reason}");
                        }
                    }
                    catch (Exception ex)
                    {
                        res.SkippedNotHosted++;
                        StingLog.Warn($"DwgFixtureBridge place {c.BlockName}: {ex.Message}");
                    }
                }
                t.Commit();
            }

            res.Messages.Add($"Placed {res.Placed} STING seed fixture(s) from {res.TotalBlocks} block(s). " +
                             "They are swap-ready — run Library → Swap to Manufacturer for real product geometry.");
            return res;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>Resolve the loaded seed FamilySymbol for a category + variant. Matches
        /// the seed family (STING_SEED_FAMILY_TXT marker, else family name) and the type by
        /// variant name (STING seeds name each type after its variant), else the default type.</summary>
        private static FamilySymbol ResolveSeedSymbol(Document doc, string category, string seedId, string variant, out string note)
        {
            note = "";
            BuiltInCategory bic = BuiltInCategory.INVALID;
            try { bic = FixturePlacementEngine.ResolveBuiltInCategoryByName(doc, category); } catch { }

            var collector = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol));
            if (bic != BuiltInCategory.INVALID) collector = collector.OfCategory(bic);
            var pool = collector.Cast<FamilySymbol>().ToList();

            var seedSyms = pool.Where(s => IsSeedSymbol(s, seedId)).ToList();
            if (seedSyms.Count == 0)
                seedSyms = pool.Where(s => string.Equals(s.Family?.Name, seedId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (seedSyms.Count == 0)
            {
                note = $"Seed '{seedId}' for {category} not loaded — skipped (re-run Rebuild Seeds).";
                return null;
            }

            FamilySymbol pick = null;
            if (!string.IsNullOrWhiteSpace(variant))
            {
                pick = seedSyms.FirstOrDefault(s => string.Equals(s.Name, variant, StringComparison.OrdinalIgnoreCase))
                    ?? seedSyms.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Name)
                            && s.Name.IndexOf(variant, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            return pick ?? seedSyms[0];   // default seed type
        }

        private static bool IsSeedSymbol(FamilySymbol s, string seedId)
        {
            try
            {
                var p = s?.LookupParameter("STING_SEED_FAMILY_TXT");
                return p != null && string.Equals(p.AsString(), seedId, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static List<SpatialElement> CollectRooms(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<SpatialElement>()
                    .Where(r => { try { return r.Area > 1e-6; } catch { return false; } })
                    .ToList();
            }
            catch { return new List<SpatialElement>(); }
        }

        private static SpatialElement FindRoom(List<SpatialElement> rooms, XYZ p)
        {
            if (rooms == null || p == null) return null;
            foreach (var r in rooms)
            {
                try { if (FixturePlacementEngine.PointInSpatial(r, p)) return r; } catch { }
            }
            return null; // best-effort: Place handles a null room (level-based / hosted)
        }
    }
}
