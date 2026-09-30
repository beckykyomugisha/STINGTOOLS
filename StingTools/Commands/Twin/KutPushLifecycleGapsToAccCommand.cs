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
// A2 (2026-10-01): this command creates ACC issues assigned into real queues, so it
// follows the project's ACC operating policy like the clash escalation does:
//   * every message goes through AccPullClashesCommand.Report (logged, not a modal
//     window, on an unattended project);
//   * an unattended run creates NOTHING unless acc_settings.json carries
//     "lifecycleGapEscalation": {"maxCount": n}, and then at most n, largest first;
//   * an interactive run shows the plan and asks before creating anything;
//   * issues are filed under the project's lifecycle issue type ("lifecycleGapEscalation.
//     issueTypeId/issueSubtypeId", else a type NAMED "Lifecycle") — never the clash one;
//   * each created issue is recorded at once (tracking sidecar + acc_issue_origins.json),
//     an auth failure stops the run, and any failure fails the step.
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

        private const string Title = "KUT — Push Lifecycle Gaps to ACC";

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // The project's ACC operating policy decides whether this run may prompt, and how
            // many issues it may create without a person (A2).
            var policy = Core.Clash.AccProjectSettingsFile.LoadFor(doc, "KUT_PushLifecycleGapsToAcc");
            void Say(string text) => Core.Clash.AccPullClashesCommand.Report(policy, Title, text);

            // 1. Credentials (same gate as AccPullClashesCommand).
            var creds = Core.Clash.AccProjectSettingsFile.LoadCredentials(doc, "KUT push lifecycle gaps");   // IM-18: project container ids first
            if (string.IsNullOrEmpty(creds.ClientId) || string.IsNullOrEmpty(creds.RefreshToken) ||
                string.IsNullOrEmpty(creds.ProjectId))
            {
                // TODO(A4 merge): replace this block with
                //   return AccProjectSettingsFile.NotConfigured(policy, creds, Title, "Nothing was sent.", allowPrompt: true);
                // (the shared helper lands on claude/acc-work-review-gaps-7e2ac7; it also names a
                // malformed settings file and fails on it). Until then: same outcome for the
                // unattended / interactive / malformed cases.
                bool malformed = policy.Source == AccPolicySource.Malformed;
                Say((malformed ? "The project ACC settings file could not be read: " + policy.DescribeSource() + "\n\n"
                               : "ACC is not set up for this project on this machine.\n\n") +
                    "BIM Coordination Center > ACC: enter the APS Client ID, 'Sign in with Autodesk', " +
                    "then 'Discover' to choose the ACC project.\n\n" + AccProjectScope.Describe(creds) + ". Nothing was sent.");
                return policy.IsUnattended || malformed ? Result.Failed : Result.Cancelled;
            }

            // 2. Build the gap set from the model.
            BOQDocument boq;
            try { boq = BOQCostManager.BuildBOQDocument(doc); }
            catch (Exception ex) { StingLog.Error("KUT_PushGapsToAcc BOQ", ex); Say("Could not build the BOQ:\n" + ex.Message); return Result.Failed; }
            // No BOQ means nothing was checked - not "no gaps".
            if (boq == null) { Say("No BOQ document could be built, so no gap was checked."); return Result.Failed; }

            var gaps = CollectGaps(doc, boq);
            if (gaps.Count == 0)
            {
                Say("No model-derivable lifecycle gaps found (every priced line has a CSI section, " +
                    "and every priced monitorable asset has a BMS point). Nothing to push.");
                return Result.Succeeded;
            }

            // 3. What STING already raised. Unreadable is NOT empty (A6): reading it as empty
            //    would raise every gap again, so the run refuses and leaves the file alone.
            string sidecar = SidecarPath(doc);
            if (string.IsNullOrEmpty(sidecar))
            {
                Say("The project has not been saved, so there is nowhere to record which gaps were raised - " +
                    "nothing was pushed (a re-run would raise them all again). Save the project first.");
                return Result.Failed;
            }
            string originsPath = Path.Combine(Path.GetDirectoryName(sidecar) ?? string.Empty, AccIssueOrigins.FileName);
            var pushed = AccPushedMap.Load(sidecar, out string pushedErr);
            var origins = AccIssueOrigins.Load(originsPath, out string originsErr);
            if (pushed == null || origins == null)
            {
                string why = pushed == null ? pushedErr : originsErr;
                Say("The record of which gaps STING already raised in ACC could not be read, so every gap would look new. " +
                    "Nothing was pushed and the file was left untouched:\n" + why);
                StingLog.Warn("KUT_PushLifecycleGapsToAcc REFUSED: " + why);
                return Result.Failed;
            }
            origins.Absorb(AccIssueImport.LifecycleGapOrigin, pushed, DateTime.UtcNow);

            // 4. The plan: largest amounts first, capped by the policy.
            var fresh = gaps.Where(g => !pushed.ContainsKey(g.Signature))
                            .OrderByDescending(g => g.AmountUgx).ThenBy(g => g.Signature, StringComparer.Ordinal)
                            .ToList();
            var plan = policy.PlanLifecycleGaps(fresh.Count);
            var toCreate = fresh.Take(plan.Take).ToList();
            StingLog.Info("KUT_PushLifecycleGapsToAcc plan — " + plan.Reason);

            var sb = new StringBuilder();
            sb.AppendLine($"Gaps found: {gaps.Count}  " +
                          $"({gaps.Count(g => g.Type == "PRICED_UNSPECIFIED")} priced-unspecified, " +
                          $"{gaps.Count(g => g.Type == "PRICED_NO_BMS_POINT")} priced-no-BMS)");
            sb.AppendLine($"Already raised in ACC: {gaps.Count - fresh.Count}");
            sb.AppendLine(plan.Reason + ".");

            if (toCreate.Count == 0)
            {
                Say(sb.ToString());
                return Result.Succeeded;
            }

            // Interactive: the plan is shown and a person says yes. Gated by the policy - an
            // unattended plan never sets AskFirst.
            if (plan.AskFirst && policy.MayPrompt)
            {
                var list = string.Join("\n", toCreate.Take(15).Select(g => "  " + g.Title)) +
                           (toCreate.Count > 15 ? $"\n  … and {toCreate.Count - 15} more" : "");
                var ask = new TaskDialog(Title)
                {
                    MainInstruction = $"Create {toCreate.Count} ACC issue(s) for lifecycle gaps?",
                    MainContent = sb + "\n" + list,
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.No,
                    AllowCancellation = true,
                };
                if (ask.Show() != TaskDialogResult.Yes) return Result.Cancelled;
            }

            // 5. The ACC issue type: this purpose's own, never the clash one. Resolved once;
            //    unresolved means NOTHING is created.
            AccFetchResult<string> subtype;
            try
            {
                subtype = AccIssueSync.ResolveNamedIssueTypeAsync(creds, policy.LifecycleGapIssueTypeId,
                    policy.LifecycleGapIssueSubtypeId, AccOperatingPolicy.LifecycleGapIssueTypeName,
                    "lifecycleGapEscalation.issueTypeId / issueSubtypeId in the project's ACC settings").GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                subtype = AccFetchResult<string>.Failure(AccFetchStatus.TransportFailed, "", 0, ex.Message);
            }
            if (!subtype.Succeeded)
            {
                Say(sb + "\nNo issue was created: there is no ACC issue type for lifecycle gaps - " + subtype.Detail);
                StingLog.Warn("KUT_PushLifecycleGapsToAcc: issue type unresolved — " + subtype.Detail);
                return Result.Failed;
            }

            // 6. Create, recording EACH issue as soon as it exists; stop on an auth failure.
            var outcome = AccIssueCreateLoop.Run(toCreate,
                g => g.Signature,
                g => g.Signature,
                g => AccIssueSync.PushIssueDetailedAsync(creds,
                         new AccIssue { Title = g.Title, Description = g.Body, Status = "open", IssueType = subtype.Value })
                     .GetAwaiter().GetResult(),
                pushed,
                m => AccPushedMap.TrySave(sidecar, m, out string e) ? null : e,
                origins,
                AccIssueImport.LifecycleGapOrigin,
                o => o.TrySave(originsPath, out string e) ? null : e,
                DateTime.UtcNow);

            sb.AppendLine();
            sb.AppendLine(outcome.Describe("lifecycle-gap issue"));
            sb.AppendLine();
            sb.AppendLine("Re-runs are idempotent (sidecar: _BIM_COORD/acc/" + AccPushedMap.LifecycleGapFileName + ").");
            foreach (var f in outcome.RecordFailures) StingLog.Error("KUT_PushLifecycleGapsToAcc: issue created but NOT recorded — " + f);
            Say(sb.ToString());
            StingLog.Info($"KUT_PushLifecycleGapsToAcc: found={gaps.Count} created={outcome.Created} skipped={outcome.Skipped} " +
                          $"failed={outcome.Failed} notRecorded={outcome.NotRecorded} stoppedOnAuth={outcome.StoppedOnAuth}");
            return outcome.HasProblems ? Result.Failed : Result.Succeeded;
        }

        private static List<GapIssue> CollectGaps(Document doc, BOQDocument boq)
        {
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
            return gaps;
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
            /// <summary>The idempotency key - unchanged from the original sidecar format.</summary>
            public string Signature => $"{Type}:{ElementId}";
        }

        // ── Idempotency sidecar — <project>/_BIM_COORD/acc/pushed_lifecycle_gaps.json ──

        private static string SidecarPath(Document doc)
        {
            string accDir = StingPaths.Meta(doc, "_BIM_COORD", "acc");
            if (string.IsNullOrEmpty(accDir)) return null;   // unsaved project - no sidecar
            try { Directory.CreateDirectory(accDir); } catch (Exception ex) { StingLog.Warn("KUT_PushGapsToAcc sidecar dir: " + ex.Message); }
            return Path.Combine(accDir, AccPushedMap.LifecycleGapFileName);
        }
    }
}
