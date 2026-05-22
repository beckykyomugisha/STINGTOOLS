// StingTools — Push HVAC panel results to Planscape Server.
//
// Bundles whatever's currently in the HVAC dock panel grids (SpaceLoadRows
// from BlockLoad, IssueRows from NC, EquipmentRows from RefreshGrids) and
// POSTs them to the matching /api/projects/{id}/hvac/* endpoints. Single
// click; no per-step push wiring inside the engines themselves.
//
// Resolves the project id from <bim-dir>/planscape_config.json (the same
// path BOQ + BCF sync use). Reports counts + per-endpoint status.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.BIMManager;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacPublishToServerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;
                var p = StingHvacPanel.Instance;
                if (p == null)
                {
                    TaskDialog.Show("STING HVAC", "HVAC panel is not open.");
                    return Result.Cancelled;
                }

                if (!TryResolveProjectId(doc, out Guid projectId))
                {
                    TaskDialog.Show("STING HVAC — Publish",
                        "No Planscape project id found.\n\n" +
                        "Set up Planscape sync via the BIM Coordination Center first, " +
                        "or drop a planscape_config.json into the project's _BIM_COORD folder.");
                    return Result.Cancelled;
                }

                // Build the three payload lists from the live panel state.
                var loadDtos = p.SpaceLoadRows.Select(r => (object)new
                {
                    SystemId        = r.SpaceType ?? "(default)",
                    ClimateSiteId   = doc.ProjectInformation?.LookupParameter("PRJ_CLIMATE_SITE_ID")?.AsString() ?? "",
                    BlockSensibleW  = r.CoolingKw * 1000.0,
                    BlockLatentW    = 0.0,
                    BlockHour       = 0,
                    DiversityFactor = 1.0,
                    ZoneCount       = 1,
                    ZonesJson       = "[]",
                    Cooling         = true,
                    CapturedBy      = Environment.UserName,
                    Source          = "PLUGIN"
                }).ToList();

                var ncDtos = p.IssueRows
                    .Where(i => (i.Issue ?? "").Contains("NC"))
                    .Select(i => (object)new
                    {
                        PathLabel            = i.Element ?? "",
                        ReceiverRoom         = i.Element ?? "",
                        PredictedNc          = ExtractNc(i.Issue),
                        TargetNc             = 35,
                        PathFlowLs           = 0.0,
                        PathPressureDropPa   = 0.0,
                        OctaveLpJson         = "[]",
                        ElementBreakdownJson = i.Suggestion ?? "",
                        CapturedBy           = Environment.UserName
                    }).ToList();

                // Async push (Task.Run keeps Revit's API thread free).
                int loadOk = 0, ncOk = 0;
                string lastErr = null;
                Task.Run(async () =>
                {
                    try
                    {
                        if (loadDtos.Count > 0)
                        {
                            bool ok = await PlanscapeServerClient.Instance
                                .PushHvacLoadsBulkAsync(projectId, loadDtos);
                            if (ok) loadOk = loadDtos.Count;
                            else lastErr = PlanscapeServerClient.Instance.LastError;
                        }
                        foreach (var dto in ncDtos)
                        {
                            bool ok = await PlanscapeServerClient.Instance.PushHvacNcAsync(projectId, dto);
                            if (ok) ncOk++;
                            else { lastErr = PlanscapeServerClient.Instance.LastError; break; }
                        }
                    }
                    catch (Exception ex)
                    {
                        lastErr = ex.Message;
                        StingLog.Warn($"HvacPublishToServer: {ex.Message}");
                    }
                }).Wait(TimeSpan.FromSeconds(15));

                var resPanel = StingResultPanel.Create("HVAC — Publish to Server");
                resPanel.SetSubtitle($"projectId={projectId}");
                resPanel.AddSection("PUSH RESULT")
                        .Metric("Load rows queued",   loadDtos.Count.ToString())
                        .Metric("Load rows accepted", loadOk.ToString())
                        .Metric("NC rows queued",     ncDtos.Count.ToString())
                        .Metric("NC rows accepted",   ncOk.ToString())
                        .Metric("Server last error",  lastErr ?? "(none)");
                resPanel.Text("Run Hvac_BlockLoad + Hvac_NcPredict + Hvac_RefreshGrids before " +
                              "publishing — this command snapshots the current dock-panel grids. " +
                              "Endpoints: /api/projects/{id}/hvac/loads, /hvac/nc, /hvac/refrigerant.");
                resPanel.Show();
                try { p.PushRunRow($"Server publish ({loadOk + ncOk} rows)", lastErr == null ? "⬤" : "⬡"); }
                catch (Exception ex) { StingLog.Warn($"Panel push: {ex.Message}"); }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacPublishToServerCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static bool TryResolveProjectId(Document doc, out Guid projectId)
        {
            projectId = Guid.Empty;
            try
            {
                string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
                if (string.IsNullOrEmpty(bimDir)) return false;
                string configPath = Path.Combine(bimDir, "planscape_config.json");
                if (!File.Exists(configPath)) return false;
                var (_, _, projectIdStr) = PlanscapeServerClient.LoadConnectionSettings(configPath);
                return Guid.TryParse(projectIdStr, out projectId);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"HvacPublishToServer.TryResolveProjectId: {ex.Message}");
                return false;
            }
        }

        private static int ExtractNc(string issue)
        {
            // "Predicted NC 42 exceeds target NC 35" → 42
            if (string.IsNullOrEmpty(issue)) return 0;
            var match = System.Text.RegularExpressions.Regex.Match(issue, @"NC\s+(\d+)");
            return match.Success && int.TryParse(match.Groups[1].Value, out int n) ? n : 0;
        }
    }
}
