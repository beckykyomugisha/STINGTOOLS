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
                foreach (var cls in result.Classes)
                {
                    if (cls.TileWidthM <= req.OverlapM + Eps || cls.TileDepthM <= req.OverlapM + Eps) continue;
                    foreach (var tile in Tile(fp.Points, req.GridAngleRad, cls.TileWidthM, cls.TileDepthM, req.OverlapM, req.PaddingM))
                    {
                        string code = (string.IsNullOrWhiteSpace(fp.Loc) ? "" : fp.Loc + "-")
                                    + cls.Key + "-" + tile.Index.ToString("D2", CultureInfo.InvariantCulture);
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
                            AngleRad = req.GridAngleRad, SeedId = cls.Seed?.Id, SeedRotated = cls.SeedRotated,
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
        public static ScopeBoxPlanFile From(ScopeBoxPlanRequest req, ScopeBoxPlanResult res, IEnumerable<string> levels,
            string footprintMode, ISet<string> failed = null)
        {
            var f = new ScopeBoxPlanFile
            {
                SavedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                FitFactor = req.FitFactor, OverlapM = req.OverlapM, PaddingM = req.PaddingM,
                GridAngleDeg = req.GridAngleRad * 180.0 / Math.PI, FootprintMode = footprintMode,
                Levels = (levels ?? Enumerable.Empty<string>()).ToList(),
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
}
