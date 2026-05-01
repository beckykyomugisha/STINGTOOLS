// StingTools — Phase 170 — Title-block factory commands
//
// Two IExternalCommand entry points wired to TitleBlockFactory:
//
//   TitleBlock_Create     — pick one family from the spec, mint it.
//   TitleBlock_CreateAll  — mint every family declared in
//                           Data/STING_TITLE_BLOCKS.json.
//
// Each .rfa is its own document with its own transaction lifecycle —
// the commands run NO transaction of their own.

using System;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockCreateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var lib = TitleBlockSpecRegistry.Load();
            if (lib?.Families == null || lib.Families.Count == 0)
            {
                TaskDialog.Show("STING — Title Block Factory",
                    "No families declared in STING_TITLE_BLOCKS.json.");
                return Result.Failed;
            }

            TitleBlockSpec pick;
            if (lib.Families.Count == 1)
            {
                pick = lib.Families[0];
            }
            else
            {
                var dlg = new TaskDialog("STING — Pick title-block family")
                {
                    MainInstruction = "Choose which family to mint",
                    AllowCancellation = true,
                };
                for (int i = 0; i < Math.Min(4, lib.Families.Count); i++)
                {
                    var f = lib.Families[i];
                    dlg.AddCommandLink((TaskDialogCommandLinkId)(1001 + i),
                        f.Id, f.Description);
                }
                var res = dlg.Show();
                int chosen = (int)res - 1001;
                if (chosen < 0 || chosen >= lib.Families.Count) return Result.Cancelled;
                pick = lib.Families[chosen];
            }

            var sharedFile = TitleBlockCommandUtils.ResolveSharedParamFile(data.Application);
            var build = TitleBlockFactory.Build(data.Application, pick, sharedFile);
            TitleBlockCommandUtils.ShowReport(pick.Id, build);
            return build.Ok ? Result.Succeeded : Result.Failed;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockCreateAllCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var lib = TitleBlockSpecRegistry.Load();
            if (lib?.Families == null || lib.Families.Count == 0)
            {
                TaskDialog.Show("STING — Title Block Factory",
                    "No families declared in STING_TITLE_BLOCKS.json.");
                return Result.Failed;
            }

            var sharedFile = TitleBlockCommandUtils.ResolveSharedParamFile(data.Application);
            var sb = new StringBuilder();
            sb.AppendLine($"Title-block library — {lib.Families.Count} families.");
            sb.AppendLine();

            int ok = 0, failed = 0;
            foreach (var spec in lib.Families)
            {
                var build = TitleBlockFactory.Build(data.Application, spec, sharedFile);
                if (build.Ok) ok++; else failed++;
                sb.AppendLine($"{(build.Ok ? "✓" : "✗")} {spec.Id,-30}  "
                    + $"params {build.ParametersAdded,3}  lines {build.LinesPlaced,3}  "
                    + $"labels {build.LabelsPlaced,3}  groups {build.ReflowGroupsBuilt}  "
                    + $"→ {build.SavedPath ?? "(not saved)"}");
                foreach (var w in build.Warnings.Take(3)) sb.AppendLine($"    ! {w}");
                foreach (var e in build.Errors.Take(3))   sb.AppendLine($"    ✗ {e}");
                foreach (var w in build.Warnings) StingLog.Warn($"TitleBlockCreateAll '{spec.Id}': {w}");
                foreach (var e in build.Errors)   StingLog.Error($"TitleBlockCreateAll '{spec.Id}': {e}");
            }

            sb.AppendLine();
            sb.AppendLine($"Done.  succeeded {ok},  failed {failed}.");
            TaskDialog.Show("STING — Title Block Factory", sb.ToString());
            return failed == 0 ? Result.Succeeded : Result.Failed;
        }
    }

    internal static class TitleBlockCommandUtils
    {
        public static string ResolveSharedParamFile(UIApplication uiApp)
        {
            // Prefer MR_PARAMETERS.txt shipped alongside the addin DLL.
            var p = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
            if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
            // Fallback to whatever Revit currently has open.
            try { return uiApp.Application.SharedParametersFilename; }
            catch { return null; }
        }

        public static void ShowReport(string id, TitleBlockBuildResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Title-block family: {id}");
            sb.AppendLine();
            sb.AppendLine($"  saved        : {r.SavedPath ?? "(not saved)"}");
            sb.AppendLine($"  parameters   : {r.ParametersAdded}");
            sb.AppendLine($"  lines        : {r.LinesPlaced}");
            sb.AppendLine($"  labels       : {r.LabelsPlaced}");
            sb.AppendLine($"  label pairs  : {r.LabelPairsPlaced}");
            sb.AppendLine($"  static text  : {r.StaticTextPlaced}");
            sb.AppendLine($"  filled regions : {r.FilledRegionsPlaced}");
            sb.AppendLine($"  reflow groups: {r.ReflowGroupsBuilt}");
            if (r.Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Warnings ({r.Warnings.Count}):");
                foreach (var w in r.Warnings.Take(15)) sb.AppendLine("  ! " + w);
                if (r.Warnings.Count > 15)
                    sb.AppendLine($"  … +{r.Warnings.Count - 15} more (StingTools.log)");
            }
            if (r.Errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Errors ({r.Errors.Count}):");
                foreach (var e in r.Errors.Take(10)) sb.AppendLine("  ✗ " + e);
            }
            foreach (var w in r.Warnings) StingLog.Warn($"TitleBlockCreate '{id}': {w}");
            foreach (var e in r.Errors)   StingLog.Error($"TitleBlockCreate '{id}': {e}");

            TaskDialog.Show("STING — Title Block Factory", sb.ToString());
        }
    }
}
