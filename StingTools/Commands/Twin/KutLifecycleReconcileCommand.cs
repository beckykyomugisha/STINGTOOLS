// KutLifecycleReconcileCommand.cs — Phase D (KUT lifecycle integration).
//
// Joins the four ledgers that already key off the same Revit element —
// SpecLink/CSI (specified) · BOQ (measured/priced) · Fohlio (procured) ·
// Niagara (commissioned) — into one asset register and surfaces the two
// commissioning-side gaps the single-ledger reconciles cannot see:
//
//   PRICED_NO_BMS_POINT   a priced MEP/equipment line whose element has no
//                         BMS device id / endpoint (a handover gap), scoped to
//                         monitorable categories only.
//   COMMISSIONED_UNPRICED a Niagara station point with no priced BOQ line
//                         (an asset-register gap).
//
// Read-only: BuildBOQDocument is the same path the BOQ audits use; the BMS
// device source is IoTDeviceRegistry. The station export is optional — without
// it the register + PRICED_NO_BMS_POINT still produce; COMMISSIONED_UNPRICED
// needs the station file.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.BOQ;
using StingTools.Core;
using StingTools.Core.Twin;
using StingTools.ExLink;

namespace StingTools.Commands.Twin
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class KutLifecycleReconcileCommand : IExternalCommand
    {
        // Categories that typically carry a BMS / IoT point — the scope for the
        // PRICED_NO_BMS_POINT handover gap.
        private static readonly HashSet<string> Monitorable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Mechanical Equipment", "Electrical Equipment", "Lighting Fixtures", "Lighting Devices",
            "Air Terminals", "Duct Accessory", "Plumbing Fixtures", "Fire Alarm Devices",
            "Security Devices", "Communication Devices", "Data Devices", "Nurse Call Devices", "Sprinklers"
        };

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            BOQDocument boq;
            try { boq = BOQCostManager.BuildBOQDocument(doc); }
            catch (Exception ex) { StingLog.Error("KUT_LifecycleReconcile BOQ", ex); TaskDialog.Show("KUT Lifecycle", "Could not build the BOQ:\n" + ex.Message); return Result.Failed; }
            if (boq == null) { TaskDialog.Show("KUT Lifecycle", "No BOQ document."); return Result.Succeeded; }

            // BMS devices by element.
            var devByElem = new Dictionary<long, IoTDeviceRef>();
            try
            {
                foreach (var d in new IoTDeviceRegistry(doc).All())
                    if (d?.BimElementId != null) devByElem[d.BimElementId.Value] = d;
            }
            catch (Exception ex) { StingLog.Warn("KUT_LifecycleReconcile devices: " + ex.Message); }

            // Optional Niagara station export (drives COMMISSIONED_UNPRICED).
            HashSet<string> stationIds = null;
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Optional: select the Niagara / BACnet station export (Cancel to skip the commissioned-unpriced check)",
                Filter = "Station export (*.csv;*.xlsx)|*.csv;*.xlsx",
                InitialDirectory = OutputLocationHelper.GetOutputDirectory(doc)
            };
            if (dlg.ShowDialog() == true)
            {
                try { stationIds = ReadIds(dlg.FileName); }
                catch (Exception ex) { StingLog.Warn("KUT station read: " + ex.Message); }
            }

            var fmap = FohlioMap.Cached(doc);

            // Build the unified register, one row per modelled BOQ asset.
            var register = new List<AssetRow>();
            var pricedDeviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pricedNoBms = new List<AssetRow>();
            double pricedNoBmsUgx = 0;

            foreach (var it in boq.AllItems)
            {
                if (it == null || it.RevitElementId < 0) continue;   // manual/PS-only rows have no element
                var el = doc.GetElement(new ElementId(it.RevitElementId));
                string cat = it.Category ?? (el != null ? ParameterHelpers.GetCategoryName(el) : "");
                bool priced = it.TotalUGX > 0;
                bool ffe = fmap != null && fmap.IsFfeCategory(cat);
                bool monitorable = Monitorable.Contains(cat ?? "");

                devByElem.TryGetValue(it.RevitElementId, out var dev);
                string devId = dev?.DeviceId ?? "";
                string endpoint = dev?.EndpointAddress ?? "";
                string fref = el != null ? ParameterHelpers.GetString(el, ParamRegistry.FOHLIO_REF) : "";

                if (priced && !string.IsNullOrEmpty(devId)) pricedDeviceIds.Add(devId);

                var row = new AssetRow
                {
                    ElementId = it.RevitElementId,
                    Item = it.ItemName ?? "",
                    Category = cat ?? "",
                    CsiSection = it.CsiSection ?? "",
                    Nrm2 = it.NRM2Section ?? "",
                    AmountUgx = it.TotalUGX,
                    FohlioRef = fref ?? "",
                    BmsDevice = devId,
                    BmsEndpoint = endpoint,
                    Spec = string.IsNullOrEmpty(it.CsiSection) ? "✗" : "✓",
                    Boq = priced ? "✓" : "✗",
                    Fohlio = !ffe ? "—" : (string.IsNullOrEmpty(fref) ? "✗" : "✓"),
                    Bms = !monitorable ? "—"
                        : !string.IsNullOrEmpty(devId) && !string.IsNullOrEmpty(endpoint) ? "✓"
                        : !string.IsNullOrEmpty(devId) ? "⚠" : "✗",
                };
                register.Add(row);

                // PRICED_NO_BMS_POINT — priced, monitorable, missing device id or endpoint.
                if (priced && monitorable && (string.IsNullOrEmpty(devId) || string.IsNullOrEmpty(endpoint)))
                {
                    pricedNoBms.Add(row);
                    pricedNoBmsUgx += it.TotalUGX;
                }
            }

            // COMMISSIONED_UNPRICED — station points with no priced BOQ line.
            var commissionedUnpriced = stationIds == null
                ? new List<string>()
                : stationIds.Where(s => !pricedDeviceIds.Contains(s)).OrderBy(s => s).ToList();

            string xlsx = WriteRegister(doc, register, pricedNoBms, commissionedUnpriced, stationIds != null);

            var sb = new StringBuilder();
            sb.AppendLine($"Assets (modelled BOQ lines): {register.Count}");
            sb.AppendLine($"   Spec ✓ {register.Count(r => r.Spec == "✓")} · BOQ ✓ {register.Count(r => r.Boq == "✓")} · " +
                          $"Fohlio ✓ {register.Count(r => r.Fohlio == "✓")} · BMS ✓ {register.Count(r => r.Bms == "✓")}");
            sb.AppendLine();
            sb.AppendLine($"PRICED_NO_BMS_POINT:    {pricedNoBms.Count}  (UGX {pricedNoBmsUgx:N0} priced, no/incomplete BMS point)");
            sb.AppendLine(stationIds == null
                ? "COMMISSIONED_UNPRICED:  (skipped — no station export selected)"
                : $"COMMISSIONED_UNPRICED:  {commissionedUnpriced.Count}  (station point, no priced BOQ line)");
            if (pricedNoBms.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Top priced-no-BMS:");
                foreach (var r in pricedNoBms.OrderByDescending(r => r.AmountUgx).Take(8))
                    sb.AppendLine($"   {r.ElementId} {r.Category,-22} UGX {r.AmountUgx:N0}");
            }
            if (xlsx != null) { sb.AppendLine(); sb.AppendLine("Register: " + xlsx); }

            new TaskDialog("KUT Lifecycle Reconcile")
            {
                MainInstruction = $"{register.Count} assets · {pricedNoBms.Count} priced-no-BMS · {commissionedUnpriced.Count} commissioned-unpriced",
                MainContent = sb.ToString()
            }.Show();
            StingLog.Info($"KUT_LifecycleReconcile: assets={register.Count} pricedNoBms={pricedNoBms.Count}(UGX {pricedNoBmsUgx:N0}) " +
                $"commissionedUnpriced={commissionedUnpriced.Count}");
            return Result.Succeeded;
        }

        private sealed class AssetRow
        {
            public long ElementId;
            public string Item, Category, CsiSection, Nrm2, FohlioRef, BmsDevice, BmsEndpoint;
            public double AmountUgx;
            public string Spec, Boq, Fohlio, Bms;
        }

        private static string WriteRegister(Document doc, List<AssetRow> register, List<AssetRow> pricedNoBms,
            List<string> commissionedUnpriced, bool stationProvided)
        {
            try
            {
                using var wb = new XLWorkbook();

                var ws = wb.AddWorksheet("Lifecycle Register");
                string[] hdr = { "Element", "Category", "Item", "CSI section", "NRM2 §", "Amount UGX",
                    "Fohlio ref", "BMS device", "BMS endpoint", "Spec", "BOQ", "Fohlio", "BMS" };
                for (int c = 0; c < hdr.Length; c++) { ws.Cell(1, c + 1).Value = hdr[c]; ws.Cell(1, c + 1).Style.Font.Bold = true; }
                int row = 2;
                foreach (var r in register.OrderBy(r => r.Category).ThenByDescending(r => r.AmountUgx))
                {
                    ws.Cell(row, 1).Value = r.ElementId;
                    ws.Cell(row, 2).Value = r.Category;
                    ws.Cell(row, 3).Value = r.Item;
                    ws.Cell(row, 4).Value = r.CsiSection;
                    ws.Cell(row, 5).Value = r.Nrm2;
                    ws.Cell(row, 6).Value = r.AmountUgx; ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0";
                    ws.Cell(row, 7).Value = r.FohlioRef;
                    ws.Cell(row, 8).Value = r.BmsDevice;
                    ws.Cell(row, 9).Value = r.BmsEndpoint;
                    ws.Cell(row, 10).Value = r.Spec;
                    ws.Cell(row, 11).Value = r.Boq;
                    ws.Cell(row, 12).Value = r.Fohlio;
                    ws.Cell(row, 13).Value = r.Bms;
                    row++;
                }
                ws.SheetView.FreezeRows(1);
                ws.Columns().AdjustToContents();

                var gw = wb.AddWorksheet("Gaps");
                string[] ghdr = { "Type", "Element / Point", "Category", "Amount UGX", "Note" };
                for (int c = 0; c < ghdr.Length; c++) { gw.Cell(1, c + 1).Value = ghdr[c]; gw.Cell(1, c + 1).Style.Font.Bold = true; }
                int gr = 2;
                foreach (var r in pricedNoBms.OrderByDescending(r => r.AmountUgx))
                {
                    gw.Cell(gr, 1).Value = "PRICED_NO_BMS_POINT";
                    gw.Cell(gr, 2).Value = r.ElementId;
                    gw.Cell(gr, 3).Value = r.Category;
                    gw.Cell(gr, 4).Value = r.AmountUgx; gw.Cell(gr, 4).Style.NumberFormat.Format = "#,##0";
                    gw.Cell(gr, 5).Value = string.IsNullOrEmpty(r.BmsDevice) ? "No BMS device id" : "BMS device has no endpoint";
                    gr++;
                }
                if (stationProvided)
                    foreach (var s in commissionedUnpriced)
                    {
                        gw.Cell(gr, 1).Value = "COMMISSIONED_UNPRICED";
                        gw.Cell(gr, 2).Value = s;
                        gw.Cell(gr, 5).Value = "Station point with no priced BOQ line";
                        gr++;
                    }
                gw.Columns().AdjustToContents();

                string path = OutputLocationHelper.GetOutputPath(doc, $"STING_KUT_Lifecycle_Register_{DateTime.Now:yyyyMMdd}.xlsx");
                wb.SaveAs(path);
                return path;
            }
            catch (Exception ex) { StingLog.Warn("KUT lifecycle register: " + ex.Message); return null; }
        }

        private static HashSet<string> ReadIds(string path)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var idHeaders = new[] { "deviceid", "device", "point", "object", "name", "id" };

            if (path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                using var wb = new XLWorkbook(path);
                var ws = wb.Worksheets.First();
                var used = ws.RangeUsed();
                if (used == null) return ids;
                int fr = used.FirstRow().RowNumber(), lr = used.LastRow().RowNumber();
                int fc = used.FirstColumn().ColumnNumber(), lc = used.LastColumn().ColumnNumber();
                var hdr = new List<string>();
                for (int c = fc; c <= lc; c++) hdr.Add(ws.Cell(fr, c).GetString().Trim().ToLowerInvariant());
                int col = FindCol(hdr, idHeaders);
                if (col < 0) return ids;
                for (int r = fr + 1; r <= lr; r++)
                {
                    string v = ws.Cell(r, fc + col).GetString().Trim();
                    if (v.Length > 0) ids.Add(v);
                }
            }
            else
            {
                var lines = File.ReadAllLines(path);
                if (lines.Length < 2) return ids;
                var hdr = StingToolsApp.ParseCsvLine(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();
                int col = FindCol(hdr, idHeaders);
                if (col < 0) return ids;
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var f = StingToolsApp.ParseCsvLine(lines[i]);
                    if (col < f.Length && !string.IsNullOrWhiteSpace(f[col])) ids.Add(f[col].Trim());
                }
            }
            return ids;
        }

        private static int FindCol(List<string> hdr, string[] cands)
        {
            foreach (var cand in cands)
                for (int i = 0; i < hdr.Count; i++)
                    if (!string.IsNullOrEmpty(hdr[i]) && hdr[i].Contains(cand)) return i;
            return -1;
        }
    }
}
