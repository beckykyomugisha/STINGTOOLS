// StingTools — Matrix placement engine (M1 backbone + M2 exact-count).
//
// Turns the declared grid (room-type x element-type -> count) into placed, hosted,
// swap-ready STING seed instances by REUSING the foundation exactly as DwgFixtureBridge
// does — SeedEnsurer (build/load seeds outside a tx), MatrixGridDistributor (exactly N
// even points), PlacementHostPreflight (host + place at each point), StingProvenanceSchema
// (audit + idempotency marker). One synthetic PlacementRule per (room, column); one
// Transaction for the whole run (single undo).
//
// Idempotency: every placed instance's UniqueId is recorded in the MatrixDocument ledger
// keyed (roomUid -> colId). A re-run SKIPS a (room, column) whose ledgered ids still
// resolve, unless replace-mode deletes them first. This makes Matrix Place safely
// re-runnable — the same acceptance-grade behaviour the prompt requires.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StingTools.Core.Storage;

namespace StingTools.Core.Placement.Matrix
{
    public sealed class MatrixCellOutcome
    {
        public string RoomUniqueId = "";
        public string RoomName = "";
        public string ColumnId = "";
        public int Requested;
        public int Placed;
        public int Skipped;          // already populated (idempotent) — not an error
        public string Note = "";
        public List<string> PlacedUniqueIds = new List<string>();
    }

    public sealed class MatrixPlaceResult
    {
        public bool DryRun;
        public int TotalPlaced;
        public int TotalRequested;
        public int RoomsTouched;
        public int CellsSkippedIdempotent;
        public int SeedsBuiltOrLoaded;
        public List<MatrixCellOutcome> Cells = new List<MatrixCellOutcome>();
        public List<string> Messages = new List<string>();
        public List<ElementId> PlacedIds = new List<ElementId>();
    }

    public static class MatrixPlacementEngine
    {
        private const string EngineName = "MatrixPlace";
        private const double MmToFt = 1.0 / 304.8;

        /// <summary>Place the whole matrix. <paramref name="matrix"/>'s ledger is updated in place
        /// (persist it after a non-dry run). <paramref name="scan"/> supplies the live rooms grouped
        /// by type. replace=true deletes previously matrix-placed instances for a (room, column)
        /// before re-placing; false skips already-populated cells (idempotent).</summary>
        public static MatrixPlaceResult Place(
            Document doc, MatrixDocument matrix, MatrixScanResult scan,
            bool replace, bool dryRun, Action<string> log = null)
        {
            var res = new MatrixPlaceResult { DryRun = dryRun };
            if (doc == null || matrix == null || scan == null)
            { res.Messages.Add("No document / matrix / scan."); return res; }

            var columns = (matrix.Columns ?? new List<MatrixColumnDef>())
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Category)).ToList();
            if (columns.Count == 0) { res.Messages.Add("No element-type columns defined."); return res; }

            // ── 1) Ensure seeds for all columns' categories — OUTSIDE any transaction. ──
            var categories = columns.Select(c => c.Category).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            try
            {
                var se = SeedEnsurer.EnsureSeedsForCategories(doc, categories);
                res.SeedsBuiltOrLoaded = se?.SeedsBuiltOrLoaded ?? 0;
                res.Messages.Add($"Seeds ensured: {res.SeedsBuiltOrLoaded} built/loaded for {categories.Count} categor(ies).");
            }
            catch (Exception ex)
            {
                StingLog.Error("MatrixPlacementEngine.EnsureSeeds", ex);
                res.Messages.Add($"Seed build failed: {ex.Message}");
                return res;
            }

