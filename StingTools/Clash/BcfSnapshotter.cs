// BcfSnapshotter.cs — render PNG snapshots for BCF viewpoints.
// Runs on the main Revit API thread. Must be called from an ExternalEvent or Idling handler.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Clash
{
    public sealed class BcfSnapshotter : IDisposable
    {
        private readonly Document _doc;
        // D8: Single reusable temp 3D view for the lifetime of the snapshotter.
        //     Prior code created+exported+deleted a fresh View3D per clash —
        //     500 clashes = 500 transient views, three Revit transactions
        //     each. Now: lazy-create one view on first call, retarget its
        //     SectionBox per clash, dispose on Dispose() / Close().
        private View3D _sharedView;
        private bool _disposed;

        public BcfSnapshotter(Document doc) { _doc = doc; }

        public string RenderSnapshot(ClashRecord clash, string outputDir)
        {
            if (_doc == null || clash == null || _disposed) return null;
            try
            {
                Directory.CreateDirectory(outputDir);

                if (_sharedView == null)
                {
                    var viewType = new FilteredElementCollector(_doc).OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>().FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);
                    if (viewType == null) return null;
                    using var tCreate = new Transaction(_doc, "STING clash snapshot view");
                    tCreate.Start();
                    _sharedView = View3D.CreateIsometric(_doc, viewType.Id);
                    _sharedView.Name = $"STING_clash_snap_{DateTime.UtcNow:yyyyMMddHHmmssfff}";
                    tCreate.Commit();
                }

                using (var t = new Transaction(_doc, "STING clash snapshot retarget"))
                {
                    t.Start();
                    var min = new XYZ(clash.AabbMin[0] - 1.6, clash.AabbMin[1] - 1.6, clash.AabbMin[2] - 1.6);
                    var max = new XYZ(clash.AabbMax[0] + 1.6, clash.AabbMax[1] + 1.6, clash.AabbMax[2] + 1.6);
                    _sharedView.SetSectionBox(new BoundingBoxXYZ { Min = min, Max = max });
                    t.Commit();
                }

                string path = Path.Combine(outputDir, $"{clash.Id}.png");
                var opts = new ImageExportOptions
                {
                    FilePath = path,
                    FitDirection = FitDirectionType.Horizontal,
                    ImageResolution = ImageResolution.DPI_150,
                    ZoomType = ZoomFitType.FitToPage,
                    PixelSize = 1024,
                    HLRandWFViewsFileType = ImageFileType.PNG,
                    ExportRange = ExportRange.SetOfViews
                };
                opts.SetViewsAndSheets(new System.Collections.Generic.List<ElementId> { _sharedView.Id });
                _doc.ExportImage(opts);
                return File.Exists(path) ? path : null;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"BcfSnapshotter failed for {clash.Id}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// D8: Dispose deletes the shared temp view in a single final
        /// transaction. Callers should wrap BcfSnapshotter in a using block
        /// around the per-run loop so the view is cleaned up exactly once.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_doc == null || _sharedView == null) return;
            try
            {
                using var t = new Transaction(_doc, "STING snapshot cleanup");
                t.Start();
                try { _doc.Delete(_sharedView.Id); }
                catch (Exception delEx) { StingLog.Warn($"BcfSnapshotter cleanup {_sharedView.Id}: {delEx.Message}"); }
                t.Commit();
            }
            catch (Exception ex) { StingLog.Warn($"BcfSnapshotter dispose: {ex.Message}"); }
            _sharedView = null;
        }
    }
}
