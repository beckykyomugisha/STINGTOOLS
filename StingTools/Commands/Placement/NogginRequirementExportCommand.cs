// Phase 139.2 O — Noggin requirement export.
//
// Collects placed Lighting/Electrical fixtures whose STING_NOGGIN_REQUIRED
// flag is set, writes a CSV (Room, Level, X_mm, Y_mm, Z_mm, BoxType,
// CatalogueRef, FixingDate) and optionally drops a STING_NogginMarker
// generic-model element at each point.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Placement;

namespace StingTools.Commands.Placement
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class NogginRequirementExportCommand : IExternalCommand
    {
        private const double FtToMm = 304.8;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData?.Application?.ActiveUIDocument?.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }

            var rows = new List<string[]>();
            try
            {
                var col = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance))
                    .WhereElementIsNotElementType();
                foreach (var el in col)
                {
                    if (!(el is FamilyInstance fi)) continue;
                    if (fi.Category == null) continue;
                    var bic = (BuiltInCategory)fi.Category.Id.Value;
                    if (bic != BuiltInCategory.OST_LightingFixtures
                     && bic != BuiltInCategory.OST_ElectricalFixtures
                     && bic != BuiltInCategory.OST_LightingDevices) continue;

                    var pNoggin = fi.LookupParameter(ParamRegistry.NOGGIN_REQUIRED);
                    if (pNoggin == null || !pNoggin.HasValue) continue;
                    int v = pNoggin.StorageType == StorageType.Integer ? pNoggin.AsInteger()
                          : pNoggin.StorageType == StorageType.Double  ? (int)pNoggin.AsDouble()
                          : 0;
                    if (v != 1) continue;

                    XYZ pt = (fi.Location as LocationPoint)?.Point ?? XYZ.Zero;
                    string room = "";
                    try { room = fi.Room?.Name ?? ""; } catch { }
                    string level = "";
                    try { level = (doc.GetElement(fi.LevelId) as Level)?.Name ?? ""; } catch { }
                    string typeName = doc.GetElement(fi.GetTypeId())?.Name ?? "";
                    string catalogueRef = fi.LookupParameter("MK_CATALOGUE_REF")?.AsString() ?? "";
                    rows.Add(new[] {
                        room, level,
                        (pt.X * FtToMm).ToString("F1"),
                        (pt.Y * FtToMm).ToString("F1"),
                        (pt.Z * FtToMm).ToString("F1"),
                        typeName, catalogueRef,
                        DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                    });
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"NogginRequirementExport collect: {ex.Message}");
                message = ex.Message;
                return Result.Failed;
            }

            string outDir = OutputLocationHelper.GetOutputPath(doc, "NogginRequirements") ?? Path.GetTempPath();
            Directory.CreateDirectory(outDir);
            string csvPath = Path.Combine(outDir,
                $"STING_NogginRequirements_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Room,Level,X_mm,Y_mm,Z_mm,BoxType,CatalogueRef,FixingDate");
                foreach (var row in rows) sb.AppendLine(string.Join(",", row.Select(Quote)));
                File.WriteAllText(csvPath, sb.ToString());
            }
            catch (Exception ex)
            {
                message = $"CSV write failed: {ex.Message}";
                return Result.Failed;
            }

            // Optionally drop marker generic-model instances if the family is loaded.
            int markersPlaced = 0;
            FamilySymbol marker = ResolveMarker(doc);
            if (marker != null && rows.Count > 0)
            {
                using (var tx = new Transaction(doc, "STING Place Noggin Markers"))
                {
                    try
                    {
                        tx.Start();
                        if (!marker.IsActive) { marker.Activate(); doc.Regenerate(); }
                        foreach (var row in rows)
                        {
                            if (!double.TryParse(row[2], out double x)) continue;
                            if (!double.TryParse(row[3], out double y)) continue;
                            if (!double.TryParse(row[4], out double z)) continue;
                            try
                            {
                                doc.Create.NewFamilyInstance(
                                    new XYZ(x / FtToMm, y / FtToMm, z / FtToMm),
                                    marker, StructuralType.NonStructural);
                                markersPlaced++;
                            }
                            catch { }
                        }
                        tx.Commit();
                    }
                    catch
                    {
                        if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    }
                }
            }

            TaskDialog.Show("STING - Noggin Export",
                $"{rows.Count} noggin requirement(s) exported.\n\n{csvPath}\n\nMarkers placed: {markersPlaced}");
            return Result.Succeeded;
        }

        private static FamilySymbol ResolveMarker(Document doc)
        {
            try
            {
                var col = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol));
                foreach (var el in col)
                {
                    if (el is FamilySymbol fs &&
                        string.Equals(fs.Family?.Name, "STING_NogginMarker", StringComparison.OrdinalIgnoreCase))
                        return fs;
                }
            }
            catch { }
            return null;
        }

        private static string Quote(string s)
        {
            if (s == null) return "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
