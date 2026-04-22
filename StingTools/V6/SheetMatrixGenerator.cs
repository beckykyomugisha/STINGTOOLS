// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/SheetMatrixGenerator.cs — S6.6 (N-G10).
//
// Reads a project sheet matrix (STING_SHEET_MATRIX.json) and
// auto-generates sheets with the correct title block and view
// placement. A row is "Plans by Level", "Elevations by Facade",
// "Sections by Axis", "Details by Element Type", etc. Each row
// expands into one sheet per iterator value (one sheet per level,
// one per facade, one per axis, ...).
//
// Run once at each submission. Far faster than manual sheet creation
// and guarantees title block + view coordinates are consistent
// across a project.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.V6
{
    public sealed class SheetMatrixRow
    {
        public string Name { get; set; } = string.Empty;            // e.g. "Plans by Level"
        public string TitleBlockName { get; set; } = string.Empty;
        public string Iterator { get; set; } = "LEVEL";              // LEVEL|FACADE|AXIS|CATEGORY|PHASE
        public string ViewFamily { get; set; } = "FloorPlan";
        public string SheetNumberPattern { get; set; } = "A-10{i:D2}";
        public double ViewportXFt { get; set; } = 1.0;
        public double ViewportYFt { get; set; } = 1.0;
        public double ViewScale { get; set; } = 100.0;               // 1:100
    }

    public sealed class SheetMatrixResult
    {
        public int RowsProcessed { get; set; }
        public int SheetsCreated { get; set; }
        public List<string> CreatedSheetNumbers { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
    }

    public static class SheetMatrixGenerator
    {
        public static List<SheetMatrixRow> LoadMatrix()
        {
            try
            {
                string dir = Path.GetDirectoryName(typeof(SheetMatrixGenerator).Assembly.Location) ?? "";
                string path = Path.Combine(dir, "Data", "Sheets", "STING_SHEET_MATRIX.json");
                if (!File.Exists(path))
                {
                    StingLog.Warn($"SheetMatrixGenerator: matrix file missing: {path}");
                    return new List<SheetMatrixRow>();
                }
                var arr = JArray.Parse(File.ReadAllText(path));
                var list = new List<SheetMatrixRow>();
                foreach (var t in arr)
                {
                    list.Add(new SheetMatrixRow
                    {
                        Name               = (string)t["name"] ?? string.Empty,
                        TitleBlockName     = (string)t["title_block"] ?? string.Empty,
                        Iterator           = (string)t["iterator"] ?? "LEVEL",
                        ViewFamily         = (string)t["view_family"] ?? "FloorPlan",
                        SheetNumberPattern = (string)t["sheet_number_pattern"] ?? "A-10{i:D2}",
                        ViewportXFt        = (double?)t["viewport_x_ft"] ?? 1.0,
                        ViewportYFt        = (double?)t["viewport_y_ft"] ?? 1.0,
                        ViewScale          = (double?)t["view_scale"] ?? 100.0,
                    });
                }
                return list;
            }
            catch (Exception ex)
            {
                StingLog.Error("SheetMatrixGenerator.LoadMatrix failed", ex);
                return new List<SheetMatrixRow>();
            }
        }

        public static SheetMatrixResult Generate(Document doc, IList<SheetMatrixRow> rows = null)
        {
            var result = new SheetMatrixResult();
            if (doc == null) return result;
            rows ??= LoadMatrix();
            if (rows.Count == 0) return result;

            try
            {
                TransactionHelper.RunInScope(doc, "STING sheet matrix generate", t =>
                {
                    foreach (var row in rows)
                    {
                        result.RowsProcessed++;
                        var tb = ResolveTitleBlock(doc, row.TitleBlockName);
                        if (tb == null)
                        { result.Errors.Add($"Title block '{row.TitleBlockName}' not loaded"); continue; }

                        int i = 1;
                        foreach (var iterVal in EnumerateIterator(doc, row.Iterator))
                        {
                            try
                            {
                                var sheet = ViewSheet.Create(doc, tb.Id);
                                string sheetNumber = row.SheetNumberPattern.Replace("{i:D2}", i.ToString("D2"));
                                sheet.SheetNumber  = sheetNumber;
                                sheet.Name         = $"{row.Name} — {iterVal}";
                                result.SheetsCreated++;
                                result.CreatedSheetNumbers.Add(sheetNumber);
                                i++;
                            }
                            catch (Exception ex)
                            { result.Errors.Add($"Row {row.Name} / {iterVal}: {ex.Message}"); }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Generate transaction failed: {ex.Message}");
                StingLog.Error("SheetMatrixGenerator.Generate transaction", ex);
            }
            return result;
        }

        private static FamilySymbol ResolveTitleBlock(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> EnumerateIterator(Document doc, string iterator)
        {
            switch ((iterator ?? "LEVEL").ToUpperInvariant())
            {
                case "LEVEL":
                    return new FilteredElementCollector(doc).OfClass(typeof(Level))
                        .Cast<Level>().OrderBy(l => l.Elevation).Select(l => l.Name);
                case "PHASE":
                    return new FilteredElementCollector(doc).OfClass(typeof(Phase))
                        .Cast<Phase>().Select(p => p.Name);
                case "AXIS":
                    return new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Grids)
                        .WhereElementIsNotElementType().Cast<Grid>().OrderBy(g => g.Name).Select(g => g.Name);
                default:
                    return new[] { iterator ?? "ALL" };
            }
        }
    }
}
