// SupplySchematicGenerator — 2D water-supply schematic in a Revit Drafting View.
// Phase 187 — borrowed-from-Plumber companion to DrainageSchematicGenerator.
//
// Walks the supply branch of the PipeNetwork, lays out the index leg vertically
// (inlet at bottom, fixtures at top), draws the network plus PRV / water-meter
// / pump / fixture symbols and labels each pipe with DN + accumulated kPa.
// Runs of pass-through pipe collapse to one line, floors are storey rows at a
// fixed pitch, and the scale is the smallest that fits the sheet slot (DTW-120).
//
// All drafting-view coordinates are in feet (Revit internal units).
// 1 mm = 1/304.8 ft.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;

namespace StingTools.Core.Plumbing
{
    public class SupplySchematicOptions
    {
        /// <summary>Filter to a single named system (e.g. "DCW"). Empty = all supply systems.</summary>
        public string SystemNameFilter { get; set; } = "";
        /// <summary>
        /// Pipe-system classifications drawn (null or empty = every pipe). Default
        /// Domestic Cold Water: this is the DCW schematic. Unfiltered, the walk from the
        /// lowest equipment node drew whatever network it touched — drainage included.
        /// </summary>
        public PipeSystemType[] Classifications { get; set; } = { PipeSystemType.DomesticColdWater };
        /// <summary>Spacing between adjacent columns (model mm; never narrower than the widest label).</summary>
        public double BranchSpacingMm  { get; set; } = 1000.0;
        /// <summary>Drawn height of one storey (model mm); floors are drawn this far apart whatever their real height.</summary>
        public double LevelHeightMm    { get; set; } = 3000.0;
        /// <summary>Paper size of the sheet slot the view goes in (0 = unknown: no fit check).</summary>
        public double SlotWidthMm      { get; set; }
        public double SlotHeightMm     { get; set; }
        /// <summary>Smallest scale the view may take.</summary>
        public int    MinScale         { get; set; } = 50;
        public bool   ShowDnLabels     { get; set; } = true;
        public bool   ShowPressureLabels { get; set; } = true;
        public bool   ShowAccessorySymbols { get; set; } = true;
        public bool   ExportDxf        { get; set; } = false;
        /// <summary>Target AutoCAD file version for DXF export
        /// (R2000 / R2004 / R2007 / R2010 / R2013 / R2018 / DEFAULT).
        /// Pulled from PlumbingSystemConfig.DxfAutoCadVersion by the command.</summary>
        public string DxfAutoCadVersion { get; set; } = "R2010";
        /// <summary>Inlet pressure (kPa) used to seed AccumulatePressure.</summary>
        public double InletPressureKpa { get; set; } = 300.0;
        /// <summary>
        /// True only when <see cref="InletPressureKpa"/> came from the project's saved
        /// plumbing configuration. When false no kPa label is printed: a pressure
        /// propagated from an assumed inlet would read as a modelled one.
        /// </summary>
        public bool   InletPressureConfigured { get; set; }
    }

    public class SupplySchematicResult
    {
        public ElementId    ViewId            { get; set; } = ElementId.InvalidElementId;
        public int          PipesDrawn        { get; set; }
        public int          AccessoriesDrawn  { get; set; }
        public int          FixturesDrawn     { get; set; }
        public string       DxfPath           { get; set; }
        /// <summary>Scale the view was drawn at (chosen to fit its sheet slot).</summary>
        public int          Scale             { get; set; }
        /// <summary>What the network was laid out from (e.g. "water meter 12345").</summary>
        public string       SourceDescription { get; set; } = "";
        /// <summary>True when no meter / tank / pump / equipment was found and the lowest node was used.</summary>
        public bool         SourceAssumed     { get; set; }
        public List<string> Warnings          { get; } = new List<string>();
    }

    public static class SupplySchematicGenerator
    {
        private const double MmToFt = 1.0 / 304.8;

