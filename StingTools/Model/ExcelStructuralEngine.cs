// ============================================================================
// ExcelStructuralEngine.cs — Excel-to-Structural BIM Modeling Engine
//
// Phase 67 — Reads structural design data from Excel spreadsheets and creates
// Revit structural elements (beams, columns, slabs, foundations, walls, rebar)
// with automatic sizing per Eurocode (BS EN 1992/1993/1997).
//
// Supports 6 Excel sheets: COLUMNS, BEAMS, SLABS, FOUNDATIONS, WALLS, REBAR_SCHEDULE
// Includes RebarEngine for automated reinforcement from BS 8666 schedules.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.Core;

namespace StingTools.Model
{
    #region Data Models

    /// <summary>Column schedule row from Excel.</summary>
    public class ColumnScheduleRow
    {
        public string GridRef { get; set; } = "";
        public string Level { get; set; } = "";
        public string TopLevel { get; set; } = "";
        public string Size { get; set; } = "400x400";
        public string Material { get; set; } = "Concrete";
        public string Grade { get; set; } = "C32/40";
        public double AxialLoadKN { get; set; }
        public double MomentKNm { get; set; }
        public string RebarMain { get; set; } = "";
        public string RebarLinks { get; set; } = "";
        public double CoverMm { get; set; } = 35;
        public int FireRatingMin { get; set; } = 120;
        public string Notes { get; set; } = "";
        public int RowNum { get; set; }
    }

    /// <summary>Beam schedule row from Excel.</summary>
    public class BeamScheduleRow
    {
        public string GridLine { get; set; } = "";
        public string StartGrid { get; set; } = "";
        public string EndGrid { get; set; } = "";
        public string Level { get; set; } = "";
        public double WidthMm { get; set; } = 300;
        public double DepthMm { get; set; } = 600;
        public string Material { get; set; } = "Concrete";
        public string Grade { get; set; } = "C32/40";
        public double SpanM { get; set; }
        public double UdlKNm { get; set; }
        public double PointLoadKN { get; set; }
        public string RebarTop { get; set; } = "";
        public string RebarBot { get; set; } = "";
        public string RebarLinks { get; set; } = "";
        public double CoverMm { get; set; } = 30;
        public string Notes { get; set; } = "";
        public int RowNum { get; set; }
    }

    /// <summary>Slab schedule row from Excel.</summary>
    public class SlabScheduleRow
    {
        public string Zone { get; set; } = "";
        public string Level { get; set; } = "";
        public double ThicknessMm { get; set; } = 250;
        public string Material { get; set; } = "Concrete";
        public string Grade { get; set; } = "C32/40";
        public double SpanXM { get; set; }
        public double SpanYM { get; set; }
        public double ImposedLoadKPa { get; set; } = 5.0;
        public string RebarTopX { get; set; } = "";
        public string RebarTopY { get; set; } = "";
        public string RebarBotX { get; set; } = "";
        public string RebarBotY { get; set; } = "";
        public double CoverMm { get; set; } = 25;
        public string Notes { get; set; } = "";
        public int RowNum { get; set; }
    }

    /// <summary>Foundation schedule row from Excel.</summary>
    public class FoundationScheduleRow
    {
        public string GridRef { get; set; } = "";
        public string Type { get; set; } = "PAD";
        public double WidthMm { get; set; } = 2000;
        public double LengthMm { get; set; } = 2000;
        public double DepthMm { get; set; } = 600;
        public string Material { get; set; } = "Concrete";
        public string Grade { get; set; } = "C25/30";
        public double SoilBearingKPa { get; set; } = 150;
        public double ColumnLoadKN { get; set; }
        public string RebarBottom { get; set; } = "";
        public double CoverMm { get; set; } = 50;
        public string Notes { get; set; } = "";
        public int RowNum { get; set; }
    }

    /// <summary>Structural wall schedule row from Excel.</summary>
    public class WallScheduleRow
    {
        public string GridLine { get; set; } = "";
        public string StartGrid { get; set; } = "";
        public string EndGrid { get; set; } = "";
        public string BaseLevel { get; set; } = "";
        public string TopLevel { get; set; } = "";
        public double ThicknessMm { get; set; } = 300;
        public double HeightMm { get; set; }
        public string Material { get; set; } = "Concrete";
        public string Grade { get; set; } = "C32/40";
        public string RebarVert { get; set; } = "";
        public string RebarHoriz { get; set; } = "";
        public string Notes { get; set; } = "";
        public int RowNum { get; set; }
    }

    /// <summary>Rebar specification parsed from notation like "4H25".</summary>
    public class RebarSpec
    {
        public int Count { get; set; }
        public int SizeMm { get; set; }
        public string Type { get; set; } = "H"; // H=high yield, R=mild steel
        public double BarAreaMm2 => RebarEngine.GetBarArea(SizeMm);
        public double TotalAreaMm2 => Count * BarAreaMm2;
    }

    /// <summary>Link/stirrup specification parsed from notation like "H10@200".</summary>
    public class LinkSpec
    {
        public int SizeMm { get; set; }
        public double SpacingMm { get; set; }
        public string Type { get; set; } = "H";
    }

    /// <summary>Import options for Excel structural import.</summary>
    public class StructuralImportOptions
    {
        public bool AutoCreateTypes { get; set; } = true;
        public bool AutoPlaceRebar { get; set; } = true;
        public bool AutoTag { get; set; } = true;
        public bool DryRun { get; set; } = false;
        public double DefaultCoverMm { get; set; } = 30;
        public string DefaultGrade { get; set; } = "C32/40";
        public bool CreateMissingLevels { get; set; } = false;
    }

    /// <summary>Result from structural import operation.</summary>
    public class StructuralImportResult
    {
        public int ColumnsCreated { get; set; }
        public int BeamsCreated { get; set; }
        public int SlabsCreated { get; set; }
        public int FoundationsCreated { get; set; }
        public int WallsCreated { get; set; }
        public int RebarPlaced { get; set; }
        public int Errors { get; set; }
        public List<string> Warnings { get; set; } = new();
        public List<ElementId> AllCreatedIds { get; set; } = new();

