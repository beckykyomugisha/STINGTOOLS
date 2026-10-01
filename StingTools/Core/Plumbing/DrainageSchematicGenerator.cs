// DrainageSchematicGenerator — 2D drainage riser schematic in a Revit Drafting View.
// Phase 179d.
//
// Walks the PipeNetwork, lays out stacks vertically and branches horizontally,
// then draws DetailLine + TextNote annotations in a new ViewDrafting at 1:50.
//
// Draws only what is modelled: a stack is a Sanitary pipe run that is more than
// 80 % vertical, about a storey tall and passing through a level (one physical
// stack = its per-storey segments grouped by plan position; a WC tail or trap
// drop is not a stack — PipeNetworkBuilder / SchematicLayoutMath.StackRuns), a branch is a drain pipe leaving a fitting on that stack, a vent
// is a Vent-classified pipe connected to the stack, and floors are the
// document's Levels. Anything the model does not supply is left out and named
// in the warnings — never drawn from a default. No stack means no view: the
// command rolls the transaction back and reports why.
//
// All drafting-view coordinates are in feet (Revit internal units). The vertical
// axis is real elevation (above the lowest Level shown); glyph sizes and text
// offsets are paper millimetres × view scale.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;

namespace StingTools.Core.Plumbing
{
    // ──────────────────────────────────────────────────────────────────────────
    // Data model
    // ──────────────────────────────────────────────────────────────────────────

    public enum SchematicNodeType
    {
        Stack,
        Branch,
        Fixture,
        Vent,
        Termination,
        Junction
    }

    public enum SchematicLineStyle
    {
        Solid,
        Dashed,     // vent pipes
        Hidden
    }

    public class SchematicNode
    {
        public string           Label         = "";
        public XYZ              Position      = XYZ.Zero;
        public SchematicNodeType Type         = SchematicNodeType.Stack;
        public int              DnMm          = 100;
        public double           Dfu           = 0.0;
        public ElementId        SourcePipeId  = ElementId.InvalidElementId;
    }

    public class SchematicLine
    {
        public XYZ              Start         = XYZ.Zero;
        public XYZ              End           = XYZ.Zero;
        public SchematicLineStyle Style       = SchematicLineStyle.Solid;
        /// <summary>DN label placed at mid-point, empty = no label.</summary>
        public string           Label         = "";
    }

    public class SchematicResult
    {
        public ElementId    ViewId               = ElementId.InvalidElementId;
        /// <summary>Stacks actually drawn (the stack line was created).</summary>
        public int          NodesDrawn           = 0;
        public int          BranchesDrawn        = 0;
        public int          VentsDrawn           = 0;
        public int          LevelsLabelled       = 0;
        public int          LinesDrawn           = 0;
        public int          AnnotationsPlaced    = 0;
        public List<string> Warnings             = new List<string>();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Options
    // ──────────────────────────────────────────────────────────────────────────

    public class DrainageSchematicOptions
    {
        /// <summary>
        /// Draw only stacks on this PipingSystem (exact name); empty = all drainage
        /// systems. Vents on other systems connected to those stacks are still drawn.
        /// </summary>
        public string SystemNameFilter    = "";
        /// <summary>
        /// Pipe-system classifications drawn. Default sanitary + vent: without it every
        /// pipe in the model (cold water, heating …) was drawn as drainage stacks.
        /// </summary>
        public PipeSystemType[] Classifications = { PipeSystemType.Sanitary, PipeSystemType.Vent };
        /// <summary>Horizontal spacing between adjacent stacks in the schematic (model mm).</summary>
        public double StackSpacingMm     = 2000.0;
        /// <summary>Unused since floors come from the document's Levels; kept for callers.</summary>
        public double LevelHeightMm      = 3000.0;
        public bool   ShowVents          = true;
        public bool   ShowFixtureSymbols = true;
        public bool   ShowDnLabels       = true;
        public bool   ShowSlopeLabels    = true;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Internal layout model
    // ──────────────────────────────────────────────────────────────────────────

