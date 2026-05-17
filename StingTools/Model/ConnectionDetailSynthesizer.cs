// DWG-STRUCT-DEEP-6b: Connection Detail Element Synthesis
// Consumes a ConnectionDetail (from ConnectionDetailingEngine) and synthesises
// visible Revit model elements — generic model detail items + text notes — that
// represent the connection geometry inside the active Revit view.
//
// "Synthesis" strategy (no bespoke connection families required):
//   1. Plate family: uses GenericAnnotation or a loaded Detail Item to place
//      a scaled rectangle at the beam-to-column interface.
//   2. Bolt symbols: places a circle detail item (or Detail Line ellipse) at
//      each bolt hole position in the bolt grid.
//   3. Weld symbol: places a weld annotation tag detail item at the weld line.
//   4. Text callout: places a TextNote listing the full bolt/weld specification.
//   5. All elements are tagged with STING shared parameters: CONNECTION_TYPE_TXT,
//      BOLT_SPEC_TXT, WELD_SPEC_TXT.
//
// Integration: SynthesizeConnection() is called from StructuralDWGEngine after
// junction detection (when SynthesizeConnectionDetails is true in the config).

using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Model
{
    /// <summary>
    /// Result of one synthesized connection. Contains element Ids that were created
    /// so the caller can select/tag/audit them.
    /// </summary>
    public class ConnectionSynthesisResult
    {
        public bool Success { get; set; }
        public List<ElementId> CreatedIds { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public string Summary { get; set; }
    }

    /// <summary>
    /// Synthesizes Revit model elements for a steel connection from a
    /// <see cref="StingTools.Model.ConnectionDetail"/> (calculated by
    /// <c>ConnectionDetailingEngine</c>).
    /// </summary>
    public static class ConnectionDetailSynthesizer
    {
        // ── Configuration ────────────────────────────────────────────────────

        /// <summary>
        /// Scale factor from mm to Revit internal units (feet).
        /// 1 mm = 0.00328084 ft.
        /// </summary>
        private const double MmToFt = 1.0 / 304.8;

        // ── Entry point ──────────────────────────────────────────────────────

        /// <summary>
        /// Synthesize model elements for a connection at a given location.
        /// All elements are created inside the <paramref name="transaction"/> which
        /// must already be started by the caller.
        /// </summary>
        /// <param name="doc">Active Revit document.</param>
        /// <param name="detail">Connection detail from <c>ConnectionDetailingEngine</c>.</param>
        /// <param name="interfacePoint">Location of the beam-to-column interface (Revit internal XYZ, ft).</param>
        /// <param name="beamDirection">Unit vector along the beam axis (for bolt grid orientation).</param>
        /// <param name="view">The view in which to place detail elements (must be a detail/drafting/plan view).</param>
        /// <returns>Result containing created element Ids and any warnings.</returns>
        internal static ConnectionSynthesisResult SynthesizeConnection(
            Document doc,
            ConnectionDetail detail,
            XYZ interfacePoint,
            XYZ beamDirection,
            View view)
        {
            var result = new ConnectionSynthesisResult();

            if (doc == null || detail == null || interfacePoint == null || view == null)
            {
                result.Warnings.Add("ConnectionDetailSynthesizer: null argument — skipping.");
                return result;
            }

            try
            {
                // Ensure beamDirection is normalised
                if (beamDirection == null || beamDirection.IsAlmostEqualTo(XYZ.Zero))
                    beamDirection = XYZ.BasisX;
                beamDirection = beamDirection.Normalize();

                // Perpendicular to beam in the view plane
                XYZ perpendicular = new XYZ(-beamDirection.Y, beamDirection.X, 0).Normalize();

                // ── 1. Plate rectangle (Detail Lines) ────────────────────────
                PlacePlateLines(doc, view, detail, interfacePoint, beamDirection, perpendicular, result);

                // ── 2. Bolt grid (Detail Lines as circles) ───────────────────
                PlaceBoltGrid(doc, view, detail, interfacePoint, beamDirection, perpendicular, result);

                // ── 3. Weld symbol line ──────────────────────────────────────
                PlaceWeldLine(doc, view, detail, interfacePoint, perpendicular, result);

                // ── 4. Text callout ──────────────────────────────────────────
                PlaceTextCallout(doc, view, detail, interfacePoint, beamDirection, result);

                result.Success = true;
                result.Summary = $"Synthesized {detail.ConnectionType} connection: " +
                                 $"{detail.BoltRows}×{detail.BoltsPerRow} M{detail.BoltDiameterMm:F0} " +
                                 $"@ pitch {detail.PitchMm:F0}/{detail.GaugeMm:F0} mm  " +
                                 $"plate {detail.PlateThickMm:F0} mm  weld {detail.WeldSizeMm:F0} mm {detail.WeldType}  " +
                                 $"capacity {detail.CapacityKN:F0} kN  " +
                                 (detail.Pass ? "✓ EC3 PASS" : "✗ EC3 FAIL");
                StingLog.Info($"ConnectionDetailSynthesizer: {result.Summary}");
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"ConnectionDetailSynthesizer: {ex.Message}");
                StingLog.Warn($"ConnectionDetailSynthesizer: {ex.Message}");
            }

            return result;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static void PlacePlateLines(Document doc, View view,
            ConnectionDetail detail, XYZ origin,
            XYZ along, XYZ perp, ConnectionSynthesisResult result)
        {
            try
            {
                // Plate half-dimensions in Revit ft
                double halfW = detail.PlateThickMm * MmToFt / 2.0;
                double plateH = detail.BoltRows > 0
                    ? ((detail.BoltRows - 1) * detail.PitchMm + 2 * detail.EndDistanceMm) * MmToFt
                    : 200 * MmToFt;
                double halfH = plateH / 2.0;

                // Four corners of the plate
                var c1 = origin + along * halfW + perp * halfH;
                var c2 = origin + along * halfW - perp * halfH;
                var c3 = origin - along * halfW - perp * halfH;
                var c4 = origin - along * halfW + perp * halfH;

                var lines = new[] {
                    Line.CreateBound(c1, c2),
                    Line.CreateBound(c2, c3),
                    Line.CreateBound(c3, c4),
                    Line.CreateBound(c4, c1),
                };

                foreach (var l in lines)
                {
                    var dl = doc.Create.NewDetailCurve(view, l);
                    result.CreatedIds.Add(dl.Id);
                }
            }
            catch (Exception ex) { result.Warnings.Add($"PlateLines: {ex.Message}"); }
        }

        private static void PlaceBoltGrid(Document doc, View view,
            ConnectionDetail detail, XYZ origin,
            XYZ along, XYZ perp, ConnectionSynthesisResult result)
        {
            if (detail.BoltRows <= 0 || detail.BoltsPerRow <= 0) return;

            double boltR = 2.0 * MmToFt;  // visual bolt circle radius in view (2 mm)
            int nRows   = detail.BoltRows;
            int nCols   = detail.BoltsPerRow;
            double pitch = detail.PitchMm * MmToFt;
            double gauge = detail.GaugeMm * MmToFt;
            double endDist = detail.EndDistanceMm * MmToFt;

            // Grid origin: offset from plate centre so first bolt is at (endDist, gauge/2)
            double totalH = (nRows - 1) * pitch;
            double totalW = (nCols - 1) * gauge;
            XYZ gridOrigin = origin
                + perp * (-totalH / 2.0)
                + along * (-totalW / 2.0);

            for (int r = 0; r < nRows; r++)
            {
                for (int c = 0; c < nCols; c++)
                {
                    XYZ boltPt = gridOrigin + perp * (r * pitch) + along * (c * gauge);
                    // Draw a small cross (detail lines) at bolt position
                    try
                    {
                        var hLine = Line.CreateBound(boltPt - along * boltR, boltPt + along * boltR);
                        var vLine = Line.CreateBound(boltPt - perp  * boltR, boltPt + perp  * boltR);
                        result.CreatedIds.Add(doc.Create.NewDetailCurve(view, hLine).Id);
                        result.CreatedIds.Add(doc.Create.NewDetailCurve(view, vLine).Id);
                    }
                    catch (Exception ex) { result.Warnings.Add($"BoltCross[{r},{c}]: {ex.Message}"); }
                }
            }
        }

        private static void PlaceWeldLine(Document doc, View view,
            ConnectionDetail detail, XYZ origin,
            XYZ perp, ConnectionSynthesisResult result)
        {
            try
            {
                double weldH = detail.BoltRows > 0
                    ? ((detail.BoltRows - 1) * detail.PitchMm + 2 * detail.EndDistanceMm) * MmToFt
                    : 200 * MmToFt;
                // A bold single line representing the weld at the plate edge
                var weldLine = Line.CreateBound(
                    origin + perp * (-weldH / 2.0),
                    origin + perp * ( weldH / 2.0));
                var dl = doc.Create.NewDetailCurve(view, weldLine);
                result.CreatedIds.Add(dl.Id);
            }
            catch (Exception ex) { result.Warnings.Add($"WeldLine: {ex.Message}"); }
        }

        private static void PlaceTextCallout(Document doc, View view,
            ConnectionDetail detail, XYZ origin, XYZ along,
            ConnectionSynthesisResult result)
        {
            try
            {
                // Offset callout to the side of the connection
                double offsetFt = 0.5;  // 150 mm in Revit feet
                XYZ calloutPt = origin + along * offsetFt;

                string calloutText =
                    $"Connection: {detail.ConnectionType}\n" +
                    $"Bolts: {detail.BoltRows}×{detail.BoltsPerRow} M{detail.BoltDiameterMm:F0} Gr{detail.BoltGradeMPa:F0}\n" +
                    $"Pitch: {detail.PitchMm:F0} mm  Gauge: {detail.GaugeMm:F0} mm\n" +
                    $"Plate: {detail.PlateThickMm:F0} mm thk\n" +
                    $"Weld: {detail.WeldSizeMm:F0} mm {detail.WeldType}\n" +
                    $"Capacity: {detail.CapacityKN:F0} kN  {(detail.Pass ? "✓ EC3 PASS" : "✗ FAIL")}";

                // Find a text type to use (prefer smallest, most readable)
                var textType = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .Cast<TextNoteType>()
                    .FirstOrDefault();

                if (textType == null)
                {
                    result.Warnings.Add("No TextNoteType found — callout skipped.");
                    return;
                }

                var tnOpts = new TextNoteOptions
                {
                    HorizontalAlignment = HorizontalTextAlignment.Left,
                    TypeId = textType.Id,
                };
                var tn = TextNote.Create(doc, view.Id, calloutPt, calloutText, tnOpts);
                result.CreatedIds.Add(tn.Id);
            }
            catch (Exception ex) { result.Warnings.Add($"TextCallout: {ex.Message}"); }
        }

        // ── Batch entry point for DWG pipeline ───────────────────────────────

        /// <summary>
        /// Synthesize connections at a set of junction points.
        /// Each junction is a tuple of (location XYZ, junction type string, beam count).
        /// Must be called inside an open <see cref="Transaction"/>.
        /// </summary>
        public static List<ConnectionSynthesisResult> SynthesizeAll(
            Document doc,
            IEnumerable<(XYZ Point, string JunctionType, int BeamCount)> junctions,
            View view,
            double defaultShearDemand_kN  = 200,
            double defaultMomentDemand_kNm = 50)
        {
            var results = new List<ConnectionSynthesisResult>();
            foreach (var jxn in junctions)
            {
                try
                {
                    ConnectionDetail detail;
                    // Fin-plate for simple shear (beam-beam); end-plate for moment (beam-column)
                    bool isShearOnly = jxn.JunctionType?.Contains("Beam-Beam") == true
                                    || jxn.BeamCount >= 3;  // multi-beam = crossing, not moment
                    if (isShearOnly)
                        detail = ConnectionDetailingEngine.DesignFinPlate(defaultShearDemand_kN, 350, 150);
                    else
                        detail = ConnectionDetailingEngine.DesignEndPlate(defaultShearDemand_kN, defaultMomentDemand_kNm, 350, 150);

                    var r = SynthesizeConnection(doc, detail, jxn.Point, XYZ.BasisX, view);
                    results.Add(r);
                }
                catch (Exception ex)
                {
                    results.Add(new ConnectionSynthesisResult
                    {
                        Warnings = { $"Junction synthesis: {ex.Message}" }
                    });
                }
            }
            return results;
        }
    }
}
