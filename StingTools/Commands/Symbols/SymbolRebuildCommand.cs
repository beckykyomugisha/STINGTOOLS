// StingTools — Symbol library rebuild (W-2).
//
// Command tag: Symbols_Rebuild
//
// The remedy half of cache invalidation. SymbolCacheManifest (W-1) stops the
// builder from skipping families whose catalogue or generator has moved on,
// but it only acts when a build runs. Anyone already holding families built
// before the manifest existed needs a way to ask for the repair.
//
// Two modes:
//   Stale only — rebuild catalogues whose SHA-256 or generator version has
//                changed; leave the rest alone. Safe, and the right choice
//                after a plug-in upgrade.
//   Force all  — regenerate every family regardless. For a library believed
//                corrupt, or to adopt a generator fix that somehow escaped
//                a GeneratorVersion bump.
//
// Distinct from Symbols_Reload, which re-loads existing .rfa into the
// document and flushes the JSON shapes cache — it never rebuilds anything.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Symbols;

namespace StingTools.Commands.Symbols
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SymbolRebuildCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            bool? force = PromptMode();
            if (force == null) return Result.Cancelled;

            string outRoot = SymbolBatchHelper.ResolveOutputRoot(doc);

            var aggregate = new SymbolCreationResult();
            var perBatch = new List<(string Label, int Created, int Existed, int Failed)>();

            foreach (var b in SymbolBatchHelper.AllBatches)
            {
                try
                {
                    var r = SymbolBatchHelper.RunBatch(doc, b.File, b.Folder, rebuildMode: force.Value);
                    aggregate.Created += r.Created;
                    aggregate.Existed += r.Existed;
                    aggregate.Failed += r.Failed;
                    aggregate.Warnings.AddRange(r.Warnings);
                    aggregate.Errors.AddRange(r.Errors);
                    perBatch.Add((b.Label, r.Created, r.Existed, r.Failed));
                }
                catch (Exception ex)
                {
                    aggregate.Failed++;
                    aggregate.Errors.Add($"{b.Label}: {ex.Message}");
                    StingLog.Error($"Symbols_Rebuild: {b.Label} failed", ex);
                }
            }

            ShowReport(aggregate, perBatch, outRoot, force.Value);
            StingLog.Info($"Symbols_Rebuild: force={force.Value} rebuilt={aggregate.Created} " +
                          $"fresh={aggregate.Existed} failed={aggregate.Failed} outRoot={outRoot}");
            return aggregate.Failed > 0 ? Result.Failed : Result.Succeeded;
        }

        /// <summary>Null = cancelled; false = stale only; true = force all.</summary>
        private static bool? PromptMode()
        {
            var td = new TaskDialog("STING Symbols — Rebuild")
            {
                MainInstruction = "Rebuild the generated symbol library",
                MainContent =
                    "Stale only — rebuild catalogues whose content or generator version has " +
                    "changed since they were built. Use this after a plug-in upgrade.\n\n" +
                    "Force all — regenerate every family regardless of the cache. Slower; use " +
                    "when the library is believed corrupt.\n\n" +
                    "Both overwrite generated families in the output folder. Manually authored " +
                    "families kept outside it are not touched.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Stale only (recommended)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Force all");

            switch (td.Show())
            {
                case TaskDialogResult.CommandLink1: return false;
                case TaskDialogResult.CommandLink2: return true;
                default: return null;
            }
        }

        private static void ShowReport(SymbolCreationResult agg,
            List<(string Label, int Created, int Existed, int Failed)> perBatch,
            string outRoot, bool force)
        {
            var sb = new StringBuilder();
            sb.AppendLine(force ? "Rebuild mode: FORCE ALL" : "Rebuild mode: STALE ONLY");
            sb.AppendLine($"Output: {outRoot}");
            sb.AppendLine();
            sb.AppendLine($"  Rebuilt      : {agg.Created}");
            sb.AppendLine($"  Left as-is   : {agg.Existed}");
            sb.AppendLine($"  Failed       : {agg.Failed}");
            sb.AppendLine();

            var touched = perBatch.Where(p => p.Created > 0 || p.Failed > 0).ToList();
            if (touched.Count == 0)
            {
                sb.AppendLine("Every catalogue was already current — nothing to rebuild.");
            }
            else
            {
                sb.AppendLine("Catalogues rebuilt:");
                foreach (var p in touched)
                    sb.AppendLine($"  · {p.Label}: {p.Created} rebuilt" +
                                  (p.Failed > 0 ? $", {p.Failed} FAILED" : ""));
            }

            if (agg.Errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Errors:");
                foreach (var e in agg.Errors.Take(10)) sb.AppendLine("  · " + e);
                if (agg.Errors.Count > 10)
                    sb.AppendLine($"  … +{agg.Errors.Count - 10} more (see StingTools.log).");
            }

            foreach (var w in agg.Warnings) StingLog.Warn($"Symbols_Rebuild: {w}");
            foreach (var e in agg.Errors) StingLog.Error($"Symbols_Rebuild: {e}");
            TaskDialog.Show("STING - Symbol Rebuild", sb.ToString());
        }
    }
}
