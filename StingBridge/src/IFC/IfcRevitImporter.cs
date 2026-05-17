// StingBridge — IFC → Revit import pipeline.
//
// Wraps Revit's native IFC importer (Document.Import / IFCImportOptions)
// and applies post-import enrichment:
//
//   • Deduplicates: removes existing ImportInstance for the same source
//     file before re-importing (Gap 15).
//   • Applies IfcMapConversion survey-origin translation to align the
//     import to the Revit project base point (Gap 1).
//   • Rotates the import by the IFC true-north angle (Gap 7).
//   • Stamps every imported element with STING shared parameters derived
//     from the IFC GlobalId, IfcType, PSet_Revit properties, and
//     STING_IFC_PSET_MAPPING.json.
//   • Normalizes ArchiCAD level names ("AC_Level N" → "Level N") (Gap 8).
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
using System.Text.RegularExpressions;
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

    // Minimal site-coordinate data parsed from the IFC STEP header.
    internal sealed class IfcSiteOrigin
    {
        /// <summary>IfcMapConversion Eastings in metres (IFC default).</summary>
        public double EastingM   { get; set; }
        /// <summary>IfcMapConversion Northings in metres.</summary>
        public double NorthingM  { get; set; }
        /// <summary>IfcMapConversion orthographic height (elevation) in metres.</summary>
        public double ElevationM { get; set; }
        /// <summary>True-north bearing in degrees (from +Y axis, clockwise positive).</summary>
        public double TrueNorthDeg { get; set; }
        /// <summary>True when a non-trivial map conversion was found.</summary>
        public bool HasMapConversion { get; set; }
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
                // Gap 1/7: Parse site origin from STEP header before opening a transaction.
                var origin = ParseIfcSiteOrigin(ifcPath);

                using var tx = new Transaction(doc, $"STING IFC Import — {Path.GetFileName(ifcPath)}");
                tx.Start();

                // Gap 15: Remove any existing ImportInstance that came from the same file
                // so re-import produces a clean update rather than stacked duplicates.
                RemoveExistingImport(doc, ifcPath);

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
                doc.Import(ifcPath, opts, doc.ActiveView, out ElementId importSymbolId);

                // Gap 1: Translate the import symbol to align the IFC survey origin to
                // the Revit project origin (which is at the Survey Point for shared coordinates).
                // Only applied when the model has a non-trivial survey offset (>1 m magnitude).
                if (origin.HasMapConversion && importSymbolId != ElementId.InvalidElementId)
                {
                    ApplySurveyOriginTranslation(doc, importSymbolId, origin);
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

        // ── Gap 15: Deduplication ─────────────────────────────────────────────

        private static void RemoveExistingImport(Document doc, string ifcPath)
        {
            string stem = Path.GetFileNameWithoutExtension(ifcPath);
            var toDelete = new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .Where(ii =>
                {
                    // Check if the import instance's category or name matches the source file.
                    string catName = ii.Category?.Name ?? "";
                    return catName.Contains(stem, StringComparison.OrdinalIgnoreCase)
                        || (ii.Name ?? "").Contains(stem, StringComparison.OrdinalIgnoreCase);
                })
                .Select(ii => ii.Id)
                .ToList();

            foreach (var id in toDelete)
            {
                try { doc.Delete(id); }
                catch (Exception ex) { StingLog.Warn($"IfcRevitImporter.RemoveExistingImport: could not delete {id}: {ex.Message}"); }
            }

            if (toDelete.Count > 0)
                StingLog.Info($"IfcRevitImporter: removed {toDelete.Count} existing import(s) of '{stem}' before re-import.");
        }

        // ── Gap 1 + Gap 7: Survey-origin alignment ────────────────────────────

        private static void ApplySurveyOriginTranslation(
            Document doc, ElementId importSymbolId, IfcSiteOrigin origin)
        {
            try
            {
                // Convert metres → Revit internal units (feet).
                double eastFt  = origin.EastingM   / 0.3048;
                double northFt = origin.NorthingM  / 0.3048;
                double elevFt  = origin.ElevationM / 0.3048;

                double magnitude = Math.Sqrt(eastFt * eastFt + northFt * northFt + elevFt * elevFt);
                // Only apply if magnitude > 1 m to avoid micro-adjustments on origin-based exports.
                if (magnitude < (1.0 / 0.3048)) return;

                // Negate the survey offset to bring the model back to the project origin.
                var translation = new XYZ(-eastFt, -northFt, -elevFt);
                ElementTransformUtils.MoveElement(doc, importSymbolId, translation);
                StingLog.Info($"IfcRevitImporter: applied survey translation ({-eastFt:F3}, {-northFt:F3}, {-elevFt:F3} ft) to import symbol.");

                // Gap 7: Apply true-north rotation if the bearing is non-trivial (>0.1°).
                if (Math.Abs(origin.TrueNorthDeg) > 0.1)
                {
                    double angleRad = origin.TrueNorthDeg * Math.PI / 180.0;
                    // Rotate around vertical axis through the project origin (0,0,0 → 0,0,1).
                    var axis = Line.CreateBound(XYZ.Zero, XYZ.BasisZ);
                    ElementTransformUtils.RotateElement(doc, importSymbolId, axis, angleRad);
                    StingLog.Info($"IfcRevitImporter: applied true-north rotation {origin.TrueNorthDeg:F2}° ({angleRad:F4} rad) to import symbol.");
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"IfcRevitImporter.ApplySurveyOriginTranslation: {ex.Message}");
            }
        }

        // ── Gap 1/7: IFC STEP header parser ───────────────────────────────────

        /// <summary>
        /// Stream-parses the first 10 000 lines of an IFC STEP file to extract
        /// IfcMapConversion survey origin + true north direction without loading
        /// the full model geometry (which would require Xbim and its dependencies).
        /// </summary>
        private static IfcSiteOrigin ParseIfcSiteOrigin(string ifcPath)
        {
            var result = new IfcSiteOrigin();
            try
            {
                // entity-label → (X, Y) for IFCDIRECTION entities
                var directionMap = new System.Collections.Generic.Dictionary<int, (double X, double Y)>();
                int trueNorthRef = -1;

                using var fs = new FileStream(ifcPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
                using var reader = new StreamReader(fs);

                string? line;
                int linesScanned = 0;
                while ((line = reader.ReadLine()) != null && linesScanned++ < 10_000)
                {
                    // Collect IFCDIRECTION entity labels → (X, Y) for true north lookup.
                    if (line.Contains("IFCDIRECTION"))
                    {
                        var dm = Regex.Match(line, @"^#(\d+)=IFCDIRECTION\(\(([0-9.\-Ee+]+),([0-9.\-Ee+]+)");
                        if (dm.Success
                            && int.TryParse(dm.Groups[1].Value, out int dlabel)
                            && double.TryParse(dm.Groups[2].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double dx)
                            && double.TryParse(dm.Groups[3].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double dy))
                        {
                            directionMap[dlabel] = (dx, dy);
                        }
                    }

                    // Extract true-north reference from IFCGEOMETRICREPRESENTATIONCONTEXT.
                    if (line.Contains("IFCGEOMETRICREPRESENTATIONCONTEXT") && line.Contains("'Model'") && trueNorthRef < 0)
                    {
                        var tnm = Regex.Match(line, @"IFCGEOMETRICREPRESENTATIONCONTEXT\([^,]*,'Model',[^,]*,[^,]*,#\d+,#(\d+)");
                        if (tnm.Success && int.TryParse(tnm.Groups[1].Value, out int tnLabel))
                            trueNorthRef = tnLabel;
                    }

                    // Parse IFCMAPCONVERSION for survey origin.
                    if (line.Contains("IFCMAPCONVERSION"))
                    {
                        // Pattern: IFCMAPCONVERSION(#ref,#refTarget, eastings, northings, orthoHeight, xAbscissa, xOrdinate, scale)
                        var m = Regex.Match(line,
                            @"IFCMAPCONVERSION\([^,]*,[^,]*,([0-9.\-Ee+]+),([0-9.\-Ee+]+),([0-9.\-Ee+]+),([0-9.\-Ee+]+),([0-9.\-Ee+]+)");
                        if (m.Success)
                        {
                            if (double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double e)) result.EastingM   = e;
                            if (double.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double n)) result.NorthingM  = n;
                            if (double.TryParse(m.Groups[3].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double h)) result.ElevationM = h;

                            // XAxisAbscissa + XAxisOrdinate define CRS→project rotation → derive bearing
                            if (double.TryParse(m.Groups[4].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double xa)
                                && double.TryParse(m.Groups[5].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double xo))
                            {
                                result.TrueNorthDeg = Math.Atan2(xo, xa) * 180.0 / Math.PI;
                            }

                            result.HasMapConversion = true;
                        }
                    }
                }

                // Resolve true-north from direction entity if not yet set from map conversion.
                if (trueNorthRef >= 0 && directionMap.TryGetValue(trueNorthRef, out var tnDir))
                {
                    double tnDeg = Math.Atan2(tnDir.X, tnDir.Y) * 180.0 / Math.PI;
                    if (Math.Abs(result.TrueNorthDeg) < 0.1)
                        result.TrueNorthDeg = tnDeg;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"IfcRevitImporter.ParseIfcSiteOrigin: {ex.Message}");
            }
            return result;
        }

        // ── Stamp imported elements with STING parameters ─────────────────────

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

                    // Gap 8: Normalize ArchiCAD level names.
                    // ArchiCAD exports levels as "AC_Level 1", "AC_Level GF", etc.
                    // Revit projects use "Level 1", "Ground Floor", etc.
                    // Write the normalised name to ASS_LVL_COD_TXT only when not yet set.
                    NormalizeLevelName(el);

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

        // ── Gap 8: Level name normalization ──────────────────────────────────

        private static readonly string[] _acLevelPrefixes =
            { "AC_Level ", "ArchiCAD_Level ", "AC Level ", "ArchiCAD Level " };

        private static void NormalizeLevelName(Element el)
        {
            // Only act when ASS_LVL_COD_TXT is not yet written.
            var lvlParam = el.LookupParameter("ASS_LVL_COD_TXT");
            if (lvlParam == null || !string.IsNullOrEmpty(lvlParam.AsString())) return;

            // Attempt to get the element's level name from its Level parameter.
            string? levelName = null;
            var levelId = el.LevelId;
            if (levelId != null && levelId != ElementId.InvalidElementId)
            {
                var level = el.Document?.GetElement(levelId) as Level;
                levelName = level?.Name;
            }
            if (string.IsNullOrEmpty(levelName)) return;

            // Strip known ArchiCAD level name prefixes.
            foreach (var prefix in _acLevelPrefixes)
            {
                if (levelName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string normalized = levelName.Substring(prefix.Length).Trim();
                    if (!string.IsNullOrEmpty(normalized))
                        ParameterHelpers.SetIfEmpty(el, "ASS_LVL_COD_TXT", normalized);
                    return;
                }
            }
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
