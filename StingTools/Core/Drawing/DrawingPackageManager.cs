// StingTools — Drawing Template Manager · Phase 137
//
// DrawingPackageManager groups STING-produced views and sheets by
// the STING_DRAWING_PACKAGE_ID_TXT parameter so users can sequence,
// audit, and bulk-export an entire issue package.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    public static class DrawingPackageManager
    {
        public sealed class PackageSummary
        {
            public string PackageId { get; set; }
            public int SheetCount { get; set; }
            public int ViewCount { get; set; }
            public List<ElementId> SheetIds { get; } = new List<ElementId>();
            public List<ElementId> ViewIds { get; } = new List<ElementId>();
        }

        public sealed class ExportResult
        {
            public string OutputPath { get; set; }
            public int SheetCount { get; set; }
            public List<string> Warnings { get; } = new List<string>();
        }

        public static List<PackageSummary> GetPackages(Document doc)
        {
            var byId = new Dictionary<string, PackageSummary>(StringComparer.Ordinal);
            if (doc == null) return new List<PackageSummary>();

            try
            {
                foreach (var s in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
                {
                    var pid = StingTools.Core.ParameterHelpers.GetString(s, DrawingTypeStamper.PARAM_DRAWING_PACKAGE_ID);
                    if (string.IsNullOrEmpty(pid)) continue;
                    if (!byId.TryGetValue(pid, out var p))
                    {
                        p = new PackageSummary { PackageId = pid };
                        byId[pid] = p;
                    }
                    p.SheetIds.Add(s.Id);
                    p.SheetCount++;
                }
                foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
                {
                    if (v.IsTemplate) continue;
                    var pid = StingTools.Core.ParameterHelpers.GetString(v, DrawingTypeStamper.PARAM_DRAWING_PACKAGE_ID);
                    if (string.IsNullOrEmpty(pid)) continue;
                    if (!byId.TryGetValue(pid, out var p))
                    {
                        p = new PackageSummary { PackageId = pid };
                        byId[pid] = p;
                    }
                    p.ViewIds.Add(v.Id);
                    p.ViewCount++;
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"DrawingPackageManager.GetPackages: {ex.Message}");
            }
            return byId.Values.OrderBy(p => p.PackageId, StringComparer.Ordinal).ToList();
        }

        public static void SetSequence(Document doc, string packageId, List<ElementId> sheetIds)
        {
            if (doc == null || string.IsNullOrEmpty(packageId) || sheetIds == null) return;
            using (var t = new Transaction(doc, "STING Sequence Package"))
            {
                t.Start();
                for (int i = 0; i < sheetIds.Count; i++)
                {
                    var s = doc.GetElement(sheetIds[i]) as ViewSheet;
                    if (s == null) continue;
                    DrawingTypeStamper.StampSheetSequence(s, i + 1);
                }
                t.Commit();
            }
        }

        public static ExportResult ExportPackage(Document doc, string packageId, string outputDir)
        {
            var result = new ExportResult { OutputPath = outputDir };
            if (doc == null || string.IsNullOrEmpty(packageId) || string.IsNullOrEmpty(outputDir))
            {
                result.Warnings.Add("ExportPackage: missing arguments.");
                return result;
            }
            try
            {
                if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
            }
            catch (Exception ex) { result.Warnings.Add($"CreateOutputDir: {ex.Message}"); return result; }

            var packages = GetPackages(doc);
            var pkg = packages.FirstOrDefault(p => string.Equals(p.PackageId, packageId, StringComparison.Ordinal));
            if (pkg == null) { result.Warnings.Add($"Package '{packageId}' not found."); return result; }

            var ordered = pkg.SheetIds
                .Select(id => doc.GetElement(id) as ViewSheet)
                .Where(s => s != null)
                .OrderBy(s => StingTools.Core.ParameterHelpers.GetInt(s, DrawingTypeStamper.PARAM_SHEET_SEQUENCE, 0))
                .ThenBy(s => s.SheetNumber)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                var s = ordered[i];
                int seq = StingTools.Core.ParameterHelpers.GetInt(s, DrawingTypeStamper.PARAM_SHEET_SEQUENCE, i + 1);
                string filename = SanitizeFilename($"{seq:D3}_{s.SheetNumber}_{s.Name}");
                try
                {
                    var opts = new PDFExportOptions { FileName = filename };
                    doc.Export(outputDir, new List<ElementId> { s.Id }, opts);
                    result.SheetCount++;
                }
                catch (Exception ex) { result.Warnings.Add($"Export '{s.SheetNumber}': {ex.Message}"); }
            }
            return result;
        }

        private static string SanitizeFilename(string name)
        {
            if (string.IsNullOrEmpty(name)) return "sheet";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }
    }
}
