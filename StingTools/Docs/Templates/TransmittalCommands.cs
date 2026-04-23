// TransmittalCommands.cs — template engine v1.1 (S10 + S13).
//
// Public commands that let the ribbon, BCC, and DocumentManagementDialog all
// create a transmittal through the same TransmittalOrchestrator pipeline
// without surgically editing BIMManagerCommands / DocumentManagementDialog
// (both are multi-thousand-line files). Callers populate a
// TransmittalRequest, call Create, and the command does:
//
//   orchestrator → render → persist transmittals.json → start workflow → audit.

using System;
using System.Diagnostics;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace Planscape.Docs.Templates
{
    /// <summary>Thin entry point so DocumentManagementDialog.QuickTransmittal
    /// can delegate to the orchestrator without an IExternalCommand roundtrip.</summary>
    public static class TransmittalCommands
    {
        public static TransmittalResult Create(Document doc, TransmittalRequest req)
            => TransmittalOrchestrator.Create(doc, req);

        public static TransmittalResult CreateWithDistribution(Document doc, TransmittalRequest req, dynamic deliverable)
        {
            var group = Planscape.Docs.Workflow.DistributionGroups.SuggestFor(doc, deliverable);
            if (group != null && (req.Recipients == null || req.Recipients.Count == 0))
            {
                foreach (var m in group.Members)
                {
                    if (string.Equals(m.Delivery, "CC", StringComparison.OrdinalIgnoreCase))
                        req.Cc.Add(m.Email);
                    else
                        req.Recipients.Add(m.Email);
                }
            }
            return TransmittalOrchestrator.Create(doc, req);
        }
    }

    /// <summary>Revit command invocation point — wired via StingCommandHandler.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateTransmittalOrchestratedCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                var doc = data.Application.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var req = new TransmittalRequest
                {
                    Subject = "Transmittal",
                    Reason = "Standard issue",
                    Method = "Email",
                    IssueDate = DateTime.UtcNow,
                    IssuedBy = Environment.UserName,
                    TemplateFamily = "B"
                };

                using var tx = new Transaction(doc, "STING — Create Transmittal");
                tx.Start();
                var result = TransmittalOrchestrator.Create(doc, req);
                tx.Commit();

                if (!result.Ok)
                {
                    TaskDialog.Show("Transmittal", "Failed: " + result.Error);
                    return Result.Failed;
                }

                var td = new TaskDialog("Transmittal created")
                {
                    MainInstruction = result.Record?.Value<string>("id"),
                    MainContent = $"Rendered: {result.DocxPath}\nTemplate: {result.TemplateId}" +
                                  (string.IsNullOrEmpty(result.WorkflowInstanceId) ? "" : $"\nWorkflow instance: {result.WorkflowInstanceId}"),
                    CommonButtons = TaskDialogCommonButtons.Close
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Open rendered file");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Reveal folder");
                var choice = td.Show();
                if (choice == TaskDialogResult.CommandLink1) SafeOpen(result.DocxPath);
                else if (choice == TaskDialogResult.CommandLink2)
                {
                    string dir = Path.GetDirectoryName(result.DocxPath);
                    if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir)) SafeOpen(dir);
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("CreateTransmittalOrchestratedCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static void SafeOpen(string path)
        {
            try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
            catch (Exception ex) { StingLog.Warn($"SafeOpen({path}): {ex.Message}"); }
        }
    }

    /// <summary>Bulk-issue selected deliverables (v1.1 S13). Uses the
    /// DocumentIdentityGenerator.Reserve block to avoid SEQ contention.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BulkIssueDeliverablesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                var doc = data.Application.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                // BCC pushes a selection list into a static holder; when not set we
                // exit early. This intentionally stays decoupled from the UI file.
                // The file DeliverableLifecycleCommands.cs defines several
                // IExternalCommand classes (Issue/ReIssue/Publish/Cancel/…)
                // but no wrapper class of the same name — use one of the
                // concrete commands to grab the assembly reference.
                var holder = typeof(Planscape.Docs.Templates.IssueDeliverableCommand).Assembly
                    .GetType("StingTools.UI.BIMCoordinationCenter");
                var field = holder?.GetField("SelectedDeliverables",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                var selection = field?.GetValue(null) as System.Collections.IEnumerable;
                if (selection == null)
                {
                    TaskDialog.Show("Bulk Issue", "No deliverables selected in BCC.");
                    return Result.Cancelled;
                }

                var engine = new TemplateEngine(doc);
                int ok = 0, fail = 0;
                using (var tx = new Transaction(doc, "STING — Bulk Issue Deliverables"))
                {
                    tx.Start();
                    foreach (dynamic d in selection)
                    {
                        try
                        {
                            var lr = DeliverableLifecycle.Issue(d, doc, engine.Registry.Manifest,
                                Environment.UserName, "Bulk issue");
                            if (lr.Ok) ok++; else fail++;
                        }
                        catch { fail++; }
                    }
                    tx.Commit();
                }
                TaskDialog.Show("Bulk Issue", $"Issued OK: {ok}, failed: {fail}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("BulkIssueDeliverablesCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
