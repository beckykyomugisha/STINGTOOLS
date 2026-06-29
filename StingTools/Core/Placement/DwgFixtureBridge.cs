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
        public int TotalCaptured { get; set; }       // items that entered the place loop (post pre-pass)
        public int Placed { get; set; }
        public int SkippedNoMapping { get; set; }    // capture didn't map to a fixture category
        public int SkippedSeedless { get; set; }     // category has no seed (runs/structure)
        public int SkippedNoSymbol { get; set; }     // seed family/variant not built/loaded
        public int SkippedNotHosted { get; set; }    // PlacementHostPreflight returned Skipped
        public int SkippedExplodedNoPoint { get; set; } // layer mapped but nothing capturable
        public int DedupedAgainstBlock { get; set; } // layer point coincided with a block insert
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
                    if (dupOfBlock) { res.DedupedAgainstBlock++; continue; }

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

                // Honest accounting (D4): roll up the mapped-but-empty layers into ONE message.
                var emptyLayers = layerHitCounts.Where(k => k.Value == 0).Select(k => k.Key).ToList();
                res.SkippedExplodedNoPoint += emptyLayers.Count;
                if (emptyLayers.Count > 0)
                {
                    string how = includeLineClusters
                        ? "no Points and no clusterable lines"
                        : "exploded - enable experimental line-cluster capture, or add DWG Points";
                    res.Messages.Add($"{emptyLayers.Count} mapped layer(s) had nothing capturable ({how}): " +
                                     string.Join(", ", emptyLayers.Take(12)) + (emptyLayers.Count > 12 ? ", ..." : ""));
                }
            }

            // Capture-mode histogram for the report.
            foreach (var grp in captured.GroupBy(c => c.Mode))
                res.CapturedByMode[grp.Key] = grp.Count();

            // ── D2 — accounting invariant A: every DETECTED item is either captured (enters
            //    the place loop) or accounted for as no-mapping / seedless / deduped. ──
            res.TotalCaptured = captured.Count;
            int detected = res.TotalBlocks + res.TotalLayerPoints;
            int prePass = captured.Count + res.SkippedNoMapping + res.SkippedSeedless + res.DedupedAgainstBlock;
            if (detected != prePass)
                StingLog.Warn($"DwgFixtureBridge accounting drift (pre-pass): detected {detected} " +
                              $"(blocks {res.TotalBlocks} + layerPts {res.TotalLayerPoints}) != " +
                              $"captured {captured.Count} + noMap {res.SkippedNoMapping} + seedless {res.SkippedSeedless} + deduped {res.DedupedAgainstBlock} = {prePass}.");

            if (captured.Count == 0)
            {
                res.Messages.Add($"Nothing placeable: {res.TotalBlocks} block(s) + {res.TotalLayerPoints} layer point(s) " +
                                 $"({res.SkippedNoMapping} unmapped, {res.SkippedSeedless} seedless, {res.SkippedExplodedNoPoint} mapped-but-empty). " +
                                 "Use Library -> Map DWG layers to assign fixture categories, or extend the DWG symbol map.");
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

            // ── D3 — seed availability pre-check, ONCE per category (read-only). A category
            //    whose seed didn't build/load is dropped as a single aggregated skip, not
            //    retried + logged per instance. ──
            var placeable = new List<Captured>();
            foreach (var grp in captured.GroupBy(c => c.Category, StringComparer.OrdinalIgnoreCase))
            {
                var sample = grp.First();
                var probe = ResolveSeedSymbol(doc, grp.Key, sample.SeedId, "", out _);
                if (probe != null) { placeable.AddRange(grp); continue; }
                int n = grp.Count();
                res.SkippedNoSymbol += n;
                res.Messages.Add($"{grp.Key}: {n} x seed '{sample.SeedId}' not built/loaded - run Library -> Rebuild Seeds, then retry.");
            }

            if (placeable.Count == 0)
            {
                res.Messages.Add("No placeable fixtures after the seed check - see the seed messages above.");
                return res;
            }

            if (dryRun)
            {
                foreach (var grp in placeable.GroupBy(c => c.Category, StringComparer.OrdinalIgnoreCase))
                    res.PlacedByCategory[grp.Key] = grp.Count();
                res.Messages.Add($"DRY RUN - {placeable.Count} fixture(s) would be placed across {res.PlacedByCategory.Count} categor(ies). No model changes made.");
                CheckPlaceInvariant(res, dryRun: true);
                return res;
            }

            // ── 4) Place + stamp inside one transaction. ──
            var roomCache = CollectRooms(doc);
            var notHostedByReason = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);   // D4 rollup
            using (var t = new Transaction(doc, "STING Place DWG Fixtures"))
            {
                t.Start();
                foreach (var c in placeable)
                {
                    try
                    {
                        var symbol = ResolveSeedSymbol(doc, c.Category, c.SeedId, c.Variant, out _);
                        // Pre-checked above; if the specific type still won't resolve, count (no spam).
                        if (symbol == null) { res.SkippedNoSymbol++; continue; }
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
                            string reason = string.IsNullOrEmpty(placed?.Reason) ? "no host found" : placed.Reason;
                            string key = $"{c.Category}: {reason}";
                            notHostedByReason[key] = (notHostedByReason.TryGetValue(key, out var nh) ? nh : 0) + 1;
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

            // D4 — roll up not-hosted skips by (category: reason), one line each.
            foreach (var kv in notHostedByReason)
                res.Messages.Add($"{kv.Value} x not hosted - {kv.Key}.");

            res.Messages.Add($"Placed {res.Placed} STING seed fixture(s) from {res.TotalCaptured} captured " +
                             $"({res.TotalBlocks} block insert(s) + {res.TotalLayerPoints} layer point(s)). " +
                             "Swap-ready - run Library -> Swap to Manufacturer for real product geometry.");
            CheckPlaceInvariant(res, dryRun: false);
            return res;
        }

        /// <summary>D2 — whole-run invariant: every CAPTURED item (those that entered the
        /// place loop) ends up either placed or accounted for as a no-seed or not-hosted
        /// skip. For a dry run only the seed pre-check has run (no placement / not-hosted),
        /// so the dry-run "would place" count + no-seed skips must equal the captured total.</summary>
        private static void CheckPlaceInvariant(DwgFixtureBridgeResult res, bool dryRun)
        {
            int placedOrWouldPlace = dryRun ? res.PlacedByCategory.Values.Sum() : res.Placed;
            int accounted = placedOrWouldPlace + res.SkippedNoSymbol + res.SkippedNotHosted;
            if (accounted != res.TotalCaptured)
                StingLog.Warn($"DwgFixtureBridge accounting drift (captured): captured {res.TotalCaptured} != " +
                              $"{(dryRun ? "wouldPlace" : "placed")} {placedOrWouldPlace} + noSymbol {res.SkippedNoSymbol} + notHosted {res.SkippedNotHosted} = {accounted}.");
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
