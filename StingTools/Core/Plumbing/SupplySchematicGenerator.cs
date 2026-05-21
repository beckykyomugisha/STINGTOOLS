// SupplySchematicGenerator — 2D water-supply schematic in a Revit Drafting View.
// Phase 187 — borrowed-from-Plumber companion to DrainageSchematicGenerator.
//
// Walks the supply branch of the PipeNetwork, lays out the index leg vertically
// (inlet at bottom, fixtures at top), draws the network plus PRV / water-meter
// / pump / fixture symbols and labels each pipe with DN + accumulated kPa.
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
        /// <summary>Horizontal spacing between adjacent branches (mm).</summary>
        public double BranchSpacingMm  { get; set; } = 1000.0;
        /// <summary>Vertical spacing per level (mm).</summary>
        public double LevelHeightMm    { get; set; } = 3000.0;
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
    }

    public class SupplySchematicResult
    {
        public ElementId    ViewId            { get; set; } = ElementId.InvalidElementId;
        public int          PipesDrawn        { get; set; }
        public int          AccessoriesDrawn  { get; set; }
        public int          FixturesDrawn     { get; set; }
        public string       DxfPath           { get; set; }
        public List<string> Warnings          { get; } = new List<string>();
    }

    public static class SupplySchematicGenerator
    {
        private const double MmToFt = 1.0 / 304.8;

        /// <summary>
        /// Generate the schematic. Caller owns the Transaction. When
        /// <paramref name="opts"/>.ExportDxf is true the resulting drafting
        /// view is also exported to <c>_BIM_COORD/exports/&lt;view-name&gt;.dxf</c>;
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
                    string.IsNullOrWhiteSpace(opts.SystemNameFilter) ? null : opts.SystemNameFilter);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"PipeNetworkBuilder.Build: {ex.Message}");
                return result;
            }

            // 2. Identify the inlet — lowest-Z Equipment or Termination node
            var inlet = net.Nodes
                .Where(n => n.Type == PipeNodeType.Equipment || n.Type == PipeNodeType.Termination)
                .OrderBy(n => n.Position?.Z ?? double.MaxValue)
                .FirstOrDefault();
            if (inlet == null) inlet = net.Nodes.OrderBy(n => n.Position?.Z ?? 0).FirstOrDefault();
            if (inlet == null)
            {
                result.Warnings.Add("No nodes in supply network — nothing to draw.");
                return result;
            }

            // 3. Run pressure propagation (PRV / meter aware overload)
            try
            {
                PipeNetworkBuilder.AccumulatePressure(net, opts.InletPressureKpa,
                    inlet.Position?.Z ?? 0, doc);
            }
            catch (Exception ex) { result.Warnings.Add($"AccumulatePressure: {ex.Message}"); }

            // 4. Create drafting view
            var vft = DrainageSchematicGenerator.FindDraftingViewType(doc);
            if (vft == null)
            {
                result.Warnings.Add("No Drafting ViewFamilyType found.");
                return result;
            }

            ViewDrafting view;
            try
            {
                view = ViewDrafting.Create(doc, vft.Id);
                view.Name = UniqueViewName(doc,
                    $"STING - Supply Schematic{(string.IsNullOrEmpty(opts.SystemNameFilter) ? "" : " " + opts.SystemNameFilter)}");
                view.Scale = 50;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"ViewDrafting.Create: {ex.Message}");
                return result;
            }
            result.ViewId = view.Id;

            // 5. Layout: simple Reingold-Tilford-ish — index leg straight up,
            //    side branches fanning out left/right at each junction.
            var coords = LayoutNetwork(net, inlet, opts);

            var textType = FindOrCreateTextType(doc, 3.0);
            var (solidId, dashedId) = GetLineStyleIds(doc);

            // Build the symbol-family cache once per generation. Maps each
            // logical glyph code (PRV / MTR / PMP / TK / FX / CK / V) to a
            // FamilySymbol when a matching detail family is loaded; misses
            // fall through to the geometric glyph fallback below.
            var symbolMap = ResolveSymbolFamilies(doc);

            // 6. Draw edges
            foreach (var edge in net.Edges)
            {
                if (edge.From == null || edge.To == null) continue;
                if (!coords.TryGetValue(edge.From.Id.Value, out var p0)) continue;
                if (!coords.TryGetValue(edge.To.Id.Value,   out var p1)) continue;

                bool isReturn = (edge.From.SystemName ?? "").IndexOf("RETURN", StringComparison.OrdinalIgnoreCase) >= 0
                              || (edge.From.SystemName ?? "").IndexOf("RECIRC", StringComparison.OrdinalIgnoreCase) >= 0;
                TryDrawDetailLine(doc, view, p0, p1, isReturn ? dashedId : solidId, result);
                result.PipesDrawn++;

                if (opts.ShowDnLabels && edge.DnMm > 0)
                {
                    var mid = new XYZ((p0.X + p1.X) / 2.0 + 0.4 * MmToFt * 100,
                                      (p0.Y + p1.Y) / 2.0, 0);
                    string label = $"DN{(int)Math.Round(edge.DnMm)}";
                    if (opts.ShowPressureLabels && edge.To.PressureKpa > 0)
                        label += $"\n{edge.To.PressureKpa:F0} kPa";
                    TryPlaceTextNote(doc, view, mid, label,
                        textType?.Id ?? ElementId.InvalidElementId, result);
                }
            }

            // 7. Draw node markers (fixtures, PRVs, meters, pumps)
            foreach (var node in net.Nodes)
            {
                if (!coords.TryGetValue(node.Id.Value, out var p)) continue;
                string sym = NodeSymbol(doc, node);
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
                        DrawSymbol(doc, view, p, sym, solidId, result);
                    }
                    if (sym == "FX") result.FixturesDrawn++;
                    else             result.AccessoriesDrawn++;
                }

                if (opts.ShowDnLabels)
                {
                    string lbl = NodeLabel(node, sym);
                    if (!string.IsNullOrEmpty(lbl))
                        TryPlaceTextNote(doc, view,
                            new XYZ(p.X + 1.2 * MmToFt * 100, p.Y, 0), lbl,
                            textType?.Id ?? ElementId.InvalidElementId, result);
                }
            }

            // 8. DXF export (optional)
            if (opts.ExportDxf)
            {
                try
                {
                    string dir = ResolveExportDir(doc);
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
                        result.Warnings.Add("Project not saved — DXF export skipped.");
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"DXF export: {ex.Message}");
                }
            }

            return result;
        }

        // ── Layout ────────────────────────────────────────────────────────────

        private static Dictionary<long, XYZ> LayoutNetwork(
            PipeNetwork net, PipeNode inlet, SupplySchematicOptions opts)
        {
            var coords = new Dictionary<long, XYZ>();
            double dx = opts.BranchSpacingMm * MmToFt;
            double dy = opts.LevelHeightMm   * MmToFt;

            // BFS, breadth = column, depth = row
            int currentRow = 0;
            int colCounter = 0;
            var visited = new HashSet<long>();
            var queue   = new Queue<(PipeNode node, int row, int col)>();
            queue.Enqueue((inlet, 0, 0));

            while (queue.Count > 0)
            {
                var (node, row, col) = queue.Dequeue();
                if (visited.Contains(node.Id.Value)) continue;
                visited.Add(node.Id.Value);
                coords[node.Id.Value] = new XYZ(col * dx, row * dy, 0);
                if (row > currentRow) { currentRow = row; colCounter = 0; }

                int childIx = 0;
                var children = node.Upstream.Concat(node.Downstream)
                    .Select(e => e.From == node ? e.To : e.From)
                    .Where(n => n != null && !visited.Contains(n.Id.Value))
                    .Distinct()
                    .ToList();

                foreach (var child in children)
                {
                    int childCol = children.Count == 1 ? col : col + childIx - children.Count / 2;
                    queue.Enqueue((child, row + 1, childCol));
                    childIx++;
                }
            }
            return coords;
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
            catch { }
            return null;
        }

        private static string NodeLabel(PipeNode node, string sym)
        {
            switch (sym)
            {
                case "PRV": return node.PressureKpa > 0 ? $"PRV ({node.PressureKpa:F0} kPa)" : "PRV";
                case "MTR": return "WM";
                case "PMP": return node.PressureKpa > 0 ? $"PUMP ({node.PressureKpa:F0} kPa)" : "PUMP";
                case "TK":  return "TANK";
                case "FX":  return node.PressureKpa > 0 ? $"FX ({node.PressureKpa:F0} kPa)" : "FX";
                case "CK":  return "CV";
                case "V":   return "V";
                default:    return "";
            }
        }

        // ── Drawing primitives ────────────────────────────────────────────────

        private static void TryDrawDetailLine(Document doc, View view, XYZ p0, XYZ p1,
            ElementId styleId, SupplySchematicResult r)
        {
            try
            {
                if (p0.DistanceTo(p1) < 1e-6) return;
                var line = Line.CreateBound(p0, p1);
                var dc = doc.Create.NewDetailCurve(view, line);
                if (styleId != null && styleId != ElementId.InvalidElementId)
                    dc.LineStyle = doc.GetElement(styleId) as GraphicsStyle;
            }
            catch (Exception ex) { r.Warnings.Add($"detail line: {ex.Message}"); }
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
            catch (Exception ex) { r.Warnings.Add($"text note: {ex.Message}"); }
        }

        private static void DrawSymbol(Document doc, View view, XYZ centre, string sym,
            ElementId styleId, SupplySchematicResult r)
        {
            // Compact glyph: a small square (60 mm × 60 mm) per symbol, drawn
            // with detail lines so it works on any project without bespoke
            // detail families.
            double h = 60 * MmToFt / 2.0;
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

        private static TextNoteType FindOrCreateTextType(Document doc, double heightMm)
        {
            try
            {
                return new FilteredElementCollector(doc).OfClass(typeof(TextNoteType))
                    .Cast<TextNoteType>().FirstOrDefault();
            }
            catch { return null; }
        }

        private static string UniqueViewName(Document doc, string baseName)
        {
            try
            {
                var existing = new HashSet<string>(
                    new FilteredElementCollector(doc).OfClass(typeof(View))
                    .Cast<View>().Select(v => v.Name ?? ""),
                    StringComparer.OrdinalIgnoreCase);
                if (!existing.Contains(baseName)) return baseName;
                for (int i = 2; i < 1000; i++)
                {
                    var candidate = $"{baseName} ({i})";
                    if (!existing.Contains(candidate)) return candidate;
                }
            }
            catch { }
            return baseName + " " + DateTime.UtcNow.Ticks;
        }

        private static string ResolveExportDir(Document doc)
        {
            try
            {
                if (string.IsNullOrEmpty(doc?.PathName)) return null;
                var dir = Path.GetDirectoryName(doc.PathName);
                if (string.IsNullOrEmpty(dir)) return null;
                return Path.Combine(dir, "_BIM_COORD", "exports");
            }
            catch { return null; }
        }

        private static string Sanitise(string s)
        {
            if (string.IsNullOrEmpty(s)) return "schematic";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = s.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            return new string(chars);
        }

        // Map a config string to the Revit ACADVersion enum. R2004 was retired
        // in Revit 2025; if a project still requests it we degrade to R2007
        // (the oldest still-supported) and warn via Default → R2018 otherwise.
        private static ACADVersion MapAcadVersion(string v)
        {
            string s = (v ?? "").Trim().ToUpperInvariant();
            switch (s)
            {
                case "R2000":   return ACADVersion.R2000;
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