    internal class StackLayout
    {
        public List<PipeNode> Segments = new List<PipeNode>();
        public double      ZMin;          // real elevation, ft
        public double      ZMax;
        public int         DnMm;
        public string      SystemName;
        public List<BranchLayout> Branches = new List<BranchLayout>();
        /// <summary>Null when no Vent-classified pipe connects to this stack.</summary>
        public VentLayout  Vent;
    }

    internal class VentLayout
    {
        public int    DnMm;
        public double ZMin;
        public double ZMax;
    }

    internal class BranchLayout
    {
        public double   Z;              // real elevation of the junction, ft
        public int      FixtureCount;
        public int      BranchDnMm;
        public double?  SlopePct;       // null = not known
        public string   FixtureLabel;
        public bool     IsLeft;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Generator
    // ──────────────────────────────────────────────────────────────────────────

    public static class DrainageSchematicGenerator
    {
        private const double MmToFt  = 1.0 / 304.8;
        // ISO 3098 text heights (paper mm).
        private const double TextSizeSmall  = 2.5;
        private const double TextSizeNormal = 3.5;
        // Segments within this plan distance are one stack.
        private const double StackPlanToleranceFt = 150 * MmToFt;
        private const double LevelToleranceFt     = 50 * MmToFt;

        // ── Main entry ────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a drainage riser schematic in a new Drafting View.
        /// Must be called inside an active Transaction.
        /// </summary>
        public static SchematicResult Generate(Document doc, DrainageSchematicOptions opts)
        {
            var result = new SchematicResult();

            if (doc == null)
            {
                result.Warnings.Add("Document is null — cannot generate schematic.");
                return result;
            }

            opts = opts ?? new DrainageSchematicOptions();

            try
            {
                // 1. Build pipe network ─────────────────────────────────────────
                // Not filtered by name here: a vent on another system connected to a
                // picked stack must still be found. The name filter picks stacks below.
                PipeNetwork network;
                try
                {
                    network = PipeNetworkBuilder.Build(doc, null, opts.Classifications);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"PipeNetworkBuilder.Build failed: {ex.Message}");
                    StingLog.Error("DrainageSchematicGenerator: PipeNetworkBuilder.Build", ex);
                    network = new PipeNetwork();
                }

                string classText = string.Join(" / ", opts.Classifications ?? new PipeSystemType[0]);
                string filterText = string.IsNullOrWhiteSpace(opts.SystemNameFilter) ? "" : $" named '{opts.SystemNameFilter}'";

                // Nothing to draw: no view, and the reason (never an empty drafting view).
                if (network.Nodes.Count == 0)
                {
                    result.Warnings.Add($"No drainage pipework found (no pipe on a system classified {classText}{filterText}) — nothing to draw.");
                    return result;
                }

                // 2. Real stacks only ───────────────────────────────────────────
                var stackSegments = network.Nodes
                    .Where(n => n.Type == PipeNodeType.Stack
                             && KindOf(n) == SchematicPipeKind.DrainPipe
                             && SchematicLayoutMath.SystemNameMatches(n.SystemName, opts.SystemNameFilter))
                    .ToList();

                if (stackSegments.Count == 0)
                {
                    result.Warnings.Add($"No drainage stack is modelled (no non-vent pipe on a system classified {classText}{filterText} "
                        + "runs vertically through a level for at least about a storey). The schematic draws only modelled stacks — nothing was drawn.");
                    return result;
                }

                var levels = CollectLevels(doc);
                var levelElevs = levels.Select(l => l.Elevation).ToList();

                var layouts = BuildStackLayouts(doc, stackSegments, network, opts, result);
                if (layouts.Count == 0)
                {
                    result.Warnings.Add("Stack pipes were found but none has a readable centreline — nothing was drawn.");
                    return result;
                }

                // 3. Drafting view ─────────────────────────────────────────────
                // DTW-119: one view per name, reused and cleared on a re-run (its sheet
                // placement survives). A new view every run ("… (2)", "… (3)") left the
                // previous run's view orphaned once the sheet took the new one.
                var view = StingTools.Core.Drawing.SchematicViewFactory.CreateOrReplace(
                    doc, ViewName(opts), out string viewError, 50);
                if (view == null)
                {
                    result.Warnings.Add(viewError ?? "The drainage schematic view could not be made.");
                    return result;
                }

                result.ViewId = view.Id;
                int scale = view.Scale;
                double P(double paperMm) => SchematicLayoutMath.PaperMmToModelFt(paperMm, scale);

                double stackSpacingFt = Math.Max(opts.StackSpacingMm * MmToFt, P(30));

                var textTypeNormal = FindClosestTextType(doc, TextSizeNormal);
                var textTypeSmall  = FindClosestTextType(doc, TextSizeSmall);
                ElementId normalId = textTypeNormal?.Id ?? ElementId.InvalidElementId;
                ElementId smallId  = textTypeSmall?.Id  ?? ElementId.InvalidElementId;

                var lineStyles = GetLineStyleIds(doc);

                // Vertical datum: the lowest Level any stack passes, else the lowest stack foot.
                double globalZMin = layouts.Min(l => l.ZMin);
                double globalZMax = layouts.Max(l => Math.Max(l.ZMax, l.Vent?.ZMax ?? l.ZMax));
                var spannedLevels = SchematicLayoutMath.LevelsSpanning(levelElevs, globalZMin, globalZMax, LevelToleranceFt);
                double baseZ = spannedLevels.Count > 0 ? Math.Min(levelElevs[spannedLevels[0]], globalZMin) : globalZMin;
                double Y(double z) => z - baseZ;

                if (levels.Count == 0)
                    result.Warnings.Add("The document has no Levels — floor lines were not drawn.");
                else if (spannedLevels.Count == 0)
                    result.Warnings.Add("No Level lies within the height of the stacks — floor lines were not drawn.");

                double tickHalf = P(3);

                // 4. Floor labels (left of the first stack, from real Levels) ─────
                foreach (int li in spannedLevels)
                {
                    if (TryPlaceTextNote(doc, view,
                            new XYZ(-tickHalf - P(22), Y(levels[li].Elevation) + P(1.5), 0),
                            levels[li].Name, smallId, result))
                        result.LevelsLabelled++;
                }

                // 5. Draw each stack ────────────────────────────────────────────
                for (int si = 0; si < layouts.Count; si++)
                {
                    var sl = layouts[si];
                    double cx = si * stackSpacingFt;
                    double yBottom = Y(sl.ZMin);
                    double yTop    = Y(sl.ZMax);

                    // Stack vertical line ──────────────────────────────────────
                    if (!TryDrawDetailLine(doc, view,
                            new XYZ(cx, yBottom, 0), new XYZ(cx, yTop, 0),
                            lineStyles.Solid, result))
                        continue;   // the stack itself failed — draw nothing that hangs off it
                    result.NodesDrawn++;

                    // Head of the stack — short horizontal tick (paper size).
                    TryDrawDetailLine(doc, view,
                        new XYZ(cx - P(2), yTop, 0), new XYZ(cx + P(2), yTop, 0),
                        lineStyles.Solid, result);

                    // Stack label ──────────────────────────────────────────────
                    if (opts.ShowDnLabels)
                    {
                        string stackLabel = sl.DnMm > 0 ? $"DN{sl.DnMm} STACK" : "STACK";
                        if (!string.IsNullOrEmpty(sl.SystemName))
                            stackLabel += $"\n{sl.SystemName}";
                        TryPlaceTextNote(doc, view,
                            new XYZ(cx + P(1), yTop + P(8), 0),
                            stackLabel, normalId, result);
                    }

                    // Floor level ticks — the Levels this stack passes ─────────
                    foreach (int li in SchematicLayoutMath.LevelsSpanning(levelElevs, sl.ZMin, sl.ZMax, LevelToleranceFt))
                    {
                        double fy = Y(levels[li].Elevation);
                        TryDrawDetailLine(doc, view,
                            new XYZ(cx - tickHalf, fy, 0), new XYZ(cx + tickHalf, fy, 0),
                            lineStyles.Solid, result);
                    }

                    // Vent — only a modelled Vent pipe connected to this stack ─
                    if (opts.ShowVents && sl.Vent != null)
                    {
                        double ventX = cx + P(4);
                        double vy0 = Y(sl.Vent.ZMin), vy1 = Y(sl.Vent.ZMax);
                        bool drawn;
                        if (vy1 - vy0 > P(1))
                        {
                            TryDrawDetailLine(doc, view,
                                new XYZ(cx, vy0, 0), new XYZ(ventX, vy0, 0), lineStyles.Dashed, result);
                            drawn = TryDrawDetailLine(doc, view,
                                new XYZ(ventX, vy0, 0), new XYZ(ventX, vy1, 0), lineStyles.Dashed, result);
                        }
                        else
                        {
                            drawn = TryDrawDetailLine(doc, view,
                                new XYZ(cx, vy0, 0), new XYZ(ventX + P(4), vy0, 0), lineStyles.Dashed, result);
                        }
                        if (drawn)
                        {
                            result.VentsDrawn++;
                            if (opts.ShowDnLabels)
                                TryPlaceTextNote(doc, view,
                                    new XYZ(ventX + P(1), (vy0 + vy1) * 0.5 + P(1), 0),
                                    sl.Vent.DnMm > 0 ? $"DN{sl.Vent.DnMm} VENT" : "VENT",
                                    smallId, result);
                        }
                    }

                    // Branches — drain pipes leaving fittings on the stack ─────
                    foreach (var br in sl.Branches)
                    {
                        double by     = Y(br.Z);
                        double brLen  = stackSpacingFt * 0.45;
                        double brEndX = br.IsLeft ? cx - brLen : cx + brLen;

                        if (!TryDrawDetailLine(doc, view,
                                new XYZ(cx, by, 0), new XYZ(brEndX, by, 0),
                                lineStyles.Solid, result))
                            continue;
                        result.BranchesDrawn++;

                        if (opts.ShowDnLabels || opts.ShowSlopeLabels)
                        {
                            var parts = new List<string>();
                            if (opts.ShowDnLabels && br.BranchDnMm > 0)
                                parts.Add($"DN{br.BranchDnMm}");
                            if (opts.ShowSlopeLabels)
                                parts.Add(SchematicLayoutMath.SlopeLabel(br.SlopePct));

                            if (parts.Any())
                                TryPlaceTextNote(doc, view,
                                    new XYZ(Math.Min(cx, brEndX) + P(1), by + P(4), 0),
                                    string.Join(" ", parts), smallId, result);
                        }

                        if (opts.ShowFixtureSymbols && br.FixtureCount > 0)
                        {
                            DrawFixtureSymbol(doc, view, brEndX, by, br.IsLeft, P(2), lineStyles.Solid, result);
                            TryPlaceTextNote(doc, view,
                                new XYZ(brEndX + (br.IsLeft ? -P(20) : P(3)), by + P(4), 0),
                                br.FixtureLabel, smallId, result);
                        }
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"DrainageSchematicGenerator.Generate: {ex.Message}");
                StingLog.Error("DrainageSchematicGenerator.Generate", ex);
                return result;
            }
        }

