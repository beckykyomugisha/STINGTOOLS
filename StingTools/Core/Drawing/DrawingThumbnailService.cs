// StingTools — Drawing Template Manager
//
// DrawingThumbnailService renders live sheet thumbnails for the
// Preflight dialog. Revit forbids Document.ExportImage on a modal
// WPF dialog thread, so the service runs via IExternalEventHandler —
// the dialog queues (DrawingType, onComplete) requests on any thread,
// Raise()s the ExternalEvent, and Revit calls Execute on the API
// thread. Execute looks for a sheet in the project whose title block
// matches DrawingType.TitleBlockFamily (i.e. already uses this
// profile), exports it as PNG, loads the PNG as a BitmapImage, and
// marshals the result back to the WPF dispatcher.
//
// When no matching sheet exists (brand-new project, profile never
// consumed yet) the service returns null so the dialog falls back to
// its vector layout preview.
//
// Lifetime: one instance + one ExternalEvent per Revit session,
// created lazily when the first dialog opens, re-used afterwards.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace StingTools.Core.Drawing
{
    public sealed class DrawingThumbnailService : IExternalEventHandler
    {
        public sealed class Request
        {
            public DrawingType DrawingType;
            public Action<BitmapSource> OnComplete;
        }

        private readonly ConcurrentQueue<Request> _queue = new ConcurrentQueue<Request>();
        private ExternalEvent _event;

        // ── Singleton accessor. Created once, re-used for the session. ──
        private static readonly object _gate = new object();
        private static DrawingThumbnailService _instance;
        public static DrawingThumbnailService Instance
        {
            get
            {
                lock (_gate)
                {
                    if (_instance == null)
                    {
                        _instance = new DrawingThumbnailService();
                        _instance._event = ExternalEvent.Create(_instance);
                    }
                    return _instance;
                }
            }
        }

        public string GetName() => "STING Drawing Thumbnail Service";

        public void Enqueue(Request r)
        {
            if (r == null || r.DrawingType == null || r.OnComplete == null) return;
            _queue.Enqueue(r);
            try { _event?.Raise(); } catch { /* Revit may reject while busy — next call will flush */ }
        }

        public void Execute(UIApplication app)
        {
            var doc = app?.ActiveUIDocument?.Document;
            while (_queue.TryDequeue(out var req))
            {
                BitmapSource bmp = null;
                try
                {
                    bmp = RenderOne(doc, req.DrawingType);
                }
                catch (Exception ex)
                {
                    StingTools.Core.StingLog.Warn(
                        $"DrawingThumbnailService.Render('{req.DrawingType?.Id}'): {ex.Message}");
                }

                // Marshal back to UI thread — the dialog updates its
                // Image control from here.
                try
                {
                    var dispatcher = Application.Current?.Dispatcher;
                    if (dispatcher != null && !dispatcher.CheckAccess())
                        dispatcher.Invoke(() => req.OnComplete?.Invoke(bmp));
                    else
                        req.OnComplete?.Invoke(bmp);
                }
                catch (Exception ex)
                {
                    StingTools.Core.StingLog.Warn(
                        $"DrawingThumbnailService.Callback('{req.DrawingType?.Id}'): {ex.Message}");
                }
            }
        }

        // ── Rendering ──

        private BitmapSource RenderOne(Document doc, DrawingType dt)
        {
            if (doc == null || dt == null) return null;

            var sheet = FindMatchingSheet(doc, dt);
            if (sheet == null) return null;

            // Export to a per-session temp folder. Cleanup is best-effort —
            // Revit will hold the file handle open until the export
            // completes, so we don't try to delete after reading.
            var tempDir = Path.Combine(Path.GetTempPath(), "sting_drawing_thumbs");
            try { Directory.CreateDirectory(tempDir); } catch { }

            var fileBase = $"{dt.Id ?? "dt"}_{DateTime.Now.Ticks}";
            var filePath = Path.Combine(tempDir, fileBase + ".png");

            var opts = new ImageExportOptions
            {
                ZoomType = ZoomFitType.FitToPage,
                PixelSize = 360,
                ImageResolution = ImageResolution.DPI_150,
                HLRandWFViewsFileType = ImageFileType.PNG,
                ShadowViewsFileType = ImageFileType.PNG,
                ExportRange = ExportRange.SetOfViews,
                FilePath = filePath,
            };
            opts.SetViewsAndSheets(new List<ElementId> { sheet.Id });

            try { doc.ExportImage(opts); }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"ExportImage for {sheet.SheetNumber}: {ex.Message}");
                return null;
            }

            // Revit appends " - Sheet - <number> - <name>.png" to FilePath
            // before the extension, so the actual file rarely matches the
            // requested name verbatim. Scan the dir for the freshest match.
            var candidates = Directory.GetFiles(tempDir, fileBase + "*.png");
            var actual = candidates.OrderByDescending(File.GetCreationTime).FirstOrDefault();
            if (string.IsNullOrEmpty(actual) || !File.Exists(actual)) return null;

            return LoadFrozenBitmap(actual);
        }

        private static ViewSheet FindMatchingSheet(Document doc, DrawingType dt)
        {
            if (dt == null || string.IsNullOrWhiteSpace(dt.TitleBlockFamily)) return null;

            try
            {
                // Find title-block instances whose FamilyName matches;
                // their OwnerViewId (or owner sheet) is the candidate.
                var tbs = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(fi => fi.Symbol?.FamilyName != null
                        && string.Equals(fi.Symbol.FamilyName, dt.TitleBlockFamily, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var tb in tbs)
                {
                    if (tb.OwnerViewId == null || tb.OwnerViewId == ElementId.InvalidElementId) continue;
                    var v = doc.GetElement(tb.OwnerViewId);
                    if (v is ViewSheet sh && !sh.IsTemplate) return sh;
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"FindMatchingSheet('{dt.Id}'): {ex.Message}");
            }
            return null;
        }

        private static BitmapSource LoadFrozenBitmap(string path)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;   // release file handle after load
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                if (bmp.CanFreeze) bmp.Freeze();
                return bmp;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"LoadFrozenBitmap('{path}'): {ex.Message}");
                return null;
            }
        }
    }
}