        /// <summary>
        /// Generate the schematic. Caller owns the Transaction. When
        /// <paramref name="opts"/>.ExportDxf is true the resulting drafting
        /// view is also exported to the project's routed DXF export folder;
        /// the path is returned on the result.
        /// </summary>
        public static SupplySchematicResult Generate(Document doc, SupplySchematicOptions opts)
        {
            var result = new SupplySchematicResult();
            if (doc == null) { result.Warnings.Add("Document is null."); return result; }
            opts = opts ?? new SupplySchematicOptions();

            // 1. Build supply-only network
            PipeNetwork net;
            try
            {
                net = PipeNetworkBuilder.Build(doc,
                    string.IsNullOrWhiteSpace(opts.SystemNameFilter) ? null : opts.SystemNameFilter,
                    opts.Classifications);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"PipeNetworkBuilder.Build: {ex.Message}");
                return result;
            }
            if (net.Edges.Count == 0)
            {
                result.Warnings.Add("No supply pipework found"
                    + (opts.Classifications != null && opts.Classifications.Length > 0
                        ? " (no pipe on a system classified " + string.Join(" / ", opts.Classifications) + ")" : "")
                    + " — nothing to draw.");
                return result;
            }

            // 2. Identify the source — a modelled water meter, tank, pump set or other
            //    equipment connected to the network. Only when none exists is the lowest
            //    node used, and the result says so.
            var connected = net.Nodes.Where(n => n.Upstream.Count + n.Downstream.Count > 0).ToList();
            var ranked = connected
                .Select(n => new { Node = n, Sym = NodeSymbol(doc, n) })
                .Select(x => new { x.Node, x.Sym, Rank = SchematicLayoutMath.SupplySourceRank(x.Sym, x.Node.Type == PipeNodeType.Equipment) })
                .Where(x => x.Rank != int.MaxValue)
                .OrderBy(x => x.Rank).ThenBy(x => x.Node.Position?.Z ?? double.MaxValue)
                .FirstOrDefault();
            PipeNode inlet = ranked?.Node;
            if (inlet != null)
            {
                result.SourceDescription = $"{SourceName(ranked.Sym, inlet)} {inlet.Id.Value}";
                // DTW-128: other equipment is a guess at the source, not a modelled inlet.
                if (SchematicLayoutMath.SupplySourceIsAssumed(ranked.Rank))
                {
                    result.SourceAssumed = true;
                    result.SourceDescription += " (assumed)";
                    result.Warnings.Add($"No water meter, tank or pump is connected to the network — laid out from "
                        + $"{SourceName(ranked.Sym, inlet)} {inlet.Id.Value}; any pressures shown are indicative.");
                }
            }
            else
            {
                inlet = (connected.Count > 0 ? connected : net.Nodes)
                    .OrderBy(n => n.Position?.Z ?? double.MaxValue).FirstOrDefault();
                if (inlet == null)
                {
                    result.Warnings.Add("No nodes in supply network — nothing to draw.");
                    return result;
                }
                result.SourceAssumed = true;
                result.SourceDescription = $"lowest node {inlet.Id.Value} (assumed)";
                result.Warnings.Add("No water meter, tank, pump or equipment is connected to the network — laid out from "
                    + $"the lowest node ({inlet.Id.Value}); any pressures shown are indicative.");
            }

            // 3. Pressure propagation (PRV / meter aware) from that source — only when
            //    the inlet pressure was configured; an assumed one is not printed.
            bool showPressure = opts.ShowPressureLabels && opts.InletPressureConfigured;
            if (opts.ShowPressureLabels && !opts.InletPressureConfigured)
                result.Warnings.Add("Pressure labels omitted: no inlet pressure is configured for this project "
                    + "(set it in the plumbing system configuration and save).");
            if (showPressure)
            {
                try
                {
                    PipeNetworkBuilder.AccumulatePressureFrom(net, inlet, opts.InletPressureKpa, doc);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"SupplySchematic: AccumulatePressure: {ex.Message}");
                    result.Warnings.Add($"AccumulatePressure: {ex.Message} — pressure labels omitted.");
                    showPressure = false;
                }
            }