        // ── View family helpers ───────────────────────────────────────────────

        public static ViewFamilyType FindDraftingViewType(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Drafting);
        }

        // ── Text note type helper ─────────────────────────────────────────────

        /// <summary>
        /// The text type whose printed height (TEXT_SIZE is a paper size) is closest to
        /// <paramref name="paperHeightMm"/>. Null when the document has none.
        /// </summary>
        internal static TextNoteType FindClosestTextType(Document doc, double paperHeightMm)
        {
            try
            {
                var types = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .Cast<TextNoteType>()
                    .ToList();
                if (types.Count == 0) return null;

                var heights = types.Select(t =>
                {
                    try { return t.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() ?? double.NaN; }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"FindClosestTextType: TEXT_SIZE of '{t.Name}': {ex.Message}");
                        return double.NaN;
                    }
                }).ToList();

                int ix = SchematicLayoutMath.ClosestIndex(heights, paperHeightMm * MmToFt);
                return ix >= 0 ? types[ix] : types[0];
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FindClosestTextType({paperHeightMm}mm): {ex.Message}");
                return null;
            }
        }

        // ── Levels ────────────────────────────────────────────────────────────

        private static List<(string Name, double Elevation)> CollectLevels(Document doc)
        {
            try
            {
                // ProjectElevation is relative to the internal origin — the same datum as
                // the pipe coordinates the stacks are measured in.
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .Select(l => (l.Name ?? "", l.ProjectElevation))
                    .OrderBy(l => l.Item2)
                    .ToList();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DrainageSchematicGenerator: levels: {ex.Message}");
                return new List<(string, double)>();
            }
        }