            // ── 2) Build the work list (read-only): per column, per placeable member room. ──
            var typeByKey = scan.Types.ToDictionary(t => t.Key, t => t, StringComparer.OrdinalIgnoreCase);
            var work = new List<(MatrixColumnDef col, MatrixRoom room, int count)>();
            foreach (var col in columns)
            {
                foreach (var t in matrix.RoomTypes ?? new List<MatrixTypeCounts>())
                {
                    if (!typeByKey.TryGetValue(t.Key, out var scanType)) continue;
                    foreach (var room in scanType.Rooms.Where(r => !r.IsLinked))
                    {
                        int n = t.CountFor(room.UniqueId, col.Id);
                        if (n <= 0) continue;
                        work.Add((col, room, n));
                    }
                }
            }
            if (work.Count == 0) { res.Messages.Add("Nothing to place — all cell counts are zero."); return res; }
            res.TotalRequested = work.Sum(w => w.count);

            if (dryRun)
            {
                foreach (var w in work)
                {
                    var oc = new MatrixCellOutcome
                    {
                        RoomUniqueId = w.room.UniqueId, RoomName = w.room.Name,
                        ColumnId = w.col.Id, Requested = w.count
                    };
                    bool already = !replace && LedgerAlive(doc, matrix, w.room.UniqueId, w.col.Id);
                    if (already) { oc.Skipped = w.count; oc.Note = "already populated"; res.CellsSkippedIdempotent++; }
                    else oc.Placed = w.count;  // would place
                    res.Cells.Add(oc);
                }
                res.TotalPlaced = res.Cells.Sum(c => c.Placed);
                res.Messages.Add($"DRY RUN — would place {res.TotalPlaced} instance(s) across {res.Cells.Count(c => c.Placed > 0)} cell(s). No model changes.");
                return res;
            }

