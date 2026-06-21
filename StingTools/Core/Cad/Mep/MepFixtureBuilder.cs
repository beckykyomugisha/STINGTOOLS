// ============================================================================
// MepFixtureBuilder.cs — Phase: MEP-from-DWG V1.
//
// Places MEP fixtures from a MepDetectionResult. For each detected block it
// resolves a FamilySymbol (by category + optional family/type hint), activates
// it, and places an unhosted/level-based instance at the block insertion point
// with the block rotation and a mounting-height-driven Z. When no symbol
// resolves the fixture is SKIPPED and counted — no synthetic geometry is ever
// created (mirrors FixturePlacementEngine's resolve-or-skip contract).
//
// Placed instances are workset-assigned and ISO 19650 auto-tagged via the same
// path native Placement-Center output uses, so they flow into tagging / BOQ /
// validation unchanged.
//
// V2 adds best-effort host-snapping: wall-mount categories snap to the nearest
// wall, ceiling-referenced fixtures to the nearest ceiling — but ONLY when the
// family is a hosted/work-plane type; any failure falls back to unhosted on the
// level (never throws away the fixture).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StingTools.Core;
using StingTools.Core.Content;   // ContentResolver, ContentRequest, MissingContent
using StingTools.Model;   // Units, ModelWorksetAssigner, ModelEngine.AutoTagCreatedElements

namespace StingTools.Core.Cad.Mep
{
    public class MepBuildResult
    {
        public int Placed { get; set; }
        public int SkippedNoSymbol { get; set; }
        public int Failed { get; set; }
        public List<ElementId> CreatedIds { get; } = new List<ElementId>();
        public List<string> Warnings { get; } = new List<string>();
        /// <summary>Families that could not be resolved (in-project or library) — the
        /// structured detail behind <see cref="SkippedNoSymbol"/>, surfaced to the
        /// command for reporting instead of a silent count.</summary>
        public List<MissingContent> Missing { get; } = new List<MissingContent>();
        /// <summary>Per-category placed / skipped-no-symbol counts (audit).</summary>
        public Dictionary<string, (int placed, int skipped)> ByCategory { get; }
            = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);
        public int Tagged { get; set; }
        /// <summary>V2 — how many of the placed fixtures were snapped to a wall/ceiling host.</summary>
        public int Hosted { get; set; }

