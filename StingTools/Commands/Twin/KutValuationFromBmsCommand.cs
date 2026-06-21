// ══════════════════════════════════════════════════════════════════════════
//  KutValuationFromBmsCommand.cs — Phase H3 (KUT lifecycle, max automation).
//
//  KUT_ValuationFromBms (read-only). Joins the priced, monitorable BOQ scope to
//  the live Niagara station: an asset whose BMS point is online + reporting a
//  value is installed and communicating, hence certifiable as commissioned. The
//  result is a commissioning-valuation % over the monitorable scope — the 5D
//  signal that feeds the Phase 191 PaymentCert engine.
//
//  NETWORK CODE — needs a reachable Niagara station configured at
//  <project>/_BIM_COORD/niagara_connection.json. Not exercised in the dev sandbox
//  (no station); built clean against the documented APIs. Verify live before use.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.BOQ;
using StingTools.Core;
using StingTools.Core.Twin;

namespace StingTools.Commands.Twin
{
    [Transaction(TransactionMode.ReadOnly)]
    public class KutValuationFromBmsCommand : IExternalCommand
    {
        // Same monitorable scope as the lifecycle reconcile (PRICED_NO_BMS_POINT).
        private static readonly HashSet<string> Monitorable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Mechanical Equipment", "Electrical Equipment", "Lighting Fixtures", "Lighting Devices",
            "Air Terminals", "Duct Accessory", "Plumbing Fixtures", "Fire Alarm Devices",
            "Security Devices", "Communication Devices", "Data Devices", "Nurse Call Devices", "Sprinklers"
        };

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx?.Doc == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            string dir = Path.GetDirectoryName(doc.PathName ?? "");
            var conn = NiagaraConnection.Load(dir);
            if (conn == null || string.IsNullOrEmpty(conn.BaseUrl))
            {
                TaskDialog.Show("BMS valuation",
                    "No Niagara station configured.\n\nCreate <project>/_BIM_COORD/niagara_connection.json " +
                    "with { \"baseUrl\": \"http://station:port\", \"pointsPath\": \"/obix/...\" } " +
                    "(and apiKey or username/password). This file is gitignored — never commit station credentials.");
                return Result.Cancelled;
            }

            var points = NiagaraJsonClient.FetchPoints(conn);
            if (points == null)
            {
                TaskDialog.Show("BMS valuation",
                    "Could not read live points from the Niagara station (see StingTools.log). " +
                    "Check the URL / credentials / network and retry.");
                return Result.Failed;
            }

            BOQDocument boq;
            try { boq = BOQCostManager.BuildBOQDocument(doc); }
            catch (Exception ex) { StingLog.Error("KUT_ValuationFromBms BOQ", ex); TaskDialog.Show("BMS valuation", "Could not build the BOQ:\n" + ex.Message); return Result.Failed; }
            if (boq == null) { TaskDialog.Show("BMS valuation", "No BOQ document."); return Result.Succeeded; }

            var devByElem = new Dictionary<long, IoTDeviceRef>();
            try
            {
                foreach (var d in new IoTDeviceRegistry(doc).All())
                    if (d?.BimElementId != null) devByElem[d.BimElementId.Value] = d;
            }
            catch (Exception ex) { StingLog.Warn("KUT_ValuationFromBms devices: " + ex.Message); }

            var assets = new List<BmsAsset>();
            foreach (var it in boq.AllItems)
            {
                if (it == null || it.RevitElementId < 0 || it.TotalUGX <= 0) continue;
                if (it.FfeOwnerProcured) continue;                         // owner-procured, not contractor commissioning
                string cat = it.Category ?? "";
                if (!Monitorable.Contains(cat)) continue;

                devByElem.TryGetValue(it.RevitElementId, out var dev);
                string devId = dev?.DeviceId ?? "";
                bool commissioned = false;
                string status = "";
                if (!string.IsNullOrEmpty(devId) && points.TryGetValue(devId, out var pt))
                {
                    status = pt.Status;
                    commissioned = BmsValuation.IsCommissioned(pt.Status, pt.HasValue);
                }
                assets.Add(new BmsAsset
                {
                    Tag = it.ItemName ?? "",
                    DeviceId = devId,
                    ValueUGX = it.TotalUGX,
                    Commissioned = commissioned,
                    Status = status
                });
            }

            var r = BmsValuation.Compute(assets);

            // CSV artefact.
            string csvPath = "";
            try
            {
                string outDir = Path.Combine(OutputLocationHelper.GetOutputDirectory(doc), "twin");
                Directory.CreateDirectory(outDir);
                csvPath = Path.Combine(outDir, "bms_valuation.csv");
                var sb = new StringBuilder();
                sb.AppendLine("Tag,DeviceId,AmountUGX,LiveStatus,Commissioned");
                foreach (var a in assets.OrderByDescending(a => a.ValueUGX))
                    sb.AppendLine($"\"{a.Tag.Replace("\"", "'")}\",{a.DeviceId},{a.ValueUGX:0},{a.Status},{(a.Commissioned ? "yes" : "no")}");
                File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) { StingLog.Warn("KUT_ValuationFromBms CSV: " + ex.Message); }

            string body =
                $"Monitorable priced scope: {r.MonitorableCount} assets, UGX {r.MonitorableValueUGX:N0}\n" +
                $"Live on BMS (commissioned): {r.CommissionedCount} assets, UGX {r.CommissionedValueUGX:N0}\n" +
                $"No BMS point yet: {r.NoPointCount} assets\n\n" +
                $"Commissioning valuation: {r.CommissionedValueFraction:P1} of monitorable value.\n\n" +
                "Feed this % to PayCert_Create for the monitorable scope. " +
                (string.IsNullOrEmpty(csvPath) ? "" : "Per-asset CSV:\n" + csvPath);
            TaskDialog.Show("BMS commissioning valuation", body);
            return Result.Succeeded;
        }
    }
}
