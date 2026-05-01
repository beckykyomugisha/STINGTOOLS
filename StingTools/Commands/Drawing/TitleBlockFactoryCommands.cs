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
using StingTools.UI;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockCreateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            // Dock-panel dispatcher passes null for ExternalCommandData; fall
            // back to StingCommandHandler.CurrentApp per the codebase
            // convention (see StingCommandHandler.RunCommand<T>).
            var uiApp = data?.Application ?? StingCommandHandler.CurrentApp;
            if (uiApp == null)
            {
                TaskDialog.Show("STING — Title Block Factory",
                    "No active Revit UIApplication — cannot resolve shared parameter file.");
                return Result.Failed;
            }

            var lib = TitleBlockSpecRegistry.Load();
            if (lib?.Families == null || lib.Families.Count == 0)
            {
                TaskDialog.Show("STING — Title Block Factory",
                    "No families declared in STING_TITLE_BLOCKS.json.");
                return Result.Failed;
            }

            // Filter out abstract specs — they're inheritance bases only,
            // not standalone families to mint.
            var concrete = lib.Families.FindAll(f => !f.Abstract);
            if (concrete.Count == 0)
            {
                TaskDialog.Show("STING — Title Block Factory",
                    $"All {lib.Families.Count} family entries in STING_TITLE_BLOCKS.json are marked abstract. "
                    + "Mark at least one with `\"abstract\": false` (or omit the field).");
                return Result.Failed;
            }

            TitleBlockSpec pick;
            if (concrete.Count == 1)
            {
                pick = concrete[0];
            }
            else
            {
                var dlg = new TaskDialog("STING — Pick title-block family")
                {
                    MainInstruction = "Choose which family to mint",
                    AllowCancellation = true,
                };
                for (int i = 0; i < Math.Min(4, concrete.Count); i++)
                {
                    var f = concrete[i];
                    dlg.AddCommandLink((TaskDialogCommandLinkId)(1001 + i),
                        f.Id, f.Description);
                }
                var res = dlg.Show();
                int chosen = (int)res - 1001;
                if (chosen < 0 || chosen >= concrete.Count) return Result.Cancelled;
                pick = concrete[chosen];
            }

            // Flatten the extends chain — child concatenates with every
            // ancestor's lines / labels / static text / filled regions /
            // slots, child wins for parameters by name and slots by id.
            pick = TitleBlockSpecRegistry.Resolve(lib, pick);

            var sharedFile = TitleBlockCommandUtils.ResolveSharedParamFile(uiApp);
            var build = TitleBlockFactory.Build(uiApp, pick, sharedFile);
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
            var uiApp = data?.Application ?? StingCommandHandler.CurrentApp;
            if (uiApp == null)
            {
                TaskDialog.Show("STING — Title Block Factory",
                    "No active Revit UIApplication — cannot resolve shared parameter file.");
                return Result.Failed;
            }

            var lib = TitleBlockSpecRegistry.Load();
            if (lib?.Families == null || lib.Families.Count == 0)
            {
                TaskDialog.Show("STING — Title Block Factory",
                    "No families declared in STING_TITLE_BLOCKS.json.");
                return Result.Failed;
            }

            var sharedFile = TitleBlockCommandUtils.ResolveSharedParamFile(uiApp);
            var concrete = lib.Families.FindAll(f => !f.Abstract);
            var sb = new StringBuilder();
            sb.AppendLine($"Title-block library — {concrete.Count} concrete families "
                + $"({lib.Families.Count - concrete.Count} abstract bases skipped).");
            sb.AppendLine();

            int ok = 0, failed = 0;
            foreach (var rawSpec in concrete)
            {
                // Flatten extends chain so the factory sees a complete spec.
                var spec = TitleBlockSpecRegistry.Resolve(lib, rawSpec);
                var build = TitleBlockFactory.Build(uiApp, spec, sharedFile);
                if (build.Ok) ok++; else failed++;
                sb.AppendLine($"{(build.Ok ? "✓" : "✗")} {spec.Id,-30}  "
                    + $"params {build.ParametersAdded,3}  lines {build.LinesPlaced,3}  "
                    + $"labels {build.LabelsPlaced,3}  slots {build.SlotsPlaced}  "
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
            // Build the full report (everything, no cap) — used for both
            // the dialog (truncated copy) and the on-disk .log.
            var full = BuildFullReport(id, r);

            // Drop the full report next to the .rfa so warnings + slot
            // bbox table survive the user closing the TaskDialog. If the
            // build didn't save, fall back to the OutputLocation chain.
            string logPath = null;
            try { logPath = WriteReportLog(id, r, full); }
            catch (Exception ex) { StingLog.Warn($"TitleBlock report log write: {ex.Message}"); }

            // Truncated dialog copy — same content, with hot lists clipped
            // and a note pointing at the full log on disk.
            var dialog = BuildDialogCopy(id, r, logPath);

            foreach (var w in r.Warnings) StingLog.Warn($"TitleBlockCreate '{id}': {w}");
            foreach (var e in r.Errors)   StingLog.Error($"TitleBlockCreate '{id}': {e}");

            TaskDialog.Show("STING — Title Block Factory", dialog);
        }

        private static string BuildFullReport(string id, TitleBlockBuildResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("===================================================================");
            sb.AppendLine($"STING Title-block factory — {id}");
            sb.AppendLine($"  generated   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"  status      : {(r.Ok ? "OK" : "FAILED")}");
            sb.AppendLine($"  saved       : {r.SavedPath ?? "(not saved)"}");
            sb.AppendLine("===================================================================");
            sb.AppendLine();
            sb.AppendLine("Counts");
            sb.AppendLine("------");
            sb.AppendLine($"  parameters     : {r.ParametersAdded}");
            sb.AppendLine($"  lines          : {r.LinesPlaced}");
            sb.AppendLine($"  labels         : {r.LabelsPlaced}");
            sb.AppendLine($"  static text    : {r.StaticTextPlaced}");
            sb.AppendLine($"  filled regions : {r.FilledRegionsPlaced}");
            sb.AppendLine($"  slots          : {r.SlotsPlaced}");

            if (r.SlotSummary.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Slots");
                sb.AppendLine("-----");
                sb.AppendLine("  id     bottom-left           top-right             flags");
                foreach (var s in r.SlotSummary) sb.AppendLine(s);
            }

            // Group warnings by leading category prefix so the operator
            // can see at a glance whether the family is ~clean or has a
            // systemic issue. Categories are inferred from the "Foo: …"
            // pattern most warnings follow.
            if (r.Warnings.Count > 0)
            {
                var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var w in r.Warnings)
                {
                    var cat = ExtractWarningCategory(w);
                    if (!groups.TryGetValue(cat, out var list))
                        groups[cat] = list = new List<string>();
                    list.Add(w);
                }

                sb.AppendLine();
                sb.AppendLine($"Warnings ({r.Warnings.Count})");
                sb.AppendLine("--------");
                foreach (var kv in groups.OrderByDescending(g => g.Value.Count))
                {
                    sb.AppendLine($"  [{kv.Key}] × {kv.Value.Count}");
                    foreach (var w in kv.Value) sb.AppendLine($"    ! {w}");
                }
            }

            if (r.Errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Errors ({r.Errors.Count})");
                sb.AppendLine("------");
                foreach (var e in r.Errors) sb.AppendLine($"  ✗ {e}");
            }

            return sb.ToString();
        }

        private static string BuildDialogCopy(string id, TitleBlockBuildResult r, string logPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Title-block family: {id}");
            sb.AppendLine();
            sb.AppendLine($"  saved        : {r.SavedPath ?? "(not saved)"}");
            sb.AppendLine($"  parameters   : {r.ParametersAdded}");
            sb.AppendLine($"  lines        : {r.LinesPlaced}");
            sb.AppendLine($"  labels       : {r.LabelsPlaced}");
            sb.AppendLine($"  static text  : {r.StaticTextPlaced}");
            sb.AppendLine($"  filled regions : {r.FilledRegionsPlaced}");
            sb.AppendLine($"  slots        : {r.SlotsPlaced}");

            if (r.SlotSummary.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Slots:");
                foreach (var s in r.SlotSummary.Take(8)) sb.AppendLine(s);
                if (r.SlotSummary.Count > 8)
                    sb.AppendLine($"  … +{r.SlotSummary.Count - 8} more (full log)");
            }

            if (r.Warnings.Count > 0)
            {
                var groups = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var w in r.Warnings)
                {
                    var cat = ExtractWarningCategory(w);
                    groups[cat] = groups.TryGetValue(cat, out var c) ? c + 1 : 1;
                }

                sb.AppendLine();
                sb.AppendLine($"Warnings ({r.Warnings.Count}) by category:");
                foreach (var kv in groups.OrderByDescending(g => g.Value))
                    sb.AppendLine($"  [{kv.Key}] × {kv.Value}");

                // Show top 8 individual warnings to give a flavour.
                sb.AppendLine();
                sb.AppendLine("Top warnings:");
                foreach (var w in r.Warnings.Take(8)) sb.AppendLine("  ! " + w);
                if (r.Warnings.Count > 8)
                    sb.AppendLine($"  … +{r.Warnings.Count - 8} more (full log)");
            }

            if (r.Errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Errors ({r.Errors.Count}):");
                foreach (var e in r.Errors.Take(5)) sb.AppendLine("  ✗ " + e);
            }

            if (!string.IsNullOrEmpty(logPath))
            {
                sb.AppendLine();
                sb.AppendLine($"Full log: {logPath}");
            }

            return sb.ToString();
        }

        private static string WriteReportLog(string id, TitleBlockBuildResult r, string full)
        {
            string dir = null;
            if (!string.IsNullOrEmpty(r.SavedPath))
            {
                try { dir = Path.GetDirectoryName(r.SavedPath); } catch { }
            }
            if (string.IsNullOrEmpty(dir))
            {
                try
                {
                    var asm = StingToolsApp.AssemblyPath;
                    if (!string.IsNullOrEmpty(asm))
                        dir = Path.GetDirectoryName(asm);
                }
                catch { }
            }
            if (string.IsNullOrEmpty(dir)) return null;
            try { Directory.CreateDirectory(dir); } catch { }

            // Sanitise the family id for filesystem use, then append a
            // timestamp so successive builds don't overwrite each other.
            var safeId = string.Concat(id.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-'));
            var stamp  = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var path   = Path.Combine(dir, $"{safeId}_{stamp}.log");
            File.WriteAllText(path, full);
            return path;
        }

        private static string ExtractWarningCategory(string w)
        {
            if (string.IsNullOrEmpty(w)) return "(empty)";
            // Most warnings follow the pattern "Foo: …" or "Foo 'bar': …"
            // — take the first run of word characters as the category.
            int i = 0;
            while (i < w.Length && (char.IsLetterOrDigit(w[i]) || w[i] == '_')) i++;
            if (i == 0) return "(misc)";
            return w.Substring(0, i);
        }
    }
}
