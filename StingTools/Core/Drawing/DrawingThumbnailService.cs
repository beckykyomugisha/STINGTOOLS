using StingTools.Core;
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
using System.Text;
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
        //
        // Hermetic per-export subfolder + deterministic ISO-style final
        // name eliminates the race condition that the scan-by-creation-
        // time approach suffered from. Layout:
        //
        //   %TEMP%/sting_drawing_thumbs/              <- session root
        //     <dt.Id>__<sheetNumber>.png              <- final (cache key)
        //     _export_<guid>/                         <- per-export sandbox
        //       whatever-revit-named-it.png
        //
        // Revit fabricates a filename by appending
        //   " - Sheet - <number> - <name>.png"
        // to ImageExportOptions.FilePath. We can't predict it, but by
        // pointing FilePath into an empty sandbox dir we guarantee the
        // sandbox contains exactly one file after the export — then we
        // move it to the deterministic ISO-style name and delete the
        // sandbox. Multiple concurrent exports are safe because each
        // gets its own sandbox.

        private BitmapSource RenderOne(Document doc, DrawingType dt)
        {
            if (doc == null || dt == null) return null;

            var sheet = FindMatchingSheet(doc, dt);
            if (sheet == null) return null;

            var sessionDir = Path.Combine(Path.GetTempPath(), "sting_drawing_thumbs");
            var sandbox    = Path.Combine(sessionDir, "_export_" + Guid.NewGuid().ToString("N"));
            try { Directory.CreateDirectory(sandbox); }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"Thumbnail sandbox create: {ex.Message}");
                return null;
            }

            // Sandbox FilePath — Revit suffixes its own junk, we don't
            // care what the in-sandbox name ends up being.
            var opts = new ImageExportOptions
            {
                ZoomType = ZoomFitType.FitToPage,
                PixelSize = 360,
                ImageResolution = ImageResolution.DPI_150,
                HLRandWFViewsFileType = ImageFileType.PNG,
                ShadowViewsFileType   = ImageFileType.PNG,
                ExportRange = ExportRange.SetOfViews,
                FilePath    = Path.Combine(sandbox, "thumb.png"),
            };
            opts.SetViewsAndSheets(new List<ElementId> { sheet.Id });

            try { doc.ExportImage(opts); }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"ExportImage for {sheet.SheetNumber}: {ex.Message}");
                TryDelete(sandbox);
                return null;
            }

            // The sandbox should now contain exactly one .png — pick it
            // up without guessing at the filename Revit synthesised.
            string exported;
            try
            {
                var pngs = Directory.GetFiles(sandbox, "*.png");
                if (pngs.Length == 0)
                {
                    StingTools.Core.StingLog.Warn(
                        $"Thumbnail sandbox empty after export for {sheet.SheetNumber}.");
                    TryDelete(sandbox);
                    return null;
                }
                exported = pngs[0];
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"Sandbox scan: {ex.Message}");
                TryDelete(sandbox);
                return null;
            }

            // Deterministic final name — ISO-style segments joined with
            // double-underscore so the cache-key is sortable, unique
            // per (DrawingType × source sheet), and readable on disk.
            var finalPath = Path.Combine(sessionDir, BuildCacheFileName(dt, sheet));
            try
            {
                if (File.Exists(finalPath)) File.Delete(finalPath);
                File.Move(exported, finalPath);
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"Thumbnail rename to '{finalPath}': {ex.Message}");
                // Fall back to reading from the sandbox before cleanup
                try { return LoadFrozenBitmap(exported); }
                finally { TryDelete(sandbox); }
            }
            TryDelete(sandbox);
            return LoadFrozenBitmap(finalPath);
        }

        /// <summary>
        /// ISO-style deterministic name:
        ///   {dt.Id}__{sanitisedSheetNumber}.png
        /// Each segment sanitised to the ISO 19650 filesystem-safe set
        /// (A-Z, 0-9, -, _); spaces collapse to single underscores.
        /// Collision-proof within a project because the pair
        /// (DrawingType id, sheet number) is unique.
        /// </summary>
        private static string BuildCacheFileName(DrawingType dt, ViewSheet sheet)
        {
            var typeSeg  = Sanitise(dt?.Id ?? "dt");
            var sheetSeg = Sanitise(sheet?.SheetNumber ?? "unknown");
            return $"{typeSeg}__{sheetSeg}.png";
        }

        private static string Sanitise(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_') sb.Append(ch);
                else if (char.IsWhiteSpace(ch) || ch == '.') sb.Append('_');
                // else: drop (colons, slashes, quotes etc.)
            }
            var outStr = sb.ToString();
            // Collapse consecutive underscores to a single underscore.
            while (outStr.Contains("__"))
                outStr = outStr.Replace("__", "_");
            return outStr.Trim('_').Length == 0 ? "x" : outStr.Trim('_');
        }

        private static void TryDelete(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch { /* best-effort */ }
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
