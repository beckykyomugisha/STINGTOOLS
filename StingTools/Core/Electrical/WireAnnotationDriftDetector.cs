// StingTools — Wire Annotation Drift Detector.
//
// Detects stale wire annotations: when a conduit's ELC_WIRE_* / ELC_CIRCUIT_*
// parameters change after the annotation was placed, the annotation text no
// longer matches what the conduit currently says.
//
// Usage:
//   var report = WireAnnotationDriftDetector.Detect(doc, view);
//   if (report.Drifted > 0)
//   {
//       using var t = new Transaction(doc, "STING Refresh Wire Annotations");
//       t.Start();
//       int n = WireAnnotationDriftDetector.RefreshDrifted(doc, view, report);
//       t.Commit();
//   }

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Commands.Electrical;

namespace StingTools.Core.Electrical
{
    // ────────────────────────────────────────────────────────────────────────────
    //  Data model
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Represents a single conduit whose wire annotation has drifted from the
    /// current conduit parameter values.
    /// </summary>
    public sealed class WireAnnotationDriftItem
    {
        public ElementId ConduitId    { get; set; }
        public string    ConduitName  { get; set; }
        /// <summary>Text currently shown by the placed annotation.</summary>
        public string    CurrentText  { get; set; }
        /// <summary>Text that should be shown given current conduit parameters.</summary>
        public string    ExpectedText { get; set; }

        public bool IsDrift =>
            !string.Equals(CurrentText, ExpectedText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Summary of a drift-detection scan across all annotated conduits in a view.
    /// </summary>
    public sealed class WireAnnotationDriftReport
    {
        public int Checked  { get; set; }
        public int Drifted  { get; set; }
        public List<WireAnnotationDriftItem> Items { get; } = new List<WireAnnotationDriftItem>();
        public string Summary => $"{Drifted}/{Checked} wire annotation(s) stale";
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Detector
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans a view for wire annotations whose text no longer matches the
    /// conduit's current parameter values, and optionally refreshes them.
    /// </summary>
    public static class WireAnnotationDriftDetector
    {
        /// <summary>
        /// Scans <paramref name="view"/> for annotated conduits and returns a
        /// <see cref="WireAnnotationDriftReport"/> describing any stale entries.
        /// Read-only — no transaction required.
        /// </summary>
        public static WireAnnotationDriftReport Detect(Document doc, View view)
        {
            var report = new WireAnnotationDriftReport();
            if (doc == null || view == null) return report;

            try
            {
                var conduits = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_Conduit)
                    .WhereElementIsNotElementType()
                    .ToElements();

                foreach (var conduit in conduits)
                {
                    // Only consider conduits that actually have a wire annotation in this view
                    var annots = AnnotationMarkerRegistry.FindByOwner(
                        doc, view, AnnotationMarkerRegistry.WireAnnotationPrefix, conduit.UniqueId);

                    if (annots.Count == 0) continue;

                    report.Checked++;

                    // Re-build expected text from current conduit parameters
                    string expectedText;
                    try
                    {
                        var wireData    = WireAnnotationEngine.ReadWireData(conduit);
                        expectedText    = WireAnnotationEngine.BuildAnnotationText(wireData, WireAnnotationStyle.Default());
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"WireAnnotationDriftDetector.Detect — BuildAnnotationText for {conduit.Id}: {ex.Message}");
                        continue;
                    }

                    // Read current annotation text (first TextNote wins; fall back to Comments)
                    string currentText = ReadAnnotationText(annots);

                    var item = new WireAnnotationDriftItem
                    {
                        ConduitId    = conduit.Id,
                        ConduitName  = conduit.Name ?? conduit.Id.ToString(),
                        CurrentText  = currentText,
                        ExpectedText = expectedText
                    };

                    if (item.IsDrift)
                    {
                        report.Drifted++;
                        report.Items.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"WireAnnotationDriftDetector.Detect: {ex.Message}");
            }

            return report;
        }

        /// <summary>
        /// Refreshes all drifted annotations reported by
        /// <see cref="Detect"/>.  Deletes the old annotations and re-places
        /// new ones using <c>WireAnnotationEngine</c>.
        /// Must be called inside an open Transaction.
        /// </summary>
        /// <returns>Number of conduits successfully refreshed.</returns>
        public static int RefreshDrifted(Document doc, View view, WireAnnotationDriftReport report)
        {
            if (doc == null || view == null || report == null) return 0;
            int refreshed = 0;

            foreach (var item in report.Items.Where(i => i.IsDrift))
            {
                try
                {
                    var conduit = doc.GetElement(item.ConduitId);
                    if (conduit == null)
                    {
                        StingLog.Warn($"WireAnnotationDriftDetector.RefreshDrifted: conduit {item.ConduitId} not found — skipping");
                        continue;
                    }

                    // Remove old annotations owned by this conduit
                    AnnotationMarkerRegistry.DeleteByOwner(
                        doc, view, AnnotationMarkerRegistry.WireAnnotationPrefix, conduit.UniqueId);
                    AnnotationMarkerRegistry.DeleteByOwner(
                        doc, view, AnnotationMarkerRegistry.TickMarkPrefix, conduit.UniqueId);

                    // Re-read and re-place using the project wire-annotation style.
                    WireAnnotationData wireData = WireAnnotationEngine.ReadWireData(conduit);
                    var style = WireAnnotationStyleOverride.Merge(
                        WireAnnotationStyle.Load(doc), conduit);
                    ElementId newAnnotId = WireAnnotationEngine.PlaceAnnotation(
                        doc, view, conduit, wireData, style);

                    if (newAnnotId != ElementId.InvalidElementId)
                        refreshed++;
                    else
                        StingLog.Warn($"WireAnnotationDriftDetector.RefreshDrifted: PlaceAnnotation returned invalid id for conduit {item.ConduitId}");
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"WireAnnotationDriftDetector.RefreshDrifted: conduit {item.ConduitId}: {ex.Message}");
                }
            }

            return refreshed;
        }

        // ── Private helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Returns the text of the first TextNote in <paramref name="annots"/>.
        /// Falls back to the Comments parameter value of the first element when
        /// no TextNote is present (covers IndependentTag annotations).
        /// </summary>
        private static string ReadAnnotationText(IList<Element> annots)
        {
            foreach (var el in annots)
            {
                if (el is TextNote tn)
                    return tn.Text ?? "";
            }

            // Fallback: read Comments
            foreach (var el in annots)
            {
                try
                {
                    var p = el.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (p != null)
                    {
                        string val = p.AsString() ?? "";
                        // Strip the ownership marker prefix to get the display text
                        int pipe = val.IndexOf('|');
                        if (pipe >= 0) val = val.Substring(pipe + 1);
                        return val;
                    }
                }
                catch { /* continue to next element */ }
            }

            return "";
        }
    }
}
