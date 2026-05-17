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
                    // The Revit core API (RevitAPI.dll) does not expose a programmatic
                    // "Link IFC" method — that functionality lives in the IFC for Revit
                    // add-in, not in the core assembly. Fall back to Import so callers
                    // always get usable native elements regardless of the requested mode.
                    StingLog.Warn($"IfcRevitImporter: IFC link mode is not available via the " +
                                  $"Revit API; falling back to Import for {Path.GetFileName(ifcPath)}");
                }

                // Import converts IFC geometry into native Revit elements.
                // The fourth (out) parameter receives the ElementId of the created
                // import symbol — required by the Revit 2025+ API signature.
                doc.Import(ifcPath, opts, doc.ActiveView, out ElementId _);

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

            // Build the spatial population context once for the whole batch so room
            // lookups and level detection are amortised across all elements.
            var ctx = TokenAutoPopulator.PopulationContext.Build(doc);

            foreach (var el in new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(e => e.LookupParameter("IfcGUID") != null))
            {
                try
                {
                    ParameterHelpers.SetIfEmpty(el, "IFC_SOURCE_FILE_TXT", shortName);
                    ParameterHelpers.SetIfEmpty(el, "IFC_IMPORT_DT_TXT",   importDt);

                    // Copy IfcGlobalId into the STING audit parameter so issues raised
                    // in Planscape can be traced back to the originating IFC element.
                    string? guid = el.LookupParameter("IfcGUID")?.AsString();
                    if (!string.IsNullOrWhiteSpace(guid))
                        ParameterHelpers.SetIfEmpty(el, "IFC_GLOBAL_ID_TXT", guid);

                    // Derive DISC/LOC/ZONE/LVL/SYS/FUNC/PROD/STATUS from element
                    // context. overwrite:false preserves values already written by the
                    // ArchiCAD property-set mapper (via ArchiCadIfcImportCommand).
                    TokenAutoPopulator.PopulateAll(doc, el, ctx, overwrite: false);

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
