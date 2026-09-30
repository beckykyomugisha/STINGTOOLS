// AccCheckLocationsCommand.cs — ACC_CheckLocations: ACC locations tree vs STING LOC / ZONE / LVL.
//
// Reads the project's ACC Location Breakdown Structure (AccLocationsClient) and compares it
// with the codes STING tags carry: LOC and ZONE from TagConfig (project_config.json, or the
// built-in defaults), and a LVL code for every Revit level, derived by the SAME rule the
// tagger uses (ParameterHelpers.GetLevelCodeForLevel). Reports ACC nodes no STING code
// matches, STING codes no ACC node carries, and names that mean the same level but read
// differently. CSV via the routed "Issue" folder.
//
// REPORT ONLY. It does not write missing codes into project_config.json: the only writer
// (TagConfig.SaveToFile) rewrites the whole file from memory and drops keys it does not
// know, so using it from here could lose project settings. Adding codes stays a person's
// edit in the Tag Config editor.
//
// ReadOnly: no model change, no ACC change.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.V6;

namespace StingTools.Core.Clash
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AccCheckLocationsCommand : IExternalCommand
    {
        private const string Title = "ACC — Locations vs STING codes";

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var policy = AccProjectSettingsFile.LoadFor(doc, "ACC_CheckLocations");
            var creds = AccProjectSettingsFile.LoadCredentials(doc, "ACC_CheckLocations");
            if (string.IsNullOrEmpty(creds.ClientId) || string.IsNullOrEmpty(creds.RefreshToken) ||
                string.IsNullOrEmpty(creds.ProjectId))
            {
                AccPullClashesCommand.Report(policy, Title, "ACC is not configured for this model (sign-in or ACC project missing). Nothing was checked.");
                return Result.Cancelled;
            }

            AccFetchResult<List<AccLocationNode>> tree;
            try { tree = AccLocationsClient.GetTreeAsync(creds).GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                StingLog.Error("ACC_CheckLocations read", ex);
                AccPullClashesCommand.Report(policy, Title, "Reading the ACC locations tree failed: " + ex.Message);
                return Result.Failed;
            }
            if (!tree.Succeeded)
            {
                AccPullClashesCommand.Report(policy, Title,
                    AccCommandOutcome.FailureMessage("the ACC locations tree", tree.Status, tree.HttpStatus, tree.Detail, creds.ProjectId) +
                    "\nNothing was compared — a failed read is not an empty tree.");
                StingLog.Warn($"ACC_CheckLocations FAILED ({tree.Status}): {tree.Detail}");
                return Result.Failed;
            }

            var codes = new List<StingSpatialCode>();
            foreach (var c in TagConfig.LocCodes ?? new List<string>()) codes.Add(new StingSpatialCode { Kind = "LOC", Code = c });
            foreach (var c in TagConfig.ZoneCodes ?? new List<string>()) codes.Add(new StingSpatialCode { Kind = "ZONE", Code = c });
            foreach (var lvl in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation))
            {
                string code = ParameterHelpers.GetLevelCodeForLevel(lvl);
                codes.Add(new StingSpatialCode { Kind = "LVL", Code = code, SourceName = lvl.Name ?? "" });
            }

            var result = AccLocationCheck.Compare(tree.Value, codes, SpatialNameCodes.LevelCodeFromName);
            string csv = WriteCsv(doc, tree.Value, result);

            var sb = new StringBuilder();
            if (tree.Status == AccFetchStatus.EmptyOk)
                sb.AppendLine("The ACC locations tree holds only its root — no locations are defined in ACC yet.");
            sb.AppendLine($"ACC location nodes checked:     {result.NodesChecked}");
            sb.AppendLine($"Matched to a STING code:        {result.Matches.Select(m => m.Node.Id).Distinct().Count()}");
            sb.AppendLine($"ACC nodes with NO STING code:   {result.NodesWithoutCode.Count}");
            sb.AppendLine($"STING codes with NO ACC node:   {result.CodesWithoutNode.Count}  " +
                          $"(LOC {result.CodesWithoutNode.Count(c => c.Kind == "LOC")}, ZONE {result.CodesWithoutNode.Count(c => c.Kind == "ZONE")}, LVL {result.CodesWithoutNode.Count(c => c.Kind == "LVL")})");
            sb.AppendLine($"Name mismatches:                {result.NameMismatches.Count}");
            if (result.NodesWithoutCode.Count > 0)
            {
                sb.AppendLine().AppendLine("ACC nodes with no STING code:");
                foreach (var n in result.NodesWithoutCode.Take(12)) sb.AppendLine("  " + n.Path);
                if (result.NodesWithoutCode.Count > 12) sb.AppendLine($"  … and {result.NodesWithoutCode.Count - 12} more (see CSV)");
            }
            if (result.CodesWithoutNode.Count > 0)
            {
                sb.AppendLine().AppendLine("STING codes with no ACC node:");
                sb.AppendLine("  " + string.Join(", ", result.CodesWithoutNode.Take(30).Select(c => $"{c.Kind} {c.Code}")) +
                              (result.CodesWithoutNode.Count > 30 ? " …" : ""));
            }
            if (result.NameMismatches.Count > 0)
            {
                sb.AppendLine().AppendLine("Name mismatches:");
                foreach (var m in result.NameMismatches.Take(10)) sb.AppendLine("  " + m.Detail);
            }
            sb.AppendLine().AppendLine("Report only — no code was added to project_config.json and nothing changed in ACC.");
            if (csv != null) sb.AppendLine("CSV: " + csv);

            if (policy.MayPrompt)
                new TaskDialog(Title)
                {
                    MainInstruction = result.NodesWithoutCode.Count + result.CodesWithoutNode.Count + result.NameMismatches.Count == 0
                        ? "ACC locations and STING codes line up"
                        : "ACC locations and STING codes differ",
                    MainContent = sb.ToString(),
                }.Show();
            else StingLog.Info($"{Title}: {sb}");
            return Result.Succeeded;
        }

        private static string WriteCsv(Document doc, List<AccLocationNode> nodes, AccLocationCheckResult r)
        {
            try
            {
                var lines = new List<string> { "Finding,AccPath,AccType,AccBarcode,StingKind,StingCode,Detail" };
                foreach (var m in r.Matches)
                    lines.Add(Line("matched", m.Node, m.Code, "by " + m.How));
                foreach (var n in r.NodesWithoutCode)
                    lines.Add(Line("acc_node_without_sting_code", n, null, ""));
                foreach (var c in r.CodesWithoutNode)
                    lines.Add(Line("sting_code_without_acc_node", null, c, c.SourceName.Length > 0 ? "Revit level '" + c.SourceName + "'" : ""));
                foreach (var m in r.NameMismatches)
                    lines.Add(Line("name_mismatch", m.Node, m.Code, m.Detail));
                string path = OutputLocationHelper.GetRoutedTimestampedPath(doc, "Issue", "STING_ACC_Locations", ".csv");
                File.WriteAllLines(path, lines, Encoding.UTF8);
                return path;
            }
            catch (Exception ex) { StingLog.Warn("ACC_CheckLocations CSV: " + ex.Message); return null; }
        }

        private static string Line(string finding, AccLocationNode n, StingSpatialCode c, string detail)
            => string.Join(",", Csv(finding), Csv(n?.Path), Csv(n?.Type), Csv(n?.Barcode), Csv(c?.Kind), Csv(c?.Code), Csv(detail));

        private static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }
}