            // 4. Layout (DTW-120) — before the view, so its scale can be chosen to fit.
            //    Runs of pass-through pipe and fittings collapse to one line between the
            //    nodes worth drawing (source, branch points, ends, fixtures, valves …);
            //    rows are storeys at a fixed pitch, not true elevation. Every element used
            //    to take its own 20 mm column, so a horizontal run spread across the sheet.
            var symbols = new Dictionary<long, string>();
            string Sym(PipeNode n)
            {
                if (n == null) return null;
                if (!symbols.TryGetValue(n.Id.Value, out var s)) symbols[n.Id.Value] = s = NodeSymbol(doc, n);
                return s;
            }
            var levelElevs = DrainageSchematicGenerator.CollectLevels(doc).Select(l => l.Elevation).ToList();
            var layout = LayoutNetwork(net, inlet, levelElevs, opts, Sym);

            var textType = DrainageSchematicGenerator.FindClosestTextType(doc, 2.5);
            var (textMm, widthFactor) = DrainageSchematicGenerator.TextMetrics(textType, 2.5);

            // Every label the drawing will carry, to size the columns and rows by.
            var nodeLabels = new Dictionary<long, string>();
            var segLabels = new Dictionary<ChainSegment, string>();
            if (opts.ShowDnLabels)
            {
                foreach (var id in layout.Cells.Keys)
                {
                    if (!net.ById.TryGetValue(id, out var n)) continue;
                    string sym = Sym(n);
                    if (string.IsNullOrEmpty(sym)) continue;
                    string lbl = NodeLabel(n, sym, showPressure, result.SourceAssumed);
                    if (!string.IsNullOrEmpty(lbl)) nodeLabels[id] = lbl;
                }
                var labelledPipes = new HashSet<long>();
                foreach (var seg in layout.Segments)
                {
                    string lbl = SegmentLabel(net, seg, labelledPipes, showPressure, result.SourceAssumed);
                    if (!string.IsNullOrEmpty(lbl)) segLabels[seg] = lbl;
                }
            }
            var allLabels = nodeLabels.Values.Concat(segLabels.Values).ToList();
            double labelW = allLabels.Count == 0 ? 0 : allLabels.Max(l => SchematicFit.EstimateTextWidthMm(l, textMm, widthFactor));
            double labelH = allLabels.Count == 0 ? textMm * 1.5 : allLabels.Max(l => SchematicFit.EstimateTextHeightMm(l, textMm));

            int minRow = layout.Cells.Count > 0 ? layout.Cells.Values.Min(c => c.Row) : 0;
            int maxRow = layout.Cells.Count > 0 ? layout.Cells.Values.Max(c => c.Row) : 0;
            int minCol = layout.Cells.Count > 0 ? layout.Cells.Values.Min(c => c.Col) : 0;
            int maxCol = layout.Cells.Count > 0 ? layout.Cells.Values.Max(c => c.Col) : 0;
            // A column is never narrower than the widest label beside it, and a sub-row
            // never shorter than the tallest label, so labels in neighbouring cells do not
            // overlap; only the nominal spacing shrinks with scale.
            double ColumnPaper(int s) => Math.Max(opts.BranchSpacingMm / s, labelW + LabelOffsetMm + 2);
            double StoreyPaper(int s) => Math.Max(opts.LevelHeightMm / s, Math.Max(MinStoreyPaperMm, SubRowsPerStorey * (labelH + 1)));
            var fit = SchematicFit.ChooseScale(
                s => ((maxCol - minCol + 1) * ColumnPaper(s),
                      (maxRow - minRow) / (double)SubRowsPerStorey * StoreyPaper(s) + labelH + 5),
                opts.SlotWidthMm, opts.SlotHeightMm, opts.MinScale > 0 ? opts.MinScale : 50);
            result.Scale = fit.Scale;
            if (fit.Problem() is string fitProblem) result.Warnings.Add(fitProblem);

