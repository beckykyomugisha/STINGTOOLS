// StingTools — Scope-box planner · the Revit half
//
// Everything that needs a Document: measuring seeds, reading footprints and the
// grid angle, copying seeds into place, importing seeds from another file, and
// colouring boxes in views. Every decision — which boxes, what size, where, what
// name — was already made by the Revit-free ScopeBoxPlanner; this file carries
// it out, and says what it could not do.
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

        /// <summary>A scope box's plan size and vertical reach, measured from its geometry.</summary>
        public sealed class Measure
        {
            public double WidthM, DepthM, ZMinFt, ZMaxFt;
            public XYZ Centre;
        }

        /// <summary>
        /// Measure a box. Refuses a rotated box: its bounding box is larger than the box,
        /// so a rotated seed would be recorded at a size it is not.
        /// </summary>
        public static bool TryMeasure(Element box, out Measure m, out string why)
        {
            m = null; why = null;
            var bb = box?.get_BoundingBox(null);
            if (bb == null) { why = "has no extent"; return false; }
            try
            {
                var geo = box.get_Geometry(new Options());
                foreach (var obj in geo ?? Enumerable.Empty<GeometryObject>())
                {
                    if (!(obj is Line ln)) continue;
                    var d = ln.Direction;
                    if (Math.Abs(d.Z) > 0.999) continue;                       // vertical edge
                    if (Math.Abs(d.X) > 1e-6 && Math.Abs(d.Y) > 1e-6)
                    { why = "is rotated — draw seeds square to project north, the planner turns them"; return false; }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ScopeBoxRevit.TryMeasure '{box.Name}': {ex.Message}"); }
            m = new Measure
            {
                WidthM = M(bb.Max.X - bb.Min.X), DepthM = M(bb.Max.Y - bb.Min.Y),
                ZMinFt = bb.Min.Z, ZMaxFt = bb.Max.Z,
                Centre = new XYZ((bb.Min.X + bb.Max.X) / 2, (bb.Min.Y + bb.Max.Y) / 2, (bb.Min.Z + bb.Max.Z) / 2),
            };
            if (m.WidthM <= 0 || m.DepthM <= 0) { why = "has no plan extent"; return false; }
            return true;
        }

        /// <summary>Every STING-SEED box, measured. Rotated or empty seeds are reported and left out.</summary>
        public static List<ScopeBoxSeed> Seeds(Document doc, List<string> warnings)
        {
            var seeds = new List<ScopeBoxSeed>();
            foreach (var el in AllBoxes(doc).Where(e => ScopeBoxNames.Classify(e.Name) == ScopeBoxKind.Seed))
            {
                if (!TryMeasure(el, out var m, out var why)) { warnings?.Add($"Seed '{el.Name}' {why} — not used."); continue; }
                seeds.Add(new ScopeBoxSeed { Id = el.Id.Value.ToString(), Name = el.Name, WidthM = m.WidthM, DepthM = m.DepthM });
            }
            return seeds;
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
        /// The building's extent from its fabric (walls, floors, roofs, columns …). With a
        /// level, only elements hosted on that level — for per-level footprints.
        /// </summary>
        public static ScopeBoxFootprint ModelFootprint(Document doc, Level level, string loc = null)
        {
            var fp = new ScopeBoxFootprint { Loc = loc, Level = level != null ? ParameterHelpers.GetLevelCodeForLevel(level) : null };
            var els = new FilteredElementCollector(doc).WherePasses(new ElementMulticategoryFilter(_footprintCats.ToList()))
                .WhereElementIsNotElementType();
            foreach (var el in els)
            {
                if (level != null && el.LevelId != level.Id) continue;
                var bb = el.get_BoundingBox(null);
                if (bb == null) continue;
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

        /// <summary>
        /// Copy the seed of each New box into place, turn it, and name it. Call inside a
        /// transaction. A box whose seed is gone, or whose name is taken since planning,
        /// is reported and skipped; nothing is created unnamed.
        /// </summary>
        public static int Create(Document doc, IEnumerable<PlannedScopeBox> boxes, List<string> report)
        {
            int made = 0;
            var names = new HashSet<string>(AllBoxes(doc).Select(e => e.Name ?? ""), StringComparer.OrdinalIgnoreCase);
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
            double lowest = levels.Count > 0 ? levels.Min(l => l.Elevation) : 0, highest = levels.Count > 0 ? levels.Max(l => l.Elevation) : 0;
            var reachWarned = new HashSet<string>();

            foreach (var b in boxes.Where(x => x.Status == PlannedBoxStatus.New))
            {
                if (names.Contains(b.Name)) { report.Add($"{b.Name}: already exists — kept."); continue; }
                if (!long.TryParse(b.SeedId, out var idVal) || !(doc.GetElement(new ElementId(idVal)) is Element seed))
                { report.Add($"{b.Name}: its seed is no longer in the project — re-plan."); continue; }
                if (!TryMeasure(seed, out var m, out var why)) { report.Add($"{b.Name}: seed '{seed.Name}' {why}."); continue; }

                if ((m.ZMinFt > lowest || m.ZMaxFt < highest) && reachWarned.Add(seed.Name))
                    report.Add($"Seed '{seed.Name}' does not span every level, so plans on the levels it misses cannot use its copies. "
                             + "Make the seed taller in a 3D view and re-create.");

                var target = new XYZ(Ft(b.CentreX), Ft(b.CentreY), m.Centre.Z);
                var copied = ElementTransformUtils.CopyElement(doc, seed.Id, target - m.Centre);
                var id = copied?.FirstOrDefault();
                if (id == null || !(doc.GetElement(id) is Element el)) { report.Add($"{b.Name}: copy failed."); continue; }

                double angle = b.AngleRad + (b.SeedRotated ? Math.PI / 2 : 0);
                if (Math.Abs(angle) > 1e-9)
                    ElementTransformUtils.RotateElement(doc, id, Line.CreateBound(target, target + XYZ.BasisZ), angle);
                try { el.Name = b.Name; names.Add(b.Name); made++; }
                catch (Exception ex)
                {
                    // An unnamed copy would be a stray duplicate of the seed. Remove it.
                    doc.Delete(id);
                    report.Add($"{b.Name}: could not be named ({ex.Message}) — the copy was removed.");
                }
            }
            return made;
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

        /// <summary>
        /// Colour every STING box in <paramref name="views"/> by <paramref name="mode"/>; Off
        /// clears. Only boxes with a STING prefix are touched. Returns boxes coloured, and
        /// counts refusals into <paramref name="report"/> rather than dropping them.
        /// </summary>
        public static int Colour(Document doc, IEnumerable<View> views, ScopeBoxStyle style, ScopeBoxColourMode mode,
            ScopeBoxPlanFile plan, List<string> report)
        {
            var boxes = AllBoxes(doc).Where(e => ScopeBoxNames.Classify(e.Name) != ScopeBoxKind.Plain).ToList();
            var subjects = boxes.ToDictionary(b => b.Id, b => Subject(doc, b, plan));
            var colours = style.Assign(mode, subjects.Values.Select(s => s.KeyFor(mode)).Where(k => k != null));
            int weight = Math.Max(1, Math.Min(16, style.Values.LineWeight));

            int applied = 0, refused = 0, badHex = 0, noKey = 0;
            foreach (var v in views)
                foreach (var b in boxes)
                {
                    var ogs = new OverrideGraphicSettings();
                    if (mode != ScopeBoxColourMode.Off)
                    {
                        // A box with nothing to colour by in this mode is CLEARED, not left
                        // as it was — otherwise switching mode leaves the last mode's colour
                        // on it and the view no longer shows what the mode says.
                        var key = subjects[b.Id].KeyFor(mode);
                        if (key == null || !colours.TryGetValue(key, out var hex)) noKey++;
                        else if (!ScopeBoxStyle.TryParseHex(hex, out var r, out var g, out var bl)) badHex++;
                        else
                        {
                            ogs.SetProjectionLineColor(new Color(r, g, bl));
                            ogs.SetProjectionLineWeight(weight);
                        }
                    }
                    try { v.SetElementOverrides(b.Id, ogs); applied++; }
                    catch (Exception ex) { refused++; StingLog.Warn($"ScopeBox colour '{b.Name}' in '{v.Name}': {ex.Message}"); }
                }
            if (refused > 0) report.Add($"{refused} box/view pair(s) refused the override — see the log.");
            if (badHex > 0) report.Add($"{badHex} box/view pair(s) were left uncoloured: their colour in the style file is not #RRGGBB.");
            if (noKey > 0 && mode != ScopeBoxColourMode.Off)
                report.Add($"{noKey} box/view pair(s) have nothing to colour by in '{mode}' mode (e.g. a seed has no size class) — shown uncoloured.");
            return applied;
        }
    }
}
