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
using System.Linq;
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
                    WriteXmlEntry(zip, "bcf.version", BuildVersionXml());

                    foreach (var c in clashes)
                    {
                        string topicGuid = DeriveStableGuid(c);
                        WriteXmlEntry(zip, $"{topicGuid}/markup.bcf", BuildMarkupXml(c, topicGuid));

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

        private static XDocument BuildVersionXml()
        {
            return new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("Version",
                    new XAttribute("VersionId", "2.1"),
                    new XElement("DetailedVersion", "2.1")));
        }

        private static XDocument BuildMarkupXml(ClashRecord c, string topicGuid)
        {
            string severity = (c.Severity ?? "MED").ToUpperInvariant();
            string bcfPriority = severity switch
            {
                "CRITICAL" => "Critical",
                "HIGH"     => "Major",
                "MED"      => "Normal",
                "MEDIUM"   => "Normal",
                "LOW"      => "Minor",
                _          => "Normal",
            };
            string bcfStatus = (c.State ?? "New") switch
            {
                "Resolved" => "Closed",
                "Void"     => "Closed",
                _          => "Active",
            };

            string title = $"[{c.Severity ?? "MED"}] {c.MatrixPairId} clash {c.Id}";
            var description = new System.Text.StringBuilder();
            description.AppendLine($"Matrix pair: {c.MatrixPairId}");
            description.AppendLine($"Severity:    {c.Severity}");
            description.AppendLine($"Tolerance:   {c.Tolerance}");
            if (c.ElementA != null)
                description.AppendLine($"Element A:   {c.ElementA.Category}:{c.ElementA.ElementId} (IFC {c.ElementA.IfcGuid})");
            if (c.ElementB != null)
                description.AppendLine($"Element B:   {c.ElementB.Category}:{c.ElementB.ElementId} (IFC {c.ElementB.IfcGuid})");
            if (!string.IsNullOrEmpty(c.ResolutionHint))
                description.AppendLine($"Suggestion:  {c.ResolutionHint}");
            if (!string.IsNullOrEmpty(c.GroupId))
                description.AppendLine($"Group:       {c.GroupId}");
            description.AppendLine($"Identity:    {c.Identity}");
            description.AppendLine($"Volume:      {c.VolumeMm3:F0} mm³");

            var creationIso = (c.FirstSeenUtc == default ? DateTime.UtcNow : c.FirstSeenUtc)
                .ToString("o", CultureInfo.InvariantCulture);
            var modifiedIso = (c.LastSeenUtc == default ? DateTime.UtcNow : c.LastSeenUtc)
                .ToString("o", CultureInfo.InvariantCulture);

            var topic = new XElement("Topic",
                new XAttribute("Guid", topicGuid),
                new XAttribute("TopicType", "Clash"),
                new XAttribute("TopicStatus", bcfStatus),
                new XElement("ReferenceLink", string.IsNullOrEmpty(c.LinkedIssueGuid) ? "" : $"sting-issue://{c.LinkedIssueGuid}"),
                new XElement("Title", title),
                new XElement("Priority", bcfPriority),
                new XElement("Index", 0),
                new XElement("Labels",
                    new XElement("Label", "clash"),
                    new XElement("Label", c.MatrixPairId ?? "")),
                new XElement("CreationDate", creationIso),
                new XElement("CreationAuthor", Environment.UserName),
                new XElement("ModifiedDate", modifiedIso),
                new XElement("ModifiedAuthor", Environment.UserName),
                new XElement("AssignedTo", ""),
                new XElement("Description", description.ToString()),
                // Lossless round-trip hints so a BCF import can reconstruct a ClashRecord.
                new XElement("StingClashIdentity", c.Identity ?? ""),
                new XElement("StingClashId", c.Id ?? ""),
                new XElement("StingMatrixPairId", c.MatrixPairId ?? ""),
                new XElement("StingSeverity", c.Severity ?? ""));

            var markup = new XElement("Markup",
                new XElement("Header"),
                topic);
            return new XDocument(new XDeclaration("1.0", "UTF-8", null), markup);
        }

        /// <summary>
        /// Derive a stable BCF topic Guid from the ClashIdentity hash so re-exports
        /// across runs collapse to the same topic (important for ACC issue dedup).
        /// </summary>
        private static string DeriveStableGuid(ClashRecord c)
        {
            string seed = c?.Identity;
            if (string.IsNullOrEmpty(seed)) return Guid.NewGuid().ToString();
            // Pad / truncate the identity hash into a 32-char hex Guid form.
            using var sha = System.Security.Cryptography.SHA1.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(seed));
            return new Guid(bytes[..16]).ToString();
        }

        private static string MinimalViewpointFallback() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<VisualizationInfo><Components/></VisualizationInfo>";
    }
}