        public void Bump(string cat, bool placed)
        {
            ByCategory.TryGetValue(cat ?? "", out var v);
            ByCategory[cat ?? ""] = placed ? (v.placed + 1, v.skipped) : (v.placed, v.skipped + 1);
        }
    }

    public class MepFixtureBuilder
    {
        private readonly Document _doc;
        // PC-Content — unified content resolution (in-project → on-disk library → miss),
        // replacing the old per-builder category index. Built once per Place() call;
        // owns its own symbol cache and surfaced MissingContent list.
        private ContentResolver _resolver;
        // P6-3 — wall-mount category set from the (data-driven) run rules; loaded per Place().
        private HashSet<string> _wallMount;

        public MepFixtureBuilder(Document doc) => _doc = doc ?? throw new ArgumentNullException(nameof(doc));

        /// <summary>Place all detected fixtures on the target level. One transaction.
        /// <paramref name="hostSnap"/> enables best-effort wall/ceiling hosting.</summary>
        public MepBuildResult Place(MepDetectionResult detection, Level level, bool hostSnap = true)
        {
            var result = new MepBuildResult();
            if (detection == null || detection.Fixtures.Count == 0 || level == null) return result;

            // Unified content resolution. DWG import may need a family the project
            // doesn't contain yet; the resolver pulls it from the content library
            // (firm/project/baseline roots) and records misses instead of skipping silently.
            _resolver = new ContentResolver(_doc, ContentManifestRegistry.Get(_doc));
            _wallMount = MepRunRulesRegistry.Get(_doc).WallMountSet();

            // Pre-collect potential hosts once. P6-2.2 — walls go into a coarse XY grid
            // (one cell = the snap tolerance) so each fixture queries only its cell ±1
            // instead of scanning every wall; the nearest-within-tol result is identical.
            // Ceilings stay a linear scan — a floor has a handful, not hundreds.
            WallGrid walls = hostSnap ? new WallGrid(WallTolFt, CollectWalls()) : null;
            List<(Element c, BoundingBoxXYZ bb)> ceilings = hostSnap ? CollectCeilings() : null;

            using (var tx = new Transaction(_doc, "STING MODEL: Place MEP Fixtures from DWG"))
            {
                tx.Start();
                try
                {
                    int i = 0;
                    foreach (var fx in detection.Fixtures)
                    {
                        i++;
                        var symbol = ResolveSymbol(fx.Rule, result);
                        if (symbol == null)
                        {
                            result.SkippedNoSymbol++;
                            result.Bump(fx.Category, placed: false);
                            continue;
                        }

                        try
                        {
                            if (!symbol.IsActive) { symbol.Activate(); _doc.Regenerate(); }

                            var bp = fx.Block.InsertionPoint;
                            var p = new XYZ(bp.X, bp.Y, level.Elevation + StingTools.Model.Units.Mm(fx.MountingHeightMm));

                            // V2 — best-effort host-snapping; falls back to unhosted.
                            var inst = PlaceWithHost(fx, symbol, level, p, hostSnap, walls, ceilings, out bool wasHosted);
                            if (wasHosted) result.Hosted++;

                            // Rotation is only applied to unhosted point-based instances;
                            // wall-hosted instances take their orientation from the host.
                            if (!wasHosted && Math.Abs(fx.Block.Rotation) > 1e-6)
                            {
                                var axis = Line.CreateBound(p, p + XYZ.BasisZ);
                                ElementTransformUtils.RotateElement(_doc, inst.Id, axis, fx.Block.Rotation);
                            }

                            ModelWorksetAssigner.Assign(_doc, inst);
                            StampMetadata(inst, fx);

                            result.CreatedIds.Add(inst.Id);
                            result.Placed++;
                            result.Bump(fx.Category, placed: true);
                        }
                        catch (Exception ex)
                        {
                            result.Failed++;
                            if (result.Warnings.Count < 30)
                                result.Warnings.Add($"'{fx.BlockName}' ({fx.Category}): {ex.Message}");
                        }

                        if (MepBatch.ShouldCancel(i, result.Warnings)) break;
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    StingLog.Error("MepFixtureBuilder.Place", ex);
                    result.Warnings.Add($"Placement batch failed (rolled back): {ex.Message}");
                    result.CreatedIds.Clear();
                    return result;
                }
            }

            // ISO 19650 auto-tag — same path native Placement-Center output uses,
            // so DWG-placed fixtures flow into tagging / BOQ / validation unchanged.
            MepBatch.AutoTag(_doc, result.CreatedIds, n => result.Tagged = n);

            // Surface which families could not be resolved (closes the silent-skip gap).
            if (_resolver?.Missing != null && _resolver.Missing.Count > 0)
                result.Missing.AddRange(_resolver.Missing);
            return result;
        }

        /// <summary>Resolve a FamilySymbol for a rule: collector by category, narrowed
        /// by optional family/type hint regex; first match wins, else first-for-category.
        /// Returns null (→ skip) when nothing in the project matches — never synthesises.</summary>
        /// <summary>Resolve a FamilySymbol for a rule via the shared ContentResolver:
        /// in-project first, then the on-disk content library. AllowBuild stays off so a
        /// DWG import never synthesises families mid-run. Returns null (→ skip; the miss
        /// is recorded on the resolver and surfaced in <see cref="MepBuildResult.Missing"/>)
        /// when nothing resolves.</summary>
        private FamilySymbol ResolveSymbol(MepFixtureRule rule, MepBuildResult result)
        {
            if (rule == null || string.IsNullOrEmpty(rule.Category)) return null;
            var res = _resolver.Resolve(_doc, new ContentRequest
            {
                Category   = rule.Category,
                FamilyHint = rule.FamilyHint,
                TypeHint   = rule.TypeHint,
                AllowLoad  = true,
                AllowBuild = false,
            });
            if (res.IsFallback && result.Warnings.Count < 30)
                result.Warnings.Add($"'{rule.Category}': no family/type-hint match - used first available symbol.");
            return res.Symbol;
        }

        // ── V2 host-snapping ─────────────────────────────────────────────────
        private enum HostIntent { None, Wall, Ceiling }
        private static readonly double WallTolFt = StingTools.Model.Units.Mm(700.0);     // snap to a wall within ~700 mm
        private static readonly double CeilingFallbackFt = StingTools.Model.Units.Mm(1500.0);

        private static readonly HashSet<string> WallMount = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Electrical Fixtures", "Data Devices", "Communication Devices",
            "Security Devices", "Fire Alarm Devices", "Nurse Call Devices"
        };

        private HostIntent HostIntentFor(MepDetectedFixture fx)
        {
            if (string.Equals(fx.Rule?.MountingReference, "Ceiling", StringComparison.OrdinalIgnoreCase))
                return HostIntent.Ceiling;
            var set = _wallMount ?? WallMount;
            if (set.Contains(fx.Category)) return HostIntent.Wall;
            return HostIntent.None;
        }

        // Only families that can actually take a host (wall-hosted / work-plane / face-based).
        private static bool IsHostable(FamilySymbol s)
        {
            var t = s?.Family?.FamilyPlacementType;
            return t == FamilyPlacementType.OneLevelBasedHosted || t == FamilyPlacementType.WorkPlaneBased;
        }

        // ── P3.1 preview honesty (public, side-effect-free) ──────────────────
        public static bool FamilyIsHostable(FamilySymbol s) => IsHostable(s);

        /// <summary>Host intent ("Wall"/"Ceiling"/"None") from a category + mounting reference,
        /// so the preview can warn which fixtures will host vs be forced unhosted.</summary>
        public static string HostIntentName(string category, string mountingReference)
        {
            if (string.Equals(mountingReference, "Ceiling", StringComparison.OrdinalIgnoreCase)) return "Ceiling";
            if (WallMount.Contains(category ?? "")) return "Wall";
            return "None";
        }

        /// <summary>Resolve the FamilySymbol a rule would place — read-only, no cache, no
        /// warnings — for the preview's placement-type check. Mirrors ResolveSymbol's
        /// category + family/type-hint matching; first match wins, else first-for-category.</summary>
        public static FamilySymbol PreviewResolveSymbol(Document doc, string category, string familyHint, string typeHint)
        {
            if (doc == null || string.IsNullOrEmpty(category)) return null;
            Regex famRx = SafeRx(familyHint), typeRx = SafeRx(typeHint);
            FamilySymbol picked = null, first = null;
            try
            {
                foreach (var fs in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>())
                {
                    if (fs.Category == null ||
                        !string.Equals(fs.Category.Name, category, StringComparison.OrdinalIgnoreCase)) continue;
                    if (first == null) first = fs;
                    if (famRx != null && !famRx.IsMatch(fs.Family?.Name ?? "")) continue;
                    if (typeRx != null && !typeRx.IsMatch(fs.Name ?? "")) continue;
                    picked = fs; break;
                }
            }
            catch (Exception ex) { StingLog.Warn($"PreviewResolveSymbol '{category}': {ex.Message}"); }
            return picked ?? first;
        }

        private static Regex SafeRx(string p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            try { return new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); } catch { return null; }
        }

        /// <summary>Place hosted when possible, else unhosted on the level. Never throws
        /// the fixture away — any hosting failure falls back to the level-based instance.
        /// TODO-VERIFY-API: the host-overload behaviour (NewFamilyInstance(pt, symbol, host,
        /// level, NonStructural)) varies by family placement type — verify in Revit.</summary>
        private FamilyInstance PlaceWithHost(MepDetectedFixture fx, FamilySymbol symbol, Level level, XYZ p,
            bool hostSnap, WallGrid walls, List<(Element c, BoundingBoxXYZ bb)> ceilings, out bool hosted)
        {
            hosted = false;
            if (hostSnap && IsHostable(symbol))
            {
                try
                {
                    var intent = HostIntentFor(fx);
                    if (intent == HostIntent.Wall && walls != null)
                    {
                        var w = NearestWall(walls, p, WallTolFt, out XYZ proj);
                        if (w != null)
                        {
                            var inst = _doc.Create.NewFamilyInstance(proj, symbol, w, level, StructuralType.NonStructural);
                            hosted = true; return inst;
                        }
                    }
                    else if (intent == HostIntent.Ceiling && ceilings != null)
                    {
                        var c = NearestCeiling(ceilings, p);
                        if (c != null)
                        {
                            var inst = _doc.Create.NewFamilyInstance(p, symbol, c, level, StructuralType.NonStructural);
                            hosted = true; return inst;
                        }
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Host-snap fallback for '{fx.BlockName}': {ex.Message}");
                    hosted = false;
                }
            }
            return _doc.Create.NewFamilyInstance(p, symbol, level, StructuralType.NonStructural);
        }

        private List<(Wall w, Line ln)> CollectWalls()
        {
            var list = new List<(Wall, Line)>();
            foreach (Wall w in new FilteredElementCollector(_doc).OfClass(typeof(Wall)).Cast<Wall>())
                if (w.Location is LocationCurve lc && lc.Curve is Line ln) list.Add((w, ln));
            return list;
        }

        private List<(Element c, BoundingBoxXYZ bb)> CollectCeilings()
        {
            var list = new List<(Element, BoundingBoxXYZ)>();
            foreach (var c in new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_Ceilings).WhereElementIsNotElementType())
            {
                var bb = c.get_BoundingBox(null);
                if (bb != null) list.Add((c, bb));
            }
            return list;
        }

        private static Wall NearestWall(WallGrid walls, XYZ p, double tolFt, out XYZ proj)
        {
            Wall best = null; double bestD = tolFt; proj = p;
            foreach (var (w, ln) in walls.Near(p))   // grid candidates cover the tol radius
            {
                var cp = MepGeom.ClosestPointOnSegment(ln.GetEndPoint(0), ln.GetEndPoint(1), p, out _, out double d, planar: true);
                if (d < bestD) { bestD = d; best = w; proj = new XYZ(cp.X, cp.Y, p.Z); }
            }
            return best;
        }

        // P6-2.2 — coarse XY grid over wall segments (cell = snap tolerance). A wall whose
        // nearest point is within tol of p is registered in a cell within ±1 of p's cell, so
        // querying p's 3×3 neighbourhood yields the same nearest-within-tol wall as a full scan.
        private sealed class WallGrid
        {
            private readonly double _cell;
            private readonly Dictionary<(int, int), List<(Wall w, Line ln)>> _cells
                = new Dictionary<(int, int), List<(Wall, Line)>>();

            public WallGrid(double cellFt, List<(Wall w, Line ln)> walls)
            {
                _cell = cellFt > 1e-6 ? cellFt : 1.0;
                foreach (var item in walls ?? new List<(Wall, Line)>())
                {
                    XYZ a = item.ln.GetEndPoint(0), b = item.ln.GetEndPoint(1);
                    int x0 = C(Math.Min(a.X, b.X)), x1 = C(Math.Max(a.X, b.X));
                    int y0 = C(Math.Min(a.Y, b.Y)), y1 = C(Math.Max(a.Y, b.Y));
                    for (int x = x0; x <= x1; x++)
                        for (int y = y0; y <= y1; y++)
                        {
                            var k = (x, y);
                            if (!_cells.TryGetValue(k, out var l)) _cells[k] = l = new List<(Wall, Line)>();
                            l.Add(item);
                        }
                }
            }

            private int C(double v) => (int)Math.Floor(v / _cell);

            public IEnumerable<(Wall w, Line ln)> Near(XYZ p)
            {
                int cx = C(p.X), cy = C(p.Y);
                var seen = new HashSet<ElementId>();
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (_cells.TryGetValue((cx + dx, cy + dy), out var l))
                            foreach (var item in l)
                                if (seen.Add(item.w.Id)) yield return item;
            }
        }

        private static Element NearestCeiling(List<(Element c, BoundingBoxXYZ bb)> ceilings, XYZ p)
        {
            Element nearest = null; double bestD = double.MaxValue;
            foreach (var (c, bb) in ceilings)
            {
                if (p.X >= bb.Min.X && p.X <= bb.Max.X && p.Y >= bb.Min.Y && p.Y <= bb.Max.Y) return c; // contained
                var ctr = new XYZ((bb.Min.X + bb.Max.X) / 2, (bb.Min.Y + bb.Max.Y) / 2, p.Z);
                double d = ctr.DistanceTo(new XYZ(p.X, p.Y, p.Z));
                if (d < bestD) { bestD = d; nearest = c; }
            }
            return bestD <= CeilingFallbackFt ? nearest : null;
        }

        /// <summary>Stamp mounting metadata so DWG-placed fixtures match native
        /// Placement-Center output. The mounting height is already encoded in the
        /// instance elevation (Z = level + height). The reference (FFL/Ceiling/
        /// Structure) is stamped to a text param when bound (graceful no-op).
        /// TODO-VERIFY-API: wire the native numeric mounting-height param (MNT_HGT_MM)
        /// in V2 once its exact name + unit (Length vs Number) is confirmed against
        /// the Placement-Center output, to avoid a unit-mismatch write here.</summary>
        private void StampMetadata(Element inst, MepDetectedFixture fx)
        {
            try
            {
                if (!string.IsNullOrEmpty(fx.Rule?.MountingReference))
                    ParameterHelpers.SetString(inst, "MOUNTING_REFERENCE_TXT", fx.Rule.MountingReference, overwrite: true);
            }
            catch (Exception ex) { StingLog.Warn($"MEP fixture stamp {inst?.Id}: {ex.Message}"); }
        }
    }
}
