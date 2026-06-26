// StingTools — Push the latest EDGE/LEED snapshot to Planscape Server (WS A6).
//
// Mirrors HvacPublishToServerCommand. Reads the most recent EdgeKpiSnapshot the
// dashboard persisted (edge_kpi_log.jsonl), projects it onto the server's
// sustainability snapshot contract and POSTs it:
//
//     POST /api/projects/{id}/sustainability/snapshots  (SustainabilityController)
//
// Gated behind the project offline-config flag (StingOfflineConfig) so an offline
// project never contacts the network. Optional / multi-user feature — single
// machines run entirely on the local JSONL log.

using System;
using System.IO;
using System.Threading.Tasks;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.BIMManager;
using StingTools.Core;
using StingTools.Core.Sustainability;
using StingTools.UI;

namespace StingTools.Commands.Sustainability
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SustainPublishToServerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var doc = SustainCmdHelper.Doc(cmd);
            if (doc == null) { TaskDialog.Show("STING Sustainability", "No document open."); return Result.Failed; }

            // Offline-mode gate — never touch the network for an offline project.
            if (StingOfflineConfig.RefuseIfOffline("Sustainability — Publish to Server",
                    "Run the Dashboard + EDGE export locally; the EDGE KPI log stays in _BIM_COORD/sustainability/."))
                return Result.Cancelled;

            string projectDir = SustainabilityRegistries.ProjectDir(doc);
            if (string.IsNullOrEmpty(projectDir))
            {
                TaskDialog.Show("STING Sustainability", "Save the project to disk first.");
                return Result.Cancelled;
            }

            var snap = EdgeKpiSnapshot.LoadPrevious(projectDir);
            if (snap == null)
            {
                TaskDialog.Show("STING Sustainability — Publish",
                    "No sustainability snapshot to publish yet.\n\nRun the Dashboard first " +
                    "(it appends an EDGE KPI snapshot), then publish.");
                return Result.Cancelled;
            }

            if (!TryResolveProjectId(doc, out Guid projectId))
            {
                TaskDialog.Show("STING Sustainability — Publish",
                    "No Planscape project id found.\n\nSet up Planscape sync via the BIM " +
                    "Coordination Center first, or drop a planscape_config.json into the " +
                    "project's _BIM_COORD folder.");
                return Result.Cancelled;
            }

            string rag = snap.EdgePassed ? "G" : (snap.EnergySavingsPct >= 20 ? "A" : "R");
            var body = new JObject
            {
                ["capturedBy"]               = Environment.UserName,
                ["energyEuiKwhM2Yr"]         = snap.EnergyEuiKwhM2Yr,
                ["energySavingsPct"]         = snap.EnergySavingsPct,
                ["waterLPersonDay"]          = snap.WaterLPersonDay,
                ["waterSavingsPct"]          = snap.WaterSavingsPct,
                ["materialCarbonKgM2"]       = snap.MaterialCarbonKgM2,
                ["materialEnergyMjM2"]       = snap.MaterialEnergyMjM2,
                ["materialEnergySavingsPct"] = snap.MaterialEnergySavingsPct,
                ["gwpReductionPct"]          = snap.GwpReductionPct,
                ["edgeLevel"]                = snap.EdgeLevel,
                ["edgePassed"]               = snap.EdgePassed,
                ["operationalCarbonKgYr"]    = snap.OperationalCarbonKgYr,
                ["occupancy"]                = snap.Occupancy,
                ["floorAreaM2"]              = snap.FloorAreaM2,
                ["supplyMode"]               = snap.SupplyMode ?? "",
                ["country"]                  = snap.Country ?? "",
                ["climateZone"]              = snap.ClimateZone ?? "",
                ["rag"]                      = rag,
                ["payloadJson"]              = JsonConvert.SerializeObject(snap)
            };

            Guid? newId = null;
            string lastErr = null;
            Task.Run(async () =>
            {
                try
                {
                    var client = PlanscapeServerClient.Instance;
                    newId = await client.PushSustainabilitySnapshotAsync(projectId, body);
                    if (!newId.HasValue) lastErr = client.LastError;
                }
                catch (Exception ex) { lastErr = ex.Message; StingLog.Warn($"SustainPublishToServer: {ex.Message}"); }
            }).Wait(TimeSpan.FromSeconds(15));

            var panel = StingResultPanel.Create("Sustainability — Publish to Server");
            panel.SetSubtitle($"projectId={projectId}");
            panel.AddSection("PUSH RESULT")
                 .Metric("Snapshot time", snap.Ts)
                 .Metric("EDGE level", $"{snap.EdgeLevel} ({(snap.EdgePassed ? "pass" : "below target")})")
                 .Metric("Energy savings", $"{snap.EnergySavingsPct:0.#}%")
                 .Metric("Pushed", newId.HasValue ? "yes" : "no")
                 .Metric("Server snapshot id", newId?.ToString() ?? "—")
                 .Metric("Server last error", lastErr ?? "(none)");
            panel.Text("Publishes the most recent Dashboard snapshot. Endpoint: " +
                       "POST /api/projects/{id}/sustainability/snapshots. Disabled for " +
                       "offline projects.");
            panel.Show();

            StingLog.Info($"Sustain_PublishToServer: pushed={newId.HasValue}, EDGE {snap.EdgeLevel}, err={lastErr ?? "none"}.");
            return Result.Succeeded;
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
            catch (Exception ex) { StingLog.Warn($"SustainPublishToServer.TryResolveProjectId: {ex.Message}"); return false; }
        }
    }
}