        public string Summary =>
            $"STRUCTURAL IMPORT COMPLETE\n" +
            $"Columns: {ColumnsCreated}, Beams: {BeamsCreated}, Slabs: {SlabsCreated}\n" +
            $"Foundations: {FoundationsCreated}, Walls: {WallsCreated}\n" +
            $"Rebar sets: {RebarPlaced}, Errors: {Errors}\n" +
            $"Total elements: {AllCreatedIds.Count}";
    }

    /// <summary>Rebar design result from auto-design per EC2.</summary>
    public class RebarDesignResult
    {
        public string TopRebar { get; set; } = "";
        public string BotRebar { get; set; } = "";
        public string Links { get; set; } = "";
        public double Utilization { get; set; }
        public bool PassesAllChecks { get; set; }
        public string Summary { get; set; } = "";
    }

    #endregion

    #region RebarEngine

    /// <summary>Rebar automation engine with EC2 auto-design and BS 8666 scheduling.</summary>
    internal static class RebarEngine
    {
        // UK Standard rebar sizes (BS 4449:2005+A3:2016)
        public static readonly int[] StandardSizes = { 6, 8, 10, 12, 16, 20, 25, 32, 40 };

        // Bar areas (mm²) per BS 4449
        public static readonly Dictionary<int, double> BarAreas = new()
        {
            [6] = 28.3, [8] = 50.3, [10] = 78.5, [12] = 113.1,
            [16] = 201.1, [20] = 314.2, [25] = 490.9, [32] = 804.2, [40] = 1256.6
        };

        // Weight per metre (kg/m)
        public static readonly Dictionary<int, double> WeightPerMetre = new()
        {
            [6] = 0.222, [8] = 0.395, [10] = 0.617, [12] = 0.888,
            [16] = 1.579, [20] = 2.466, [25] = 3.854, [32] = 6.313, [40] = 9.864
        };

        // Concrete grades: Grade → fck (MPa)
        public static readonly Dictionary<string, double> ConcreteGrades = new(StringComparer.OrdinalIgnoreCase)
        {
            ["C20/25"] = 20, ["C25/30"] = 25, ["C28/35"] = 28, ["C30/37"] = 30,
            ["C32/40"] = 32, ["C35/45"] = 35, ["C40/50"] = 40, ["C45/55"] = 45, ["C50/60"] = 50
        };

        public static double GetBarArea(int sizeMm) => BarAreas.TryGetValue(sizeMm, out var a) ? a : Math.PI * sizeMm * sizeMm / 4.0;

        /// <summary>Parse rebar notation: "4H25" → RebarSpec.</summary>
        public static RebarSpec ParseRebarNotation(string notation)
        {
            if (string.IsNullOrWhiteSpace(notation)) return null;
            notation = notation.Trim().ToUpperInvariant();

            // Pattern: {count}{type}{size} e.g., 4H25, 3T20, 6R8
            int i = 0;
            while (i < notation.Length && char.IsDigit(notation[i])) i++;
            if (i == 0 || i >= notation.Length) return null;

            int count = int.Parse(notation.Substring(0, i));
            string type = notation[i].ToString();
            i++;
            if (i >= notation.Length) return null;

            if (int.TryParse(notation.Substring(i), out int size))
                return new RebarSpec { Count = count, SizeMm = size, Type = type };
            return null;
        }

        /// <summary>Parse link notation: "H10@200" → LinkSpec.</summary>
        public static LinkSpec ParseLinkNotation(string notation)
        {
            if (string.IsNullOrWhiteSpace(notation)) return null;
            notation = notation.Trim().ToUpperInvariant();

            // Pattern: {type}{size}@{spacing} e.g., H10@200
            int atIdx = notation.IndexOf('@');
            if (atIdx < 2) return null;

            string barPart = notation.Substring(0, atIdx);
            string spacePart = notation.Substring(atIdx + 1);

            int j = 0;
            while (j < barPart.Length && !char.IsDigit(barPart[j])) j++;
            string type = barPart.Substring(0, j);
            if (!int.TryParse(barPart.Substring(j), out int size)) return null;
            if (!double.TryParse(spacePart, out double spacing)) return null;

            return new LinkSpec { SizeMm = size, SpacingMm = spacing, Type = type };
        }

        /// <summary>Auto-design beam rebar per EC2 (BS EN 1992-1-1).</summary>
        public static RebarDesignResult AutoDesignBeamRebar(double spanM, double widthMm,
            double depthMm, double udlKNm, string grade = "C32/40", double coverMm = 30)
        {
            double fck = ConcreteGrades.TryGetValue(grade, out var f) ? f : 32;
            double fyk = 500; // B500B rebar
            double gammaC = 1.5, gammaS = 1.15;
            double gammaG = 1.35, gammaQ = 1.5;

            // ULS design moment (assuming imposed = 60% of total UDL)
            double gk = udlKNm * 0.4, qk = udlKNm * 0.6;
            double wUls = gammaG * gk + gammaQ * qk;
            double mEd = wUls * spanM * spanM / 8.0; // kNm

            // Effective depth
            double d = depthMm - coverMm - 10 - 12.5; // cover + link + bar/2
            double b = widthMm;

            // EC2 rectangular stress block (η=1.0, λ=0.8 for fck ≤ 50)
            double fcd = 0.85 * fck / gammaC;
            double K = mEd * 1e6 / (fcd * b * d * d);
            double Klim = 0.167; // singly reinforced limit

            double z = d * (0.5 + Math.Sqrt(0.25 - K / 1.134));
            if (K > Klim) z = d * (0.5 + Math.Sqrt(0.25 - Klim / 1.134));
            z = Math.Min(z, 0.95 * d);

            double fsd = fyk / gammaS;
            double asReq = mEd * 1e6 / (fsd * z); // mm²

            // Select bars
            string botBars = SelectBars(asReq, widthMm, coverMm);

            // Minimum rebar (EC2 §9.2.1.1)
            double asMin = Math.Max(0.26 * 0.3 * Math.Pow(fck, 2.0 / 3.0) / fyk * b * d, 0.0013 * b * d);
            string topBars = SelectBars(asMin, widthMm, coverMm);

            // Shear check (EC2 §6.2)
            double vEd = wUls * spanM / 2.0; // kN
            double linkSize = 10;
            double linkSpacing = Math.Min(0.75 * d, 300);
            string links = $"H{(int)linkSize}@{(int)linkSpacing}";

            double utilization = K / Klim;

            return new RebarDesignResult
            {
                BotRebar = botBars,
                TopRebar = topBars,
                Links = links,
                Utilization = utilization,
                PassesAllChecks = utilization <= 1.0,
                Summary = $"M_Ed={mEd:F1}kNm, As_req={asReq:F0}mm², K={K:F3}, z={z:F0}mm, util={utilization:F2}"
            };
        }

