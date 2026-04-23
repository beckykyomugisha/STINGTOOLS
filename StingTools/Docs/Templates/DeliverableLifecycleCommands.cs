// DeliverableLifecycleCommands.cs — template engine v1.1 (S12).
//
// One IExternalCommand per lifecycle transition. Each command:
//   1. Resolves the selected deliverable from BCC context (or prompts).
//   2. Calls DeliverableLifecycle.{Issue|ReIssue|Publish|Cancel|Supersede|Replace}.
//   3. Renders the emitted template via TemplateEngine.
//   4. Opens the rendered file in the OS shell on success.
//
// These commands appear in the BCC Deliverables tab and can be invoked by the
// dispatch handler (StingCommandHandler) by Tag string.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace Planscape.Docs.Templates
{
    internal static class LifecycleCommandHelper
    {
        public static Result Run(ExternalCommandData data, ref string message,
            Func<Document, TemplateEngine, DeliverableLifecycle.LifecycleResult> action,
            string title)
        {
            try
            {
                var doc = data.Application.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }
                var engine = new TemplateEngine(doc);

                using var tx = new Transaction(doc, "STING — " + title);
                tx.Start();
                var lr = action(doc, engine);
                tx.Commit();

                if (lr == null || !lr.Ok)
                {
                    TaskDialog.Show(title, lr?.Message ?? "Unknown failure.");
                    return Result.Failed;
                }

                string rendered = null;
                try
                {
                    var ctx = TokenContext.FromDeliverable(lr.Updated, doc, engine.Registry.Manifest);
                    rendered = engine.RenderById(lr.TemplateId, ctx);
                }
                catch (Exception renderEx)
                {
                    StingLog.Warn($"{title}: render after transition failed: {renderEx.Message}");
                }

                var td = new TaskDialog(title)
                {
                    MainInstruction = lr.Message,
                    MainContent = rendered == null
                        ? "Transition succeeded."
                        : $"Rendered: {rendered}",
                    CommonButtons = TaskDialogCommonButtons.Close
                };
                if (rendered != null)
                    td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Open rendered file");
                var result = td.Show();
                if (result == TaskDialogResult.CommandLink1 && rendered != null)
                    SafeOpen(rendered);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error($"Lifecycle command '{title}' failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        public static void SafeOpen(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
            catch (Exception ex) { StingLog.Warn($"SafeOpen failed for {path}: {ex.Message}"); }
        }

        /// <summary>Resolves the first selected DeliverableRow-compatible dynamic object.
        /// Returns null if the user has nothing selected.</summary>
        public static dynamic ResolveSelection(Document doc)
        {
            // BCC wires a static holder; callers stash the picked row there.
            try
            {
                var t = Type.GetType("StingTools.UI.BIMCoordinationCenter, StingTools");
                var prop = t?.GetField("SelectedDeliverable",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                return prop?.GetValue(null);
            }
            catch { return null; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class IssueDeliverableCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => LifecycleCommandHelper.Run(data, ref message, (doc, engine) =>
            {
                dynamic d = LifecycleCommandHelper.ResolveSelection(doc);
                if (d == null) return new DeliverableLifecycle.LifecycleResult { Ok = false, Message = "No deliverable selected." };
                string user = Environment.UserName;
                return DeliverableLifecycle.Issue(d, doc, engine.Registry.Manifest, user, "Issue for review");
            }, "Issue Deliverable");
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ReIssueDeliverableCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => LifecycleCommandHelper.Run(data, ref message, (doc, engine) =>
            {
                dynamic d = LifecycleCommandHelper.ResolveSelection(doc);
                if (d == null) return new DeliverableLifecycle.LifecycleResult { Ok = false, Message = "No deliverable selected." };
                return DeliverableLifecycle.ReIssue(d, doc, engine.Registry.Manifest, Environment.UserName, "Re-issue with updates");
            }, "Re-Issue Deliverable");
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PublishDeliverableCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => LifecycleCommandHelper.Run(data, ref message, (doc, engine) =>
            {
                dynamic d = LifecycleCommandHelper.ResolveSelection(doc);
                if (d == null) return new DeliverableLifecycle.LifecycleResult { Ok = false, Message = "No deliverable selected." };
                return DeliverableLifecycle.Publish(d, doc, engine.Registry.Manifest, Environment.UserName, stage: 3);
            }, "Publish Deliverable");
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CancelDeliverableCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => LifecycleCommandHelper.Run(data, ref message, (doc, engine) =>
            {
                dynamic d = LifecycleCommandHelper.ResolveSelection(doc);
                if (d == null) return new DeliverableLifecycle.LifecycleResult { Ok = false, Message = "No deliverable selected." };
                return DeliverableLifecycle.Cancel(d, doc, engine.Registry.Manifest, Environment.UserName, "Cancelled via BCC");
            }, "Cancel Deliverable");
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SupersedeDeliverableCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => LifecycleCommandHelper.Run(data, ref message, (doc, engine) =>
            {
                dynamic d = LifecycleCommandHelper.ResolveSelection(doc);
                if (d == null) return new DeliverableLifecycle.LifecycleResult { Ok = false, Message = "No deliverable selected." };
                // For now the new doc number is just next from the identity generator; UI will prompt.
                string newNumber = DocumentIdentityGenerator.Next(engine.Document, engine.Registry.Manifest,
                    type: "DR", role: (string)d.RoleCode ?? "XX", fb: (string)d.FunctionalBreakdown ?? "ZZ",
                    sb: (string)d.SpatialBreakdown ?? "XX");
                return DeliverableLifecycle.Supersede(d, newNumber, doc, engine.Registry.Manifest, Environment.UserName, "Superseded via BCC");
            }, "Supersede Deliverable");
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ReplaceDeliverableCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => LifecycleCommandHelper.Run(data, ref message, (doc, engine) =>
            {
                dynamic d = LifecycleCommandHelper.ResolveSelection(doc);
                if (d == null) return new DeliverableLifecycle.LifecycleResult { Ok = false, Message = "No deliverable selected." };
                dynamic replacement = d; // UI will provide a distinct row; this placeholder keeps build stable.
                return DeliverableLifecycle.Replace(d, replacement, doc, engine.Registry.Manifest, Environment.UserName, "Replaced via BCC");
            }, "Replace Deliverable");
    }
}