        // ── Graph classification ──────────────────────────────────────────────

        private static SchematicPipeKind KindOf(PipeNode n)
        {
            if (n == null) return SchematicPipeKind.Other;
            if (n.Type == PipeNodeType.Fixture)   return SchematicPipeKind.Fixture;
            if (n.Type == PipeNodeType.Equipment) return SchematicPipeKind.Other;
            if (n.IsPipeElement)
                return n.Classification == PipeSystemType.Vent ? SchematicPipeKind.VentPipe : SchematicPipeKind.DrainPipe;
            // Fittings and in-line accessories (cleanouts, AAVs …).
            return SchematicPipeKind.Fitting;
        }

        private static IEnumerable<long> NeighbourIds(PipeNetwork net, long id)
        {
            if (!net.ById.TryGetValue(id, out var n)) yield break;
            foreach (var e in n.Upstream)   if (e.From != null) yield return e.From.Id.Value;
            foreach (var e in n.Downstream) if (e.To   != null) yield return e.To.Id.Value;
        }

        private static bool TryGetZExtent(Document doc, PipeNode n, out double zMin, out double zMax)
        {
            zMin = zMax = 0;
            try
            {
                if (doc.GetElement(n.Id) is Pipe p && p.Location is LocationCurve lc && lc.Curve != null)
                {
                    double a = lc.Curve.GetEndPoint(0).Z, b = lc.Curve.GetEndPoint(1).Z;
                    zMin = Math.Min(a, b); zMax = Math.Max(a, b);
                    return true;
                }
            }
            catch (Exception ex) { StingLog.Warn($"DrainageSchematicGenerator: extent of {n?.Id}: {ex.Message}"); }
            return false;
        }

