// ClashBcfExportCommand.cs — rec-16.
// Writes a BCF 2.1 ZIP from the latest clashes.json:
//   bcf.version
//   {topic-guid}/markup.bcf       — Markup + Topic + optional Comments (we have none)
//   {topic-guid}/viewpoint.bcfv   — real viewpoint from BcfViewpointBuilder.FromClash()
//   {topic-guid}/snapshot.png     — PNG from BcfSnapshotter (best-effort; skipped on failure)
//
// Consumers: Solibri / BIMcollab / Navisworks / ACC BIM 360.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Core.Clash
{
    [Transaction(TransactionMode.Manual)]   // BcfSnapshotter needs to create + delete temp View3D
    [Regeneration(RegenerationOption.Manual)]
    public sealed class ClashBcfExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                string outDir = OutputLocationHelper.GetOutputDirectory(doc) ?? Path.GetTempPath();
                string clashesJson = Path.Combine(outDir, "clashes.json");
                var run = ClashPersistence.Load(clashesJson);
                if (run == null || run.Clashes == null || run.Clashes.Count == 0)
                {
                    TaskDialog.Show("STING Clash BCF",
                        $"No clashes to export.\n\nExpected: {clashesJson}\n\nRun clash detection first.");
                    return Result.Cancelled;
                }

                string bcfPath = ExportToBcf(doc, run.Clashes, outDir);
                if (string.IsNullOrEmpty(bcfPath))
                {
                    TaskDialog.Show("STING Clash BCF",
                        "BCF export failed — see StingTools.log for details.");
                    return Result.Failed;
                }
                TaskDialog.Show("STING Clash BCF",
                    $"Exported {run.Clashes.Count} topic(s) to BCF 2.1.\n\nSaved: {bcfPath}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ClashBcfExportCommand.Execute", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// C6: Reusable export entry point. Returns the path of the written
        /// .bcfzip on success, empty string on failure. Used by both this
        /// command's interactive Execute path and ClashRunCommand's auto-
        /// export-on-critical hook.
        /// </summary>
        public static string ExportToBcf(Document doc, List<ClashRecord> clashes, string outDir)
        {
            if (doc == null || clashes == null || clashes.Count == 0 || string.IsNullOrEmpty(outDir))
                return "";

            try
            {
                Directory.CreateDirectory(outDir);
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                string bcfPath = Path.Combine(outDir, $"clashes_{stamp}.bcfzip");

                // D8: Snapshotter now reuses one temp 3D view for the whole
                //     batch via IDisposable. The using-block guarantees the
                //     view is cleaned up even on exception.
                using var snapshotter = new BcfSnapshotter(doc);
                var snapshotDir = Path.Combine(outDir, $"clash_snapshots_{stamp}");
                int wroteViewpoints = 0, wroteSnapshots = 0;

                using (var fs = File.Create(bcfPath))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
                {
                    WriteXmlEntry(zip, "bcf.version", BcfMarkupBuilder.BuildVersionXml());

                    foreach (var c in clashes)
                    {
                        string topicGuid = BcfMarkupBuilder.DeriveStableGuid(c);
                        WriteXmlEntry(zip, $"{topicGuid}/markup.bcf", BcfMarkupBuilder.BuildMarkupXml(c, topicGuid));

                        // rec-16: Real viewpoint via BcfViewpointBuilder (feet→metre
                        // conversion handled inside — rec-14).
                        string bcfv;
                        try { bcfv = BcfViewpointBuilder.FromClash(c).BuildBcfv(); wroteViewpoints++; }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"BcfViewpoint for {c.Id}: {ex.Message}");
                            bcfv = MinimalViewpointFallback();
                        }
                        var bcfvEntry = zip.CreateEntry($"{topicGuid}/viewpoint.bcfv", CompressionLevel.Optimal);
                        using (var stream = bcfvEntry.Open())
                        using (var sw = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
                        {
                            sw.Write(bcfv);
                        }

                        // Best-effort snapshot. Skipped cleanly on any failure.
                        try
                        {
                            var png = snapshotter.RenderSnapshot(c, snapshotDir);
                            if (!string.IsNullOrEmpty(png) && File.Exists(png))
                            {
                                var pngEntry = zip.CreateEntry($"{topicGuid}/snapshot.png", CompressionLevel.Optimal);
                                using (var es = pngEntry.Open())
                                using (var fr = File.OpenRead(png))
                                {
                                    fr.CopyTo(es);
                                }
                                wroteSnapshots++;
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"Snapshot for {c.Id}: {ex.Message}"); }
                    }
                }

                // G6: Snapshots now embedded in the ZIP; the temp dir on disk is
                // a redundant per-run artefact. Without cleanup it accumulates
                // at ~50 clashes × 100 KB = 5 MB/run, indefinitely.
                // Best-effort: swallow cleanup failures (file locks etc.) so the
                // export is still reported success.
                try
                {
                    if (Directory.Exists(snapshotDir))
                    {
                        Directory.Delete(snapshotDir, recursive: true);
                    }
                }
                catch (Exception cleanupEx)
                {
                    StingLog.Warn($"ClashBcfExport snapshot-dir cleanup: {cleanupEx.Message} (dir: {snapshotDir})");
                }

                StingLog.Info($"ClashBcfExport: {clashes.Count} topics, {wroteViewpoints} viewpoints, " +
                    $"{wroteSnapshots} snapshots → {bcfPath}");
                return bcfPath;
            }
            catch (Exception ex)
            {
                StingLog.Error("ClashBcfExportCommand.ExportToBcf", ex);
                return "";
            }
        }

        private static void WriteXmlEntry(ZipArchive zip, string entryPath, XDocument xdoc)
        {
            var entry = zip.CreateEntry(entryPath, CompressionLevel.Optimal);
            using var stream = entry.Open();
            using var sw = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
            xdoc.Save(sw);
        }

        private static string MinimalViewpointFallback() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<VisualizationInfo><Components/></VisualizationInfo>";
    }
}
