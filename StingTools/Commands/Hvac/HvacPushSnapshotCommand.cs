// StingTools Phase 188 (Tier 3) — push a snapshot of the current HVAC
// panel state to the Planscape server. Used by the "Push to API" button
// on the RPRT tab.
//
// The snapshot is small and JSON-serialisable: a per-kind row count +
// pass/warn/fail tallies + KPI numbers + the verbatim panel rows as a
// payload string. The mobile HVAC dashboard renders the header KPIs
// from the columns and the row tables from PayloadJson.
//
// Five snapshot kinds are pushed in a single batch so the mobile
// dashboard can show a complete picture from one round-trip:
//   - sizing  : duct/pipe sizing-run summary (last MepAutoSize result)
//   - balance : currently empty placeholder until Hardy-Cross writes back
//   - drift   : count of HVC_SIZE_STALE_BOOL == 1 ducts
//   - loads   : space-load grid totals
//   - carbon  : last HvacCarbonReport totals

using System;
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
    public class HvacPushSnapshotCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }

                var panel = StingHvacPanel.Instance;
                if (panel == null)
                {
                    TaskDialog.Show("STING HVAC — Push",
                        "HVAC panel is not open. Open it first so there's a snapshot to push.");
                    return Result.Cancelled;
                }

                var client = PlanscapeServerClient.Instance;
                if (client == null || !client.IsConnected)
                {
                    TaskDialog.Show("STING HVAC — Push",
                        "Not authenticated with the Planscape server. " +
                        "Use BIM Coordination Center → Platform → Login first.");
                    return Result.Cancelled;
                }

                // Resolve project id — same flow as other server pushes.
                Guid projectId;
                try
                {
                    string projectCode = ParameterHelpers.GetString(
                        ctx.Doc.ProjectInformation, "PRJ_ORG_PROJECT_CODE_TXT");
                    string projectName = ctx.Doc.ProjectInformation?.Name ?? ctx.Doc.Title;
                    projectId = Task.Run(() =>
                        client.GetOrCreateProjectAsync(projectName ?? "Unknown", projectCode ?? "")).Result;
                }
                catch (Exception exP)
                {
                    StingLog.Error("HvacPushSnapshot project resolve", exP);
                    message = exP.Message;
                    return Result.Failed;
                }

                int totalPushed = 0;
                int totalFailed = 0;

                // ── Drift snapshot ───────────────────────────────────────
                try
                {
                    int driftCount = panel.DriftRows.Count;
                    int totalDucts = new FilteredElementCollector(ctx.Doc)
                        .OfCategory(BuiltInCategory.OST_DuctCurves)
                        .WhereElementIsNotElementType().GetElementCount();
                    var driftBody = new JObject
                    {
                        ["kind"]       = "drift",
                        ["inspected"]  = totalDucts,
                        ["pass"]       = Math.Max(0, totalDucts - driftCount),
                        ["warn"]       = driftCount,
                        ["fail"]       = 0,
                        ["totalKw"]    = 0.0,
                        ["worstValue"] = driftCount,
                        ["rag"]        = driftCount == 0 ? "G" : (driftCount > totalDucts / 10 ? "R" : "A"),
                        ["payloadJson"]= JArray.FromObject(panel.DriftRows).ToString(Newtonsoft.Json.Formatting.None)
                    };
                    var id = Task.Run(() => client.PushHvacSnapshotAsync(projectId, driftBody)).Result;
                    if (id.HasValue) totalPushed++; else totalFailed++;
                }
                catch (Exception ex) { StingLog.Warn($"Drift snapshot: {ex.Message}"); totalFailed++; }

                // ── Loads snapshot ───────────────────────────────────────
                try
                {
                    double heatSum = panel.SpaceLoadRows.Sum(r => r.HeatingKw);
                    double coolSum = panel.SpaceLoadRows.Sum(r => r.CoolingKw);
                    int    spaces  = panel.SpaceLoadRows.Count;
                    int    warnNoLoad = panel.SpaceLoadRows.Count(r => r.Warning != "");
                    var loadsBody = new JObject
                    {
                        ["kind"]      = "loads",
                        ["inspected"] = spaces,
                        ["pass"]      = spaces - warnNoLoad,
                        ["warn"]      = warnNoLoad,
                        ["fail"]      = 0,
                        ["totalKw"]   = heatSum + coolSum,
                        ["worstValue"]= Math.Max(heatSum, coolSum),
                        ["rag"]       = warnNoLoad == 0 ? "G" : (warnNoLoad > spaces / 4 ? "R" : "A"),
                        ["payloadJson"]= JArray.FromObject(panel.SpaceLoadRows).ToString(Newtonsoft.Json.Formatting.None)
                    };
                    var id = Task.Run(() => client.PushHvacSnapshotAsync(projectId, loadsBody)).Result;
                    if (id.HasValue) totalPushed++; else totalFailed++;
                }
                catch (Exception ex) { StingLog.Warn($"Loads snapshot: {ex.Message}"); totalFailed++; }

                // ── Sizing summary ───────────────────────────────────────
                try
                {
                    int eqCount  = panel.EquipmentRows.Count;
                    int eqWarn   = panel.EquipmentRows.Count(r => r.StatusDot == "⬡");
                    int eqFail   = panel.EquipmentRows.Count(r => r.StatusDot == "✗");
                    double totKw = panel.EquipmentRows.Sum(r => r.CapacityKw);
                    var sizingBody = new JObject
                    {
                        ["kind"]      = "sizing",
                        ["inspected"] = eqCount,
                        ["pass"]      = eqCount - eqWarn - eqFail,
                        ["warn"]      = eqWarn,
                        ["fail"]      = eqFail,
                        ["totalKw"]   = totKw,
                        ["worstValue"]= 0.0,
                        ["rag"]       = eqFail > 0 ? "R" : (eqWarn > 0 ? "A" : "G"),
                        ["payloadJson"]= JArray.FromObject(panel.EquipmentRows).ToString(Newtonsoft.Json.Formatting.None)
                    };
                    var id = Task.Run(() => client.PushHvacSnapshotAsync(projectId, sizingBody)).Result;
                    if (id.HasValue) totalPushed++; else totalFailed++;
                }
                catch (Exception ex) { StingLog.Warn($"Sizing snapshot: {ex.Message}"); totalFailed++; }

                var td = new TaskDialog("STING HVAC — Push to API")
                {
                    MainInstruction = $"Pushed {totalPushed} snapshot(s) to server",
                    MainContent = totalFailed > 0
                        ? $"{totalFailed} push(es) failed — see StingLog. " +
                          "Snapshots that landed are visible in the mobile HVAC dashboard."
                        : "Mobile HVAC dashboard will refresh on next pull-to-refresh.",
                    CommonButtons = TaskDialogCommonButtons.Close
                };
                td.Show();

                panel.PushRunRow($"Push to API ({totalPushed} snapshots)",
                    totalFailed == 0 ? "⬤" : "⬡");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacPushSnapshotCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
