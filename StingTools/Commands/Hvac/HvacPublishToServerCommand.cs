// StingTools — Push HVAC panel results to Planscape Server.
//
// Bundles whatever's currently in the HVAC dock panel grids (SpaceLoadRows
// from BlockLoad, IssueRows from NC) and POSTs them to the server's unified
// HVAC snapshot route — POST /api/projects/{id}/hvac/snapshots — with a "kind"
// discriminator (HvacController). Single click; no per-step push wiring inside
// the engines themselves.
//
// P1-B fix: the old build targeted /hvac/loads + /hvac/nc, routes that never
// existed server-side (the methods were Task.FromResult(false) stubs). It now
// wraps each grid in the snapshot envelope (per-row detail in PayloadJson) and
// calls the real PushHvacSnapshotAsync.
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
using Newtonsoft.Json.Linq;
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

                // Build the per-row payloads from the live panel state. These ride
                // inside the snapshot envelope's PayloadJson — the mobile HVAC
                // dashboard renders the row tables from it.
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

                var ncRows = p.IssueRows
                    .Where(i => (i.Issue ?? "").Contains("NC"))
                    .ToList();
                var ncDtos = ncRows.Select(i => (object)new
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

                // The server exposes ONE write route — POST /hvac/snapshots with a
                // "kind" discriminator (HvacController). Wrap each grid as a snapshot;
                // KPI columns drive the dashboard RAG cards, PayloadJson the row table.
                int loadSpaces = loadDtos.Count;
                var loadsBody = new JObject
                {
                    ["kind"]        = "loads",
                    ["inspected"]   = loadSpaces,
                    ["pass"]        = loadSpaces,
                    ["warn"]        = 0,
                    ["fail"]        = 0,
                    ["totalKw"]     = p.SpaceLoadRows.Sum(r => r.CoolingKw + r.HeatingKw),
                    ["worstValue"]  = p.SpaceLoadRows.Count == 0
                        ? 0.0 : p.SpaceLoadRows.Max(r => Math.Max(r.CoolingKw, r.HeatingKw)),
                    ["rag"]         = "G",
                    ["payloadJson"] = JArray.FromObject(loadDtos).ToString(Newtonsoft.Json.Formatting.None)
                };

                int ncOver  = ncRows.Count(i => ExtractNc(i.Issue) > 35);
                int worstNc = ncRows.Count == 0 ? 0 : ncRows.Max(i => ExtractNc(i.Issue));
                var ncBody = new JObject
                {
                    ["kind"]        = "nc",
                    ["inspected"]   = ncRows.Count,
                    ["pass"]        = ncRows.Count - ncOver,
                    ["warn"]        = 0,
                    ["fail"]        = ncOver,
                    ["totalKw"]     = 0.0,
                    ["worstValue"]  = worstNc,
                    ["rag"]         = ncOver == 0 ? "G" : (ncOver > ncRows.Count / 2 ? "R" : "A"),
                    ["payloadJson"] = JArray.FromObject(ncDtos).ToString(Newtonsoft.Json.Formatting.None)
                };

                // Async push (Task.Run keeps Revit's API thread free).
                int snapshotsOk = 0;
                string lastErr = null;
                Task.Run(async () =>
                {
                    try
                    {
                        var client = PlanscapeServerClient.Instance;
                        if (loadSpaces > 0)
                        {
                            var id = await client.PushHvacSnapshotAsync(projectId, loadsBody);
                            if (id.HasValue) snapshotsOk++; else lastErr = client.LastError;
                        }
                        if (ncRows.Count > 0)
                        {
                            var id = await client.PushHvacSnapshotAsync(projectId, ncBody);
                            if (id.HasValue) snapshotsOk++; else lastErr = client.LastError;
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
                        .Metric("Load rows",         loadDtos.Count.ToString())
                        .Metric("NC rows",           ncRows.Count.ToString())
                        .Metric("Snapshots pushed",  snapshotsOk.ToString())
                        .Metric("Server last error", lastErr ?? "(none)");
                resPanel.Text("Run Hvac_BlockLoad + Hvac_NcPredict + Hvac_RefreshGrids before " +
                              "publishing — this command snapshots the current dock-panel grids. " +
                              "Endpoint: POST /api/projects/{id}/hvac/snapshots (kind=loads, kind=nc).");
                resPanel.Show();
                try { p.PushRunRow($"Server publish ({snapshotsOk} snapshots)", lastErr == null ? "⬤" : "⬡"); }
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
