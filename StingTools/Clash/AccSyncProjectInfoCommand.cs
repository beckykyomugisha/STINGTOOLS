// AccSyncProjectInfoCommand.cs — ACC_SyncProjectInfo: ACC project record -> Revit Project Information.
//
// Reads the ACC project's own record (AccProjectDetailsClient: construction/admin, falling
// back to the Data Management name on a 403) and shows, row by row, what ACC holds against
// what Revit Project Information / the PRJ_ORG_* parameters hold now.
//
// NEVER A SILENT OVERWRITE. Interactively, only the rows the person TICKS are written, and
// no row is pre-ticked. Unattended (acc_settings.json "unattended": true) it is REPORT ONLY:
// a scheduled cycle has nobody to decide whether ACC's project name beats the model's, so it
// writes the diff to the log and a routed CSV and changes nothing.
//
// A failed read changes nothing and says why; a 403 is reported as "needs Account/Project
// Admin", never as "ACC has no details".

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Select;
using StingTools.V6;

namespace StingTools.Core.Clash
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AccSyncProjectInfoCommand : IExternalCommand
    {
        private const string Title = "ACC — Project Information";

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var policy = AccProjectSettingsFile.LoadFor(doc, "ACC_SyncProjectInfo");
            var creds = AccProjectSettingsFile.LoadCredentials(doc, "ACC_SyncProjectInfo");
            if (string.IsNullOrEmpty(creds.ClientId) || string.IsNullOrEmpty(creds.RefreshToken) ||
                string.IsNullOrEmpty(creds.ProjectId))
            {
                return AccProjectSettingsFile.NotConfigured(policy, creds, Title, "Nothing was changed.");
            }

            AccFetchResult<AccProjectDetails> got;
            try { got = AccProjectDetailsClient.GetAsync(creds).GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                StingLog.Error("ACC_SyncProjectInfo read", ex);
                AccPullClashesCommand.Report(policy, Title, "Reading the ACC project failed: " + ex.Message + "\nNothing was changed.");
                return Result.Failed;
            }
            if (!got.Succeeded)
            {
                AccPullClashesCommand.Report(policy, Title,
                    AccCommandOutcome.FailureMessage("the ACC project record", got.Status, got.HttpStatus, got.Detail, creds.ProjectId) +
                    "\nProject Information was left untouched.");
                StingLog.Warn($"ACC_SyncProjectInfo FAILED ({got.Status}): {got.Detail}");
                return Result.Failed;
            }

            var pi = doc.ProjectInformation;
            if (pi == null)
            {
                AccPullClashesCommand.Report(policy, Title, "This document has no Project Information element. Nothing was changed.");
                return Result.Failed;
            }
            var rows = AccProjectInfoDiff.Build(got.Value, (kind, target) => ReadCurrent(pi, kind, target),
                ParamRegistry.ORG_PROJECT_CODE, ParamRegistry.PRJ_ORG_CURRENCY_TXT);
            var differing = rows.Where(r => r.Writable).ToList();
            string csv = WriteCsv(doc, got.Value, rows);
            string report = Describe(got.Value, rows, csv);

            if (!policy.MayPrompt)
            {
                StingLog.Info($"{Title} (unattended — report only, nothing written): {report}");
                return Result.Succeeded;
            }

            if (differing.Count == 0)
            {
                new TaskDialog(Title)
                {
                    MainInstruction = "Project Information already matches ACC",
                    MainContent = report,
                }.Show();
                return Result.Succeeded;
            }

            var items = differing.Select(r => new StingListPicker.ListItem
            {
                Label = r.Label,
                Detail = $"ACC: '{Short(r.AccValue)}'   Revit now: '{(r.CurrentValue.Length == 0 ? "(empty)" : Short(r.CurrentValue))}'   → {r.Target}",
                Tag = r,
                IsSelected = false,   // nothing is pre-ticked: every write is a decision
            }).ToList();
            var picked = StingListPicker.Show(Title,
                $"Tick the ACC values to copy into Project Information ({differing.Count} differ). Unticked rows are left as they are." +
                (got.Value.Partial ? "  NOTE: " + got.Value.Limitation : ""),
                items, allowMultiSelect: true);
            if (picked == null || picked.Count == 0)
            {
                TaskDialog.Show(Title, "Nothing ticked — Project Information was not changed.\n\n" + report);
                return Result.Cancelled;
            }

            var written = new List<string>();
            var failed = new List<string>();
            using (var t = new Transaction(doc, "STING ACC Project Information"))
            {
                t.Start();
                foreach (var r in picked.Select(p => p.Tag).OfType<AccProjectInfoRow>())
                {
                    try
                    {
                        if (Write(pi, r)) written.Add($"{r.Label}: '{Short(r.CurrentValue)}' → '{Short(r.AccValue)}'");
                        else failed.Add($"{r.Label}: the parameter {r.Target} is read-only or not a text parameter");
                    }
                    catch (Exception ex)
                    {
                        failed.Add($"{r.Label}: {ex.Message}");
                        StingLog.Warn($"ACC_SyncProjectInfo write {r.Target}: {ex.Message}");
                    }
                }
                if (written.Count == 0) t.RollBack();
                else t.Commit();
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Written ({written.Count}):");
            foreach (var w in written) sb.AppendLine("  " + w);
            if (failed.Count > 0)
            {
                sb.AppendLine($"NOT written ({failed.Count}):");
                foreach (var f in failed) sb.AppendLine("  " + f);
            }
            if (csv != null) sb.AppendLine().AppendLine("Diff CSV: " + csv);
            TaskDialog.Show(Title, sb.ToString());
            StingLog.Info($"ACC_SyncProjectInfo: wrote {written.Count}, failed {failed.Count}: " + string.Join(" | ", written.Concat(failed)));
            return failed.Count > 0 ? Result.Failed : Result.Succeeded;
        }

        private static BuiltInParameter BuiltInFor(string key) => key switch
        {
            AccProjectInfoDiff.BuiltInName => BuiltInParameter.PROJECT_NAME,
            AccProjectInfoDiff.BuiltInNumber => BuiltInParameter.PROJECT_NUMBER,
            AccProjectInfoDiff.BuiltInAddress => BuiltInParameter.PROJECT_ADDRESS,
            AccProjectInfoDiff.BuiltInStatus => BuiltInParameter.PROJECT_STATUS,
            _ => BuiltInParameter.INVALID,
        };

        /// <summary>The model's value, or null when the target does not exist here.</summary>
        private static string ReadCurrent(ProjectInfo pi, AccInfoTargetKind kind, string target)
        {
            try
            {
                if (kind == AccInfoTargetKind.BuiltIn)
                {
                    var p = pi.get_Parameter(BuiltInFor(target));
                    return p == null ? null : (p.AsString() ?? "");
                }
                if (kind == AccInfoTargetKind.Shared)
                    return pi.LookupParameter(target) == null ? null : ParameterHelpers.GetValueText(pi, target) ?? "";
            }
            catch (Exception ex) { StingLog.Warn($"ACC_SyncProjectInfo read {target}: {ex.Message}"); }
            return null;
        }

        private static bool Write(ProjectInfo pi, AccProjectInfoRow r)
        {
            if (r.TargetKind == AccInfoTargetKind.BuiltIn)
            {
                var p = pi.get_Parameter(BuiltInFor(r.Target));
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) return false;
                return p.Set(r.AccValue);
            }
            if (r.TargetKind == AccInfoTargetKind.Shared)
                return ParameterHelpers.SetString(pi, r.Target, r.AccValue, overwrite: true);
            return false;
        }

        private static string Describe(AccProjectDetails d, List<AccProjectInfoRow> rows, string csv)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"ACC project '{d.Name}' [{d.Id}] — read from {(d.Source == "admin" ? "the ACC Admin API" : "Data Management (name only)")}.");
            if (d.Partial) sb.AppendLine("NOTE: " + d.Limitation);
            sb.AppendLine();
            foreach (var r in rows)
            {
                string state = r.State switch
                {
                    AccInfoRowState.Same => "same",
                    AccInfoRowState.Differs => "DIFFERS",
                    AccInfoRowState.AccEmpty => "not set in ACC",
                    _ => r.TargetKind == AccInfoTargetKind.None ? "info only" : "no parameter in model",
                };
                sb.AppendLine($"  [{state}] {r.Label}: ACC '{Short(r.AccValue)}'" +
                              (r.TargetKind == AccInfoTargetKind.None ? "" : $"  |  Revit '{Short(r.CurrentValue)}'"));
            }
            if (csv != null) sb.AppendLine().AppendLine("CSV: " + csv);
            return sb.ToString();
        }

        private static string WriteCsv(Document doc, AccProjectDetails d, List<AccProjectInfoRow> rows)
        {
            try
            {
                var lines = new List<string> { "Field,Target,State,AccValue,RevitValue" };
                foreach (var r in rows)
                    lines.Add(string.Join(",", Csv(r.Label), Csv(r.Target), Csv(r.State.ToString()), Csv(r.AccValue), Csv(r.CurrentValue)));
                if (d.Partial) lines.Add($"{Csv("NOTE")},,,{Csv(d.Limitation)},");
                string path = OutputLocationHelper.GetRoutedTimestampedPath(doc, "Issue", "STING_ACC_ProjectInfo", ".csv");
                File.WriteAllLines(path, lines, Encoding.UTF8);
                return path;
            }
            catch (Exception ex) { StingLog.Warn("ACC_SyncProjectInfo CSV: " + ex.Message); return null; }
        }

        private static string Short(string s)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ");
            return s.Length <= 60 ? s : s.Substring(0, 57) + "...";
        }

        private static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }
}
