// BcfSnapshotter.cs — render PNG snapshots for BCF viewpoints.
// Runs on the main Revit API thread. Must be called from an ExternalEvent or Idling handler.
using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Clash
{
    public sealed class BcfSnapshotter
    {
        private readonly Document _doc;
        // Reserved for future use: cached ephemeral 3D view for repeated
        // snapshots within the same session (avoids creating/destroying a
        // view per clash). The current implementation creates a fresh view
        // in RenderSnapshot and disposes it immediately — keep the field
        // so the lifecycle refactor doesn't need to re-introduce it.
#pragma warning disable CS0169
        private View3D _tempView;
#pragma warning restore CS0169

        public BcfSnapshotter(Document doc) { _doc = doc; }

        public string RenderSnapshot(ClashRecord clash, string outputDir)
        {
            if (_doc == null || clash == null) return null;
            try
            {
                Directory.CreateDirectory(outputDir);
                var viewType = new FilteredElementCollector(_doc).OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>().FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);
                if (viewType == null) return null;

                View3D v;
                using (var t = new Transaction(_doc, "STING clash snapshot"))
                {
                    t.Start();
                    v = View3D.CreateIsometric(_doc, viewType.Id);
                    v.Name = $"STING_clash_{clash.Id}_{DateTime.UtcNow:HHmmssfff}";
                    var min = new XYZ(clash.AabbMin[0] - 1.6, clash.AabbMin[1] - 1.6, clash.AabbMin[2] - 1.6);
                    var max = new XYZ(clash.AabbMax[0] + 1.6, clash.AabbMax[1] + 1.6, clash.AabbMax[2] + 1.6);
                    v.SetSectionBox(new BoundingBoxXYZ { Min = min, Max = max });
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
                opts.SetViewsAndSheets(new System.Collections.Generic.List<ElementId> { v.Id });
                _doc.ExportImage(opts);

                using (var t = new Transaction(_doc, "STING snapshot cleanup"))
                {
                    t.Start();
                    try { _doc.Delete(v.Id); }
                    // H9: Temp 3D-view cleanup. If the view can't be deleted
                    // (e.g. another transaction is holding it), log but don't
                    // fail the overall snapshot — the PNG is already on disk.
                    catch (Exception delEx) { StingLog.Warn($"BcfSnapshotter cleanup {v.Id}: {delEx.Message}"); }
                    t.Commit();
                }

                return File.Exists(path) ? path : null;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"BcfSnapshotter failed for {clash.Id}: {ex.Message}");
                return null;
            }
        }
    }
}