            // ── 3) Place inside ONE transaction (single undo). ──
            var symbolCache = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
            var touchedRooms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var t = new Transaction(doc, "STING Matrix Place"))
            {
                t.Start();
                foreach (var w in work)
                {
                    var oc = new MatrixCellOutcome
                    {
                        RoomUniqueId = w.room.UniqueId, RoomName = w.room.Name,
                        ColumnId = w.col.Id, Requested = w.count
                    };
                    try { PlaceCell(doc, matrix, w.col, w.room, w.count, replace, symbolCache, oc, res); }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"MatrixPlace cell {w.col.Id}/{w.room.Name}: {ex.Message}");
                        oc.Note = $"error: {ex.Message}";
                    }
                    res.Cells.Add(oc);
                    if (oc.Placed > 0) touchedRooms.Add(w.room.UniqueId);
                    log?.Invoke($"{w.room.Name}: {w.col.DisplayLabel()} -> {oc.Placed}/{oc.Requested}");
                }
                t.Commit();
            }

            res.TotalPlaced = res.Cells.Sum(c => c.Placed);
            res.RoomsTouched = touchedRooms.Count;
            res.CellsSkippedIdempotent = res.Cells.Count(c => c.Skipped > 0 && c.Placed == 0);
            res.Messages.Add($"Placed {res.TotalPlaced} of {res.TotalRequested} requested instance(s) across {res.RoomsTouched} room(s). " +
                             "Swap-ready — run Library -> Swap to Manufacturer for real product geometry.");
            return res;
        }

        private static void PlaceCell(
            Document doc, MatrixDocument matrix, MatrixColumnDef col, MatrixRoom room, int count,
            bool replace, Dictionary<string, FamilySymbol> symbolCache, MatrixCellOutcome oc, MatrixPlaceResult res)
        {
            // Idempotency: skip / replace existing ledgered instances.
            var alive = LedgerAliveIds(doc, matrix, room.UniqueId, col.Id);
            if (alive.Count > 0)
            {
                if (!replace) { oc.Skipped = count; oc.Note = $"already populated ({alive.Count})"; return; }
                foreach (var id in alive) { try { doc.Delete(id); } catch { } }
                matrix.ClearLedger(room.UniqueId, col.Id);
            }

            // Resolve the seed symbol for this column's category + variant.
            var symbol = ResolveSymbol(doc, col.Category, col.Variant, symbolCache);
            if (symbol == null) { oc.Note = $"seed for '{col.Category}' not built/loaded"; return; }
            if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }

            // Anchor + points.
            string anchor = string.IsNullOrWhiteSpace(col.Anchor)
                ? MatrixDefaults.DefaultAnchor(col.Category, col.AutoGrid) : col.Anchor;
            double heightMm = col.MountingHeightMm > 0 ? col.MountingHeightMm : ResolveDefaultHeight(doc, col.Category);
            double anchorZ = ResolveAnchorZ(room, anchor, heightMm);
            double minSpacing = 300.0;   // sensible intra-cell floor; wall/ceiling grids widen naturally

            DistributionResult dist;
            bool ceilingLike = anchor.Equals("LIGHTING_GRID", StringComparison.OrdinalIgnoreCase)
                            || anchor.Equals("CEILING_CENTRE", StringComparison.OrdinalIgnoreCase)
                            || anchor.Equals("ROOM_CENTRE", StringComparison.OrdinalIgnoreCase);
            if (ceilingLike)
                dist = MatrixGridDistributor.EvenGrid(room.Element, count, minSpacing, wallClearanceMm: 300.0, anchorZ);
            else
                dist = MatrixGridDistributor.WallRun(room.Element, count, minSpacing, insetMm: 100.0, anchorZ);

            if (!string.IsNullOrEmpty(dist.Note)) oc.Note = dist.Note;
            if (dist.Points.Count == 0) { if (string.IsNullOrEmpty(oc.Note)) oc.Note = "no placeable points"; return; }

            var rule = new PlacementRule
            {
                RuleId = $"matrix:{col.Id}:{col.Category}",
                CategoryFilter = col.Category,
                VariantHint = col.Variant ?? "",
                AnchorType = anchor,
                MountingReference = ceilingLike && !anchor.Equals("ROOM_CENTRE", StringComparison.OrdinalIgnoreCase)
                    ? "CEILING" : "FFL",
                MountingHeightMm = heightMm > 0 ? heightMm : 300.0,
                HeightStandard = col.HeightStandard ?? "",
                MinSpacingMm = minSpacing
            };

            foreach (var p in dist.Points)
            {
                try
                {
                    var placed = PlacementHostPreflight.Place(doc, symbol, room.Element, p, rule);
                    if (placed?.Placed != null)
                    {
                        oc.Placed++;
                        res.PlacedIds.Add(placed.Placed.Id);
                        string uid = SafeUid(placed.Placed);
                        if (!string.IsNullOrEmpty(uid)) oc.PlacedUniqueIds.Add(uid);
                        if (heightMm > 0) TrySetMntHgt(placed.Placed, heightMm);
                        try { StingProvenanceSchema.Stamp(placed.Placed, EngineName,
                            $"col:{col.Id}|{col.Category}|var:{col.Variant}|room:{room.UniqueId}"); } catch { }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"MatrixPlace point {col.Category}: {ex.Message}"); }
            }

            // Record the ledger for idempotent re-runs.
            matrix.SetLedger(room.UniqueId, col.Id, oc.PlacedUniqueIds);
            if (oc.Placed < count && string.IsNullOrEmpty(oc.Note))
                oc.Note = $"placed {oc.Placed} of {count}";
        }

        // ── seed symbol resolution (mirrors DwgFixtureBridge.ResolveSeedSymbol) ─────
        private static FamilySymbol ResolveSymbol(
            Document doc, string category, string variant, Dictionary<string, FamilySymbol> cache)
        {
            string key = $"{category}|{variant}";
            if (cache.TryGetValue(key, out var cached)) return cached;

            FamilySymbol pick = null;
            try
            {
                string seedId = CategoryToSeedRegistry.Resolve(doc, category);
                BuiltInCategory bic = BuiltInCategory.INVALID;
                try { bic = FixturePlacementEngine.ResolveBuiltInCategoryByName(doc, category); } catch { }

                var collector = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol));
                if (bic != BuiltInCategory.INVALID) collector = collector.OfCategory(bic);
                var pool = collector.Cast<FamilySymbol>().ToList();

                // Prefer the STING seed family, else any family named seedId, else any in-category
                // symbol (lets a project that has a real family for the category use it).
                List<FamilySymbol> candidates = pool.Where(s => IsSeed(s, seedId)).ToList();
                if (candidates.Count == 0 && !string.IsNullOrEmpty(seedId))
                    candidates = pool.Where(s => string.Equals(s.Family?.Name, seedId, StringComparison.OrdinalIgnoreCase)).ToList();
                if (candidates.Count == 0) candidates = pool;
                if (candidates.Count == 0) { cache[key] = null; return null; }

                if (!string.IsNullOrWhiteSpace(variant))
                {
                    pick = candidates.FirstOrDefault(s => string.Equals(s.Name, variant, StringComparison.OrdinalIgnoreCase))
                        ?? candidates.FirstOrDefault(s => VariantParam(s)?.Equals(variant, StringComparison.OrdinalIgnoreCase) == true)
                        ?? candidates.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Name)
                                && s.Name.IndexOf(variant, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                pick = pick ?? candidates[0];
            }
            catch (Exception ex) { StingLog.Warn($"MatrixPlacementEngine.ResolveSymbol '{category}': {ex.Message}"); }
            cache[key] = pick;
            return pick;
        }

        private static bool IsSeed(FamilySymbol s, string seedId)
        {
            if (string.IsNullOrEmpty(seedId)) return false;
            try { return string.Equals(s?.LookupParameter("STING_SEED_FAMILY_TXT")?.AsString(), seedId, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        private static string VariantParam(FamilySymbol s)
        { try { return s?.LookupParameter("STING_FIXTURE_VARIANT_TXT")?.AsString(); } catch { return null; } }

        // ── height + Z helpers ─────────────────────────────────────────────
        private static double ResolveDefaultHeight(Document doc, string category)
        {
            try { return CategoryHeightDefaults.Resolve(doc, category)?.MountingHeightMm ?? 0.0; }
            catch { return 0.0; }
        }

        private static double ResolveAnchorZ(MatrixRoom room, string anchor, double heightMm)
        {
            double levelZ = room.LevelElevationFt;
            bool ceiling = anchor.Equals("LIGHTING_GRID", StringComparison.OrdinalIgnoreCase)
                        || anchor.Equals("CEILING_CENTRE", StringComparison.OrdinalIgnoreCase);
            if (ceiling)
            {
                try
                {
                    var bb = room.Element.get_BoundingBox(null);
                    if (bb != null) return bb.Max.Z;   // room top ~ ceiling soffit; hosted families snap
                }
                catch { }
                return levelZ + 2700.0 * MmToFt;        // fallback ceiling height
            }
            return levelZ + Math.Max(0, heightMm) * MmToFt;   // FFL + mounting height (wall / equipment)
        }

        private static void TrySetMntHgt(Element el, double valueMm)
        {
            try
            {
                var p = el?.LookupParameter("MNT_HGT_MM");
                if (p == null || p.IsReadOnly) return;
                switch (p.StorageType)
                {
                    case StorageType.Double: p.Set(valueMm * MmToFt); break;
                    case StorageType.String: p.Set(valueMm.ToString("F1")); break;
                    case StorageType.Integer: p.Set((int)Math.Round(valueMm)); break;
                }
            }
            catch { }
        }

        // ── idempotency ledger ─────────────────────────────────────────────
        private static bool LedgerAlive(Document doc, MatrixDocument matrix, string roomUid, string colId)
            => LedgerAliveIds(doc, matrix, roomUid, colId).Count > 0;

        private static List<ElementId> LedgerAliveIds(Document doc, MatrixDocument matrix, string roomUid, string colId)
        {
            var alive = new List<ElementId>();
            foreach (var uid in matrix.LedgerFor(roomUid, colId))
            {
                try { var el = doc.GetElement(uid); if (el != null) alive.Add(el.Id); } catch { }
            }
            return alive;
        }

        private static string SafeUid(Element el) { try { return el?.UniqueId ?? ""; } catch { return ""; } }
    }
}
