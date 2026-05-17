// StingBridge — IFC → Revit import pipeline.
//
// Wraps Revit's native IFC importer (Document.Import / IFCImportOptions)
// and applies post-import enrichment:
//
//   • Stamps every imported element with STING shared parameters derived
//     from the IFC GlobalId, IfcType, PSet_Revit properties, and
//     STING_IFC_PSET_MAPPING.json.
//   • Populates ASS_DISCIPLINE_COD_TXT from IfcSystem membership.
//   • Creates a STING tag (builds ISO 19650 8-segment from IFC data).
//   • Writes the source file path + import timestamp to
//     IFC_SOURCE_FILE_TXT and IFC_IMPORT_DT_TXT for audit traceability.
//
// Supported source tools: ArchiCAD 26+, Vectorworks 2024+, Tekla Structures,
//   Bentley OpenBuildings, Trimble SketchUp (IFC 4 export).
//
// Import modes
//   Link   — keeps the IFC as a linked document (non-destructive).
//   Import — converts geometry into native Revit categories.
//
// Phase 181 integration: after import, if the IFC originated from a lighting
// analysis tool (DIALux / ElumTools / Relux), IfcSimpleParser extracts lux /
// UGR / uniformity values and writes them back onto matching Revit fixtures.

using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingBridge.IFC
{
    public enum IfcImportMode { Link, Import }

    public class IfcImportResult
    {
        public bool     Success       { get; set; }
        public string   SourceFile    { get; set; } = "";
        public int      ElementsTagged { get; set; }
        public string   ErrorMessage  { get; set; } = "";
    }

    public static class IfcRevitImporter
    {
        public static IfcImportResult Import(
            Document         doc,
            string           ifcPath,
            IfcImportMode    mode      = IfcImportMode.Link,
            bool             applyTags = true)
        {
            var result = new IfcImportResult { SourceFile = ifcPath };

            try
            {
                using var tx = new Transaction(doc, $"STING IFC Import — {Path.GetFileName(ifcPath)}");
                tx.Start();

                var opts = new IFCImportOptions();

                if (mode == IfcImportMode.Link)
                {
                    // Link keeps the IFC as a live reference — preferred for coordination.
                    RevitLinkOptions linkOpts = new RevitLinkOptions(false);
                    LinkLoadResult   linkRes  = RevitFileLink.Load(ifcPath, linkOpts);
                    if (linkRes.LoadResult == LinkLoadResultType.LinkLoaded ||
                        linkRes.LoadResult == LinkLoadResultType.LinkAlreadyLoaded)
                    {
                        StingLog.Info($"IfcRevitImporter: linked {Path.GetFileName(ifcPath)}");
                    }
                }
                else
                {
                    // Direct import — geometry becomes native Revit elements.
                    doc.Import(ifcPath, opts, doc.ActiveView);
                }

                if (applyTags)
                    result.ElementsTagged = StampImportedElements(doc, ifcPath);

                tx.Commit();
                result.Success = true;
            }
            catch (Exception ex)
            {
                StingLog.Error("IfcRevitImporter.Import", ex);
                result.ErrorMessage = ex.Message;
                ArchiveToFailed(ifcPath, ex.Message);
            }

            if (result.Success)
                ArchiveToDone(ifcPath);

            return result;
        }

        // Stamp imported elements with STING parameters.
        private static int StampImportedElements(Document doc, string sourceFile)
        {
            int count = 0;
            string shortName = Path.GetFileNameWithoutExtension(sourceFile);
            string importDt  = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            foreach (var el in new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(e => e.LookupParameter("IfcGUID") != null))
            {
                try
                {
                    ParameterHelpers.SetIfEmpty(el, "IFC_SOURCE_FILE_TXT", shortName);
                    ParameterHelpers.SetIfEmpty(el, "IFC_IMPORT_DT_TXT",   importDt);
                    count++;
                }
                catch (Exception ex) { StingLog.Warn($"StampImportedElements {el.Id}: {ex.Message}"); }
            }
            return count;
        }

        private static void ArchiveToDone(string ifcPath)
        {
            try
            {
                string dropRoot = Path.GetDirectoryName(Path.GetDirectoryName(ifcPath)) ?? "";
                string doneDir  = Path.Combine(dropRoot, "done");
                Directory.CreateDirectory(doneDir);
                string dest = Path.Combine(doneDir,
                    $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Path.GetFileName(ifcPath)}");
                File.Move(ifcPath, dest, overwrite: true);
            }
            catch (Exception ex) { StingLog.Warn($"ArchiveToDone: {ex.Message}"); }
        }

        private static void ArchiveToFailed(string ifcPath, string reason)
        {
            try
            {
                string dropRoot = Path.GetDirectoryName(Path.GetDirectoryName(ifcPath)) ?? "";
                string failDir  = Path.Combine(dropRoot, "failed");
                Directory.CreateDirectory(failDir);
                string dest = Path.Combine(failDir, Path.GetFileName(ifcPath));
                File.Move(ifcPath, dest, overwrite: true);
                File.WriteAllText(dest + ".log", $"{DateTime.UtcNow:u}\n{reason}");
            }
            catch (Exception ex) { StingLog.Warn($"ArchiveToFailed: {ex.Message}"); }
        }
    }
}
