// StingTools — Design Options Dashboard command.
//
// Read-only diagnostic that surfaces the DesignOptionDashboardData
// summary as a TaskDialog. Acts as the launchpad until the BIM
// Coordination Center "Options" tab UI lands.

using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.DesignOptions;

namespace StingTools.Commands.DesignOptions
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class OptionsDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            var doc = ctx.Doc;

            DesignOptionRegistry.InvalidateCache(doc);
            var sum = DesignOptionDashboardData.Build(doc);

            var sb = new StringBuilder();
            sb.AppendLine($"RAG status            : {sum.RagStatus}");
            sb.AppendLine($"Sets                  : {sum.Sets}");
            sb.AppendLine($"Options               : {sum.Options}");
            sb.AppendLine($"Primary options       : {sum.PrimaryOptions}");
            sb.AppendLine($"Decided sets          : {sum.DecidedSets} / {sum.Sets}");
            sb.AppendLine($"Overdue undecided     : {sum.OverdueSets}");
            sb.AppendLine($"Client-facing sets    : {sum.ClientFacingSets}");
            sb.AppendLine($"Options w/ issues     : {sum.OptionsWithIssues}");
            sb.AppendLine($"Options w/ sheets     : {sum.OptionsWithSheets}");
            sb.AppendLine();

            if (sum.Rows.Count == 0)
            {
                sb.AppendLine("(no design option sets)");
            }
            else
            {
                sb.AppendLine($"{"Set",-22} {"Option",-18} {"Status",-9} {"Elems",6} {"Sheets",6} {"Issues",6} {"ΔCost",10} {"ΔCO2",10}");
                sb.AppendLine(new string('─', 96));
                foreach (var r in sum.Rows.OrderBy(x => x.SetName).ThenBy(x => x.OptionName))
                {
                    sb.AppendLine($"{Trim(r.SetName,22),-22} {Trim(r.OptionName,18),-18} {r.DecisionStatus,-9} {r.ElementCount,6} {r.LockedSheets,6} {r.LinkedIssues,6} {r.CostDelta,10:N0} {r.CarbonDelta,10:N0}");
                }
            }

            TaskDialog.Show("STING — Design Options Dashboard", sb.ToString());
            return Result.Succeeded;
        }

        private static string Trim(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n - 1) + "…";
        }
    }
}