        // ── Layout builder ────────────────────────────────────────────────────

        private static List<StackLayout> BuildStackLayouts(Document doc,
            List<PipeNode> stackSegments, PipeNetwork network, DrainageSchematicOptions opts,
            SchematicResult result)
        {
            var layouts = new List<StackLayout>();

            var groups = SchematicLayoutMath.ClusterByPlanPosition(
                stackSegments.Select(n => (n.Id.Value, n.Position?.X ?? 0, n.Position?.Y ?? 0)).ToList(),
                StackPlanToleranceFt);

            Func<long, IEnumerable<long>> neighbours = id => NeighbourIds(network, id);
            Func<long, SchematicPipeKind> kind = id => network.ById.TryGetValue(id, out var n) ? KindOf(n) : SchematicPipeKind.Other;
            Func<long, double> dn = id => network.ById.TryGetValue(id, out var n) ? n.DnMm : 0;
            var allStackIds = new HashSet<long>(stackSegments.Select(n => n.Id.Value));

            foreach (var g in groups)
            {
                var segs = g.Select(id => network.ById[id]).ToList();
                double zMin = double.MaxValue, zMax = double.MinValue;
                foreach (var s in segs)
                {
                    if (!TryGetZExtent(doc, s, out var a, out var b)) continue;
                    zMin = Math.Min(zMin, a); zMax = Math.Max(zMax, b);
                }
                if (zMin > zMax) continue;

                var sl = new StackLayout
                {
                    Segments   = segs,
                    ZMin       = zMin,
                    ZMax       = zMax,
                    DnMm       = (int)Math.Round(segs.Max(s => s.DnMm)),
                    SystemName = segs.Select(s => s.SystemName).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? ""
                };

                // Vent: a modelled Vent pipe connected to the stack, with its real DN.
                if (opts.ShowVents)
                {
                    var hit = SchematicGraphRules.FindStackVent(g, neighbours, kind, dn);
                    if (hit != null)
                    {
                        double vMin = double.MaxValue, vMax = double.MinValue;
                        foreach (var vid in hit.VentIds)
                        {
                            if (!network.ById.TryGetValue(vid, out var vn)) continue;
                            if (!TryGetZExtent(doc, vn, out var a, out var b)) continue;
                            vMin = Math.Min(vMin, a); vMax = Math.Max(vMax, b);
                        }
                        if (vMin <= vMax)
                            sl.Vent = new VentLayout { DnMm = (int)Math.Round(hit.DnMm), ZMin = vMin, ZMax = vMax };
                    }
                }

                // Branches: drain pipes leaving a fitting on the stack.
                var groupSet = new HashSet<long>(g);
                var seenBranchPipes = new HashSet<long>();
                int branchIdx = 0;
                var junctions = g.SelectMany(id => neighbours(id))
                                 .Distinct()
                                 .Where(id => !groupSet.Contains(id) && kind(id) == SchematicPipeKind.Fitting)
                                 .Select(id => network.ById[id])
                                 .OrderBy(j => j.Position?.Z ?? 0)
                                 .ToList();
                foreach (var j in junctions)
                {
                    foreach (var bid in neighbours(j.Id.Value).Distinct())
                    {
                        if (groupSet.Contains(bid) || !seenBranchPipes.Add(bid)) continue;
                        if (kind(bid) != SchematicPipeKind.DrainPipe) continue;
                        var bn = network.ById[bid];
                        if (bn.Type == PipeNodeType.Stack) continue;  // another stack, not a branch

                        var edge = j.Upstream.Concat(j.Downstream)
                            .FirstOrDefault(e => e.From == bn || e.To == bn);
                        double? slope = edge != null && edge.LengthM > 0 ? edge.SlopePct : (double?)null;

                        // Stop at this stack and every other one: a main joining two stacks
                        // must not credit one stack's fixtures to the other.
                        var blocked = new HashSet<long>(allStackIds) { j.Id.Value };
                        int fixtures = SchematicGraphRules.CountFixtures(bid, blocked, neighbours, kind);

                        sl.Branches.Add(new BranchLayout
                        {
                            Z            = j.Position?.Z ?? bn.Position?.Z ?? zMin,
                            FixtureCount = fixtures,
                            BranchDnMm   = (int)Math.Round(bn.DnMm),
                            SlopePct     = slope,
                            FixtureLabel = fixtures > 0 ? $"FIXTURES × {fixtures}" : "",
                            IsLeft       = branchIdx % 2 != 0
                        });
                        branchIdx++;
                    }
                }

                if (sl.Branches.Count == 0)
                    result.Warnings.Add($"Stack {g[0]}: no drain branch is connected to a fitting on it — drawn without branches.");
                if (opts.ShowVents && sl.Vent == null)
                    result.Warnings.Add($"Stack {g[0]}: no Vent-classified pipe connects to it — no vent drawn.");

                layouts.Add(sl);
            }

            return layouts;
        }

