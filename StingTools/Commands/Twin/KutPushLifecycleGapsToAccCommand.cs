// KutPushLifecycleGapsToAccCommand.cs — Phase G (KUT lifecycle integration).
//
// Pushes the model-derivable four-ledger gaps as ACC Issues so the coordination
// team actions cost/spec/commissioning gaps in ACC alongside clash issues. Reuses
// the LIVE ACC client (StingTools.V6.AccIssueSync) — same 3-legged OAuth, 429
// back-off and credentials file as AccPullClashesCommand. Idempotent via a sidecar
// keyed on a stable gap signature, mirroring pushed_clashes.json.
//
// Scope: the two gaps computable from the model alone —
//   PRICED_UNSPECIFIED   a priced BOQ line with no CSI section (spec gap).
//   PRICED_NO_BMS_POINT  a priced monitorable asset with no BMS device/endpoint.
// The file-dependent gaps (SPECIFIED_UNPRICED needs the SpecLink ToC,
// COMMISSIONED_UNPRICED needs the Niagara station export) stay with their
// reconcile commands' XLSX outputs — a future enhancement can push those too.
//
// Read-only on the Revit side: the issue push is network, not a model transaction.
// Network code — verify against a live ACC project before relying on it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.BOQ;
using StingTools.Core;
using StingTools.Core.Twin;
using StingTools.V6;