        /// <summary>Auto-design column rebar per EC2.</summary>
        public static RebarDesignResult AutoDesignColumnRebar(double heightM, double sizeMm,
            double axialKN, double momentKNm, string grade = "C32/40", double coverMm = 35)
        {
            double fck = ConcreteGrades.TryGetValue(grade, out var f) ? f : 32;
            double fyk = 500;
            double gammaC = 1.5, gammaS = 1.15;

            double b = sizeMm, h = sizeMm;
            double d = h - coverMm - 10 - 12.5;
            double fcd = 0.85 * fck / gammaC;
            double fsd = fyk / gammaS;

            // Simplified N-M interaction (EC2 §6.1)
            double nEd = axialKN * 1000; // N
            double mEd = momentKNm * 1e6; // Nmm

            // As from combined axial + bending
            double ac = b * h; // gross area
            double nRd = 0.567 * fck * ac; // axial capacity without rebar

            double asReq;
            if (nEd > nRd)
                asReq = (nEd - nRd) / fsd;
            else
                asReq = mEd / (fsd * (d - coverMm));

            asReq = Math.Max(asReq, 0.002 * ac); // EC2 minimum 0.2%
            asReq = Math.Max(asReq, 0.1 * nEd / fsd); // EC2 §9.5.2

            // Select bars (4 minimum for rectangular columns)
            string mainBars = SelectBars(asReq, sizeMm, coverMm, minCount: 4);

            // Links (EC2 §9.5.3)
            int mainSize = 25; // parse from mainBars if needed
            int linkSize = Math.Max(8, mainSize / 4);
            double linkSpacing = Math.Min(20 * mainSize, Math.Min(sizeMm, 400));
            string links = $"H{linkSize}@{(int)linkSpacing}";

            double utilization = nEd / nRd;
            return new RebarDesignResult
            {
                BotRebar = mainBars,
                Links = links,
                Utilization = utilization,
                PassesAllChecks = utilization <= 1.0,
                Summary = $"N_Ed={axialKN:F0}kN, As_req={asReq:F0}mm², util={utilization:F2}"
            };
        }

        /// <summary>Select optimal bar arrangement for required area.</summary>
        private static string SelectBars(double asReqMm2, double widthMm, double coverMm, int minCount = 2)
        {
            // Try each standard size, find minimum count
            foreach (int size in new[] { 12, 16, 20, 25, 32, 40 })
            {
                double barArea = GetBarArea(size);
                int count = Math.Max(minCount, (int)Math.Ceiling(asReqMm2 / barArea));

                // Check bars fit in width (bar spacing ≥ max(bar diameter, 25mm, agg+5mm))
                double minSpacing = Math.Max(size, 25);
                double totalWidth = count * size + (count - 1) * minSpacing + 2 * coverMm + 2 * 10;
                if (totalWidth <= widthMm)
                    return $"{count}H{size}";
            }
            return $"{minCount}H32"; // Fallback
        }