        // ── Drawing helpers ───────────────────────────────────────────────────

        /// <summary>Draws one detail line; true only when it was created.</summary>
        private static bool TryDrawDetailLine(Document doc, View view,
            XYZ start, XYZ end, ElementId lineStyleId, SchematicResult result)
        {
            try
            {
                if (start.IsAlmostEqualTo(end)) return false;
                var line = Line.CreateBound(start, end);
                var dl = doc.Create.NewDetailCurve(view, line);
                if (lineStyleId != null && lineStyleId != ElementId.InvalidElementId)
                {
                    try { dl.LineStyle = doc.GetElement(lineStyleId) as GraphicsStyle; }
                    catch (Exception ex) { StingLog.Warn($"DrainageSchematic: line style: {ex.Message}"); }
                }
                result.LinesDrawn++;
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DrainageSchematic: detail line failed: {ex.Message}");
                result.Warnings.Add($"DetailLine failed ({start.X:F2},{start.Y:F2})→({end.X:F2},{end.Y:F2}): {ex.Message}");
                return false;
            }
        }

        private static bool TryPlaceTextNote(Document doc, View view,
            XYZ position, string text, ElementId textTypeId, SchematicResult result)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            try
            {
                if (textTypeId == null || textTypeId == ElementId.InvalidElementId)
                    textTypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
                if (textTypeId == null || textTypeId == ElementId.InvalidElementId)
                {
                    result.Warnings.Add($"TextNote '{text}': the document has no text type.");
                    return false;
                }
                TextNote.Create(doc, view.Id, position, text, textTypeId);
                result.AnnotationsPlaced++;
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DrainageSchematic: text note failed: {ex.Message}");
                result.Warnings.Add($"TextNote failed '{text}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Draws a simple fixture symbol: two diagonal lines forming a 'V' at the branch end.
        /// </summary>
        private static void DrawFixtureSymbol(Document doc, View view,
            double x, double y, bool isLeft, double sizeFt, ElementId lineStyleId, SchematicResult result)
        {
            double dir = isLeft ? -1 : 1;

            TryDrawDetailLine(doc, view,
                new XYZ(x, y, 0),
                new XYZ(x + dir * sizeFt, y + sizeFt, 0),
                lineStyleId, result);

            TryDrawDetailLine(doc, view,
                new XYZ(x, y, 0),
                new XYZ(x + dir * sizeFt * 0.5, y - sizeFt, 0),
                lineStyleId, result);
        }

        // ── Line style lookup ─────────────────────────────────────────────────

        private static (ElementId Solid, ElementId Dashed) GetLineStyleIds(Document doc)
        {
            ElementId solid  = ElementId.InvalidElementId;
            ElementId dashed = ElementId.InvalidElementId;
            try
            {
                var linesCategory = doc.Settings.Categories
                    .get_Item(BuiltInCategory.OST_Lines);

                if (linesCategory?.SubCategories != null)
                {
                    foreach (Category sub in linesCategory.SubCategories)
                    {
                        string name = sub.Name ?? "";
                        if (name.IndexOf("Dash", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Hidden", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (dashed == ElementId.InvalidElementId)
                                dashed = sub.GetGraphicsStyle(GraphicsStyleType.Projection)?.Id ?? ElementId.InvalidElementId;
                        }
                        else if (name.IndexOf("Thin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 name.IndexOf("Medium", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (solid == ElementId.InvalidElementId)
                                solid = sub.GetGraphicsStyle(GraphicsStyleType.Projection)?.Id ?? ElementId.InvalidElementId;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"GetLineStyleIds: {ex.Message}");
            }

            return (solid, dashed == ElementId.InvalidElementId ? solid : dashed);
        }

        /// <summary>The view's name: one per system filter, so a re-run reuses it.</summary>
        internal static string ViewName(DrainageSchematicOptions opts)
            => "STING - Drainage Schematic Riser"
             + (string.IsNullOrWhiteSpace(opts?.SystemNameFilter) ? "" : " " + opts.SystemNameFilter.Trim());
    }
}