namespace StingTools.Commands.Twin
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class KutPushLifecycleGapsToAccCommand : IExternalCommand
    {
        // Categories that typically carry a BMS / IoT point — the scope for the
        // PRICED_NO_BMS_POINT gap (mirrors KutLifecycleReconcileCommand.Monitorable).
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

            // 1. Credentials (same gate as AccPullClashesCommand).
            var creds = AccIssueSync.LoadCredentials();
            if (string.IsNullOrEmpty(creds.ClientId) || string.IsNullOrEmpty(creds.RefreshToken) ||
                string.IsNullOrEmpty(creds.ProjectId))
            {
                TaskDialog.Show("KUT — Push Lifecycle Gaps to ACC",
                    "ACC credentials are not configured.\n\n" +
                    "Create %APPDATA%\\Planscape\\acc_credentials.json with at least:\n" +
                    "  ClientId, ClientSecret, RefreshToken, ProjectId\n" +
                    "(set IssueTypeId so ACC accepts the issues).");
                return Result.Cancelled;
            }

            // 2. Build the gap set from the model.
            BOQDocument boq;
            try { boq = BOQCostManager.BuildBOQDocument(doc); }
            catch (Exception ex) { StingLog.Error("KUT_PushGapsToAcc BOQ", ex); TaskDialog.Show("KUT — Push Gaps", "Could not build the BOQ:\n" + ex.Message); return Result.Failed; }
            if (boq == null) { TaskDialog.Show("KUT — Push Gaps", "No BOQ document."); return Result.Succeeded; }

            var devByElem = new Dictionary<long, IoTDeviceRef>();
            try
            {
                foreach (var d in new IoTDeviceRegistry(doc).All())
                    if (d?.BimElementId != null) devByElem[d.BimElementId.Value] = d;
            }
            catch (Exception ex) { StingLog.Warn("KUT_PushGapsToAcc devices: " + ex.Message); }

            double valueFloor = TagConfig.GetConfigDouble("KUT_ACC_GAP_VALUE_FLOOR", 0.0);

            var gaps = new List<GapIssue>();
            foreach (var it in boq.AllItems)
            {
                if (it == null || it.RevitElementId < 0 || it.TotalUGX <= 0) continue; // priced model rows only
                string cat = it.Category ?? "";
                string room = LevelLoc(it);

                // PRICED_UNSPECIFIED — priced, no CSI section.
                if (string.IsNullOrEmpty(it.CsiSection))
                    gaps.Add(new GapIssue
                    {
                        Type = "PRICED_UNSPECIFIED",
                        ElementId = it.RevitElementId,
                        Category = cat,
                        AmountUgx = it.TotalUGX,
                        Title = $"PRICED_UNSPECIFIED: {cat} (UGX {it.TotalUGX:N0}) has no specification",
                        Body = $"Element {it.RevitElementId} '{it.ItemName}' ({cat}) is priced at UGX {it.TotalUGX:N0} " +
                               $"(NRM2 §{it.NRM2Section}) but carries no CSI section. Assign a spec or confirm scope.{room}"
                    });

                // PRICED_NO_BMS_POINT — priced monitorable asset with no BMS endpoint.
                if (Monitorable.Contains(cat) && it.TotalUGX >= valueFloor)
                {
                    devByElem.TryGetValue(it.RevitElementId, out var dev);
                    bool noPoint = dev == null || string.IsNullOrEmpty(dev.DeviceId) || string.IsNullOrEmpty(dev.EndpointAddress);
                    if (noPoint)
                        gaps.Add(new GapIssue
                        {
                            Type = "PRICED_NO_BMS_POINT",
                            ElementId = it.RevitElementId,
                            Category = cat,
                            AmountUgx = it.TotalUGX,
                            Title = $"PRICED_NO_BMS_POINT: {cat} (UGX {it.TotalUGX:N0}) has no BMS point",
                            Body = $"Element {it.RevitElementId} '{it.ItemName}' ({cat}, UGX {it.TotalUGX:N0}) is a priced " +
                                   $"monitorable asset with no BMS device id / endpoint — a commissioning/handover gap. " +
                                   $"Tag ICT_HEALTHIOT_DEVICE_ID_TXT + _ENDPOINT_TXT, then re-run.{room}"
                        });
                }
            }

            if (gaps.Count == 0)
            {
                TaskDialog.Show("KUT — Push Lifecycle Gaps to ACC",
                    "No model-derivable lifecycle gaps found (every priced line has a CSI section, " +
                    "and every priced monitorable asset has a BMS point). Nothing to push.");
                return Result.Succeeded;
            }

            // 3. Idempotency — skip gaps already pushed.
            string sidecar = SidecarPath(doc);
            var pushed = LoadSidecar(sidecar);   // signature → issue id

            int created = 0, skipped = 0, failed = 0;
            foreach (var g in gaps)
            {
                string sig = $"{g.Type}:{g.ElementId}";
                if (pushed.ContainsKey(sig)) { skipped++; continue; }

                var issue = new AccIssue { Title = g.Title, Description = g.Body, Status = "open" };
                string id;
                try { id = AccIssueSync.PushIssueAsync(creds, issue).GetAwaiter().GetResult(); }
                catch (Exception ex) { StingLog.Warn($"KUT_PushGapsToAcc push {sig}: {ex.Message}"); id = null; }

                if (!string.IsNullOrEmpty(id)) { pushed[sig] = id; created++; }
                else failed++;
            }

            SaveSidecar(sidecar, pushed);

            var sb = new StringBuilder();
            sb.AppendLine($"Gaps found: {gaps.Count}  " +
                          $"({gaps.Count(g => g.Type == "PRICED_UNSPECIFIED")} priced-unspecified, " +
                          $"{gaps.Count(g => g.Type == "PRICED_NO_BMS_POINT")} priced-no-BMS)");
            sb.AppendLine();
            sb.AppendLine($"ACC issues created:  {created}");
            sb.AppendLine($"Already pushed (skipped): {skipped}");
            if (failed > 0) sb.AppendLine($"Failed (will retry next run): {failed}");
            sb.AppendLine();
            sb.AppendLine("Re-runs are idempotent (sidecar: _BIM_COORD/acc/pushed_lifecycle_gaps.json).");

            new TaskDialog("KUT — Push Lifecycle Gaps to ACC")
            {
                MainInstruction = $"{created} issue(s) created · {skipped} skipped · {failed} failed",
                MainContent = sb.ToString()
            }.Show();
            StingLog.Info($"KUT_PushLifecycleGapsToAcc: found={gaps.Count} created={created} skipped={skipped} failed={failed}");
            return Result.Succeeded;
        }

        private static string LevelLoc(BOQLineItem it)
        {
            string s = string.Join(" · ", new[] { it.Level, it.Location }.Where(x => !string.IsNullOrEmpty(x)));
            return string.IsNullOrEmpty(s) ? "" : $" [{s}]";
        }

        private sealed class GapIssue
        {
            public string Type, Category, Title, Body;
            public long ElementId;
            public double AmountUgx;
        }

        // ── Idempotency sidecar — <project>/_BIM_COORD/acc/pushed_lifecycle_gaps.json ──

        private static string SidecarPath(Document doc)
        {
            string dir;
            try { dir = Path.GetDirectoryName(doc?.PathName ?? ""); } catch { dir = null; }
            if (string.IsNullOrEmpty(dir)) dir = OutputLocationHelper.GetOutputDirectory(doc);
            string accDir = Path.Combine(dir ?? ".", "_BIM_COORD", "acc");
            try { Directory.CreateDirectory(accDir); } catch { }
            return Path.Combine(accDir, "pushed_lifecycle_gaps.json");
        }

        private static Dictionary<string, string> LoadSidecar(string path)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (!File.Exists(path)) return map;
                var jo = JObject.Parse(File.ReadAllText(path));
                foreach (var p in jo.Properties()) map[p.Name] = (string)p.Value;
            }
            catch (Exception ex) { StingLog.Warn("KUT_PushGapsToAcc sidecar load: " + ex.Message); }
            return map;
        }

        private static void SaveSidecar(string path, Dictionary<string, string> map)
        {
            try
            {
                var jo = new JObject();
                foreach (var kv in map) jo[kv.Key] = kv.Value;
                File.WriteAllText(path, jo.ToString(Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn("KUT_PushGapsToAcc sidecar save: " + ex.Message); }
        }
    }
}