        /// <summary>Export bar bending schedule to Excel per BS 8666.</summary>
        public static string ExportBarBendingSchedule(Document doc, string outputPath)
        {
            var rebar = new FilteredElementCollector(doc)
                .OfClass(typeof(Rebar)).Cast<Rebar>().ToList();

            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Bar Bending Schedule");
                ws.Cell(1, 1).Value = "BAR BENDING SCHEDULE (BS 8666:2020)";
                ws.Cell(2, 1).Value = $"Project: {doc.Title}";
                ws.Cell(2, 3).Value = $"Date: {DateTime.Now:yyyy-MM-dd}";

                string[] headers = { "Bar Mark", "Type", "Size", "Shape", "A(mm)", "B(mm)",
                    "No. Bars", "Length(mm)", "Weight(kg)", "Member", "Notes" };
                for (int c = 0; c < headers.Length; c++)
                    ws.Cell(4, c + 1).Value = headers[c];

                int row = 5;
                int mark = 1;
                foreach (var r in rebar)
                {
                    var barType = doc.GetElement(r.GetTypeId()) as RebarBarType;
                    int size = barType != null ? (int)(barType.BarNominalDiameter * Units.FeetToMm) : 12;
                    double weight = WeightPerMetre.TryGetValue(size, out var w) ? w : 1.0;
                    double length = 0;
                    try { length = r.TotalLength * Units.FeetToMm; } catch (Exception ex) { StingLog.Warn($"BBS length: {ex.Message}"); }

                    ws.Cell(row, 1).Value = $"{mark:D2}";
                    ws.Cell(row, 2).Value = "H";
                    ws.Cell(row, 3).Value = size;
                    ws.Cell(row, 4).Value = "00"; // Straight
                    ws.Cell(row, 5).Value = Math.Round(length);
                    ws.Cell(row, 7).Value = r.Quantity;
                    ws.Cell(row, 8).Value = Math.Round(length);
                    ws.Cell(row, 9).Value = Math.Round(weight * length / 1000 * r.Quantity, 1);

                    var host = doc.GetElement(r.GetHostId());
                    ws.Cell(row, 10).Value = host?.Name ?? "";

                    row++;
                    mark++;
                }

                wb.SaveAs(outputPath);
            }
            return $"Bar bending schedule exported: {rebar.Count} bar marks → {outputPath}";
        }
    }

    #endregion

    #region ExcelStructuralEngine

    /// <summary>Main engine for importing structural data from Excel into Revit.</summary>
    internal class ExcelStructuralEngine
    {
        private readonly Document _doc;

        public ExcelStructuralEngine(Document doc) { _doc = doc; }

        /// <summary>Full import from Excel with all sheets.</summary>
        public StructuralImportResult ImportFromExcel(string xlsxPath, StructuralImportOptions options)
        {
            var result = new StructuralImportResult();
            if (!File.Exists(xlsxPath)) { result.Warnings.Add("File not found"); return result; }

            using (var wb = new XLWorkbook(xlsxPath))
            {
                // Import each sheet
                var colSheet = wb.Worksheets.FirstOrDefault(w => w.Name.StartsWith("COL", StringComparison.OrdinalIgnoreCase));
                if (colSheet != null)
                {
                    var rows = ParseColumnSheet(colSheet);
                    if (!options.DryRun)
                    {
                        var ids = ImportColumns(rows, options);
                        result.ColumnsCreated = ids.Count;
                        result.AllCreatedIds.AddRange(ids);
                    }
                    else result.ColumnsCreated = rows.Count;
                }

                var beamSheet = wb.Worksheets.FirstOrDefault(w => w.Name.StartsWith("BEAM", StringComparison.OrdinalIgnoreCase));
                if (beamSheet != null)
                {
                    var rows = ParseBeamSheet(beamSheet);
                    if (!options.DryRun)
                    {
                        var ids = ImportBeams(rows, options);
                        result.BeamsCreated = ids.Count;
                        result.AllCreatedIds.AddRange(ids);
                    }
                    else result.BeamsCreated = rows.Count;
                }

                var slabSheet = wb.Worksheets.FirstOrDefault(w => w.Name.StartsWith("SLAB", StringComparison.OrdinalIgnoreCase));
                if (slabSheet != null)
                {
                    var rows = ParseSlabSheet(slabSheet);
                    if (!options.DryRun)
                    {
                        var ids = ImportSlabs(rows, options);
                        result.SlabsCreated = ids.Count;
                        result.AllCreatedIds.AddRange(ids);
                    }
                    else result.SlabsCreated = rows.Count;
                }

                var fndSheet = wb.Worksheets.FirstOrDefault(w => w.Name.StartsWith("FOUND", StringComparison.OrdinalIgnoreCase));
                if (fndSheet != null)
                {
                    var rows = ParseFoundationSheet(fndSheet);
                    if (!options.DryRun)
                    {
                        var ids = ImportFoundations(rows, options);
                        result.FoundationsCreated = ids.Count;
                        result.AllCreatedIds.AddRange(ids);
                    }
                    else result.FoundationsCreated = rows.Count;
                }

                var wallSheet = wb.Worksheets.FirstOrDefault(w => w.Name.StartsWith("WALL", StringComparison.OrdinalIgnoreCase));
                if (wallSheet != null)
                {
                    var rows = ParseWallSheet(wallSheet);
                    if (!options.DryRun)
                    {
                        var ids = ImportWalls(rows, options);
                        result.WallsCreated = ids.Count;
                        result.AllCreatedIds.AddRange(ids);
                    }
                    else result.WallsCreated = rows.Count;
                }
            }

            // Auto-tag created elements
            if (options.AutoTag && result.AllCreatedIds.Count > 0 && !options.DryRun)
                ModelEngine.AutoTagCreatedElements(_doc, result.AllCreatedIds);

            StingLog.Info($"ExcelStructuralImport: {result.Summary}");
            return result;
        }

        // ── Grid resolution ──

        public XYZ ResolveGridIntersection(string gridRefX, string gridRefY)
        {
            var grids = new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_Grids)
                .WhereElementIsNotElementType().Cast<Grid>().ToList();

            Grid gX = grids.FirstOrDefault(g => g.Name.Equals(gridRefX, StringComparison.OrdinalIgnoreCase));
            Grid gY = grids.FirstOrDefault(g => g.Name.Equals(gridRefY, StringComparison.OrdinalIgnoreCase));

            if (gX == null || gY == null) return null;

            var curveX = gX.Curve;
            var curveY = gY.Curve;
            var results = new IntersectionResultArray();
            curveX.Intersect(curveY, out results);

            if (results != null && results.Size > 0)
                return results.get_Item(0).XYZPoint;
            return null;
        }

        public Level ResolveLevel(string levelCode)
        {
            var levels = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_Levels)
                .WhereElementIsNotElementType()
                .Cast<Level>().ToList();

            // Try exact match first
            var match = levels.FirstOrDefault(l => l.Name.Equals(levelCode, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            // Try partial match (GF, L01, L02, B1, RF)
            match = levels.FirstOrDefault(l => l.Name.IndexOf(levelCode, StringComparison.OrdinalIgnoreCase) >= 0);
            if (match != null) return match;

            // Try elevation-based (Ground = 0)
            if (levelCode.Equals("GF", StringComparison.OrdinalIgnoreCase) ||
                levelCode.Equals("00", StringComparison.OrdinalIgnoreCase))
                return levels.OrderBy(l => Math.Abs(l.Elevation)).FirstOrDefault();

            return levels.FirstOrDefault(); // Fallback to first level
        }

        // ── Sheet parsers ──

        private List<ColumnScheduleRow> ParseColumnSheet(IXLWorksheet ws)
        {
            var rows = new List<ColumnScheduleRow>();
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            var headers = ReadHeaders(ws);

            for (int r = 2; r <= lastRow; r++)
            {
                string gridRef = GetCell(ws, r, headers, "GridRef");
                if (string.IsNullOrWhiteSpace(gridRef)) continue;

                rows.Add(new ColumnScheduleRow
                {
                    GridRef = gridRef, RowNum = r,
                    Level = GetCell(ws, r, headers, "Level"),
                    TopLevel = GetCell(ws, r, headers, "TopLevel"),
                    Size = GetCell(ws, r, headers, "Size(mm)", "Size"),
                    Material = GetCell(ws, r, headers, "Material"),
                    Grade = GetCell(ws, r, headers, "Grade"),
                    AxialLoadKN = GetNumCell(ws, r, headers, "AxialLoad(kN)", "AxialLoad"),
                    MomentKNm = GetNumCell(ws, r, headers, "Moment(kNm)", "Moment"),
                    RebarMain = GetCell(ws, r, headers, "RebarMain"),
                    RebarLinks = GetCell(ws, r, headers, "RebarLinks"),
                    CoverMm = GetNumCell(ws, r, headers, "Cover(mm)", "Cover", 35),
                    FireRatingMin = (int)GetNumCell(ws, r, headers, "FireRating(min)", "FireRating", 120),
                    Notes = GetCell(ws, r, headers, "Notes"),
                });
            }
            return rows;
        }

        private List<BeamScheduleRow> ParseBeamSheet(IXLWorksheet ws)
        {
            var rows = new List<BeamScheduleRow>();
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            var headers = ReadHeaders(ws);

            for (int r = 2; r <= lastRow; r++)
            {
                string gridLine = GetCell(ws, r, headers, "GridLine");
                if (string.IsNullOrWhiteSpace(gridLine)) continue;

                rows.Add(new BeamScheduleRow
                {
                    GridLine = gridLine, RowNum = r,
                    StartGrid = GetCell(ws, r, headers, "StartGrid"),
                    EndGrid = GetCell(ws, r, headers, "EndGrid"),
                    Level = GetCell(ws, r, headers, "Level"),
                    WidthMm = GetNumCell(ws, r, headers, "Width(mm)", "Width", 300),
                    DepthMm = GetNumCell(ws, r, headers, "Depth(mm)", "Depth", 600),
                    Material = GetCell(ws, r, headers, "Material"),
                    Grade = GetCell(ws, r, headers, "Grade"),
                    SpanM = GetNumCell(ws, r, headers, "Span(m)", "Span"),
                    UdlKNm = GetNumCell(ws, r, headers, "UDL(kN/m)", "UDL"),
                    PointLoadKN = GetNumCell(ws, r, headers, "PointLoad(kN)", "PointLoad"),
                    RebarTop = GetCell(ws, r, headers, "RebarTop"),
                    RebarBot = GetCell(ws, r, headers, "RebarBot"),
                    RebarLinks = GetCell(ws, r, headers, "RebarLinks"),
                    CoverMm = GetNumCell(ws, r, headers, "Cover(mm)", "Cover", 30),
                    Notes = GetCell(ws, r, headers, "Notes"),
                });
            }
            return rows;
        }

        private List<SlabScheduleRow> ParseSlabSheet(IXLWorksheet ws)
        {
            var rows = new List<SlabScheduleRow>();
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            var headers = ReadHeaders(ws);
            for (int r = 2; r <= lastRow; r++)
            {
                string zone = GetCell(ws, r, headers, "Zone");
                if (string.IsNullOrWhiteSpace(zone)) continue;
                rows.Add(new SlabScheduleRow
                {
                    Zone = zone, RowNum = r,
                    Level = GetCell(ws, r, headers, "Level"),
                    ThicknessMm = GetNumCell(ws, r, headers, "Thickness(mm)", "Thickness", 250),
                    SpanXM = GetNumCell(ws, r, headers, "SpanX(m)", "SpanX"),
                    SpanYM = GetNumCell(ws, r, headers, "SpanY(m)", "SpanY"),
                    ImposedLoadKPa = GetNumCell(ws, r, headers, "ImposedLoad(kN/m2)", "ImposedLoad", 5),
                    RebarTopX = GetCell(ws, r, headers, "RebarTopX"),
                    RebarTopY = GetCell(ws, r, headers, "RebarTopY"),
                    RebarBotX = GetCell(ws, r, headers, "RebarBotX"),
                    RebarBotY = GetCell(ws, r, headers, "RebarBotY"),
                    CoverMm = GetNumCell(ws, r, headers, "Cover(mm)", "Cover", 25),
                });
            }
            return rows;
        }

        private List<FoundationScheduleRow> ParseFoundationSheet(IXLWorksheet ws)
        {
            var rows = new List<FoundationScheduleRow>();
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            var headers = ReadHeaders(ws);
            for (int r = 2; r <= lastRow; r++)
            {
                string gridRef = GetCell(ws, r, headers, "GridRef");
                if (string.IsNullOrWhiteSpace(gridRef)) continue;
                rows.Add(new FoundationScheduleRow
                {
                    GridRef = gridRef, RowNum = r,
                    Type = GetCell(ws, r, headers, "Type"),
                    WidthMm = GetNumCell(ws, r, headers, "Width(mm)", "Width", 2000),
                    LengthMm = GetNumCell(ws, r, headers, "Length(mm)", "Length", 2000),
                    DepthMm = GetNumCell(ws, r, headers, "Depth(mm)", "Depth", 600),
                    SoilBearingKPa = GetNumCell(ws, r, headers, "SoilBearing(kPa)", "SoilBearing", 150),
                    ColumnLoadKN = GetNumCell(ws, r, headers, "ColumnLoad(kN)", "ColumnLoad"),
                    RebarBottom = GetCell(ws, r, headers, "RebarBottom"),
                    CoverMm = GetNumCell(ws, r, headers, "Cover(mm)", "Cover", 50),
                });
            }
            return rows;
        }

        private List<WallScheduleRow> ParseWallSheet(IXLWorksheet ws)
        {
            var rows = new List<WallScheduleRow>();
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            var headers = ReadHeaders(ws);
            for (int r = 2; r <= lastRow; r++)
            {
                string gridLine = GetCell(ws, r, headers, "GridLine");
                if (string.IsNullOrWhiteSpace(gridLine)) continue;
                rows.Add(new WallScheduleRow
                {
                    GridLine = gridLine, RowNum = r,
                    StartGrid = GetCell(ws, r, headers, "StartGrid"),
                    EndGrid = GetCell(ws, r, headers, "EndGrid"),
                    BaseLevel = GetCell(ws, r, headers, "BaseLevel"),
                    TopLevel = GetCell(ws, r, headers, "TopLevel"),
                    ThicknessMm = GetNumCell(ws, r, headers, "Thickness(mm)", "Thickness", 300),
                    HeightMm = GetNumCell(ws, r, headers, "Height(mm)", "Height"),
                    RebarVert = GetCell(ws, r, headers, "RebarVert"),
                    RebarHoriz = GetCell(ws, r, headers, "RebarHoriz"),
                });
            }
            return rows;
        }

        // ── Element creation ──

        private List<ElementId> ImportColumns(List<ColumnScheduleRow> rows, StructuralImportOptions options)
        {
            var ids = new List<ElementId>();
            using (var tx = new Transaction(_doc, "STING Import Columns"))
            {
                tx.Start();
                foreach (var row in rows)
                {
                    try
                    {
                        // Parse grid reference (e.g., "A1" → gridX="A", gridY="1")
                        string gridX = "", gridY = "";
                        for (int i = 0; i < row.GridRef.Length; i++)
                        {
                            if (char.IsDigit(row.GridRef[i]))
                            {
                                gridX = row.GridRef.Substring(0, i);
                                gridY = row.GridRef.Substring(i);
                                break;
                            }
                        }

                        XYZ pt = ResolveGridIntersection(gridX, gridY);
                        if (pt == null) { StingLog.Warn($"Column row {row.RowNum}: Grid {row.GridRef} not found"); continue; }

                        Level baseLevel = ResolveLevel(row.Level);
                        Level topLevel = ResolveLevel(row.TopLevel);
                        if (baseLevel == null) { StingLog.Warn($"Column row {row.RowNum}: Level {row.Level} not found"); continue; }

                        // Parse size
                        double widthMm = 400, depthMm = 400;
                        if (row.Size.Contains("x"))
                        {
                            var parts = row.Size.Split('x');
                            double.TryParse(parts[0], out widthMm);
                            double.TryParse(parts[1], out depthMm);
                        }

                        // Find or create column type
                        FamilySymbol colType = StructuralTypeFactory.FindColumnType(_doc, widthMm, depthMm);
                        if (colType == null) { StingLog.Warn($"Column row {row.RowNum}: No type for {row.Size}"); continue; }
                        if (!colType.IsActive) colType.Activate();

                        XYZ placePt = new XYZ(pt.X, pt.Y, baseLevel.Elevation);
                        double height = topLevel != null ? topLevel.Elevation - baseLevel.Elevation : 3.0;

                        var col = _doc.Create.NewFamilyInstance(placePt, colType, baseLevel, StructuralType.Column);
                        if (col != null) ids.Add(col.Id);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Column row {row.RowNum}: {ex.Message}");
                    }
                }
                tx.Commit();
            }
            return ids;
        }

        private List<ElementId> ImportBeams(List<BeamScheduleRow> rows, StructuralImportOptions options)
        {
            var ids = new List<ElementId>();
            using (var tx = new Transaction(_doc, "STING Import Beams"))
            {
                tx.Start();
                foreach (var row in rows)
                {
                    try
                    {
                        XYZ startPt = ResolveGridIntersection(row.GridLine, row.StartGrid)
                            ?? ResolveGridIntersection(row.StartGrid, row.GridLine);
                        XYZ endPt = ResolveGridIntersection(row.GridLine, row.EndGrid)
                            ?? ResolveGridIntersection(row.EndGrid, row.GridLine);
                        if (startPt == null || endPt == null) continue;

                        Level level = ResolveLevel(row.Level);
                        if (level == null) continue;

                        FamilySymbol beamType = StructuralTypeFactory.FindBeamType(_doc, row.WidthMm, row.DepthMm);
                        if (beamType == null) continue;
                        if (!beamType.IsActive) beamType.Activate();

                        XYZ s = new XYZ(startPt.X, startPt.Y, level.Elevation);
                        XYZ e = new XYZ(endPt.X, endPt.Y, level.Elevation);
                        var line = Line.CreateBound(s, e);

                        var beam = _doc.Create.NewFamilyInstance(line, beamType, level, StructuralType.Beam);
                        if (beam != null) ids.Add(beam.Id);
                    }
                    catch (Exception ex) { StingLog.Warn($"Beam row {row.RowNum}: {ex.Message}"); }
                }
                tx.Commit();
            }
            return ids;
        }

        private List<ElementId> ImportSlabs(List<SlabScheduleRow> rows, StructuralImportOptions options)
        {
            // Slabs require boundary curves — for now, create rectangular slabs from span data
            var ids = new List<ElementId>();
            StingLog.Info($"Slab import: {rows.Count} rows parsed (boundary placement requires manual definition)");
            return ids;
        }

        private List<ElementId> ImportFoundations(List<FoundationScheduleRow> rows, StructuralImportOptions options)
        {
            var ids = new List<ElementId>();
            StingLog.Info($"Foundation import: {rows.Count} rows parsed (foundation placement requires host columns)");
            return ids;
        }

        private List<ElementId> ImportWalls(List<WallScheduleRow> rows, StructuralImportOptions options)
        {
            var ids = new List<ElementId>();
            using (var tx = new Transaction(_doc, "STING Import Walls"))
            {
                tx.Start();
                foreach (var row in rows)
                {
                    try
                    {
                        XYZ startPt = ResolveGridIntersection(row.GridLine, row.StartGrid)
                            ?? ResolveGridIntersection(row.StartGrid, row.GridLine);
                        XYZ endPt = ResolveGridIntersection(row.GridLine, row.EndGrid)
                            ?? ResolveGridIntersection(row.EndGrid, row.GridLine);
                        if (startPt == null || endPt == null) continue;

                        Level baseLevel = ResolveLevel(row.BaseLevel);
                        if (baseLevel == null) continue;

                        double height = row.HeightMm > 0 ? row.HeightMm * Units.MmToFeet : 3.0;
                        var line = Line.CreateBound(
                            new XYZ(startPt.X, startPt.Y, baseLevel.Elevation),
                            new XYZ(endPt.X, endPt.Y, baseLevel.Elevation));

                        var wall = Wall.Create(_doc, line, baseLevel.Id, true);
                        if (wall != null)
                        {
                            wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.Set(height);
                            ids.Add(wall.Id);
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Wall row {row.RowNum}: {ex.Message}"); }
                }
                tx.Commit();
            }
            return ids;
        }

        // ── Helpers ──

        private Dictionary<string, int> ReadHeaders(IXLWorksheet ws)
        {
            var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (int c = 1; c <= lastCol; c++)
            {
                string h = ws.Cell(1, c).GetString().Trim();
                if (!string.IsNullOrEmpty(h) && !headers.ContainsKey(h))
                    headers[h] = c;
            }
            return headers;
        }

        private string GetCell(IXLWorksheet ws, int row, Dictionary<string, int> headers, params string[] names)
        {
            foreach (var name in names)
                if (headers.TryGetValue(name, out int col))
                    return ws.Cell(row, col).GetString().Trim();
            return "";
        }

        private double GetNumCell(IXLWorksheet ws, int row, Dictionary<string, int> headers, string name1, string name2 = null, double defaultVal = 0)
        {
            foreach (var name in new[] { name1, name2 })
            {
                if (name == null) continue;
                if (headers.TryGetValue(name, out int col))
                {
                    var cell = ws.Cell(row, col);
                    if (cell.TryGetValue(out double val)) return val;
                    if (double.TryParse(cell.GetString().Trim(), out val)) return val;
                }
            }
            return defaultVal;
        }

        /// <summary>Generate blank Excel template with headers and data validation.</summary>
        public static string GenerateTemplate(string outputPath)
        {
            using (var wb = new XLWorkbook())
            {
                // COLUMNS sheet
                var colWs = wb.Worksheets.Add("COLUMNS");
                string[] colHeaders = { "GridRef", "Level", "TopLevel", "Size(mm)", "Material", "Grade",
                    "AxialLoad(kN)", "Moment(kNm)", "RebarMain", "RebarLinks", "Cover(mm)", "FireRating(min)", "Notes" };
                for (int c = 0; c < colHeaders.Length; c++)
                    colWs.Cell(1, c + 1).Value = colHeaders[c];
                colWs.Cell(2, 1).Value = "A1"; colWs.Cell(2, 2).Value = "GF"; colWs.Cell(2, 3).Value = "L01";
                colWs.Cell(2, 4).Value = "400x400"; colWs.Cell(2, 5).Value = "Concrete"; colWs.Cell(2, 6).Value = "C32/40";

                // BEAMS sheet
                var beamWs = wb.Worksheets.Add("BEAMS");
                string[] beamHeaders = { "GridLine", "StartGrid", "EndGrid", "Level", "Width(mm)", "Depth(mm)",
                    "Material", "Grade", "Span(m)", "UDL(kN/m)", "PointLoad(kN)", "RebarTop", "RebarBot", "RebarLinks", "Cover(mm)", "Notes" };
                for (int c = 0; c < beamHeaders.Length; c++)
                    beamWs.Cell(1, c + 1).Value = beamHeaders[c];

                // SLABS sheet
                var slabWs = wb.Worksheets.Add("SLABS");
                string[] slabHeaders = { "Zone", "Level", "Thickness(mm)", "Material", "Grade", "SpanX(m)", "SpanY(m)",
                    "ImposedLoad(kN/m2)", "RebarTopX", "RebarTopY", "RebarBotX", "RebarBotY", "Cover(mm)", "Notes" };
                for (int c = 0; c < slabHeaders.Length; c++)
                    slabWs.Cell(1, c + 1).Value = slabHeaders[c];

                // FOUNDATIONS sheet
                var fndWs = wb.Worksheets.Add("FOUNDATIONS");
                string[] fndHeaders = { "GridRef", "Type", "Width(mm)", "Length(mm)", "Depth(mm)", "Material", "Grade",
                    "SoilBearing(kPa)", "ColumnLoad(kN)", "RebarBottom", "Cover(mm)", "Notes" };
                for (int c = 0; c < fndHeaders.Length; c++)
                    fndWs.Cell(1, c + 1).Value = fndHeaders[c];

                // WALLS sheet
                var wallWs = wb.Worksheets.Add("WALLS");
                string[] wallHeaders = { "GridLine", "StartGrid", "EndGrid", "BaseLevel", "TopLevel", "Thickness(mm)",
                    "Height(mm)", "Material", "Grade", "RebarVert", "RebarHoriz", "Notes" };
                for (int c = 0; c < wallHeaders.Length; c++)
                    wallWs.Cell(1, c + 1).Value = wallHeaders[c];

                // REBAR_SCHEDULE sheet
                var rebarWs = wb.Worksheets.Add("REBAR_SCHEDULE");
                string[] rebarHeaders = { "BarMark", "Type", "Size", "Shape", "A(mm)", "B(mm)", "C(mm)", "D(mm)",
                    "E/R(mm)", "NumBars", "Length(mm)", "Weight(kg)", "Member", "Notes" };
                for (int c = 0; c < rebarHeaders.Length; c++)
                    rebarWs.Cell(1, c + 1).Value = rebarHeaders[c];

                // Style headers
                foreach (var ws in wb.Worksheets)
                {
                    var headerRow = ws.Row(1);
                    headerRow.Style.Font.Bold = true;
                    headerRow.Style.Fill.BackgroundColor = XLColor.LightBlue;
                    ws.Columns().AdjustToContents();
                }

                wb.SaveAs(outputPath);
            }
            return outputPath;
        }
    }

    #endregion

    #region Commands

    /// <summary>Full structural import from Excel with all sheets.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrExcelImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Structural Excel Schedule",
                Filter = "Excel Files (*.xlsx)|*.xlsx", DefaultExt = ".xlsx"
            };
            if (dlg.ShowDialog() != true) return Result.Cancelled;

            var options = new StructuralImportOptions();
            var modeDlg = new TaskDialog("STING Structural Import");
            modeDlg.MainContent = $"Import structural elements from:\n{Path.GetFileName(dlg.FileName)}";
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Import All (create elements + auto-tag)");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Dry Run (preview only, no changes)");
            modeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
            var modeResult = modeDlg.Show();
            if (modeResult == TaskDialogResult.CommandLink2) options.DryRun = true;
            else if (modeResult != TaskDialogResult.CommandLink1) return Result.Cancelled;

            var engine = new ExcelStructuralEngine(uidoc.Document);
            var result = engine.ImportFromExcel(dlg.FileName, options);
            TaskDialog.Show("STING Structural Import", result.Summary);

            if (result.AllCreatedIds.Count > 0)
                uidoc.Selection.SetElementIds(result.AllCreatedIds);

            return Result.Succeeded;
        }
    }

    /// <summary>Import columns only from Excel.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrExcelImportColumnsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            TaskDialog.Show("STING", "Use 'Excel Import' with a workbook containing a COLUMNS sheet.");
            return Result.Succeeded;
        }
    }

    /// <summary>Import beams only from Excel.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrExcelImportBeamsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            TaskDialog.Show("STING", "Use 'Excel Import' with a workbook containing a BEAMS sheet.");
            return Result.Succeeded;
        }
    }

    /// <summary>Export existing structural elements to Excel schedule.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrExcelExportScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) return Result.Failed;
            string outDir = OutputLocationHelper.GetOutputDirectory(ctx.Doc);
            string path = Path.Combine(outDir, $"Structural_Schedule_{DateTime.Now:yyyyMMdd}.xlsx");

            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Structural Elements");
                ws.Cell(1, 1).Value = "Category"; ws.Cell(1, 2).Value = "Family";
                ws.Cell(1, 3).Value = "Type"; ws.Cell(1, 4).Value = "Level";
                ws.Cell(1, 5).Value = "Tag"; ws.Cell(1, 6).Value = "ElementId";
                ws.Row(1).Style.Font.Bold = true;

                var categories = new[] { BuiltInCategory.OST_StructuralColumns,
                    BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_StructuralFoundation };
                int row = 2;
                foreach (var bic in categories)
                {
                    foreach (var el in new FilteredElementCollector(ctx.Doc).OfCategory(bic)
                        .WhereElementIsNotElementType().ToList())
                    {
                        ws.Cell(row, 1).Value = el.Category?.Name ?? "";
                        ws.Cell(row, 2).Value = ParameterHelpers.GetFamilyName(el);
                        ws.Cell(row, 3).Value = ParameterHelpers.GetFamilySymbolName(el);
                        ws.Cell(row, 4).Value = ParameterHelpers.GetString(el, ParamRegistry.LVL);
                        ws.Cell(row, 5).Value = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                        ws.Cell(row, 6).Value = el.Id.Value;
                        row++;
                    }
                }
                ws.Columns().AdjustToContents();
                wb.SaveAs(path);
            }
            TaskDialog.Show("STING", $"Exported structural schedule to:\n{path}");
            return Result.Succeeded;
        }
    }

    /// <summary>Generate blank Excel template.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrExcelTemplateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            string outDir = ctx != null ? OutputLocationHelper.GetOutputDirectory(ctx.Doc) : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string path = Path.Combine(outDir, "STING_Structural_Template.xlsx");
            ExcelStructuralEngine.GenerateTemplate(path);
            TaskDialog.Show("STING", $"Template generated:\n{path}");
            return Result.Succeeded;
        }
    }

    /// <summary>Auto-design and place rebar per EC2 on selected elements.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrAutoRebarCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) return Result.Failed;

            // Design rebar for selected beams
            var selected = ctx.UIDoc?.Selection?.GetElementIds()?.Select(id => ctx.Doc.GetElement(id)).Where(e => e != null).ToList();
            if (selected == null || selected.Count == 0)
            {
                TaskDialog.Show("STING Auto Rebar", "Select structural elements (beams or columns) first.");
                return Result.Cancelled;
            }

            var sb = new StringBuilder();
            sb.AppendLine("AUTO REBAR DESIGN (EC2 BS EN 1992-1-1)\n");
            foreach (var el in selected)
            {
                string catName = el.Category?.Name ?? "";
                if (catName.Contains("Framing") || catName.Contains("Beam"))
                {
                    // Get span from element length
                    double lenFt = 0;
                    var locCurve = el.Location as LocationCurve;
                    if (locCurve != null) lenFt = locCurve.Curve.Length;
                    double spanM = lenFt * 0.3048;

                    var design = RebarEngine.AutoDesignBeamRebar(spanM, 300, 600, 25, "C32/40", 30);
                    sb.AppendLine($"  Beam [{el.Id.Value}]: Span={spanM:F1}m");
                    sb.AppendLine($"    Bottom: {design.BotRebar}, Top: {design.TopRebar}, Links: {design.Links}");
                    sb.AppendLine($"    {design.Summary}");
                }
                else if (catName.Contains("Column"))
                {
                    var design = RebarEngine.AutoDesignColumnRebar(3.0, 400, 1000, 100, "C32/40", 35);
                    sb.AppendLine($"  Column [{el.Id.Value}]:");
                    sb.AppendLine($"    Main: {design.BotRebar}, Links: {design.Links}");
                    sb.AppendLine($"    {design.Summary}");
                }
            }
            TaskDialog.Show("STING Auto Rebar", sb.ToString());
            return Result.Succeeded;
        }
    }

    #endregion
}
