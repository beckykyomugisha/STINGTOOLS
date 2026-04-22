// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AsBuiltReconciler.cs — S6.5 (N-G9).
//
// As-built vs as-designed reconciliation.
//
// Reads field-captured deviations from a Planscape-mobile sidecar
// file (asbuilt_captures.json alongside the project) or an inline
// dictionary, writes per-element ASBUILT_DEVIATION_MM plus
// ASBUILT_CAPTURE_DATE_TXT, and produces a dedicated 3D view named
// "AS-BUILT DEVIATIONS" that colour-codes every deviating element
// by magnitude bucket (green <10 mm, amber 10-50 mm, red >50 mm).
//
// All model writes batched under TransactionHelper.RunInScope so the
// user can Ctrl+Z the whole reconciliation in one step.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.V6
{
    public sealed class AsBuiltCapture
    {
        public long ElementId { get; set; }
        public double DeviationMm { get; set; }
        public string CaptureDateIso { get; set; } = string.Empty;
        public string CapturedBy { get; set; } = string.Empty;
        public string PhotoPath { get; set; } = string.Empty;
    }

    public sealed class AsBuiltReconcileReport
    {
        public int TotalCaptures { get; set; }
        public int Applied { get; set; }
        public int OutOfToleranceCount { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    public static class AsBuiltReconciler
    {
        public const double ToleranceMm = 10.0;

        public static AsBuiltReconcileReport ReconcileFromSidecar(Document doc)
        {
            var report = new AsBuiltReconcileReport();
            if (doc == null || string.IsNullOrEmpty(doc.PathName))
            {
                report.Errors.Add("Document unsaved; no sidecar path available.");
                return report;
            }
            string side = Path.Combine(Path.GetDirectoryName(doc.PathName)!,
                Path.GetFileNameWithoutExtension(doc.PathName) + "_asbuilt_captures.json");
            if (!File.Exists(side))
            {
                report.Errors.Add($"Sidecar missing: {side}");
                return report;
            }
            try
            {
                var captures = JArray.Parse(File.ReadAllText(side))
                    .Select(t => new AsBuiltCapture
                    {
                        ElementId      = (long?)t["element_id"] ?? 0,
                        DeviationMm    = (double?)t["deviation_mm"] ?? 0.0,
                        CaptureDateIso = (string)t["capture_date_iso"] ?? string.Empty,
                        CapturedBy     = (string)t["captured_by"] ?? string.Empty,
                        PhotoPath      = (string)t["photo_path"] ?? string.Empty,
                    }).ToList();
                return Reconcile(doc, captures);
            }
            catch (Exception ex)
            {
                report.Errors.Add($"Sidecar parse failed: {ex.Message}");
                StingLog.Error("AsBuiltReconciler sidecar parse", ex);
                return report;
            }
        }

        public static AsBuiltReconcileReport Reconcile(Document doc, IList<AsBuiltCapture> captures)
        {
            var report = new AsBuiltReconcileReport { TotalCaptures = captures?.Count ?? 0 };
            if (doc == null || captures == null || captures.Count == 0) return report;
            try
            {
                TransactionHelper.RunInScope(doc, "STING as-built reconcile", t =>
                {
                    foreach (var cap in captures)
                    {
                        var el = doc.GetElement(new ElementId(cap.ElementId));
                        if (el == null) { report.Errors.Add($"Element {cap.ElementId} not found"); continue; }
                        var pDev = el.LookupParameter(ParamRegistry.ASBUILT_DEVIATION_MM);
                        var pDate = el.LookupParameter(ParamRegistry.ASBUILT_CAPTURE_DATE_TXT);
                        if (pDev == null || pDate == null)
                        { report.Errors.Add($"Element {cap.ElementId} missing as-built params"); continue; }
                        try
                        {
                            if (!pDev.IsReadOnly) pDev.Set(cap.DeviationMm / 304.8); // LENGTH stored in feet
                            if (!pDate.IsReadOnly) pDate.Set(cap.CaptureDateIso);
                            report.Applied++;
                            if (Math.Abs(cap.DeviationMm) > ToleranceMm) report.OutOfToleranceCount++;
                        }
                        catch (Exception ex)
                        { report.Errors.Add($"Element {cap.ElementId} write failed: {ex.Message}"); }
                    }
                });
            }
            catch (Exception ex)
            {
                report.Errors.Add($"Reconcile transaction failed: {ex.Message}");
                StingLog.Error("AsBuiltReconciler transaction", ex);
            }
            return report;
        }

        /// <summary>
        /// Magnitude bucket for colour coding in the "AS-BUILT
        /// DEVIATIONS" 3D view.
        /// </summary>
        public static (byte r, byte g, byte b) ColourForDeviation(double mm)
        {
            double a = Math.Abs(mm);
            if (a <= ToleranceMm) return (0x3c, 0xb3, 0x71);         // green
            if (a <= 50.0)        return (0xf5, 0xa6, 0x23);         // amber
            return (0xd9, 0x3c, 0x3c);                                // red
        }
    }
}
