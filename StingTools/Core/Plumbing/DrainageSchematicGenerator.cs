// DrainageSchematicGenerator — 2D drainage riser schematic in a Revit Drafting View.
// Phase 179d.
//
// Walks the PipeNetwork, lays out stacks vertically and branches horizontally,
// then draws DetailLine + TextNote annotations in a new ViewDrafting at 1:50.
//
// All drafting-view coordinates are in feet (Revit internal units).
// 1 mm = 1/304.8 ft  →  constant MmToFt used throughout.

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
        public int          NodesDrawn           = 0;
        public int          LinesDrawn           = 0;
        public int          AnnotationsPlaced    = 0;
        public List<string> Warnings             = new List<string>();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Options
    // ──────────────────────────────────────────────────────────────────────────

    public class DrainageSchematicOptions
    {
        /// <summary>Filter to a single named system; empty = all drainage systems.</summary>
        public string SystemNameFilter    = "";
        /// <summary>Horizontal spacing between adjacent stacks in the schematic (mm).</summary>
        public double StackSpacingMm     = 800.0;
        /// <summary>Vertical height representing one floor in the schematic (mm).</summary>
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
        public PipeNode    Node;
        public int         ColumnIndex;   // 0-based left-to-right
        public int         FloorCount;    // number of levels served
        public string      SystemName;
        public List<BranchLayout> Branches = new List<BranchLayout>();
        public bool        HasVent;
        public int         VentDnMm;
    }

    internal class BranchLayout
    {
        public int      FloorIndex;     // 0 = ground
        public int      FixtureCount;
        public int      BranchDnMm;
        public double   SlopePct;
        public string   FixtureLabel;   // e.g. "WC × 4"
        public bool     IsLeft;         // branch goes left (alternate sides per floor)
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Generator
    // ──────────────────────────────────────────────────────────────────────────

    public static class DrainageSchematicGenerator
    {
        private const double MmToFt  = 1.0 / 304.8;
        private const double TextSizeSmall  = 2.5;   // mm — passed to FindOrCreateTextType
        private const double TextSizeNormal = 3.0;

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
                PipeNetwork network;
                try
                {
                    network = PipeNetworkBuilder.Build(doc,
                        string.IsNullOrWhiteSpace(opts.SystemNameFilter) ? null : opts.SystemNameFilter);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"PipeNetworkBuilder.Build failed: {ex.Message}");
                    network = new PipeNetwork();
                }

                // 2. Identify stacks (nodes typed Stack or with many downstream edges) ──
                var stackNodes = network.Nodes
                    .Where(n => n.Type == PipeNodeType.Stack ||
                                (n.Downstream.Count >= 2 && n.DnMm >= 75))
                    .ToList();

                if (!stackNodes.Any())
                {
                    // Fallback: treat nodes with highest accumulated DFU as stacks
                    stackNodes = network.Nodes
                        .OrderByDescending(n => n.DfuAccumulated)
                        .Take(Math.Max(1, network.Nodes.Count / 4))
                        .ToList();
                }

                // 3. Create Drafting View ───────────────────────────────────────
                var vft = FindDraftingViewType(doc);
                if (vft == null)
                {
                    result.Warnings.Add("No Drafting ViewFamilyType found — cannot create view.");
                    return result;
                }

                ViewDrafting view;
                try
                {
                    view = ViewDrafting.Create(doc, vft.Id);
                    view.Name = GenerateUniqueViewName(doc, "STING - Drainage Schematic Riser");
                    view.Scale = 50;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Could not create ViewDrafting: {ex.Message}");
                    return result;
                }

                result.ViewId = view.Id;

                // 4. Build layout ───────────────────────────────────────────────
                var layouts = BuildStackLayouts(stackNodes, network, opts);

                double stackSpacingFt = opts.StackSpacingMm * MmToFt;
                double levelHeightFt  = opts.LevelHeightMm  * MmToFt;

                var textTypeNormal = FindOrCreateTextType(doc, TextSizeNormal);
                var textTypeSmall  = FindOrCreateTextType(doc, TextSizeSmall);

                // GraphicsStyle for lines ── use default thin line
                var lineStyles = GetLineStyleIds(doc);

                // Solid fill for future use (symbols)
                ElementId solidFillId = GetSolidFillPatternId(doc);

                // 5. Draw each stack ────────────────────────────────────────────
                for (int si = 0; si < layouts.Count; si++)
                {
                    var sl = layouts[si];
                    double cx = si * stackSpacingFt;
                    double yBottom = 0.0;
                    double yTop    = sl.FloorCount * levelHeightFt;

                    // Stack vertical line ──────────────────────────────────────
                    TryDrawDetailLine(doc, view,
                        new XYZ(cx, yBottom, 0),
                        new XYZ(cx, yTop,    0),
                        lineStyles.Solid, result);

                    // Stack termination (open top — short horizontal tick) ─────
                    TryDrawDetailLine(doc, view,
                        new XYZ(cx - 0.2 * MmToFt * 100, yTop, 0),
                        new XYZ(cx + 0.2 * MmToFt * 100, yTop, 0),
                        lineStyles.Solid, result);

                    // Stack label ──────────────────────────────────────────────
                    if (opts.ShowDnLabels)
                    {
                        string stackLabel = $"DN{sl.Node.DnMm} STACK";
                        if (!string.IsNullOrEmpty(sl.SystemName))
                            stackLabel += $"\n{sl.SystemName}";

                        TryPlaceTextNote(doc, view,
                            new XYZ(cx + 0.05, yTop + 0.1, 0),
                            stackLabel,
                            textTypeNormal?.Id ?? ElementId.InvalidElementId,
                            result);
                    }

                    // Floor level tick marks ───────────────────────────────────
                    for (int fi = 0; fi <= sl.FloorCount; fi++)
                    {
                        double fy = fi * levelHeightFt;
                        double tickLen = 150 * MmToFt;
                        TryDrawDetailLine(doc, view,
                            new XYZ(cx - tickLen, fy, 0),
                            new XYZ(cx + tickLen, fy, 0),
                            lineStyles.Solid, result);

                        // Floor level label (left of first stack only)
                        if (si == 0)
                        {
                            string lvlLabel = fi == 0 ? "GL" : $"L{fi:D2}";
                            TryPlaceTextNote(doc, view,
                                new XYZ(cx - tickLen - 0.15, fy - 0.05, 0),
                                lvlLabel,
                                textTypeSmall?.Id ?? ElementId.InvalidElementId,
                                result);
                        }
                    }

                    // Vent line ────────────────────────────────────────────────
                    if (opts.ShowVents && sl.HasVent)
                    {
                        double ventX = cx + 200 * MmToFt;
                        TryDrawDetailLine(doc, view,
                            new XYZ(ventX, yBottom, 0),
                            new XYZ(ventX, yTop + 0.3, 0),
                            lineStyles.Dashed, result);

                        if (opts.ShowDnLabels)
                        {
                            TryPlaceTextNote(doc, view,
                                new XYZ(ventX + 0.05, yTop * 0.5, 0),
                                $"DN{sl.VentDnMm} VENT",
                                textTypeSmall?.Id ?? ElementId.InvalidElementId,
                                result);
                        }
                    }

                    // Branches ─────────────────────────────────────────────────
                    foreach (var br in sl.Branches)
                    {
                        double by     = br.FloorIndex * levelHeightFt + 0.3 * levelHeightFt;
                        double brLen  = stackSpacingFt * 0.45;
                        double brEndX = br.IsLeft ? cx - brLen : cx + brLen;

                        // Horizontal branch line
                        TryDrawDetailLine(doc, view,
                            new XYZ(cx, by, 0),
                            new XYZ(brEndX, by, 0),
                            lineStyles.Solid, result);

                        // DN + slope label
                        if (opts.ShowDnLabels || opts.ShowSlopeLabels)
                        {
                            var parts = new List<string>();
                            if (opts.ShowDnLabels)
                                parts.Add($"DN{br.BranchDnMm}");
                            if (opts.ShowSlopeLabels && br.SlopePct > 0)
                                parts.Add($"× {br.SlopePct:0.##}%");

                            if (parts.Any())
                            {
                                TryPlaceTextNote(doc, view,
                                    new XYZ((cx + brEndX) * 0.5, by + 0.05, 0),
                                    string.Join(" ", parts),
                                    textTypeSmall?.Id ?? ElementId.InvalidElementId,
                                    result);
                            }
                        }

                        // Fixture symbol / label
                        if (opts.ShowFixtureSymbols && br.FixtureCount > 0)
                        {
                            string fixLabel = string.IsNullOrEmpty(br.FixtureLabel)
                                ? $"× {br.FixtureCount}"
                                : br.FixtureLabel;

                            // Small arc symbol for fixture (approximated with short lines)
                            DrawFixtureSymbol(doc, view, brEndX, by, br.IsLeft,
                                lineStyles.Solid, result);

                            TryPlaceTextNote(doc, view,
                                new XYZ(brEndX + (br.IsLeft ? -0.1 : 0.1), by + 0.07, 0),
                                fixLabel,
                                textTypeSmall?.Id ?? ElementId.InvalidElementId,
                                result);
                        }
                    }

                    result.NodesDrawn++;
                }

                // 6. Ground drain (horizontal collector at base) ────────────────
                if (layouts.Count > 1)
                {
                    double x0 = 0;
                    double x1 = (layouts.Count - 1) * stackSpacingFt;
                    TryDrawDetailLine(doc, view,
                        new XYZ(x0, -levelHeightFt * 0.3, 0),
                        new XYZ(x1, -levelHeightFt * 0.3, 0),
                        lineStyles.Solid, result);

                    for (int si = 0; si < layouts.Count; si++)
                    {
                        double cx = si * stackSpacingFt;
                        TryDrawDetailLine(doc, view,
                            new XYZ(cx, 0, 0),
                            new XYZ(cx, -levelHeightFt * 0.3, 0),
                            lineStyles.Solid, result);
                    }

                    TryPlaceTextNote(doc, view,
                        new XYZ(x0, -levelHeightFt * 0.35, 0),
                        "TO DRAIN",
                        textTypeSmall?.Id ?? ElementId.InvalidElementId,
                        result);
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

        private static TextNoteType FindOrCreateTextType(Document doc, double heightMm)
        {
            try
            {
                var types = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .Cast<TextNoteType>()
                    .ToList();

                if (!types.Any()) return null;

                // Find the closest match by text size
                double targetFt = heightMm * MmToFt;
                return types
                    .OrderBy(t =>
                    {
                        try
                        {
                            double h = t.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() ?? 0;
                            return Math.Abs(h - targetFt);
                        }
                        catch { return double.MaxValue; }
                    })
                    .First();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FindOrCreateTextType({heightMm}mm): {ex.Message}");
                return null;
            }
        }

        // ── Layout builder ────────────────────────────────────────────────────

        private static List<StackLayout> BuildStackLayouts(
            List<PipeNode> stackNodes, PipeNetwork network, DrainageSchematicOptions opts)
        {
            var layouts = new List<StackLayout>();

            for (int i = 0; i < stackNodes.Count; i++)
            {
                var node = stackNodes[i];

                // Approximate floor count from node height above lowest node
                double minY = network.Nodes.Any() ? network.Nodes.Min(n => n.Position.Z) : 0;
                double heightFt = node.Position.Z - minY;
                int floorCount = Math.Max(2, (int)Math.Ceiling(heightFt / (opts.LevelHeightMm * MmToFt)));

                // Vent sizing (roughly half stack DN)
                int ventDnMm = node.DnMm > 0 ? Math.Max(50, (int)(node.DnMm / 2)) : 50;

                var sl = new StackLayout
                {
                    Node        = node,
                    ColumnIndex = i,
                    FloorCount  = floorCount,
                    SystemName  = node.SystemName,
                    HasVent     = opts.ShowVents,
                    VentDnMm    = ventDnMm
                };

                // Build branches from upstream fixture nodes
                var upstreamFixtures = node.Upstream
                    .Select(e => e.From)
                    .Where(n => n.Type == PipeNodeType.Fixture)
                    .ToList();

                // Group by approximate Z to simulate per-floor branches
                var byFloor = upstreamFixtures
                    .GroupBy(n => (int)Math.Round((n.Position.Z - minY) / (opts.LevelHeightMm * MmToFt)))
                    .OrderBy(g => g.Key)
                    .Take(floorCount);

                int branchIdx = 0;
                foreach (var floorGroup in byFloor)
                {
                    var edge = node.Upstream
                        .FirstOrDefault(e => floorGroup.Any(f => f.Id == e.From.Id));

                    int branchDn = edge != null ? (int)Math.Round(edge.DnMm) : 50;
                    double slope = edge?.SlopePct ?? 1.25;

                    int count = floorGroup.Count();
                    string fixLabel = count > 0 ? $"FIXTURES × {count}" : "";

                    sl.Branches.Add(new BranchLayout
                    {
                        FloorIndex    = Math.Max(0, floorGroup.Key),
                        FixtureCount  = count,
                        BranchDnMm    = branchDn,
                        SlopePct      = slope,
                        FixtureLabel  = fixLabel,
                        IsLeft        = branchIdx % 2 != 0
                    });
                    branchIdx++;
                }

                // If no upstream fixtures found, add at least one indicative branch per floor
                if (!sl.Branches.Any())
                {
                    for (int f = 0; f < Math.Min(floorCount, 4); f++)
                    {
                        sl.Branches.Add(new BranchLayout
                        {
                            FloorIndex   = f,
                            FixtureCount = 0,
                            BranchDnMm   = Math.Max(50, (int)(node.DnMm / 2)),
                            SlopePct     = 1.25,
                            FixtureLabel = "",
                            IsLeft       = f % 2 != 0
                        });
                    }
                }

                layouts.Add(sl);
            }

            return layouts;
        }

        // ── Drawing helpers ───────────────────────────────────────────────────

        private static void TryDrawDetailLine(Document doc, View view,
            XYZ start, XYZ end, ElementId lineStyleId, SchematicResult result)
        {
            try
            {
                if (start.IsAlmostEqualTo(end)) return;
                var line = Line.CreateBound(start, end);
                var dl = doc.Create.NewDetailCurve(view, line);
                if (lineStyleId != null && lineStyleId != ElementId.InvalidElementId)
                {
                    try { dl.LineStyle = doc.GetElement(lineStyleId) as GraphicsStyle; }
                    catch { /* non-fatal */ }
                }
                result.LinesDrawn++;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"DetailLine failed ({start.X:F2},{start.Y:F2})→({end.X:F2},{end.Y:F2}): {ex.Message}");
            }
        }

        private static void TryPlaceTextNote(Document doc, View view,
            XYZ position, string text, ElementId textTypeId, SchematicResult result)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            try
            {
                TextNote.Create(doc, view.Id, position, text, textTypeId);
                result.AnnotationsPlaced++;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"TextNote failed '{text}': {ex.Message}");
            }
        }

        /// <summary>
        /// Draws a simple fixture symbol: two diagonal lines forming a 'V' at the branch end.
        /// </summary>
        private static void DrawFixtureSymbol(Document doc, View view,
            double x, double y, bool isLeft, ElementId lineStyleId, SchematicResult result)
        {
            double sz = 80 * MmToFt;
            double dir = isLeft ? -1 : 1;

            TryDrawDetailLine(doc, view,
                new XYZ(x, y, 0),
                new XYZ(x + dir * sz, y + sz, 0),
                lineStyleId, result);

            TryDrawDetailLine(doc, view,
                new XYZ(x, y, 0),
                new XYZ(x + dir * sz * 0.5, y - sz, 0),
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
                                dashed = sub.Id;
                        }
                        else if (name.IndexOf("Thin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 name.IndexOf("Medium", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (solid == ElementId.InvalidElementId)
                                solid = sub.Id;
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

        private static ElementId GetSolidFillPatternId(Document doc)
        {
            try
            {
                var fp = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);
                return fp?.Id ?? ElementId.InvalidElementId;
            }
            catch { return ElementId.InvalidElementId; }
        }

        private static string GenerateUniqueViewName(Document doc, string baseName)
        {
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewDrafting))
                .Cast<ViewDrafting>()
                .Select(v => v.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!existing.Contains(baseName)) return baseName;

            for (int i = 2; i < 100; i++)
            {
                string candidate = $"{baseName} ({i})";
                if (!existing.Contains(candidate)) return candidate;
            }

            return $"{baseName} ({DateTime.UtcNow:HHmmss})";
        }
    }
}
