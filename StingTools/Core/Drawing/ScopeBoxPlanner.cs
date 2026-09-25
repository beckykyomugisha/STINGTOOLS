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

    public enum PlannedBoxStatus { New, Exists, NoSeed }

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
                        result.Boxes.Add(new PlannedScopeBox
                        {
                            Name = name, AreaCode = code, ClassKey = cls.Key, Loc = fp.Loc, Level = fp.Level,
                            CentreX = tile.X, CentreY = tile.Y, WidthM = cls.TileWidthM, DepthM = cls.TileDepthM,
                            AngleRad = req.GridAngleRad, SeedId = cls.Seed?.Id, SeedRotated = cls.SeedRotated,
                            Row = tile.Row, Column = tile.Column,
                            Status = req.ExistingNames != null && req.ExistingNames.Contains(name) ? PlannedBoxStatus.Exists
                                   : cls.Seed == null ? PlannedBoxStatus.NoSeed : PlannedBoxStatus.New,
                        });
                    }
                }
            }

            var dupes = result.Boxes.GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            foreach (var n in dupes)
                result.Warnings.Add($"'{n}' is planned twice — two footprints share a building code and level. Give each STING-LOC box its own code.");
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
        public const int CurrentSchema = 1;
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
        }

        public static ScopeBoxPlanFile From(ScopeBoxPlanRequest req, ScopeBoxPlanResult res, IEnumerable<string> levels, string footprintMode)
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
            foreach (var b in res.Boxes.Where(b => b.Status != PlannedBoxStatus.NoSeed))
                f.Boxes.Add(new BoxEntry { Name = b.Name, ClassKey = b.ClassKey, Loc = b.Loc, Level = b.Level, WidthM = b.WidthM, DepthM = b.DepthM });
            return f;
        }

        /// <summary>
        /// Lay a new plan over the saved one. Planning building B must not orphan building
        /// A's boxes, so entries the new plan does not mention are kept; entries it does
        /// mention — boxes by name, classes by key — are replaced. Settings and levels are
        /// the new plan's, since they are what the person just chose.
        /// </summary>
        public static ScopeBoxPlanFile Merge(ScopeBoxPlanFile saved, ScopeBoxPlanFile incoming)
        {
            if (incoming == null) return saved;
            if (saved == null) return incoming;
            var merged = JsonConvert.DeserializeObject<ScopeBoxPlanFile>(JsonConvert.SerializeObject(incoming));
            foreach (var c in saved.Classes.Where(c => merged.Classes.All(n => n.Key != c.Key)))
                merged.Classes.Add(c);
            foreach (var b in saved.Boxes.Where(b => merged.Boxes.All(n => !string.Equals(n.Name, b.Name, StringComparison.OrdinalIgnoreCase))))
                merged.Boxes.Add(b);
            return merged;
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
            var cls = Classes.FirstOrDefault(c => c.Key == box.ClassKey);
            if (cls == null || cls.DrawingTypes.Count == 0) { why = $"its size class '{box.ClassKey}' has no drawing types in the plan"; return false; }
            drawingTypes = cls.DrawingTypes.ToList();
            levels = level != null ? new List<string> { level } : Levels.ToList();
            return true;
        }
    }
}
