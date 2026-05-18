// StingTools v4 MVP — Phase I.5 sleeve → BCF 2.1 round-trip.
//
// After PlaceSleevesCommand runs (or against an existing sleeve
// population), this command emits one BCF 2.1 topic per sleeve so
// structural coordinators receive a per-penetration reservation
// request. BcfEngine already exists and is used by the main
// PlatformLinkCommand.BCFExport workflow — we reuse it verbatim.
//
// One topic per sleeve:
//   Title       = "Sleeve request — <HostCat> at <Level> (<Bore>mm)"
//   Type        = "RFI"    (request for coordination)
//   Priority    = HIGH when sleeve has fire rating, else MEDIUM
//   Labels      = [ STING_PHASE_I, SLEEVE, <HostCategory> ]
//   RefLink     = STING_SLEEVE_PFV_UUID  (stable Tekla key)
//   Description = size + host fire rating + UL system + rule id
//
// After export the BCF file lands in <project>/_BIM_COORD/bcf/
// and can be uploaded to ACC / BIMcollab / Solibri / Trimble
// Connect for assignee/resolver action.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Planscape.Shared.BCF;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Mep
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportSleeveBcfCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // Collect placed sleeves — any FamilyInstance with
            // STING_SLEEVE_PFV_UUID set.
            var sleeves = new List<FamilyInstance>();
            try
            {
                var col = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance));
                foreach (FamilyInstance fi in col)
                {
                    try
                    {
                        var p = fi.LookupParameter("STING_SLEEVE_PFV_UUID");
                        if (p?.AsString() is { Length: > 0 }) sleeves.Add(fi);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            { StingLog.Warn($"ExportSleeveBcfCommand: collector: {ex.Message}"); }

            if (sleeves.Count == 0)
            {
                TaskDialog.Show("STING v4 — Sleeve BCF Export",
                    "No STING-placed sleeves found (no FamilyInstance has STING_SLEEVE_PFV_UUID).\n\n" +
                    "Run STING v4 Place Sleeves first.");
                return Result.Cancelled;
            }

            var issues = new List<CoordIssue>();
            foreach (var fi in sleeves)
            {
                try { issues.Add(BuildIssue(doc, fi)); }
                catch (Exception ex2)
                { StingLog.Warn($"Sleeve BCF build {fi?.Id}: {ex.Message}"); }
            }

            string outDir, path;
            try
            {
                var projDir = Path.GetDirectoryName(doc.PathName ?? "") ?? Path.GetTempPath();
                outDir = Path.Combine(projDir, "_BIM_COORD", "bcf");
                Directory.CreateDirectory(outDir);
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                path = Path.Combine(outDir, $"sting_sleeves_{stamp}.bcf");
            }
            catch (Exception ex)
            {
                StingLog.Error("ExportSleeveBcfCommand: path resolve", ex);
                message = ex.Message;
                return Result.Failed;
            }

            // BcfEngine.Export returns the topic count it wrote.
            int exportedCount;
            bool exported;
            try
            {
                exportedCount = BcfEngine.Export(issues, path);
                exported = exportedCount > 0;
            }
            catch (Exception ex)
            {
                StingLog.Error("ExportSleeveBcfCommand: BcfEngine.Export", ex);
                message = ex.Message;
                return Result.Failed;
            }

            var panel = StingResultPanel.Create("v4 Sleeve → BCF Export");
            panel.SetSubtitle(exported ? $"{issues.Count} topics → {path}" : "Export reported failure");
            panel.AddSection("SUMMARY")
                 .Metric("Sleeves found", sleeves.Count.ToString())
                 .Metric("Topics built",  issues.Count.ToString())
                 .Metric("High priority", issues.Count(i => i.Priority == "HIGH").ToString());
            panel.AddSection("NEXT STEPS")
                 .Text("Upload the BCF file to ACC Issues / BIMcollab / Solibri / Trimble Connect.")
                 .Text("Returned BCF updates (assignee / status) round-trip back through")
                 .Text("BIMManager.BCFImportCommand — the topic GUID keeps the linkage stable.");
            panel.Show();
            return Result.Succeeded;
        }

        private static CoordIssue BuildIssue(Document doc, FamilyInstance sleeve)
        {
            string pfvUuid = sleeve.LookupParameter("STING_SLEEVE_PFV_UUID")?.AsString() ?? sleeve.UniqueId;
            string bore    = sleeve.LookupParameter("STING_SLEEVE_BORE_MM")?.AsDouble().ToString("F0") ?? "";
            string width   = sleeve.LookupParameter("STING_SLEEVE_WIDTH_MM")?.AsDouble().ToString("F0") ?? "";
            string height  = sleeve.LookupParameter("STING_SLEEVE_HEIGHT_MM")?.AsDouble().ToString("F0") ?? "";
            string depth   = sleeve.LookupParameter("STING_SLEEVE_DEPTH_MM")?.AsDouble().ToString("F0") ?? "";
            string rule    = sleeve.LookupParameter("STING_SLEEVE_RULE_ID")?.AsString() ?? "";
            string fireRat = sleeve.LookupParameter("STING_SLEEVE_HOST_FIRE_RATING")?.AsString() ?? "";
            string ulSys   = sleeve.LookupParameter("STING_SLEEVE_UL_SYS")?.AsString() ?? "";

            string hostCat = sleeve.Host?.Category?.Name ?? "Unknown host";
            string levelName = "";
            try
            {
                var lvlId = sleeve.LevelId;
                if (lvlId != null && lvlId != ElementId.InvalidElementId)
                {
                    var lvl = doc.GetElement(lvlId) as Level;
                    levelName = lvl?.Name ?? "";
                }
            }
            catch { }

            string sizeBlurb = !string.IsNullOrEmpty(bore)
                ? $"bore {bore} mm"
                : $"opening {width}×{height} mm";

            var issue = new CoordIssue
            {
                Guid          = pfvUuid,                // reuse the PFV UUID as BCF topic GUID
                Title         = $"Sleeve request — {hostCat} @ {levelName} ({sizeBlurb})",
                Type          = "RFI",
                Priority      = string.IsNullOrEmpty(fireRat) ? "MEDIUM" : "HIGH",
                Status        = "OPEN",
                ReferenceLink = $"STING-SLEEVE-{pfvUuid.Substring(0, 8).ToUpperInvariant()}",
                CreationDate  = DateTime.UtcNow,
                Description   =
                    $"Sleeve placed by STING v4.\n" +
                    $"Host: {hostCat} @ {levelName}\n" +
                    $"Size: {sizeBlurb}, depth {depth} mm\n" +
                    $"Rule: {rule}\n" +
                    $"Fire rating: {(string.IsNullOrEmpty(fireRat) ? "not set" : fireRat)}\n" +
                    $"UL firestop system: {(string.IsNullOrEmpty(ulSys) ? "TBD" : ulSys)}\n" +
                    $"PFV UUID: {pfvUuid}",
            };
            issue.Labels.Add("STING_PHASE_I");
            issue.Labels.Add("SLEEVE");
            if (!string.IsNullOrEmpty(hostCat)) issue.Labels.Add(hostCat);
            if (!string.IsNullOrEmpty(fireRat))  issue.Labels.Add($"FIRE_{fireRat}");
            return issue;
        }
    }
}
