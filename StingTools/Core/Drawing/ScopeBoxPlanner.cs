// StingTools — Scope-box planner · from "these drawing types" to named boxes
//
//   ticked drawing types ──► size classes      one per (paper, scale): the box must fit every type in it
//   seeds in the project ──► one seed per class the LARGEST seed that still fits (turned 90° if that fits better)
//   footprints          ──► tiles              overlapping boxes covering each footprint, in the grid's frame
//   tiles               ──► names              STING-AREA::[<loc>-]<class>-<nn>[::<level>]
//
// A box can only be as big as its seed, because Revit cannot resize a scope box.
// So the tile size is the seed's size, not the class's maximum. A class with no
// seed that fits is still planned — at the largest size that WOULD fit — so the
// person sees how many boxes it needs and exactly what size to draw; those rows
// are marked NoSeed and nothing is created for them.
//
// Units are metres in plan (X east, Y north). The Revit layer converts.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace StingTools.Core.Drawing
{
    /// <summary>A region to cover: a building (STING-LOC box) or the model, optionally on one level.</summary>
    public sealed class ScopeBoxFootprint
    {
        /// <summary>Building code from a STING-LOC box, or null for "the whole model".</summary>
        public string Loc { get; set; }
        /// <summary>Level code when footprints are planned per level; null serves every level.</summary>
        public string Level { get; set; }
        /// <summary>Plan points (m) the boxes must cover — element extents, or a LOC box's corners.</summary>
        public List<(double X, double Y)> Points { get; set; } = new List<(double X, double Y)>();
        /// <summary>
        /// The footprint's own frame, radians anticlockwise from X — set for a turned
        /// STING-LOC box, so its boxes are laid square to the building rather than to the
        /// grid, and its true corners (not its larger bounding box) are covered (DTW-92).
        /// Null follows the request's grid angle.
        /// </summary>
        public double? AngleRad { get; set; }
    }

    /// <summary>A hand-drawn seed box, measured.</summary>
    public sealed class ScopeBoxSeed
    {
        /// <summary>Opaque id the Revit layer uses to find the element again.</summary>
        public string Id { get; set; }
        public string Name { get; set; }
        public double WidthM { get; set; }
        public double DepthM { get; set; }
        public string Label => ScopeBoxNames.Metres(WidthM) + " × " + ScopeBoxNames.Metres(DepthM) + " m";
    }

    public sealed class ScopeBoxSizeClass
    {
        public string Key { get; set; }
        public int Scale { get; set; }
        public string Paper { get; set; }
        /// <summary>Largest extent every type in the class accepts.</summary>
        public double MaxWidthM { get; set; }
        public double MaxDepthM { get; set; }
        public List<string> DrawingTypeIds { get; set; } = new List<string>();
        /// <summary>Discipline codes of the member types — used to colour and to explain.</summary>
        public List<string> Disciplines { get; set; } = new List<string>();
        public ScopeBoxSeed Seed { get; set; }
        /// <summary>True when the seed is placed turned 90°.</summary>
        public bool SeedRotated { get; set; }
        /// <summary>The tile size actually used: the seed as placed, or the class maximum when there is no seed.</summary>
        public double TileWidthM { get; set; }
        public double TileDepthM { get; set; }
    }

    public enum PlannedBoxStatus
    {
        /// <summary>Not in the model yet; will be copied from its seed.</summary>
        New,
        /// <summary>In the model, where and as big as planned — left alone.</summary>
        Exists,
        /// <summary>In the model at the planned size but in the wrong place or turned — will be moved.</summary>
        Moved,
        /// <summary>In the model at a different size. A scope box cannot be resized, so it is reported, not touched.</summary>
        Mismatch,
        /// <summary>No seed fits its size class; nothing can be created.</summary>
        NoSeed,
    }

    /// <summary>A scope box already in the model, measured in its own frame.</summary>
    public sealed class ExistingScopeBox
    {
        public double CentreX { get; set; }
        public double CentreY { get; set; }
        public double WidthM { get; set; }
        public double DepthM { get; set; }
        public double AngleRad { get; set; }
    }

    public sealed class PlannedScopeBox
    {
        public string Name { get; set; }
        public string AreaCode { get; set; }
        public string ClassKey { get; set; }
        public string Loc { get; set; }
        public string Level { get; set; }
        public double CentreX { get; set; }
        public double CentreY { get; set; }
        public double WidthM { get; set; }
        public double DepthM { get; set; }
        /// <summary>Rotation of the box about its centre, radians, anticlockwise from X.</summary>
        public double AngleRad { get; set; }
        public string SeedId { get; set; }
        public bool SeedRotated { get; set; }
        public PlannedBoxStatus Status { get; set; }
        /// <summary>Why the status is what it is — shown next to Moved and Mismatch rows.</summary>
        public string StatusNote { get; set; }
        /// <summary>For Moved / Mismatch: the box as it is in the model now.</summary>
        public ExistingScopeBox Existing { get; set; }
        /// <summary>For Moved: the turn (radians, anticlockwise) to apply about the planned centre after moving.</summary>
        public double RotateBy { get; set; }
        public int Row { get; set; }
        public int Column { get; set; }
    }

    public sealed class ScopeBoxPlanRequest
    {
        public List<DrawingType> DrawingTypes { get; set; } = new List<DrawingType>();
        public IReadOnlyDictionary<string, (double W, double H)> Drawables { get; set; }
        public List<ScopeBoxFootprint> Footprints { get; set; } = new List<ScopeBoxFootprint>();
        public List<ScopeBoxSeed> Seeds { get; set; } = new List<ScopeBoxSeed>();
        /// <summary>Scope-box names already in the project; a planned name found here is not created again.</summary>
        public ISet<string> ExistingNames { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// Where those boxes actually are. A name alone says nothing about position or size,
        /// so without this a re-plan would keep an old box in the old place and call it done.
        /// A name in <see cref="ExistingNames"/> but not here could not be measured and is
        /// treated as Exists.
        /// </summary>
        public IDictionary<string, ExistingScopeBox> ExistingBoxes { get; set; } = new Dictionary<string, ExistingScopeBox>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Above this many boxes the plan is flagged, so one stray element miles away cannot quietly make thousands.</summary>
        public int MaxBoxes { get; set; } = DefaultMaxBoxes;
        public const int DefaultMaxBoxes = 200;
        public double FitFactor { get; set; } = ScopeBoxSizing.DefaultFitFactor;
        /// <summary>Overlap between neighbouring boxes — where match lines go.</summary>
        public double OverlapM { get; set; } = 2.0;
        /// <summary>Clearance added round each footprint so walls on the edge are not cropped.</summary>
        public double PaddingM { get; set; } = 1.0;
        /// <summary>Angle of the project grid, radians. Boxes are laid out square to it.</summary>
        public double GridAngleRad { get; set; }
    }

    public sealed class ScopeBoxMissingSeed
    {
        public string ClassKey { get; set; }
        public double MaxWidthM { get; set; }
        public double MaxDepthM { get; set; }
        public string Instruction =>
            $"Draw a scope box no larger than {ScopeBoxNames.Metres(Math.Floor(MaxWidthM))} × "
          + $"{ScopeBoxNames.Metres(Math.Floor(MaxDepthM))} m (either way round), tall enough to span every "
          + $"level, then register it as a seed. It serves the {ClassKey} drawings.";
    }

    public sealed class ScopeBoxPlanResult
    {
        public List<ScopeBoxSizeClass> Classes { get; } = new List<ScopeBoxSizeClass>();
        public List<PlannedScopeBox> Boxes { get; } = new List<PlannedScopeBox>();
        public List<ScopeBoxMissingSeed> MissingSeeds { get; } = new List<ScopeBoxMissingSeed>();
        public List<string> Warnings { get; } = new List<string>();
        public int CountToCreate => Boxes.Count(b => b.Status == PlannedBoxStatus.New);
        public int CountToMove => Boxes.Count(b => b.Status == PlannedBoxStatus.Moved);
        /// <summary>More boxes than the request's cap: the caller must confirm before creating.</summary>
        public bool ExceedsCap { get; set; }
    }

    public static class ScopeBoxPlanner
    {
        private const double Eps = 1e-6;

        public static ScopeBoxPlanResult Plan(ScopeBoxPlanRequest req)
        {
            var result = new ScopeBoxPlanResult();
            if (req == null) { result.Warnings.Add("No request."); return result; }
            if (req.OverlapM < 0) { result.Warnings.Add("Overlap cannot be negative."); return result; }

            // 1. Size classes.
            foreach (var dt in req.DrawingTypes ?? new List<DrawingType>())
            {
                if (!ScopeBoxSizing.TryMaxExtent(dt, req.Drawables, req.FitFactor, out var w, out var d, out var why))
                {
                    result.Warnings.Add($"{dt?.Id}: not planned — {why}.");
                    continue;
                }
                var key = ScopeBoxSizing.SizeClassKey(dt);
                var cls = result.Classes.FirstOrDefault(c => c.Key == key);
                if (cls == null)
                {
                    cls = new ScopeBoxSizeClass
                    {
                        Key = key, Paper = dt.PaperSize, Scale = ScopeBoxSizing.MainSlot(dt)?.Scale ?? dt.Scale,
                        MaxWidthM = w, MaxDepthM = d,
                    };
                    result.Classes.Add(cls);
                }
                cls.MaxWidthM = Math.Min(cls.MaxWidthM, w);
                cls.MaxDepthM = Math.Min(cls.MaxDepthM, d);
                cls.DrawingTypeIds.Add(dt.Id);
                if (!string.IsNullOrWhiteSpace(dt.Discipline) && !cls.Disciplines.Contains(dt.Discipline))
                    cls.Disciplines.Add(dt.Discipline);
            }
            result.Classes.Sort((a, b) => a.Scale != b.Scale ? a.Scale.CompareTo(b.Scale) : string.CompareOrdinal(a.Key, b.Key));

            // 2. One seed per class.
            foreach (var cls in result.Classes)
            {
                var pick = PickSeed(req.Seeds, cls.MaxWidthM, cls.MaxDepthM);
                if (pick.Seed != null)
                {
                    cls.Seed = pick.Seed; cls.SeedRotated = pick.Rotated;
                    cls.TileWidthM = pick.Rotated ? pick.Seed.DepthM : pick.Seed.WidthM;
                    cls.TileDepthM = pick.Rotated ? pick.Seed.WidthM : pick.Seed.DepthM;
                }
                else
                {
                    cls.TileWidthM = Math.Floor(cls.MaxWidthM);
                    cls.TileDepthM = Math.Floor(cls.MaxDepthM);
                    result.MissingSeeds.Add(new ScopeBoxMissingSeed
                        { ClassKey = cls.Key, MaxWidthM = cls.MaxWidthM, MaxDepthM = cls.MaxDepthM });
                }
                if (cls.TileWidthM <= req.OverlapM + Eps || cls.TileDepthM <= req.OverlapM + Eps)
                    result.Warnings.Add($"{cls.Key}: a {ScopeBoxNames.Metres(cls.TileWidthM)} × "
                        + $"{ScopeBoxNames.Metres(cls.TileDepthM)} m box cannot overlap by {ScopeBoxNames.Metres(req.OverlapM)} m — class skipped.");
            }

            // 3. Tiles per footprint per class.
            foreach (var fp in req.Footprints ?? new List<ScopeBoxFootprint>())
            {
                if (fp?.Points == null || fp.Points.Count == 0)
                {
                    result.Warnings.Add($"Footprint {fp?.Loc ?? "(model)"}{(fp?.Level != null ? " at " + fp.Level : "")} has no geometry — skipped.");
                    continue;
                }
                // DTW-92: a turned building box is tiled in its own frame.
                double frame = fp.AngleRad ?? req.GridAngleRad;
                foreach (var cls in result.Classes)
                {
                    if (cls.TileWidthM <= req.OverlapM + Eps || cls.TileDepthM <= req.OverlapM + Eps) continue;
                    string groupPrefix = (string.IsNullOrWhiteSpace(fp.Loc) ? "" : fp.Loc + "-") + cls.Key + "-";
                    foreach (var (tile, code) in LayoutGroup(req, fp, frame, cls, groupPrefix, result.Warnings))
                    {
                        string name = ScopeBoxNames.ComposeArea(code, fp.Level);
                        if (string.IsNullOrEmpty(name))
                        {
                            result.Warnings.Add($"'{code}' / '{fp.Level}' cannot form a legal area name — skipped.");
                            continue;
                        }
                        var box = new PlannedScopeBox
                        {
                            Name = name, AreaCode = code, ClassKey = cls.Key, Loc = fp.Loc, Level = fp.Level,
                            CentreX = tile.X, CentreY = tile.Y, WidthM = cls.TileWidthM, DepthM = cls.TileDepthM,
                            AngleRad = frame, SeedId = cls.Seed?.Id, SeedRotated = cls.SeedRotated,
                            Row = tile.Row, Column = tile.Column,
                        };
                        Judge(box, req, cls.Seed != null);
                        result.Boxes.Add(box);
                    }
                }
            }

            var dupes = result.Boxes.GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            foreach (var n in dupes)
                result.Warnings.Add($"'{n}' is planned twice — two footprints share a building code and level. "
                    + "Give each STING-LOC box its own code, and each level its own level code.");
            if (req.MaxBoxes > 0 && result.Boxes.Count > req.MaxBoxes)
            {
                result.ExceedsCap = true;
                result.Warnings.Add($"{result.Boxes.Count} boxes planned — more than {req.MaxBoxes}. The footprint may include "
                    + "something far from the building (a stray line, a site element); check it before creating.");
            }
            return result;
        }

        /// <summary>A box of one planning group already in the model: its name, number and where it is.</summary>
        private sealed class GroupBox
        {
            public string Name; public int Index; public ExistingScopeBox Box; public double U, V;
        }

        /// <summary>
        /// The tiles of one (footprint, size class) group and the area code each is named by.
        ///
        /// DTW-91: tiles used to be numbered 1.. row by row over a run centred on the
        /// footprint, and matched to the model by name alone. When the model grew, the run
        /// re-centred and renumbered, so every existing box was judged "Moved" onto other
        /// ground — silently changing what its views, sheets and match lines showed.
        ///
        /// Now, when boxes of this group are already in the model:
        ///   • the run is laid on their lattice (same pitch, anchored on the lowest-numbered
        ///     box that is the tile size and square to the frame), so they stay where they are;
        ///   • each tile takes the name of the existing box whose centre lies inside it
        ///     (nearest first) — position before name;
        ///   • tiles with no existing box get numbers after the group's highest, skipping any
        ///     name already in the model;
        ///   • an existing box no tile covers is named in a warning — it is left in the model
        ///     and dropped from the plan, never moved onto another tile.
        /// With no existing boxes the layout and numbering are exactly as before.
        /// </summary>
        private static List<(ScopeBoxTile Tile, string Code)> LayoutGroup(ScopeBoxPlanRequest req, ScopeBoxFootprint fp,
            double frame, ScopeBoxSizeClass cls, string groupPrefix, List<string> warnings)
        {
            double w = cls.TileWidthM, d = cls.TileDepthM, c = Math.Cos(frame), s = Math.Sin(frame);
            string level = string.IsNullOrWhiteSpace(fp.Level) ? null : fp.Level;

            // Boxes of this group in the model (measured), and the highest number any name of
            // the group already uses (measured or not).
            var group = new List<GroupBox>();
            int maxIndex = 0;
            var names = new HashSet<string>(req.ExistingNames ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
            if (req.ExistingBoxes != null) foreach (var k in req.ExistingBoxes.Keys) names.Add(k);
            foreach (var n in names)
            {
                if (!TryGroupIndex(n, groupPrefix, level, out int idx)) continue;
                maxIndex = Math.Max(maxIndex, idx);
                if (req.ExistingBoxes != null && req.ExistingBoxes.TryGetValue(n, out var ex) && ex != null)
                    group.Add(new GroupBox { Name = n, Index = idx, Box = ex,
                        U = ex.CentreX * c + ex.CentreY * s, V = -ex.CentreX * s + ex.CentreY * c });
            }

            string Code(int i) => groupPrefix + i.ToString("D2", CultureInfo.InvariantCulture);
            // Nothing measured to anchor on: the original layout. A name in the model that
            // could not be measured still matches by name in Judge ("left as it is").
            if (group.Count == 0)
                return Tile(fp.Points, frame, w, d, req.OverlapM, req.PaddingM).Select(t => (t, Code(t.Index))).ToList();

            var anchor = group.Where(g => FitsTile(g.Box, w, d, frame)).OrderBy(g => g.Index).FirstOrDefault();
            var tiles = anchor == null
                ? Tile(fp.Points, frame, w, d, req.OverlapM, req.PaddingM)
                : TileOnLattice(fp.Points, frame, w, d, req.OverlapM, req.PaddingM, anchor.U, anchor.V);

            // Position before name: each existing box goes to the tile that contains its
            // centre, nearest pairs first, one box per tile.
            var pairs = new List<(int T, GroupBox G, double Dist)>();
            for (int t = 0; t < tiles.Count; t++)
            {
                double tu = tiles[t].X * c + tiles[t].Y * s, tv = -tiles[t].X * s + tiles[t].Y * c;
                foreach (var g in group)
                {
                    double du = g.U - tu, dv = g.V - tv;
                    if (Math.Abs(du) <= w / 2 + Eps && Math.Abs(dv) <= d / 2 + Eps)
                        pairs.Add((t, g, Math.Sqrt(du * du + dv * dv)));
                }
            }
            var codes = new string[tiles.Count];
            var used = new HashSet<GroupBox>();
            foreach (var p in pairs.OrderBy(p => p.Dist).ThenBy(p => p.G.Index))
            {
                if (codes[p.T] != null || used.Contains(p.G)) continue;
                codes[p.T] = Code(p.G.Index);
                used.Add(p.G);
            }
            int next = maxIndex;
            for (int t = 0; t < tiles.Count; t++)
            {
                if (codes[t] != null) continue;
                string code;
                do
                {
                    next++;
                    code = Code(next);
                    var clash = ScopeBoxNames.ComposeArea(code, level);
                    if (!names.Contains(clash)) break;
                    warnings.Add($"'{clash}' is already in the model but not where a box of this layout goes — the new box is numbered past it.");
                } while (true);
                codes[t] = code;
            }
            foreach (var g in group.Where(g => !used.Contains(g)).OrderBy(g => g.Index))
                warnings.Add($"'{g.Name}' is not covered by the new layout — it is left in the model where it is, "
                           + "dropped from the saved plan, and not moved onto another box's ground. Delete it if it is no longer wanted.");
            return tiles.Select((t, i) => (t, codes[i])).ToList();
        }

        /// <summary>The number of an area name in a planning group (same building + size class prefix, same level).</summary>
        private static bool TryGroupIndex(string name, string groupPrefix, string level, out int index)
        {
            index = 0;
            if (!ScopeBoxNames.TryParseArea(name, out var area, out var lvl, out _)) return false;
            if (!string.Equals(lvl ?? "", level ?? "", StringComparison.OrdinalIgnoreCase)) return false;
            if (!area.StartsWith(groupPrefix, StringComparison.OrdinalIgnoreCase)) return false;
            return int.TryParse(area.Substring(groupPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out index) && index > 0;
        }

        /// <summary>The box is the tile size (either way round) and square to the frame (mod a quarter turn).</summary>
        private static bool FitsTile(ExistingScopeBox ex, double w, double d, double frame)
        {
            bool same = Math.Abs(ex.WidthM - w) < SizeToleranceM && Math.Abs(ex.DepthM - d) < SizeToleranceM;
            bool crossed = Math.Abs(ex.WidthM - d) < SizeToleranceM && Math.Abs(ex.DepthM - w) < SizeToleranceM;
            double turn = NormaliseHalfTurn(ex.AngleRad - frame);
            double rest = turn - Math.Round(turn / (Math.PI / 2)) * Math.PI / 2;
            if (Math.Abs(rest) > AngleToleranceRad) return false;
            bool quarter = ((int)Math.Round(turn / (Math.PI / 2)) & 1) != 0;
            return quarter ? crossed : same;
        }

        /// <summary>
        /// Cover the points with tiles on the lattice through (<paramref name="anchorU"/>,
        /// <paramref name="anchorV"/>) in the frame, at the overlap pitch: the fewest lattice
        /// tiles whose union covers the padded extent. Rows top first, columns left to right;
        /// <see cref="ScopeBoxTile.Index"/> is 1-based in that order.
        /// </summary>
        public static List<ScopeBoxTile> TileOnLattice(IList<(double X, double Y)> points, double angleRad,
            double boxW, double boxD, double overlap, double padding, double anchorU, double anchorV)
        {
            var tiles = new List<ScopeBoxTile>();
            if (points == null || points.Count == 0) return tiles;
            if (boxW <= overlap || boxD <= overlap) throw new ArgumentException("box must be larger than overlap");
            double c = Math.Cos(angleRad), s = Math.Sin(angleRad);
            double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
            foreach (var p in points)
            {
                double u = p.X * c + p.Y * s, v = -p.X * s + p.Y * c;
                minU = Math.Min(minU, u); maxU = Math.Max(maxU, u);
                minV = Math.Min(minV, v); maxV = Math.Max(maxV, v);
            }
            minU -= padding; maxU += padding; minV -= padding; maxV += padding;
            double stepU = boxW - overlap, stepV = boxD - overlap;
            // Leftmost tile whose left edge still reaches minU; rightmost whose right edge reaches maxU.
            int i0 = (int)Math.Floor((minU + boxW / 2 - anchorU) / stepU + Eps);
            int i1 = (int)Math.Ceiling((maxU - boxW / 2 - anchorU) / stepU - Eps);
            int j0 = (int)Math.Floor((minV + boxD / 2 - anchorV) / stepV + Eps);
            int j1 = (int)Math.Ceiling((maxV - boxD / 2 - anchorV) / stepV - Eps);
            if (i1 < i0) i1 = i0;
            if (j1 < j0) j1 = j0;
            int index = 0;
            for (int j = j1, row = 0; j >= j0; j--, row++)                // top row first
                for (int i = i0, col = 0; i <= i1; i++, col++)
                {
                    double u = anchorU + i * stepU, v = anchorV + j * stepV;
                    tiles.Add(new ScopeBoxTile(u * c - v * s, u * s + v * c, row, col, ++index));
                }
            return tiles;
        }

        /// <summary>Distance and angle within which an existing box counts as "where planned".</summary>
        public const double PositionToleranceM = 0.05, SizeToleranceM = 0.05, AngleToleranceRad = 0.2 * Math.PI / 180;

        /// <summary>
        /// Decide what to do with a planned box, comparing it with any box of the same name
        /// already in the model. A scope box can be moved and turned, never resized.
        ///
        /// A measured box's angle is only known modulo a quarter turn — whichever edge is read
        /// first sets it — so the difference is folded into ±45°, and folding across a quarter
        /// turn swaps the measured sides. After that the comparison is exact: sides equal and
        /// no turn left means the same rectangle; sides equal but swapped means it needs a
        /// quarter turn; otherwise a partial turn. <see cref="PlannedScopeBox.RotateBy"/> is
        /// the turn a move must apply about the planned centre.
        /// </summary>
        public static void Judge(PlannedScopeBox box, ScopeBoxPlanRequest req, bool hasSeed)
        {
            if (req.ExistingBoxes != null && req.ExistingBoxes.TryGetValue(box.Name, out var ex) && ex != null)
            {
                box.Existing = ex;
                double turn = NormaliseHalfTurn(ex.AngleRad - box.AngleRad);
                int quarters = (int)Math.Round(turn / (Math.PI / 2));
                double rest = turn - quarters * Math.PI / 2;                          // within ±45°
                bool swap = (quarters & 1) != 0;
                double w = swap ? ex.DepthM : ex.WidthM, d = swap ? ex.WidthM : ex.DepthM;
                bool same = Math.Abs(w - box.WidthM) < SizeToleranceM && Math.Abs(d - box.DepthM) < SizeToleranceM;
                bool crossed = Math.Abs(w - box.DepthM) < SizeToleranceM && Math.Abs(d - box.WidthM) < SizeToleranceM;
                if (!same && !crossed)
                {
                    box.Status = PlannedBoxStatus.Mismatch;
                    box.StatusNote = $"in the model at {ScopeBoxNames.Metres(ex.WidthM)} × {ScopeBoxNames.Metres(ex.DepthM)} m, planned "
                        + $"{ScopeBoxNames.Metres(box.WidthM)} × {ScopeBoxNames.Metres(box.DepthM)} m — a scope box cannot be resized; delete it and create again";
                    return;
                }
                // Turn needed to bring the model box onto the plan: undo the leftover angle, and a
                // quarter turn more when only the crossed orientation matches.
                double rotate = -rest + (same ? 0 : Math.PI / 2);
                double off = Math.Sqrt((ex.CentreX - box.CentreX) * (ex.CentreX - box.CentreX) + (ex.CentreY - box.CentreY) * (ex.CentreY - box.CentreY));
                bool turned = Math.Abs(NormaliseHalfTurn(rotate)) > AngleToleranceRad && Math.Abs(Math.Abs(NormaliseHalfTurn(rotate)) - Math.PI) > AngleToleranceRad;
                if (off < PositionToleranceM && !turned) { box.Status = PlannedBoxStatus.Exists; return; }
                box.Status = PlannedBoxStatus.Moved;
                box.RotateBy = turned ? rotate : 0;
                box.StatusNote = turned
                    ? $"{ScopeBoxNames.Metres(off)} m away and turned {ScopeBoxNames.Metres(rotate * 180 / Math.PI)}° — will be moved and turned into place"
                    : $"{ScopeBoxNames.Metres(off)} m from its planned place — will be moved";
                return;
            }
            if (req.ExistingNames != null && req.ExistingNames.Contains(box.Name))
            {
                box.Status = PlannedBoxStatus.Exists;
                box.StatusNote = "in the model but could not be measured — left as it is";
                return;
            }
            box.Status = hasSeed ? PlannedBoxStatus.New : PlannedBoxStatus.NoSeed;
        }

        /// <summary>An angle folded into (-π, π].</summary>
        public static double NormaliseHalfTurn(double a)
        {
            a %= 2 * Math.PI;
            if (a > Math.PI) a -= 2 * Math.PI;
            if (a <= -Math.PI) a += 2 * Math.PI;
            return a;
        }

        /// <summary>
        /// A level code per level, unique. GetLevelCodeForLevel maps "Level 1" and
        /// "Level 1 SSL" both to L01; two levels sharing a code would share one area box
        /// name, and the second level would get no boxes. Later duplicates get "-2", "-3"…
        /// in the order given (lowest level first when the caller sorts by elevation).
        /// </summary>
        public static Dictionary<long, string> UniqueLevelCodes(IEnumerable<(long Id, string Code)> levels)
        {
            var result = new Dictionary<long, string>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, raw) in levels ?? Enumerable.Empty<(long, string)>())
            {
                var code = string.IsNullOrWhiteSpace(raw) ? "XX" : raw.Trim();
                var candidate = code;
                for (int n = 2; used.Contains(candidate); n++) candidate = code + "-" + n.ToString(CultureInfo.InvariantCulture);
                used.Add(candidate);
                result[id] = candidate;
            }
            return result;
        }

        /// <summary>
        /// Level codes that stay with their level (DTW-90). Codes come from level NAMES, and
        /// area boxes (STING-AREA::…::L01) and the saved plan (levels ["L01"]) are keyed by
        /// code — so renaming a level, or inserting one whose name reads the same code lower
        /// down (the "-2" de-duplication went by elevation), moved the code to another level
        /// and orphaned every box and plan entry that used it.
        ///
        /// The saved plan now records each code's level UniqueId (<paramref name="savedCodeToUid"/>).
        /// A code bound to a level still in the project stays on that level whatever its name
        /// now reads; every other level gets its name's code, de-duplicated with "-2", "-3"…
        /// in the order given (lowest first). Each case where the name and the bound code
        /// disagree is reported in <paramref name="notes"/>. With no saved ids (a plan from
        /// before this, or none) the result is exactly <see cref="UniqueLevelCodes"/>.
        /// </summary>
        /// <param name="levels">(element id, UniqueId, name, code read from the name), lowest level first.</param>
        public static Dictionary<long, string> StableLevelCodes(
            IEnumerable<(long Id, string UniqueId, string Name, string Code)> levels,
            IDictionary<string, string> savedCodeToUid, List<string> notes)
        {
            var list = (levels ?? Enumerable.Empty<(long, string, string, string)>()).ToList();
            var result = new Dictionary<long, string>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var savedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string Read(string raw) => string.IsNullOrWhiteSpace(raw) ? "XX" : raw.Trim();

            foreach (var kv in (savedCodeToUid ?? new Dictionary<string, string>())
                         .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                         .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                var code = kv.Key.Trim();
                var lvl = list.FirstOrDefault(l => string.Equals(l.UniqueId, kv.Value, StringComparison.OrdinalIgnoreCase));
                if (lvl.UniqueId == null)
                {
                    notes?.Add($"The level the saved plan records as '{code}' is no longer in the project; '{code}' goes to whichever level's name reads it now.");
                    continue;
                }
                if (result.ContainsKey(lvl.Id) || used.Contains(code)) continue;
                result[lvl.Id] = code;
                used.Add(code);
                savedCodes.Add(code);
                var reads = Read(lvl.Code);
                if (!string.Equals(reads, code, StringComparison.OrdinalIgnoreCase))
                    notes?.Add($"Level '{lvl.Name}' now reads '{reads}' from its name but keeps '{code}', the code its scope boxes and saved plan use.");
            }

            foreach (var l in list)
            {
                if (result.ContainsKey(l.Id)) continue;
                var code = Read(l.Code);
                var candidate = code;
                for (int n = 2; used.Contains(candidate); n++) candidate = code + "-" + n.ToString(CultureInfo.InvariantCulture);
                used.Add(candidate);
                result[l.Id] = candidate;
                if (savedCodes.Contains(code))
                    notes?.Add($"Level '{l.Name}' reads '{code}', but the saved plan binds '{code}' to another level; this level is '{candidate}'.");
            }
            return result;
        }

        /// <summary>
        /// The largest seed that fits inside maxW × maxD, either way round. Ties go to the
        /// unrotated placement, then to the seed listed first, so the choice is stable.
        /// </summary>
        public static (ScopeBoxSeed Seed, bool Rotated) PickSeed(IEnumerable<ScopeBoxSeed> seeds, double maxW, double maxD)
        {
            ScopeBoxSeed best = null; bool bestRot = false; double bestArea = 0;
            foreach (var s in seeds ?? Enumerable.Empty<ScopeBoxSeed>())
            {
                if (s == null || s.WidthM <= 0 || s.DepthM <= 0) continue;
                double area = s.WidthM * s.DepthM;
                if (area <= bestArea + Eps) continue;
                if (s.WidthM <= maxW + Eps && s.DepthM <= maxD + Eps) { best = s; bestRot = false; bestArea = area; }
                else if (s.DepthM <= maxW + Eps && s.WidthM <= maxD + Eps) { best = s; bestRot = true; bestArea = area; }
            }
            return (best, bestRot);
        }

        public readonly struct ScopeBoxTile
        {
            public ScopeBoxTile(double x, double y, int row, int col, int index) { X = x; Y = y; Row = row; Column = col; Index = index; }
            public double X { get; }
            public double Y { get; }
            public int Row { get; }
            public int Column { get; }
            /// <summary>1-based, numbered left to right then top to bottom — the order drawings are read.</summary>
            public int Index { get; }
        }

        /// <summary>How many boxes of size <paramref name="box"/> overlapping by <paramref name="overlap"/> cover <paramref name="span"/>.</summary>
        public static int CountAlong(double span, double box, double overlap)
        {
            if (box <= overlap) throw new ArgumentException("box must be larger than overlap");
            if (span <= box + Eps) return 1;
            return (int)Math.Ceiling((span - overlap) / (box - overlap) - Eps);
        }

        /// <summary>
        /// Cover the points with overlapping boxes laid square to the grid. The run of boxes
        /// is centred on the footprint, so any spare length is shared equally at both ends.
        /// </summary>
        public static List<ScopeBoxTile> Tile(IList<(double X, double Y)> points, double angleRad,
            double boxW, double boxD, double overlap, double padding)
        {
            var tiles = new List<ScopeBoxTile>();
            if (points == null || points.Count == 0) return tiles;
            double c = Math.Cos(angleRad), s = Math.Sin(angleRad);
            double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
            foreach (var p in points)
            {
                double u = p.X * c + p.Y * s, v = -p.X * s + p.Y * c;   // into the grid's frame
                minU = Math.Min(minU, u); maxU = Math.Max(maxU, u);
                minV = Math.Min(minV, v); maxV = Math.Max(maxV, v);
            }
            minU -= padding; maxU += padding; minV -= padding; maxV += padding;
            double spanU = maxU - minU, spanV = maxV - minV;
            int nu = CountAlong(spanU, boxW, overlap), nv = CountAlong(spanV, boxD, overlap);
            double stepU = boxW - overlap, stepV = boxD - overlap;
            double firstU = (minU + maxU) / 2 - (nu - 1) * stepU / 2;
            double firstV = (minV + maxV) / 2 + (nv - 1) * stepV / 2;   // top row first
            int index = 0;
            for (int row = 0; row < nv; row++)
                for (int col = 0; col < nu; col++)
                {
                    double u = firstU + col * stepU, v = firstV - row * stepV;
                    tiles.Add(new ScopeBoxTile(u * c - v * s, u * s + v * c, row, col, ++index));   // back to plan
                }
            return tiles;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  The saved plan — _BIM_COORD/scope_box_plan.json
    //
    //  The only record of which drawing types an area box serves. A box's name
    //  says what it IS (an area, a size class); the plan says what it is FOR.
    //  Production reads it; a box the plan does not list is reported, not guessed.
    // ─────────────────────────────────────────────────────────────────────

    public sealed class ScopeBoxPlanFile
    {
        // Schema 2: each box carries its own drawing types and levels. Schema 1 kept them on
        // the size class and the file, so planning a second building rewrote what the first
        // building's boxes produced. A schema-1 file still reads: a box with no list of its
        // own falls back to its class's and the file's.
        public const int CurrentSchema = 2;
        [JsonProperty("schema")]      public int Schema { get; set; } = CurrentSchema;
        [JsonProperty("savedUtc")]    public string SavedUtc { get; set; }
        [JsonProperty("fitFactor")]   public double FitFactor { get; set; } = ScopeBoxSizing.DefaultFitFactor;
        [JsonProperty("overlapM")]    public double OverlapM { get; set; } = 2.0;
        [JsonProperty("paddingM")]    public double PaddingM { get; set; } = 1.0;
        [JsonProperty("gridAngleDeg")] public double GridAngleDeg { get; set; }
        [JsonProperty("footprintMode")] public string FootprintMode { get; set; }
        /// <summary>Levels a level-less area box is produced on. Empty means every level.</summary>
        [JsonProperty("levels")]      public List<string> Levels { get; set; } = new List<string>();
        /// <summary>
        /// Level code → the level's UniqueId (DTW-90). Codes are read from level names, so a
        /// rename or an inserted level could move a code; with this the code stays on its
        /// level (<see cref="ScopeBoxPlanner.StableLevelCodes"/>). Absent in older plans —
        /// they resolve by code and get ids on their next save.
        /// </summary>
        [JsonProperty("levelIds", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> LevelIds { get; set; }
        [JsonProperty("classes")]     public List<ClassEntry> Classes { get; set; } = new List<ClassEntry>();
        [JsonProperty("boxes")]       public List<BoxEntry> Boxes { get; set; } = new List<BoxEntry>();

        public sealed class ClassEntry
        {
            [JsonProperty("key")]          public string Key { get; set; }
            [JsonProperty("scale")]        public int Scale { get; set; }
            [JsonProperty("paper")]        public string Paper { get; set; }
            [JsonProperty("maxWidthM")]    public double MaxWidthM { get; set; }
            [JsonProperty("maxDepthM")]    public double MaxDepthM { get; set; }
            [JsonProperty("seed")]         public string Seed { get; set; }
            [JsonProperty("drawingTypes")] public List<string> DrawingTypes { get; set; } = new List<string>();
            [JsonProperty("disciplines")]  public List<string> Disciplines { get; set; } = new List<string>();
        }

        public sealed class BoxEntry
        {
            [JsonProperty("name")]    public string Name { get; set; }
            [JsonProperty("class")]   public string ClassKey { get; set; }
            [JsonProperty("loc", NullValueHandling = NullValueHandling.Ignore)]   public string Loc { get; set; }
            [JsonProperty("level", NullValueHandling = NullValueHandling.Ignore)] public string Level { get; set; }
            [JsonProperty("widthM")]  public double WidthM { get; set; }
            [JsonProperty("depthM")]  public double DepthM { get; set; }
            [JsonProperty("centreX")] public double CentreX { get; set; }
            [JsonProperty("centreY")] public double CentreY { get; set; }
            [JsonProperty("angleDeg")] public double AngleDeg { get; set; }
            /// <summary>The drawing types this box is produced for. Null in a schema-1 file.</summary>
            [JsonProperty("drawingTypes", NullValueHandling = NullValueHandling.Ignore)] public List<string> DrawingTypes { get; set; }
            /// <summary>The levels this box is produced on. Null in a schema-1 file.</summary>
            [JsonProperty("levels", NullValueHandling = NullValueHandling.Ignore)] public List<string> Levels { get; set; }

            /// <summary>The planning group a re-plan replaces as a whole: building, level, size class.</summary>
            public string GroupKey => (Loc ?? "") + "|" + (Level ?? "") + "|" + (ClassKey ?? "");
        }

        /// <param name="levels">Levels a level-less box is produced on.</param>
        /// <param name="failed">Names that were planned New / Moved but whose creation failed; not recorded.</param>
        /// <param name="levelIds">Level code → UniqueId for the project's levels (DTW-90).</param>
        public static ScopeBoxPlanFile From(ScopeBoxPlanRequest req, ScopeBoxPlanResult res, IEnumerable<string> levels,
            string footprintMode, ISet<string> failed = null, IDictionary<string, string> levelIds = null)
        {
            var f = new ScopeBoxPlanFile
            {
                SavedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                FitFactor = req.FitFactor, OverlapM = req.OverlapM, PaddingM = req.PaddingM,
                GridAngleDeg = req.GridAngleRad * 180.0 / Math.PI, FootprintMode = footprintMode,
                Levels = (levels ?? Enumerable.Empty<string>()).ToList(),
                LevelIds = levelIds == null ? null
                    : new Dictionary<string, string>(levelIds, StringComparer.OrdinalIgnoreCase),
            };
            foreach (var c in res.Classes)
                f.Classes.Add(new ClassEntry
                {
                    Key = c.Key, Scale = c.Scale, Paper = c.Paper, MaxWidthM = c.MaxWidthM, MaxDepthM = c.MaxDepthM,
                    Seed = c.Seed?.Name, DrawingTypes = c.DrawingTypeIds.ToList(), Disciplines = c.Disciplines.ToList(),
                });
            var levelList = (levels ?? Enumerable.Empty<string>()).ToList();
            foreach (var b in res.Boxes)
            {
                // NoSeed boxes do not exist. Mismatch boxes exist but at another size, and are
                // recorded at that size so production's size check can warn about them.
                if (b.Status == PlannedBoxStatus.NoSeed) continue;
                if (failed != null && failed.Contains(b.Name)) continue;
                var cls = res.Classes.FirstOrDefault(c => c.Key == b.ClassKey);
                bool mismatch = b.Status == PlannedBoxStatus.Mismatch && b.Existing != null;
                f.Boxes.Add(new BoxEntry
                {
                    Name = b.Name, ClassKey = b.ClassKey, Loc = b.Loc, Level = b.Level,
                    WidthM = mismatch ? b.Existing.WidthM : b.WidthM,
                    DepthM = mismatch ? b.Existing.DepthM : b.DepthM,
                    CentreX = mismatch ? b.Existing.CentreX : b.CentreX,
                    CentreY = mismatch ? b.Existing.CentreY : b.CentreY,
                    AngleDeg = (mismatch ? b.Existing.AngleRad : b.AngleRad) * 180.0 / Math.PI,
                    DrawingTypes = cls?.DrawingTypeIds.ToList() ?? new List<string>(),
                    Levels = b.Level != null ? new List<string> { b.Level } : levelList.ToList(),
                });
            }
            return f;
        }

        /// <summary>
        /// Lay a new plan over the saved one. A re-plan replaces whole groups — every saved box
        /// of a (building, level, size class) the new plan covers — so a box the new layout no
        /// longer has is dropped rather than produced from a stale entry. Groups the new plan
        /// does not touch (another building, another scale) are kept exactly as they were,
        /// including their own drawing types and levels. Classes merge by key with their type
        /// lists unioned, since they are now only a summary. Settings are the new plan's.
        /// </summary>
        public static ScopeBoxPlanFile Merge(ScopeBoxPlanFile saved, ScopeBoxPlanFile incoming)
        {
            if (incoming == null) return saved;
            if (saved == null) return incoming;
            var merged = JsonConvert.DeserializeObject<ScopeBoxPlanFile>(JsonConvert.SerializeObject(incoming));
            merged.Schema = CurrentSchema;
            var replaced = new HashSet<string>(incoming.Boxes.Select(b => b.GroupKey), StringComparer.OrdinalIgnoreCase);
            foreach (var b in saved.Boxes)
            {
                if (replaced.Contains(b.GroupKey)) continue;
                if (merged.Boxes.Any(n => string.Equals(n.Name, b.Name, StringComparison.OrdinalIgnoreCase))) continue;
                // A schema-1 entry carried nothing of its own; pin it to what it meant then.
                var keep = JsonConvert.DeserializeObject<BoxEntry>(JsonConvert.SerializeObject(b));
                if (keep.DrawingTypes == null)
                    keep.DrawingTypes = saved.Classes.FirstOrDefault(c => c.Key == keep.ClassKey)?.DrawingTypes?.ToList() ?? new List<string>();
                if (keep.Levels == null)
                    keep.Levels = keep.Level != null ? new List<string> { keep.Level } : saved.Levels.ToList();
                merged.Boxes.Add(keep);
            }
            foreach (var c in saved.Classes)
            {
                var n = merged.Classes.FirstOrDefault(x => x.Key == c.Key);
                if (n == null) { merged.Classes.Add(c); continue; }
                foreach (var t in c.DrawingTypes.Where(t => !n.DrawingTypes.Contains(t))) n.DrawingTypes.Add(t);
                foreach (var d in c.Disciplines.Where(d => !n.Disciplines.Contains(d))) n.Disciplines.Add(d);
            }
            // Level ids: the saved bindings, with the new plan's winning where both name a code
            // (the new plan's codes were assigned honouring the saved ones, so they only differ
            // where a saved level has since been deleted).
            if (saved.LevelIds != null || incoming.LevelIds != null)
            {
                var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in saved.LevelIds ?? new Dictionary<string, string>()) ids[kv.Key] = kv.Value;
                foreach (var kv in incoming.LevelIds ?? new Dictionary<string, string>()) ids[kv.Key] = kv.Value;
                merged.LevelIds = ids;
            }
            return merged;
        }

        /// <summary>
        /// Drop entries for boxes no longer in the model. Returns the names dropped so the
        /// caller can say so; an entry for a deleted box would otherwise live on forever.
        /// </summary>
        public List<string> PruneMissing(ISet<string> namesInModel)
        {
            var gone = Boxes.Where(b => namesInModel == null || !namesInModel.Contains(b.Name)).Select(b => b.Name).ToList();
            Boxes.RemoveAll(b => gone.Contains(b.Name));
            return gone;
        }

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);

        /// <summary>Null for a missing or empty file; throws for a malformed one so it is reported, not treated as "no plan".</summary>
        public static ScopeBoxPlanFile FromJson(string json)
            => string.IsNullOrWhiteSpace(json) ? null : JsonConvert.DeserializeObject<ScopeBoxPlanFile>(json);

        /// <summary>
        /// What to produce for an area box: its drawing types and the levels. A box the
        /// plan does not list, or whose class is gone, returns false with the reason.
        /// </summary>
        public bool TryResolve(string boxName, out List<string> drawingTypes, out List<string> levels, out string why)
        {
            drawingTypes = null; levels = null; why = null;
            if (!ScopeBoxNames.TryParseArea(boxName, out _, out var level, out var bad))
            { why = bad ?? "not an area box"; return false; }
            var box = Boxes.FirstOrDefault(b => string.Equals(b.Name, boxName, StringComparison.OrdinalIgnoreCase));
            if (box == null) { why = "not in the saved plan — open the Scope Box Planner and save a plan that includes it"; return false; }
            // The box's own lists first (schema 2); a schema-1 box falls back to its class and the file.
            var types = box.DrawingTypes ?? Classes.FirstOrDefault(c => c.Key == box.ClassKey)?.DrawingTypes;
            if (types == null || types.Count == 0) { why = $"no drawing types are recorded for it (size class '{box.ClassKey}')"; return false; }
            drawingTypes = types.ToList();
            levels = level != null ? new List<string> { level } : (box.Levels ?? Levels).ToList();
            if (levels.Count == 0) { why = "no levels are recorded for it — tick at least one level in the planner and create again"; return false; }
            return true;
        }
    }

    /// <summary>
    /// What to produce for one STING-AREA:: box, with or without a saved plan.
    ///
    /// The saved plan (scope_box_plan.json) says, per box, which drawing types and
    /// levels it was created for. A project whose area boxes were drawn by hand — or
    /// whose plan file was never saved, or does not list a box — has no such record,
    /// and production used to fail outright; the MEP preset then also skipped the
    /// per-level route (the project HAS area boxes), so no plans were produced at all.
    /// Without a plan entry the box is produced from itself: its ::level when the name
    /// carries one, else every level (the caller keeps only the levels the box's height
    /// reaches), with the caller's default drawing types.
    /// </summary>
    public static class AreaBoxResolution
    {
        /// <summary>
        /// The plan's answer when it has one for <paramref name="boxName"/>; otherwise the
        /// box's own level (or <paramref name="allLevelCodes"/>) with
        /// <paramref name="defaultTypes"/>. <paramref name="fromPlan"/> says which.
        /// False with <paramref name="why"/> for a malformed name, or when there is no plan
        /// entry and no default types.
        /// </summary>
        public static bool Resolve(ScopeBoxPlanFile plan, string boxName, IList<string> defaultTypes,
            IList<string> allLevelCodes, out List<string> drawingTypes, out List<string> levels,
            out bool fromPlan, out string why)
        {
            drawingTypes = null; levels = null; fromPlan = false; why = null;
            if (!ScopeBoxNames.TryParseArea(boxName, out _, out var level, out var bad))
            { why = bad ?? "not an area box"; return false; }

            string planWhy = null;
            if (plan != null && plan.TryResolve(boxName, out var pt, out var pl, out planWhy))
            { drawingTypes = pt; levels = pl; fromPlan = true; return true; }

            var types = (defaultTypes ?? new List<string>()).Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (types.Count == 0)
            {
                why = plan == null
                    ? "no saved plan, and no default drawing types to produce it with"
                    : (planWhy ?? "not in the saved plan") + ", and no default drawing types to produce it with";
                return false;
            }
            drawingTypes = types;
            levels = level != null
                ? new List<string> { level }
                : (allLevelCodes ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (levels.Count == 0) { why = "its name carries no level and the project has no levels"; return false; }
            return true;
        }
    }
}
