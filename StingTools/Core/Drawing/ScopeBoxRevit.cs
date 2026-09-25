// StingTools — Scope-box planner · the Revit half
//
// Everything that needs a Document: measuring boxes, reading footprints and the
// grid angle, copying seeds into place, moving boxes a re-plan has shifted,
// importing seeds from another file, and colouring boxes in views. Every decision —
// which boxes, what size, where, what name — was already made by the Revit-free
// ScopeBoxPlanner; this file carries it out, and says what it could not do.
//
// What the Revit API allows here, and what it does not:
//   • cannot create a scope box, or change its size         → copy a seed (CopyElement / CopyElements)
//   • can move and rotate one                                → ElementTransformUtils
//   • can rename one                                         → Element.Name
//   • cannot give scope boxes subcategories                  → colour per element per view (SetElementOverrides)
//   • a plan view offers only boxes that cross its level     → seeds must be drawn tall; checked and reported
//
// Units: Revit internal feet in, metres out to the planner.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Drawing
{
    public static class ScopeBoxRevit
    {
        public const double FeetPerMetre = 1.0 / 0.3048;
        private static double M(double feet) => feet * 0.3048;
        private static double Ft(double metres) => metres * FeetPerMetre;

        public static List<Element> AllBoxes(Document doc)
            => doc == null ? new List<Element>()
             : new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                   .WhereElementIsNotElementType().ToElements().ToList();

        /// <summary>A scope box measured in its own frame: plan size, angle and vertical reach.</summary>
        public sealed class Measure
        {
            public double WidthM, DepthM, ZMinFt, ZMaxFt;
            /// <summary>Angle of the measuring axis, folded into (-45°, 45°]. Width is along it.</summary>
            public double AngleRad;
            public XYZ Centre;
            /// <summary>True when the geometry had no edges and the bounding box was used; exact only for an unturned box.</summary>
            public bool FromBoundingBox;
            public ExistingScopeBox ToExisting() => new ExistingScopeBox
                { CentreX = Centre.X * 0.3048, CentreY = Centre.Y * 0.3048, WidthM = WidthM, DepthM = DepthM, AngleRad = AngleRad };
        }

        /// <summary>
        /// Measure a box from its edges, in its own frame, so a box turned to an angled grid
        /// comes back at its true size rather than its larger bounding box. A geometry read
        /// that throws fails the measurement — it does not fall back to a size that may be wrong.
        /// With <paramref name="requireSquare"/>, a box not square to project north is refused
        /// (seeds: the planner turns them, so they must start unturned).
        /// </summary>
        public static bool TryMeasure(Element box, out Measure m, out string why, bool requireSquare = false)
        {
            m = null; why = null;
            var bb = box?.get_BoundingBox(null);
            if (bb == null) { why = "has no extent"; return false; }
            var pts = new List<XYZ>();
            double? axis = null;
            try
            {
                var geo = box.get_Geometry(new Options());
                foreach (var obj in geo ?? Enumerable.Empty<GeometryObject>())
                {
                    if (!(obj is Line ln)) continue;
                    var d = ln.Direction;
                    if (Math.Abs(d.Z) > 0.999) continue;                       // vertical edge
                    if (axis == null) axis = Math.Atan2(d.Y, d.X);
                    pts.Add(ln.GetEndPoint(0)); pts.Add(ln.GetEndPoint(1));
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ScopeBoxRevit.TryMeasure '{box.Name}': {ex.Message}");
                why = "could not be measured (its geometry could not be read)";
                return false;
            }

            m = new Measure { ZMinFt = bb.Min.Z, ZMaxFt = bb.Max.Z };
            if (axis == null || pts.Count < 4)
            {
                m.FromBoundingBox = true;
                m.WidthM = M(bb.Max.X - bb.Min.X); m.DepthM = M(bb.Max.Y - bb.Min.Y);
                m.Centre = (bb.Min + bb.Max) / 2;
            }
            else
            {
                double a = axis.Value % (Math.PI / 2);                             // fold into (-45°, 45°]
                if (a > Math.PI / 4) a -= Math.PI / 2;
                if (a <= -Math.PI / 4) a += Math.PI / 2;
                double c = Math.Cos(a), s = Math.Sin(a);
                double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
                foreach (var p in pts)
                {
                    double u = p.X * c + p.Y * s, v = -p.X * s + p.Y * c;
                    minU = Math.Min(minU, u); maxU = Math.Max(maxU, u);
                    minV = Math.Min(minV, v); maxV = Math.Max(maxV, v);
                }
                double cu = (minU + maxU) / 2, cv = (minV + maxV) / 2;
                m.AngleRad = a;
                m.WidthM = M(maxU - minU); m.DepthM = M(maxV - minV);
                m.Centre = new XYZ(cu * c - cv * s, cu * s + cv * c, (bb.Min.Z + bb.Max.Z) / 2);
            }
            if (m.WidthM <= 0 || m.DepthM <= 0) { why = "has no plan extent"; return false; }
            if (requireSquare && Math.Abs(m.AngleRad) > 1e-4)
            { why = "is turned — draw seeds square to project north; the planner turns them"; return false; }
            return true;
        }

        /// <summary>Every STING-SEED box, measured. Turned, empty or unreadable seeds are reported and left out.</summary>
        public static List<ScopeBoxSeed> Seeds(Document doc, List<string> warnings)
        {
            var seeds = new List<ScopeBoxSeed>();
            foreach (var el in AllBoxes(doc).Where(e => ScopeBoxNames.Classify(e.Name) == ScopeBoxKind.Seed))
            {
                if (!TryMeasure(el, out var m, out var why, requireSquare: true)) { warnings?.Add($"Seed '{el.Name}' {why} — not used."); continue; }
                seeds.Add(new ScopeBoxSeed { Id = el.Id.Value.ToString(), Name = el.Name, WidthM = m.WidthM, DepthM = m.DepthM });
            }
            return seeds;
        }

        /// <summary>
        /// Every STING-AREA box, measured, keyed by name — what the planner compares a
        /// re-plan against. Boxes that cannot be measured are left out (the planner then
        /// treats the name as existing but unmeasured) and reported.
        /// </summary>
        public static Dictionary<string, ExistingScopeBox> AreaBoxes(Document doc, List<string> warnings)
        {
            var map = new Dictionary<string, ExistingScopeBox>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in AllBoxes(doc).Where(e => ScopeBoxNames.Classify(e.Name) == ScopeBoxKind.Area))
            {
                if (!TryMeasure(el, out var m, out var why)) { warnings?.Add($"'{el.Name}' {why}; it is left as it is."); continue; }
                map[el.Name] = m.ToExisting();
            }
            return map;
        }

        /// <summary>Each STING-LOC box as a footprint (its four corners).</summary>
        public static List<ScopeBoxFootprint> BuildingFootprints(Document doc, List<string> warnings)
        {
            var list = new List<ScopeBoxFootprint>();
            foreach (var el in AllBoxes(doc).Where(e => ScopeBoxNames.Classify(e.Name) == ScopeBoxKind.Building))
            {
                var code = el.Name.Substring(ScopeBoxNames.LocPrefix.Length);
                if (!ScopeBoxNames.IsValidSegment(code)) { warnings?.Add($"'{el.Name}': building code '{code}' cannot appear in a box name — skipped."); continue; }
                var bb = el.get_BoundingBox(null);
                if (bb == null) continue;
                list.Add(new ScopeBoxFootprint
                {
                    Loc = code,
                    Points = { (M(bb.Min.X), M(bb.Min.Y)), (M(bb.Max.X), M(bb.Max.Y)), (M(bb.Min.X), M(bb.Max.Y)), (M(bb.Max.X), M(bb.Min.Y)) },
                });
            }
            return list;
        }

        private static readonly BuiltInCategory[] _footprintCats =
        {
            BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs,
            BuiltInCategory.OST_Columns, BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Stairs, BuiltInCategory.OST_CurtainWallPanels, BuiltInCategory.OST_Ceilings,
        };

        /// <summary>
        /// Level codes for every level, unique (see <see cref="ScopeBoxPlanner.UniqueLevelCodes"/>).
        /// The one mapping used for footprints, the saved plan and production, so a level
        /// always gets the same code in all three.
        /// </summary>
        public static Dictionary<long, string> LevelCodes(Document doc)
            => ScopeBoxPlanner.UniqueLevelCodes(
                new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation).ThenBy(l => l.Id.Value)
                    .Select(l => (l.Id.Value, ParameterHelpers.GetLevelCodeForLevel(l))));

        /// <summary>
        /// The building's extent from its fabric (walls, floors, roofs, columns …). With a
        /// level, only what is physically on that storey: elements whose height overlaps the
        /// band from this level up to the next. By height rather than by LevelId, because
        /// framing, stairs and curtain panels often carry no LevelId, and a wall two storeys
        /// tall belongs to both.
        /// </summary>
        public static ScopeBoxFootprint ModelFootprint(Document doc, Level level, string levelCode, string loc = null)
        {
            var fp = new ScopeBoxFootprint { Loc = loc, Level = level != null ? levelCode : null };
            double bandLo = double.MinValue, bandHi = double.MaxValue;
            if (level != null)
            {
                bandLo = level.Elevation;
                var above = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .Where(l => l.Elevation > level.Elevation + 1e-6).OrderBy(l => l.Elevation).FirstOrDefault();
                bandHi = above?.Elevation ?? level.Elevation + Ft(4.0);
            }
            var els = new FilteredElementCollector(doc).WherePasses(new ElementMulticategoryFilter(_footprintCats.ToList()))
                .WhereElementIsNotElementType();
            foreach (var el in els)
            {
                var bb = el.get_BoundingBox(null);
                if (bb == null) continue;
                if (level != null && (bb.Max.Z < bandLo - 1e-6 || bb.Min.Z >= bandHi - 1e-6)) continue;
                fp.Points.Add((M(bb.Min.X), M(bb.Min.Y)));
                fp.Points.Add((M(bb.Max.X), M(bb.Max.Y)));
                fp.Points.Add((M(bb.Min.X), M(bb.Max.Y)));
                fp.Points.Add((M(bb.Max.X), M(bb.Min.Y)));
            }
            return fp;
        }

        /// <summary>
        /// The angle the grids run at, folded into [-45°, 45°) and weighted by grid length.
        /// Zero when there are no straight grids.
        /// </summary>
        public static double GridAngleRad(Document doc)
        {
            var votes = new Dictionary<int, double>();
            foreach (var g in new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>())
            {
                if (!(g.Curve is Line ln)) continue;
                double a = Math.Atan2(ln.Direction.Y, ln.Direction.X) * 180 / Math.PI;
                a = ((a % 90) + 90) % 90; if (a >= 45) a -= 90;
                int key = (int)Math.Round(a * 10);
                votes[key] = (votes.TryGetValue(key, out var v) ? v : 0) + ln.Length;
            }
            return votes.Count == 0 ? 0 : votes.OrderByDescending(kv => kv.Value).First().Key / 10.0 * Math.PI / 180;
        }

        /// <summary>What Create did, box by box.</summary>
        public sealed class CreateResult
        {
            public List<string> Created { get; } = new List<string>();
            public List<string> Moved { get; } = new List<string>();
            /// <summary>New or Moved boxes that could not be made or moved — kept out of the saved plan.</summary>
            public HashSet<string> Failed { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Copy the seed of each New box into place, turn it and name it; move each Moved box
        /// onto its planned centre and angle. Call inside a transaction. Every box is tried on
        /// its own: one failure is reported and recorded in <see cref="CreateResult.Failed"/>,
        /// it does not undo the others. Nothing is left unnamed.
        /// </summary>
        public static CreateResult Create(Document doc, IEnumerable<PlannedScopeBox> boxes, List<string> report)
        {
            var result = new CreateResult();
            var byName = AllBoxes(doc).GroupBy(e => e.Name ?? "", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
            double lowest = levels.Count > 0 ? levels.Min(l => l.Elevation) : 0, highest = levels.Count > 0 ? levels.Max(l => l.Elevation) : 0;
            var reachWarned = new HashSet<string>();

            foreach (var b in boxes)
            {
                if (b.Status == PlannedBoxStatus.Mismatch) { report.Add($"{b.Name}: {b.StatusNote}."); continue; }
                if (b.Status != PlannedBoxStatus.New && b.Status != PlannedBoxStatus.Moved) continue;
                ElementId made = null;
                try
                {
                    if (b.Status == PlannedBoxStatus.Moved)
                    {
                        if (!byName.TryGetValue(b.Name, out var el) || !TryMeasure(el, out var em, out var ewhy))
                        { result.Failed.Add(b.Name); report.Add($"{b.Name}: could not be found or measured to move it."); continue; }
                        var to = new XYZ(Ft(b.CentreX), Ft(b.CentreY), em.Centre.Z);
                        ElementTransformUtils.MoveElement(doc, el.Id, to - em.Centre);
                        if (Math.Abs(b.RotateBy) > 1e-9)
                            ElementTransformUtils.RotateElement(doc, el.Id, Line.CreateBound(to, to + XYZ.BasisZ), b.RotateBy);
                        result.Moved.Add(b.Name);
                        continue;
                    }

                    if (byName.ContainsKey(b.Name)) { report.Add($"{b.Name}: already exists — kept."); continue; }
                    if (!long.TryParse(b.SeedId, out var idVal) || !(doc.GetElement(new ElementId(idVal)) is Element seed))
                    { result.Failed.Add(b.Name); report.Add($"{b.Name}: its seed is no longer in the project — re-plan."); continue; }
                    if (!TryMeasure(seed, out var m, out var why, requireSquare: true))
                    { result.Failed.Add(b.Name); report.Add($"{b.Name}: seed '{seed.Name}' {why}."); continue; }

                    if ((m.ZMinFt > lowest || m.ZMaxFt < highest) && reachWarned.Add(seed.Name))
                        report.Add($"Seed '{seed.Name}' does not span every level, so plans on the levels it misses cannot use its copies. "
                                 + "Make the seed taller in a 3D view and create again.");

                    var target = new XYZ(Ft(b.CentreX), Ft(b.CentreY), m.Centre.Z);
                    made = ElementTransformUtils.CopyElement(doc, seed.Id, target - m.Centre)?.FirstOrDefault();
                    if (made == null || !(doc.GetElement(made) is Element copy))
                    { result.Failed.Add(b.Name); report.Add($"{b.Name}: copy failed."); made = null; continue; }

                    double angle = b.AngleRad + (b.SeedRotated ? Math.PI / 2 : 0);
                    if (Math.Abs(angle) > 1e-9)
                        ElementTransformUtils.RotateElement(doc, made, Line.CreateBound(target, target + XYZ.BasisZ), angle);
                    copy.Name = b.Name;
                    byName[b.Name] = copy;
                    result.Created.Add(b.Name);
                }
                catch (Exception ex)
                {
                    // An unnamed or half-placed copy would be a stray duplicate of the seed. Remove it.
                    if (made != null) { try { doc.Delete(made); } catch (Exception dex) { StingLog.Warn($"ScopeBox cleanup '{b.Name}': {dex.Message}"); } }
                    result.Failed.Add(b.Name);
                    report.Add($"{b.Name}: failed ({ex.Message})" + (made != null ? " — the copy was removed." : "."));
                    StingLog.Error($"ScopeBoxRevit.Create '{b.Name}'", ex);
                }
            }
            return result;
        }

        /// <summary>
        /// Copy every STING-SEED box from <paramref name="source"/> that <paramref name="target"/>
        /// lacks by name. Call inside a transaction on the target.
        /// </summary>
        public static int ImportSeeds(Document source, Document target, List<string> report)
        {
            var have = new HashSet<string>(AllBoxes(target).Select(e => e.Name ?? ""), StringComparer.OrdinalIgnoreCase);
            var ids = AllBoxes(source)
                .Where(e => ScopeBoxNames.Classify(e.Name) == ScopeBoxKind.Seed)
                .Where(e => { if (have.Contains(e.Name)) { report.Add($"'{e.Name}' is already here — skipped."); return false; } return true; })
                .Select(e => e.Id).ToList();
            if (ids.Count == 0) { report.Add("No new STING-SEED:: boxes in that file."); return 0; }
            var copied = ElementTransformUtils.CopyElements(source, ids, target, Transform.Identity, new CopyPasteOptions());
            return copied?.Count ?? 0;
        }

        /// <summary>Plan views (not templates) — where scope boxes are drawn and coloured.</summary>
        public static List<View> PlanViews(Document doc)
            => new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<View>()
                   .Where(v => !v.IsTemplate && v.ViewType != ViewType.AreaPlan).ToList();

        /// <summary>What colouring needs to know about a box, from its name, the plan and the catalogue.</summary>
        public static ScopeBoxColourSubject Subject(Document doc, Element box, ScopeBoxPlanFile plan)
        {
            var s = new ScopeBoxColourSubject { Name = box.Name };
            var kind = ScopeBoxNames.Classify(box.Name);
            if (kind == ScopeBoxKind.DrawingType && ScopeBoxBinder.TryParseName(box.Name, out var bnd, out _))
                s.Discipline = DrawingTypeRegistry.Get(doc, bnd.DrawingTypeId)?.Discipline;
            if (kind == ScopeBoxKind.Area && plan != null)
            {
                var entry = plan.Boxes.FirstOrDefault(x => string.Equals(x.Name, box.Name, StringComparison.OrdinalIgnoreCase));
                if (entry != null)
                {
                    s.SizeClass = entry.ClassKey; s.Loc = entry.Loc;
                    s.Discipline = plan.Classes.FirstOrDefault(c => c.Key == entry.ClassKey)?.Disciplines?.FirstOrDefault();
                }
            }
            return s;
        }

        /// <summary>Box/view pairs coloured, and pairs cleared because the box had nothing to colour by.</summary>
        public struct ColourResult { public int Coloured, Cleared; }

        /// <summary>
        /// The colour each STING box gets under <paramref name="mode"/>, keyed by box name. The
        /// planner dialog's swatches use this too, so the swatch and the model agree.
        /// </summary>
        public static Dictionary<string, string> ColoursByBox(Document doc, ScopeBoxStyle style, ScopeBoxColourMode mode, ScopeBoxPlanFile plan)
        {
            var boxes = AllBoxes(doc).Where(e => ScopeBoxNames.Classify(e.Name) != ScopeBoxKind.Plain).ToList();
            var subjects = boxes.Select(b => Subject(doc, b, plan)).ToList();
            var colours = style.Assign(mode, subjects.Select(s => s.KeyFor(mode)).Where(k => k != null));
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in subjects)
            {
                var key = s.KeyFor(mode);
                if (key != null && colours.TryGetValue(key, out var hex)) result[s.Name] = hex;
            }
            return result;
        }

        /// <summary>
        /// Colour every STING box in <paramref name="views"/> by <paramref name="mode"/>; Off
        /// clears. Only boxes with a STING prefix are touched. A box with nothing to colour by
        /// in this mode is cleared — otherwise switching mode would leave the last mode's colour
        /// on it — and counted as cleared, not coloured. Refusals are reported.
        /// </summary>
        public static ColourResult Colour(Document doc, IEnumerable<View> views, ScopeBoxStyle style, ScopeBoxColourMode mode,
            ScopeBoxPlanFile plan, List<string> report)
        {
            var boxes = AllBoxes(doc).Where(e => ScopeBoxNames.Classify(e.Name) != ScopeBoxKind.Plain).ToList();
            var colours = mode == ScopeBoxColourMode.Off ? new Dictionary<string, string>() : ColoursByBox(doc, style, mode, plan);
            int weight = Math.Max(1, Math.Min(16, style.Values.LineWeight));

            var r = new ColourResult();
            int refused = 0, badHex = 0;
            foreach (var v in views)
                foreach (var b in boxes)
                {
                    var ogs = new OverrideGraphicSettings();
                    bool colour = false;
                    if (colours.TryGetValue(b.Name ?? "", out var hex))
                    {
                        if (ScopeBoxStyle.TryParseHex(hex, out var cr, out var cg, out var cb))
                        {
                            ogs.SetProjectionLineColor(new Color(cr, cg, cb));
                            ogs.SetProjectionLineWeight(weight);
                            colour = true;
                        }
                        else badHex++;
                    }
                    try
                    {
                        v.SetElementOverrides(b.Id, ogs);
                        if (colour) r.Coloured++; else r.Cleared++;
                    }
                    catch (Exception ex) { refused++; StingLog.Warn($"ScopeBox colour '{b.Name}' in '{v.Name}': {ex.Message}"); }
                }
            if (refused > 0) report.Add($"{refused} box/view pair(s) refused the override — see the log.");
            if (badHex > 0) report.Add($"{badHex} box/view pair(s) were left uncoloured: their colour in the style file is not #RRGGBB.");
            return r;
        }
    }
}
