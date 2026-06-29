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
        public int TotalBlocks { get; set; }
        public int Placed { get; set; }
        public int SkippedNoMapping { get; set; }   // block didn't map to a STING category
        public int SkippedSeedless { get; set; }     // category has no seed (runs/structure)
        public int SkippedNoSymbol { get; set; }     // seed family/variant not resolvable
        public int SkippedNotHosted { get; set; }    // PlacementHostPreflight returned Skipped
        public bool DryRun { get; set; }
        public Dictionary<string, int> PlacedByCategory { get; } =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public List<string> Messages { get; } = new List<string>();
        public List<ElementId> PlacedIds { get; } = new List<ElementId>();
    }

    public static class DwgFixtureBridge
    {
        private const string EngineName = "DwgFixtureBridge";

        // A captured fixture block resolved to its STING target (read-only pre-pass).
        private sealed class Captured
        {
            public XYZ Point;
            public string BlockName = "";
            public string LayerName = "";
            public string Category = "";
            public string SeedId = "";
            public string Variant = "";
            public string Anchor = "WALL_MIDPOINT";
        }

        /// <summary>Pick the (first / only, else selected) DWG import and run the bridge.
        /// Returns a result even when nothing is found (with a message).</summary>
        public static DwgFixtureBridgeResult PlaceFromFirstImport(Document doc, bool dryRun)
        {
            var res = new DwgFixtureBridgeResult { DryRun = dryRun };
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
            return PlaceFromImport(doc, import, dryRun);
        }

        /// <summary>Capture fixture blocks from <paramref name="import"/>, map each to a
        /// STING seed, ensure the seeds are built, then place + stamp them at the block
        /// points. Caller must NOT have an open transaction (seed build opens its own).</summary>
        public static DwgFixtureBridgeResult PlaceFromImport(Document doc, ImportInstance import, bool dryRun)
        {
            var res = new DwgFixtureBridgeResult { DryRun = dryRun };
            if (doc == null || import == null) { res.Messages.Add("No document / import."); return res; }

            // ── 1) Capture blocks (read-only) — REUSE the DWG geometry engine. ──
            List<DetectedBlock> blocks;
            try
            {
                var extraction = new CADToModelEngine(doc).PreviewImport(import);
                blocks = extraction?.Blocks ?? new List<DetectedBlock>();
            }
            catch (Exception ex)
            {
                StingLog.Error("DwgFixtureBridge.PreviewImport", ex);
                res.Messages.Add($"Could not read the DWG import: {ex.Message}");
                return res;
            }
            res.TotalBlocks = blocks.Count;
            if (blocks.Count == 0)
            {
                res.Messages.Add("No MEP/fixture blocks detected in the import (only runs/lines, or an empty DWG).");
                return res;
            }

            // ── 2) Resolve each block → category → seed (read-only pre-pass). ──
            var captured = new List<Captured>();
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
                    Category = map.Category, SeedId = seedId, Variant = map.VariantHint ?? "", Anchor = map.Anchor
                });
            }
            if (captured.Count == 0)
            {
                res.Messages.Add($"None of the {blocks.Count} block(s) mapped to a seeded STING category " +
                                 $"({res.SkippedNoMapping} unmapped, {res.SkippedSeedless} seedless). " +
                                 "Extend Data/Placement/DWG_SYMBOL_MAP.json (or the project override).");
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
                            // Provenance + the source DWG block/layer (audit) — caller owns the tx.
                            try { StingProvenanceSchema.Stamp(placed.Placed, EngineName,
                                $"DWG:{c.BlockName}|{c.LayerName}|seed:{c.SeedId}|var:{c.Variant}"); }
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
