// ══════════════════════════════════════════════════════════════════════════
//  SpecLinkImportCommand.cs — Phase H4 (KUT lifecycle, max automation).
//
//  SpecLink_ImportFolder (read-only). Watch-folder front for the spec→bill loop:
//  scans <project>/_BIM_COORD/speclink/ for exported SpecLink project-manual CSVs
//  (RIB/Deltek SpecLink has no public API — Word/PDF/CSV export is the integration
//  surface) and builds sections.json — the store SpecStore reads at BOQ-build time
//  so the issued specification writes the line descriptions (Phase H1).
//
//  Mirrors the StingBridge watch-IFC drop-folder pattern: drop the export in,
//  run the command (or chain it as step 0 of WORKFLOW_KUT_LifecycleReconcile so
//  the lifecycle coordinator refreshes the spec store unattended).
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.BOQ;
using StingTools.Core;

namespace StingTools.Commands.Classification
{
    [Transaction(TransactionMode.ReadOnly)]
    public class SpecLinkImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx?.Doc == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            string dir = Path.GetDirectoryName(doc.PathName ?? "");
            if (string.IsNullOrEmpty(dir))
            {
                TaskDialog.Show("SpecLink import", "Save the project first so STING can find its _BIM_COORD folder.");
                return Result.Cancelled;
            }

            string specDir = Path.Combine(dir, "_BIM_COORD", "speclink");
            Directory.CreateDirectory(specDir);
            // Newest export wins. A re-issued SpecLink manual is dropped beside
            // the previous one; ordering by last-write-time DESCENDING + the
            // first-seen-wins merge below means the most recent file's section
            // text supersedes an older file's on a collision, without the user
            // having to delete the superseded export.
            var csvs = Directory.EnumerateFiles(specDir, "*.csv", SearchOption.TopDirectoryOnly)
                .Where(p => !Path.GetFileName(p).Equals("sections.json", StringComparison.OrdinalIgnoreCase))
                .Select(p => new FileInfo(p))
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .ThenBy(fi => fi.Name, StringComparer.OrdinalIgnoreCase)
                .Select(fi => fi.FullName)
                .ToList();

            if (csvs.Count == 0)
            {
                TaskDialog.Show("SpecLink import",
                    "No CSV found in:\n" + specDir + "\n\nExport the SpecLink project manual " +
                    "(table of contents, optionally with description + unit columns) as CSV and drop it here, " +
                    "then re-run. Columns: Section, Title[, Description][, Unit].");
                return Result.Cancelled;
            }

            // Merge — newest file (first in the time-ordered list) + earliest
            // row within a file wins on a section collision.
            var merged = new Dictionary<string, SpecSection>(StringComparer.Ordinal);
            int rows = 0, files = 0;
            foreach (var p in csvs)
            {
                try
                {
                    var part = SpecStore.ParseManualCsv(File.ReadAllLines(p));
                    files++;
                    int added = 0;
                    foreach (var kv in part)
                    {
                        rows++;
                        if (!merged.ContainsKey(kv.Key)) { merged[kv.Key] = kv.Value; added++; }
                    }
                    StingLog.Info($"SpecLink import {Path.GetFileName(p)}: {added} new section(s) of {part.Count} (newer files take precedence).");
                }
                catch (Exception ex) { StingLog.Warn($"SpecLink import {Path.GetFileName(p)}: {ex.Message}"); }
            }

            int withDesc = merged.Values.Count(s => !string.IsNullOrWhiteSpace(s.Description));
            string outPath = Path.Combine(specDir, "sections.json");
            try { File.WriteAllText(outPath, SpecStore.Serialize(merged)); }
            catch (Exception ex)
            {
                StingLog.Error("SpecLink import write", ex);
                TaskDialog.Show("SpecLink import", "Could not write sections.json:\n" + ex.Message);
                return Result.Failed;
            }

            // Drop the per-document spec cache so the next BOQ build reads the fresh store.
            CsiMap.Invalidate();

            TaskDialog.Show("SpecLink import",
                $"Imported {merged.Count} spec section(s) from {files} CSV file(s) ({rows} rows read).\n" +
                $"{withDesc} carry description text (these drive BOQ line descriptions).\n\n" +
                "Newest export wins on a section collision — re-issued manuals supersede older drops automatically.\n\n" +
                "Written:\n" + outPath + "\n\nRe-run a BOQ export — spec'd items now bill from the specification.");
            return Result.Succeeded;
        }
    }
}
