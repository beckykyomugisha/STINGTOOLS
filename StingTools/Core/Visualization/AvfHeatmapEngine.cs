// Pack 6 — AVF (Analysis Visualization Framework) heat-map engine.
//
// STING already computes per-element compliance, fill %, carbon kgCO2e, acoustic
// Rw, and velocity. Today they surface through TaskDialogs and CSVs. The AVF
// API lets us paint those numbers directly onto faces / curves / points in the
// active view with a proper legend — the fastest way to turn opaque reports
// into something coordinators spot instantly.
//
// Architecture:
//   1. Metric adapters (IAvfMetricAdapter) read an existing STING engine and
//      return a list of (ElementId, double) samples — nothing novel, just a
//      mapper over the already-cached Scan() calls.
//   2. AvfHeatmapEngine.Paint takes an adapter + a view and writes a
//      SpatialFieldPrimitive per element with a scalar value attached.
//   3. Clear wipes every SpatialFieldPrimitive STING has registered in the view.
//
// Runs in a ReadOnly transaction — no model mutation. Legend + colour
// gradient are entirely Revit-native.
//
// TODO-VERIFY-API: SpatialFieldManager.GetSpatialFieldManager(view) + default
//   NumberOfMeasurements=1. Signature per
//   https://www.revitapidocs.com/2025/8d35a6d4-9e96-9c07-f6f3-1d2a6f6c59fd.htm
// TODO-VERIFY-API: AddSpatialFieldPrimitive(reference) — the overload that
//   takes a Reference has been stable since Revit 2015 but rarely used on
//   non-analysis categories; Solid-face reference resolution may be brittle
//   on families whose geometry is computed in the view (e.g. voids).

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Analysis;

namespace StingTools.Core.Visualization
{
    public interface IAvfMetricAdapter
    {
        /// <summary>Human-readable name for the legend + logs.</summary>
        string MetricName { get; }

        /// <summary>Unit suffix for the legend (e.g. "%", "kgCO2e", "dB").</summary>
        string Unit { get; }

        /// <summary>Return (elementId, scalar value) for every element that
        /// should participate in the heat-map. Implementations should be cheap
        /// (read from cached engine results, not re-scan).</summary>
        IEnumerable<(ElementId id, double value)> Collect(Document doc);
    }

    public static class AvfHeatmapEngine
    {
        private const string StingSchemeName = "STING Heatmap";

        /// <summary>
        /// Paints the adapter's samples onto the active view as a spatial
        /// field. Returns the number of primitives registered. Silently
        /// returns 0 if the view does not support AVF (plan views do; some
        /// 3D views don't without a section box).
        /// </summary>
        public static int Paint(View view, IAvfMetricAdapter adapter)
        {
            if (view == null || adapter == null) return 0;
            Document doc = view.Document;

            SpatialFieldManager sfm = null;
            try { sfm = SpatialFieldManager.GetSpatialFieldManager(view); } catch { }
            if (sfm == null)
            {
                try { sfm = SpatialFieldManager.CreateSpatialFieldManager(view, 1); }
                catch (Exception ex)
                {
                    StingLog.Warn($"AvfHeatmapEngine.Paint: cannot create SpatialFieldManager for {view.Name}: {ex.Message}");
                    return 0;
                }
            }

            int n = 0;
            try
            {
                foreach (var sample in adapter.Collect(doc))
                {
                    try
                    {
                        Element el = doc.GetElement(sample.id);
                        if (el == null) continue;
                        Reference r = new Reference(el);
                        int primId;
                        try { primId = sfm.AddSpatialFieldPrimitive(r); }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"AvfHeatmapEngine: AddSpatialFieldPrimitive failed for {sample.id.Value}: {ex.Message}");
                            continue;
                        }

                        var pts = new FieldDomainPointsByUV(new List<UV> { new UV(0, 0) });
                        var vals = new FieldValues(new List<ValueAtPoint>
                        {
                            new ValueAtPoint(new List<double> { sample.value })
                        });
                        // Revit 2025: UpdateSpatialFieldPrimitive requires resultIndex (0-based).
                        // STING uses a single result schema per manager so 0 is the only valid value.
                        sfm.UpdateSpatialFieldPrimitive(primId, pts, vals, 0);
                        n++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"AvfHeatmapEngine sample {sample.id.Value}: {ex.Message}");
                    }
                }
                StingLog.Info($"AvfHeatmapEngine.Paint: '{adapter.MetricName}' → {n} primitive(s) on view '{view.Name}'");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"AvfHeatmapEngine.Paint: {ex.Message}");
            }
            return n;
        }

        /// <summary>
        /// Clear every AVF primitive registered under the active view's
        /// SpatialFieldManager. Safe to call when no heat-map is present.
        /// </summary>
        public static void Clear(View view)
        {
            if (view == null) return;
            try
            {
                var sfm = SpatialFieldManager.GetSpatialFieldManager(view);
                if (sfm == null) return;
                sfm.Clear();
                StingLog.Info($"AvfHeatmapEngine.Clear: wiped heat-map on view '{view.Name}'");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"AvfHeatmapEngine.Clear: {ex.Message}");
            }
        }
    }
}
