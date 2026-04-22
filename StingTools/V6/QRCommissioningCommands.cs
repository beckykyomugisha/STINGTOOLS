using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.V6
{
    /// <summary>N-G16: advance commissioning state for the currently selected element
    /// (or element resolved by pasted unique-id). Operative prompt via TaskDialog.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class QRAdvanceCommissioningCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                var doc = ctx.Doc;

                var selIds = ctx.UIDoc?.Selection?.GetElementIds();
                if (selIds == null || selIds.Count == 0)
                {
                    TaskDialog.Show("STING", "Select the element to advance (or scan its QR into selection first).");
                    return Result.Cancelled;
                }

                // Operative name is resolved from the logged-in Windows user;
                // richer UI (a scan dialog) lives in the dock panel. // TODO-VERIFY-API: replace with QRScanDialog in dock panel.
                string operative = Environment.UserName;

                var results = new List<(string uid, QRCommissioningWorkflow.TransitionResult r)>();
                using (var t = new Transaction(doc, "STING QR Advance Commissioning"))
                {
                    t.Start();
                    foreach (var id in selIds)
                    {
                        var el = doc.GetElement(id);
                        if (el == null) continue;
                        var scan = new QRCommissioningWorkflow.ScanPayload
                        {
                            ElementUniqueId = el.UniqueId,
                            Operative = operative,
                        };
                        results.Add((el.UniqueId, QRCommissioningWorkflow.Advance(doc, scan)));
                    }
                    t.Commit();
                }

                int ok = results.Count(x => x.r.Ok);
                var fail = results.Where(x => !x.r.Ok).Take(5)
                    .Select(x => $"{x.uid}: {x.r.Reason}").ToList();
                string summary = $"Advanced {ok} / {results.Count} elements.";
                if (fail.Any()) summary += "\n\nFirst failures:\n" + string.Join("\n", fail);
                TaskDialog.Show("STING Commissioning", summary);
                StingLog.Info($"QR advance: {ok}/{results.Count} OK");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("QRAdvanceCommissioningCommand failed", ex);
                TaskDialog.Show("STING", $"QR advance failed: {ex.Message}");
                return Result.Failed;
            }
        }
    }

    /// <summary>Generates commissioning progress report (states per category + audit tail).</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class QRCommissioningReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                var doc = ctx.Doc;

                var byState = new Dictionary<string, int>();
                var byCategory = new Dictionary<string, Dictionary<string, int>>();
                foreach (var el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    string st = ParameterHelpers.GetString(el, ParamRegistry.COMM_STATE_TXT);
                    if (string.IsNullOrEmpty(st)) continue;
                    byState[st] = byState.TryGetValue(st, out var n) ? n + 1 : 1;
                    string cat = el.Category?.Name ?? "(uncategorised)";
                    if (!byCategory.TryGetValue(cat, out var m))
                        byCategory[cat] = m = new Dictionary<string, int>();
                    m[st] = m.TryGetValue(st, out var nn) ? nn + 1 : 1;
                }

                var audit = QRCommissioningWorkflow.ReadAudit(QRCommissioningWorkflow.AuditLogPath(doc));

                string path = OutputLocationHelper.GetTimestampedPath(doc, "STING_Commissioning_Report", ".csv");
                using (var w = new StreamWriter(path))
                {
                    w.WriteLine("Section,Key,State,Count");
                    foreach (var kv in byState.OrderBy(k => Array.IndexOf(QRCommissioningWorkflow.States, k.Key)))
                        w.WriteLine($"Total,,{kv.Key},{kv.Value}");
                    foreach (var catKv in byCategory.OrderBy(k => k.Key))
                        foreach (var stKv in catKv.Value.OrderBy(k => Array.IndexOf(QRCommissioningWorkflow.States, k.Key)))
                            w.WriteLine($"Category,\"{catKv.Key}\",{stKv.Key},{stKv.Value}");
                    w.WriteLine();
                    w.WriteLine("AuditTail (last 50)");
                    w.WriteLine("When,Element,From,To,Operative,Witness,Notes");
                    foreach (var a in audit.Skip(Math.Max(0, audit.Count - 50)))
                        w.WriteLine($"{a.When},\"{a.ElementName}\",{a.FromState},{a.ToState},{a.Operative},{a.Witness},\"{(a.Notes ?? "").Replace("\"", "'")}\"");
                }

                int total = byState.Values.Sum();
                var sb = new StringBuilder();
                sb.AppendLine($"Commissioning — {total} elements tracked");
                foreach (var st in QRCommissioningWorkflow.States)
                    if (byState.TryGetValue(st, out var n)) sb.AppendLine($"  {st}: {n}");
                sb.AppendLine();
                sb.AppendLine($"Report: {path}");
                TaskDialog.Show("STING Commissioning", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("QRCommissioningReportCommand failed", ex);
                TaskDialog.Show("STING", $"Report failed: {ex.Message}");
                return Result.Failed;
            }
        }
    }
}