            // 5. Drafting view — DTW-119: reused and cleared on a re-run, never "… (2)".
            var view = StingTools.Core.Drawing.SchematicViewFactory.CreateOrReplace(
                doc, ViewName(opts), out string viewError, fit.Scale);
            if (view == null)
            {
                result.Warnings.Add(viewError ?? "The supply schematic view could not be made.");
                return result;
            }
            result.ViewId = view.Id;

            // Paper mm × the view's real scale: glyphs print at their paper size.
            int scale = view.Scale;
            double P(double paperMm) => SchematicLayoutMath.PaperMmToModelFt(paperMm, scale);
            double colFt = P(ColumnPaper(scale));
            double subRowFt = P(StoreyPaper(scale)) / SubRowsPerStorey;
            var coords = layout.Cells.ToDictionary(kv => kv.Key,
                kv => new XYZ((kv.Value.Col - minCol) * colFt, (kv.Value.Row - minRow) * subRowFt, 0));

            var (solidId, dashedId) = GetLineStyleIds(doc);

            // Build the symbol-family cache once per generation. Maps each
            // logical glyph code (PRV / MTR / PMP / TK / FX / CK / V) to a
            // FamilySymbol when a matching detail family is loaded; misses
            // fall through to the geometric glyph fallback below.
            var symbolMap = ResolveSymbolFamilies(doc);

            // 6. Draw the runs: straight, or an L (along the parent's row, then up or
            //    down the child's column). DTW-128: each pipe's DN is labelled once.
            var drawnPipes = new HashSet<long>();
            foreach (var seg in layout.Segments)
            {
                if (!coords.TryGetValue(seg.From, out var p0)) continue;
                if (!coords.TryGetValue(seg.To,   out var p1)) continue;
                net.ById.TryGetValue(seg.From, out var fromNode);

                bool isReturn = (fromNode?.SystemName ?? "").IndexOf("RETURN", StringComparison.OrdinalIgnoreCase) >= 0
                              || (fromNode?.SystemName ?? "").IndexOf("RECIRC", StringComparison.OrdinalIgnoreCase) >= 0;
                var style = isReturn ? dashedId : solidId;
                bool drawn;
                var corner = new XYZ(p1.X, p0.Y, 0);
                if (Math.Abs(p0.X - p1.X) < 1e-9 || Math.Abs(p0.Y - p1.Y) < 1e-9)
                    drawn = TryDrawDetailLine(doc, view, p0, p1, style, result);
                else
                    drawn = TryDrawDetailLine(doc, view, p0, corner, style, result)
                          & TryDrawDetailLine(doc, view, corner, p1, style, result);
                if (!drawn) continue;
                foreach (var id in new[] { seg.From }.Concat(seg.Through).Concat(new[] { seg.To }))
                    if (net.ById.TryGetValue(id, out var n) && n.IsPipeElement) drawnPipes.Add(id);

                if (segLabels.TryGetValue(seg, out var lbl))
                {
                    XYZ at = Math.Abs(p0.Y - p1.Y) > 1e-9
                        ? new XYZ(p1.X + P(2), (p0.Y + p1.Y) / 2.0 + P(labelH / 2), 0)   // beside the vertical leg
                        : new XYZ((p0.X + p1.X) / 2.0, p0.Y + P(labelH + 1), 0);         // above the horizontal run
                    TryPlaceTextNote(doc, view, at, lbl, textType?.Id ?? ElementId.InvalidElementId, result);
                }
            }
            result.PipesDrawn = drawnPipes.Count;

