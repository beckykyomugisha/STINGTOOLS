using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Temp
{
    /// <summary>Shared material property helper for BLE and MEP commands.</summary>
    internal static class MaterialPropertyHelper
    {
        /// <summary>
        /// Apply material appearance properties from CSV columns.
        /// CSV column indices (header row 2):
        ///   35: BLE_APP-IDENTITY-CLASS
        ///   36: BLE_APP-COLOR (e.g., "RGB 221-221-219")
        ///   37: BLE_APP-TRANSPARENCY
        ///   38: BLE_APP-SMOOTHNESS
        ///   39: BLE_APP-SHININESS
        ///   44: BLE_APP-DESCRIPTION
        ///   45: BLE_APP-COMMENTS
        /// </summary>
        public static void ApplyMaterialProperties(Material mat, string[] cols)
        {
            try
            {
                // Color (column 36)
                if (cols.Length > 36)
                {
                    Color color = ParseRgb(cols[36]);
                    if (color != null)
                        mat.Color = color;
                }

                // Transparency (column 37): 0-100
                if (cols.Length > 37 && int.TryParse(cols[37].Trim(), out int transparency))
                {
                    mat.Transparency = Math.Max(0, Math.Min(100, transparency));
                }

                // Smoothness (column 38): 0-100
                if (cols.Length > 38 && int.TryParse(cols[38].Trim().Replace(".0", ""), out int smoothness))
                {
                    mat.Smoothness = Math.Max(0, Math.Min(100, smoothness));
                }

                // Shininess (column 39): 0-128
                if (cols.Length > 39 && int.TryParse(cols[39].Trim().Replace(".0", ""), out int shininess))
                {
                    mat.Shininess = Math.Max(0, Math.Min(128, shininess));
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Material props '{mat.Name}': {ex.Message}");
            }
        }

        /// <summary>Parse "RGB 221-221-219" or "221,221,219" into a Color.</summary>
        public static Color ParseRgb(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var match = Regex.Match(value.Trim(), @"(\d{1,3})\D+(\d{1,3})\D+(\d{1,3})");
            if (match.Success &&
                byte.TryParse(match.Groups[1].Value, out byte r) &&
                byte.TryParse(match.Groups[2].Value, out byte g) &&
                byte.TryParse(match.Groups[3].Value, out byte b))
            {
                return new Color(r, g, b);
            }
            return null;
        }
    }

    /// <summary>
    /// Ported from STINGTemp 2_Materials.panel — Create BLE Materials.
    /// Creates building-element materials from BLE_MATERIALS.csv.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateBLEMaterialsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            string csvPath = StingToolsApp.FindDataFile("BLE_MATERIALS.csv");

            if (csvPath == null)
            {
                TaskDialog.Show("Create BLE Materials",
                    "BLE_MATERIALS.csv not found in the data directory.\n" +
                    $"Searched: {StingToolsApp.DataPath}");
                return Result.Failed;
            }

            // Skip comment line (row 0: "# v2.2 ...") and header row
            var lines = File.ReadAllLines(csvPath)
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                .Skip(1) // skip header
                .ToList();

            int created = 0;
            int skipped = 0;

            using (Transaction tx = new Transaction(doc, "Create BLE Materials"))
            {
                tx.Start();

                // Get existing material names for dedup
                var existingNames = new HashSet<string>(
                    new FilteredElementCollector(doc)
                        .OfClass(typeof(Material))
                        .Cast<Material>()
                        .Select(m => m.Name));

                foreach (string line in lines)
                {
                    // BLE_MATERIALS columns: SOURCE_SHEET(0), MAT_DISCIPLINE(1),
                    // MAT_ISO_19650_ID(2), MAT_CODE(3), MAT_ELEMENT_TYPE(4),
                    // MAT_CATEGORY(5), MAT_NAME(6), ...
                    string[] cols = StingToolsApp.ParseCsvLine(line);
                    if (cols.Length < 7) continue;

                    string matName = cols[6].Trim();
                    if (string.IsNullOrEmpty(matName)) continue;

                    if (existingNames.Contains(matName))
                    {
                        skipped++;
                        continue;
                    }

                    ElementId newId = Material.Create(doc, matName);
                    if (newId != ElementId.InvalidElementId)
                    {
                        Material mat = doc.GetElement(newId) as Material;
                        if (mat != null)
                            MaterialPropertyHelper.ApplyMaterialProperties(mat, cols);
                        created++;
                        existingNames.Add(matName);
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Create BLE Materials",
                $"Created {created} BLE materials.\nSkipped {skipped} (already exist).\n" +
                $"Source: {Path.GetFileName(csvPath)}");

            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Ported from STINGTemp 2_Materials.panel — Create MEP Materials.
    /// Creates MEP materials from MEP_MATERIALS.csv.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateMEPMaterialsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            string csvPath = StingToolsApp.FindDataFile("MEP_MATERIALS.csv");

            if (csvPath == null)
            {
                TaskDialog.Show("Create MEP Materials",
                    "MEP_MATERIALS.csv not found in the data directory.\n" +
                    $"Searched: {StingToolsApp.DataPath}");
                return Result.Failed;
            }

            // Skip comment line (row 0: "# v2.2 ...") and header row
            var lines = File.ReadAllLines(csvPath)
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                .Skip(1) // skip header
                .ToList();

            int created = 0;
            int skipped = 0;

            using (Transaction tx = new Transaction(doc, "Create MEP Materials"))
            {
                tx.Start();

                var existingNames = new HashSet<string>(
                    new FilteredElementCollector(doc)
                        .OfClass(typeof(Material))
                        .Cast<Material>()
                        .Select(m => m.Name));

                foreach (string line in lines)
                {
                    // MEP_MATERIALS columns: same layout as BLE — MAT_NAME is at index 6
                    string[] cols = StingToolsApp.ParseCsvLine(line);
                    if (cols.Length < 7) continue;

                    string matName = cols[6].Trim();
                    if (string.IsNullOrEmpty(matName)) continue;

                    if (existingNames.Contains(matName))
                    {
                        skipped++;
                        continue;
                    }

                    ElementId newId = Material.Create(doc, matName);
                    if (newId != ElementId.InvalidElementId)
                    {
                        Material mat = doc.GetElement(newId) as Material;
                        if (mat != null)
                            MaterialPropertyHelper.ApplyMaterialProperties(mat, cols);
                        created++;
                        existingNames.Add(matName);
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Create MEP Materials",
                $"Created {created} MEP materials.\nSkipped {skipped} (already exist).\n" +
                $"Source: {Path.GetFileName(csvPath)}");

            return Result.Succeeded;
        }
    }
}