            // 7. Draw node markers (fixtures, PRVs, meters, pumps)
            foreach (var kv in coords)
            {
                if (!net.ById.TryGetValue(kv.Key, out var node)) continue;
                var p = kv.Value;
                string sym = Sym(node);
                if (string.IsNullOrEmpty(sym)) continue;

                if (opts.ShowAccessorySymbols)
                {
                    bool placed = false;
                    if (symbolMap != null && symbolMap.TryGetValue(sym, out var fs) && fs != null)
                    {
                        placed = TryPlaceSymbolInstance(doc, view, p, fs, result);
                    }
                    if (!placed)
                    {
                        // Fall back to geometric glyph
                        DrawSymbol(doc, view, p, sym, P(3), solidId, result);
                    }
                    if (sym == "FX") result.FixturesDrawn++;
                    else             result.AccessoriesDrawn++;
                }

                if (nodeLabels.TryGetValue(kv.Key, out var lbl))
                    TryPlaceTextNote(doc, view,
                        new XYZ(p.X + P(LabelOffsetMm), p.Y + P(labelH / 2), 0), lbl,
                        textType?.Id ?? ElementId.InvalidElementId, result);
            }

            // 8. DXF export (optional)
            if (opts.ExportDxf)
            {
                try
                {
                    string dir = ResolveExportDir(doc, out string why);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                        var dxfOpts = new DXFExportOptions
                        {
                            FileVersion = MapAcadVersion(opts.DxfAutoCadVersion)
                        };
                        string safeName = Sanitise(view.Name);
                        doc.Export(dir, safeName, new List<ElementId> { view.Id }, dxfOpts);
                        result.DxfPath = Path.Combine(dir, safeName + ".dxf");
                    }
                    else
                    {
                        result.Warnings.Add("DXF export skipped: " + why);
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"SupplySchematic: DXF export: {ex.Message}");
                    result.Warnings.Add($"DXF export: {ex.Message}");
                }
            }

            return result;
        }

        // ── Layout ────────────────────────────────────────────────────────────

        // Rows per storey: a node sits at the nearest quarter storey above its level.
        private const int    SubRowsPerStorey = 4;
        private const double MinStoreyPaperMm = 16;
        private const double LabelOffsetMm    = 4;

        private sealed class SupplyLayout
        {
            public Dictionary<long, (int Row, int Col)> Cells = new Dictionary<long, (int Row, int Col)>();
            public List<ChainSegment> Segments = new List<ChainSegment>();
        }

        /// <summary>
        /// Cells of the nodes worth drawing — the source, branch points and ends, and
        /// anything with a symbol — and the runs between them. A node with exactly two
        /// neighbours and no symbol (a pipe, a coupling, an elbow) is passed through.
        /// </summary>
        private static SupplyLayout LayoutNetwork(PipeNetwork net, PipeNode inlet,
            IReadOnlyList<double> levelElevs, SupplySchematicOptions opts, Func<PipeNode, string> sym)
        {
            var layout = new SupplyLayout();
            IEnumerable<long> Nbrs(long id)
            {
                if (!net.ById.TryGetValue(id, out var n)) yield break;
                foreach (var e in n.Upstream)   if (e.From != null) yield return e.From.Id.Value;
                foreach (var e in n.Downstream) if (e.To   != null) yield return e.To.Id.Value;
            }
            bool Keep(long id)
            {
                if (!net.ById.TryGetValue(id, out var n)) return true;
                return Nbrs(id).Distinct().Count() != 2 || !string.IsNullOrEmpty(sym(n));
            }

            long start = inlet.Id.Value;
            layout.Segments = SchematicFit.CollapseChains(start, Nbrs, Keep);

            double z0 = inlet.Position?.Z ?? 0;
            double fallbackStoreyFt = Math.Max(1.0, opts.LevelHeightMm) * MmToFt;
            int RowOf(long id)
            {
                double z = net.ById.TryGetValue(id, out var n) && n.Position != null ? n.Position.Z : z0;
                return (int)Math.Round(SchematicFit.StoreyRow(levelElevs, z, fallbackStoreyFt) * SubRowsPerStorey);
            }
            layout.Cells = SchematicFit.LayoutTree(start, layout.Segments, RowOf);
            return layout;
        }

        /// <summary>
        /// DN label of a run: its pipes' DNs in order (each once), and the pressure at the
        /// pipe nearest its far end. Null when every pipe in it was already labelled.
        /// </summary>
        private static string SegmentLabel(PipeNetwork net, ChainSegment seg, HashSet<long> labelledPipes,
            bool showPressure, bool sourceAssumed)
        {
            var pipes = new[] { seg.From }.Concat(seg.Through).Concat(new[] { seg.To })
                .Select(id => net.ById.TryGetValue(id, out var n) ? n : null)
                .Where(n => n != null && n.IsPipeElement && n.DnMm > 0)
                .ToList();
            var fresh = pipes.Where(n => labelledPipes.Add(n.Id.Value)).ToList();
            if (fresh.Count == 0) return null;
            var dns = new List<int>();
            foreach (var n in fresh)
            {
                int dn = (int)Math.Round(n.DnMm);
                if (dns.Count == 0 || dns[dns.Count - 1] != dn) dns.Add(dn);
            }
            string label = "DN" + string.Join("/", dns);
            string kpa = showPressure ? SchematicLayoutMath.PressureLabel(fresh[fresh.Count - 1].PressureKpa, true, sourceAssumed) : null;
            return kpa != null ? label + "\n" + kpa : label;
        }
        // ── Node classification + symbol mapping ──────────────────────────────

        private static string NodeSymbol(Document doc, PipeNode node)
        {
            if (node?.Id == null) return null;
            try
            {
                var el = doc.GetElement(node.Id);
                if (el is FamilyInstance fi)
                {
                    var bic = (BuiltInCategory)(fi.Category?.Id?.Value ?? 0);
                    string s = ((fi.Symbol?.Family?.Name ?? "") + " " +
                                (fi.Symbol?.Name ?? "")).ToUpperInvariant();
                    if (bic == BuiltInCategory.OST_PlumbingFixtures) return "FX";
                    if (bic == BuiltInCategory.OST_PipeAccessory)
                    {
                        if (s.Contains("PRV") || s.Contains("PRESSURE REDUC")) return "PRV";
                        if (s.Contains("METER")) return "MTR";
                        if (s.Contains("CHECK")) return "CK";
                        if (s.Contains("VALVE")) return "V";
                    }
                    if (bic == BuiltInCategory.OST_MechanicalEquipment)
                    {
                        if (s.Contains("PUMP")) return "PMP";
                        if (s.Contains("TANK") || s.Contains("CYLINDER")) return "TK";
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"SupplySchematic: classify node {node.Id}: {ex.Message}"); }
            return null;
        }

        private static string SourceName(string sym, PipeNode node)
        {
            switch (sym)
            {
                case "MTR": return "water meter";
                case "TK":  return "tank";
                case "PMP": return "pump";
                default:    return node.Type == PipeNodeType.Equipment ? "equipment" : "node";
            }
        }

        private static string NodeLabel(PipeNode node, string sym, bool showPressure, bool sourceAssumed)
        {
            string kpa = showPressure ? SchematicLayoutMath.PressureLabel(node.PressureKpa, true, sourceAssumed) : null;
            string With(string name) => kpa != null ? $"{name} ({kpa})" : name;
            switch (sym)
            {
                case "PRV": return With("PRV");
                case "MTR": return "WM";
                case "PMP": return With("PUMP");
                case "TK":  return "TANK";
                case "FX":  return With("FX");
                case "CK":  return "CV";
                case "V":   return "V";
                default:    return "";
            }
        }

        // ── Drawing primitives ────────────────────────────────────────────────

        /// <summary>Draws one detail line; true only when it was created.</summary>
        private static bool TryDrawDetailLine(Document doc, View view, XYZ p0, XYZ p1,
            ElementId styleId, SupplySchematicResult r)
        {
            try
            {
                if (p0.DistanceTo(p1) < 1e-6) return false;
                var line = Line.CreateBound(p0, p1);
                var dc = doc.Create.NewDetailCurve(view, line);
                if (styleId != null && styleId != ElementId.InvalidElementId)
                {
                    try { dc.LineStyle = doc.GetElement(styleId) as GraphicsStyle; }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"SupplySchematic: line style: {ex.Message}");
                        if (r.Warnings.Count < 20) r.Warnings.Add($"A line was drawn without its line style: {ex.Message}");
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SupplySchematic: detail line failed: {ex.Message}");
                r.Warnings.Add($"detail line: {ex.Message}");
                return false;
            }
        }

        private static void TryPlaceTextNote(Document doc, View view, XYZ p, string text,
            ElementId typeId, SupplySchematicResult r)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return;
                if (typeId == null || typeId == ElementId.InvalidElementId)
                    typeId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
                if (typeId == null || typeId == ElementId.InvalidElementId) return;
                TextNote.Create(doc, view.Id, p, text, typeId);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SupplySchematic: text note failed: {ex.Message}");
                r.Warnings.Add($"text note: {ex.Message}");
            }
        }

        private static void DrawSymbol(Document doc, View view, XYZ centre, string sym,
            double sizeFt, ElementId styleId, SupplySchematicResult r)
        {
            // Compact glyph: a small square per symbol (sizeFt = paper mm × view
            // scale), drawn with detail lines so it works on any project without
            // bespoke detail families.
            double h = sizeFt / 2.0;
            var tl = new XYZ(centre.X - h, centre.Y + h, 0);
            var tr = new XYZ(centre.X + h, centre.Y + h, 0);
            var br = new XYZ(centre.X + h, centre.Y - h, 0);
            var bl = new XYZ(centre.X - h, centre.Y - h, 0);
            TryDrawDetailLine(doc, view, tl, tr, styleId, r);
            TryDrawDetailLine(doc, view, tr, br, styleId, r);
            TryDrawDetailLine(doc, view, br, bl, styleId, r);
            TryDrawDetailLine(doc, view, bl, tl, styleId, r);
            // Diagonal for PRV / MTR
            if (sym == "PRV" || sym == "MTR" || sym == "CK")
                TryDrawDetailLine(doc, view, tl, br, styleId, r);
        }

        // Resolve project-loaded detail families per glyph code. Each glyph
        // code is mapped to a set of family-name patterns; the first loaded
        // family that matches wins. Returning null entries means "no family
        // found — fall back to the geometric glyph". Naming convention
        // mirrors the STING_ISO_SYMBOLS_INDEX.csv used by IsoSymbolPlacer
        // (fabrication side) so projects can ship one symbol library.
        private static Dictionary<string, FamilySymbol> ResolveSymbolFamilies(Document doc)
        {
            var map = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
            var patterns = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "PRV", new[] { "STING_SYM_PRV", "PRV", "PRESSURE REDUCING" } },
                { "MTR", new[] { "STING_SYM_METER", "WATER METER", "METER" } },
                { "PMP", new[] { "STING_SYM_PUMP", "PUMP" } },
                { "TK",  new[] { "STING_SYM_TANK", "TANK", "CYLINDER" } },
                { "FX",  new[] { "STING_SYM_FIXTURE", "FIXTURE TAP", "DRAW-OFF" } },
                { "CK",  new[] { "STING_SYM_CHECK", "CHECK VALVE", "NRV" } },
                { "V",   new[] { "STING_SYM_VALVE", "ISOLATION VALVE", "VALVE" } },
            };

            FamilySymbol[] detailSymbols;
            try
            {
                detailSymbols = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(fs => fs.Family?.FamilyCategory != null
                              && (BuiltInCategory)(fs.Family.FamilyCategory.Id?.Value ?? 0)
                                  == BuiltInCategory.OST_DetailComponents)
                    .ToArray();
            }
            catch
            {
                detailSymbols = new FamilySymbol[0];
            }

            foreach (var kv in patterns)
            {
                FamilySymbol pick = null;
                foreach (var pat in kv.Value)
                {
                    var patUp = pat.ToUpperInvariant();
                    pick = detailSymbols.FirstOrDefault(fs =>
                        (((fs.Family?.Name ?? "") + " " + (fs.Name ?? "")).ToUpperInvariant())
                        .Contains(patUp));
                    if (pick != null) break;
                }
                map[kv.Key] = pick;
            }
            return map;
        }

        private static bool TryPlaceSymbolInstance(Document doc, View view, XYZ centre,
            FamilySymbol fs, SupplySchematicResult r)
        {
            try
            {
                if (fs == null) return false;
                if (!fs.IsActive) fs.Activate();
                doc.Create.NewFamilyInstance(centre, fs, view);
                return true;
            }
            catch (Exception ex)
            {
                r.Warnings.Add($"symbol family '{fs?.Name}': {ex.Message}");
                return false;
            }
        }

        // ── Style + helpers ───────────────────────────────────────────────────

        private static (ElementId solid, ElementId dashed) GetLineStyleIds(Document doc)
        {
            ElementId solid = ElementId.InvalidElementId, dashed = ElementId.InvalidElementId;
            try
            {
                var cat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                foreach (Category sub in cat.SubCategories)
                {
                    var nm = (sub.Name ?? "").ToUpperInvariant();
                    if (solid == ElementId.InvalidElementId &&
                        (nm.Contains("THIN") || nm.Contains("SOLID")))
                        solid = sub.GetGraphicsStyle(GraphicsStyleType.Projection)?.Id ?? ElementId.InvalidElementId;
                    if (dashed == ElementId.InvalidElementId && nm.Contains("DASH"))
                        dashed = sub.GetGraphicsStyle(GraphicsStyleType.Projection)?.Id ?? ElementId.InvalidElementId;
                }
            }
            catch { }
            return (solid, dashed == ElementId.InvalidElementId ? solid : dashed);
        }

        /// <summary>The view's name: one per classification and system filter, so a re-run reuses it.</summary>
        internal static string ViewName(SupplySchematicOptions opts)
        {
            bool dcwOnly = opts?.Classifications != null && opts.Classifications.Length == 1
                        && opts.Classifications[0] == PipeSystemType.DomesticColdWater;
            return "STING - Supply Schematic" + (dcwOnly ? " DCW" : "")
                 + (string.IsNullOrWhiteSpace(opts?.SystemNameFilter) ? "" : " " + opts.SystemNameFilter.Trim());
        }

        /// <summary>
        /// The project's routed DXF export folder, or null with the real reason in
        /// <paramref name="why"/> (not a blanket "project not saved").
        /// </summary>
        private static string ResolveExportDir(Document doc, out string why)
        {
            why = null;
            if (string.IsNullOrEmpty(doc?.PathName))
            {
                why = "the project has not been saved, so it has no export folder.";
                return null;
            }
            try
            {
                string dir = StingPaths.Export(doc, "DXF");
                if (string.IsNullOrEmpty(dir))
                    why = "no export folder could be resolved for DXF.";
                return dir;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SupplySchematic: resolving the DXF export folder: {ex.Message}");
                why = $"the DXF export folder could not be resolved: {ex.Message}";
                return null;
            }
        }

        private static string Sanitise(string s)
        {
            if (string.IsNullOrEmpty(s)) return "schematic";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = s.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            return new string(chars);
        }

        // Map a config string to the Revit ACADVersion enum. R2000 and R2004
        // were removed in Revit 2025+; callers requesting them are degraded
        // to R2007 (oldest still-supported).
        private static ACADVersion MapAcadVersion(string v)
        {
            string s = (v ?? "").Trim().ToUpperInvariant();
            switch (s)
            {
                case "R2000":   return ACADVersion.R2007; // R2000 unavailable on Revit 2025+
                case "R2004":   return ACADVersion.R2007; // R2004 unavailable on Revit 2025+
                case "R2007":   return ACADVersion.R2007;
                case "R2010":   return ACADVersion.R2010;
                case "R2013":   return ACADVersion.R2013;
                case "R2018":   return ACADVersion.R2018;
                case "DEFAULT": return ACADVersion.Default;
                default:        return ACADVersion.R2010;
            }
        }
    }
}
