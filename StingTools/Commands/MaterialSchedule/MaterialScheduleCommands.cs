// ══════════════════════════════════════════════════════════════════════════
//  MaterialScheduleCommands.cs — MAT-SCHED entry points.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.BOQ.MaterialSchedule;
using StingTools.Core;
using StingTools.Core.MaterialSchedule;

namespace StingTools.Commands.MaterialSchedule
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MaterialScheduleExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                var doc = ctx.Doc;

                // Prices in or out.
                var priceDlg = new TaskDialog("Material Schedule")
                {
                    MainInstruction = "Include prices?",
                    MainContent = "A priced schedule carries Rate, Amount, contingency and a grand total. "
                                + "A quantities-only schedule is a buy-list for the site team.",
                    CommonButtons = TaskDialogCommonButtons.Cancel
                };
                priceDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Priced schedule",
                    "Quantities plus rates, amounts, contingency and grand total.");
                priceDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Quantities only",
                    "Commodities, units and order quantities. No money.");
                var choice = priceDlg.Show();
                if (choice == TaskDialogResult.Cancel) return Result.Cancelled;

                var options = new MaterialScheduleOptions
                {
                    ShowPrices = choice == TaskDialogResult.CommandLink1,
                    ContingencyPct = 5.0
                };

                // MAT-SCHED-7 — compound take-off is what produces cement, sand,
                // blocks and bricks. It defaults OFF and is set in no shipped
                // file, so without this the export silently returns a schedule
                // with no materials in it. Offer it BEFORE the build, not as a
                // warning afterwards.
                bool forceCompound = false;
                if (!StingTools.BOQ.Takeoff.CompoundTakeoffBuilder.Enabled())
                {
                    var cd = new TaskDialog("Material Schedule")
                    {
                        MainInstruction = "Compound take-off is off — this export would contain no materials",
                        MainContent =
                            "Cement, sand, blocks, bricks and paint come from breaking walls and slabs into "
                          + "their constituents. That is currently disabled (COST_COMPOUND_TAKEOFF), so the "
                          + "schedule would list composite elements instead of things you can buy.",
                        CommonButtons = TaskDialogCommonButtons.Cancel
                    };
                    cd.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        "Enable it for this export", "Recommended. Uses compound take-off for this run only; "
                        + "your project configuration is not changed.");
                    cd.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                        "Export without it", "You will get composite elements, not commodities.");
                    var cr = cd.Show();
                    if (cr == TaskDialogResult.Cancel) return Result.Cancelled;
                    forceCompound = cr == TaskDialogResult.CommandLink1;
                }

                // MATSCHED-9 — the tools model divides by the programme, so a
                // missing duration means no tools at all. Ask once rather than
                // silently dropping a section the reference schedule opens with.
                MaterialScheduleBuilder.DurationDaysOverride = 0;
                if (SiteToolsGatherer.ReadDurationDays(doc) <= 0)
                {
                    var dd = new TaskDialog("Material Schedule — site tools")
                    {
                        MainInstruction = "How long is the programme?",
                        MainContent =
                            "Site tools are sized from the gangs the measured work implies, and gang "
                          + "size divides by the programme length. Project Information carries no "
                          + $"{SiteToolsGatherer.DurationParam}, so pick the nearest below or skip.  "
                          + "These figures are contractor practice, not a measurement standard — "
                          + "NRM2 prices tools in preliminaries.",
                        CommonButtons = TaskDialogCommonButtons.Cancel
                    };
                    dd.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "6 months (180 days)");
                    dd.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "12 months (360 days)");
                    dd.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Skip site tools",
                        "The schedule is produced without a tools section.");
                    var dr = dd.Show();
                    if (dr == TaskDialogResult.Cancel) return Result.Cancelled;
                    if (dr == TaskDialogResult.CommandLink1) MaterialScheduleBuilder.DurationDaysOverride = 180;
                    else if (dr == TaskDialogResult.CommandLink2) MaterialScheduleBuilder.DurationDaysOverride = 360;
                }

                MaterialScheduleBuildResult built;
                if (forceCompound)
                {
                    using (StingTools.BOQ.Takeoff.CompoundTakeoffBuilder.ForceEnabled(doc))
                        built = MaterialScheduleBuilder.Build(doc, options);
                }
                else
                {
                    built = MaterialScheduleBuilder.Build(doc, options);
                }
                var msDoc = built.Document;

                if (msDoc.Stages.Count == 0)
                {
                    TaskDialog.Show("Material Schedule",
                        "No material commodities were produced.\n\n"
                        + string.Join("\n\n", built.Warnings));
                    return Result.Cancelled;
                }

                // Reconciliation gate — skippable, mirroring the BOQ coverage gate.
                if (!msDoc.Reconciliation.IsClean)
                {
                    var issues = msDoc.Reconciliation.Issues;
                    var gate = new TaskDialog("Material Schedule — reconciliation")
                    {
                        MainInstruction = $"{issues.Count} reconciliation issue(s) found",
                        MainContent = string.Join("\n", issues.Take(6).Select(i => $"• [{i.Code}] {i.Message}"))
                                    + (issues.Count > 6 ? $"\n… and {issues.Count - 6} more." : "")
                                    + "\n\nAll issues are listed on the Validation sheet.",
                        CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.Cancel,
                        DefaultButton = TaskDialogResult.Cancel
                    };
                    gate.VerificationText = "Export anyway";
                    if (gate.Show() != TaskDialogResult.Yes) return Result.Cancelled;
                }

                string path = StingPaths.ExportFile(doc, "MaterialSchedule",
                    $"MaterialSchedule_{msDoc.ProjectCode}", ".xlsx");
                MaterialScheduleXlsxWriter.Write(msDoc, path);

                string warn = built.Warnings.Count > 0
                    ? "\n\nWarnings:\n" + string.Join("\n", built.Warnings.Select(w => "• " + w))
                    : "";
                TaskDialog.Show("Material Schedule",
                    $"{msDoc.Stages.Count} stage(s), "
                  + $"{msDoc.Stages.Sum(s => s.Commodities.Count)} commodity row(s).\n"
                  + (options.ShowPrices ? $"Grand total: UGX {msDoc.GrandTotalUGX:N0}\n" : "")
                  + $"\n{path}{warn}");

                try
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
                }
                catch (Exception ex) { StingLog.Warn($"Open material schedule xlsx: {ex.Message}"); }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MaterialScheduleExportCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  MatSched_PriceCommodities — the rate editor's entry point.
    //
    //  READ-ONLY on the model. It builds the schedule to learn which
    //  commodities exist and what they already cost, opens a grid, and writes
    //  ONE file: the project override. The shipped corporate baseline is never
    //  touched — ShippedDataIntegrityTests asserts its shape, and a project's
    //  supplier quote is not a corporate standard.
    //
    //  It builds with compound take-off FORCED on, unconditionally and without
    //  asking. The export asks because a user may legitimately want composite
    //  rows; here there is no such choice — with the gate off the schedule
    //  contains no cement, sand, blocks or bricks, so the editor would open
    //  offering to price a handful of composite elements while silently
    //  omitting every commodity the user came to price.
    // ══════════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    public class MaterialSchedulePriceCommoditiesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                var doc = ctx.Doc;

                string target = StingPaths.MetaFile(doc, "_BIM_COORD", "commodity_rates.csv");
                if (string.IsNullOrEmpty(target))
                {
                    TaskDialog.Show("STING — Price commodities",
                        "This project has not been saved, so there is no project folder to write "
                      + "commodity_rates.csv into.\n\nSave the Revit project first.");
                    return Result.Cancelled;
                }

                // Site tools need no programme here — the editor prices
                // commodities, and a missing tools section changes none of them.
                MaterialScheduleBuilder.DurationDaysOverride = 0;

                MaterialScheduleBuildResult built;
                using (StingTools.BOQ.Takeoff.CompoundTakeoffBuilder.ForceEnabled(doc))
                    built = MaterialScheduleBuilder.Build(doc,
                        new MaterialScheduleOptions { ShowPrices = true, ContingencyPct = 0 });

                if (built?.Document == null || built.Document.Stages.Count == 0)
                {
                    TaskDialog.Show("STING — Price commodities",
                        "No commodities were produced, so there is nothing to price.\n\n"
                      + string.Join("\n\n", built?.Warnings
                            ?? new System.Collections.Generic.List<string>()));
                    return Result.Cancelled;
                }

                // Load whatever is already in the project file so the merge can
                // preserve rows this model no longer produces. Parsed with the
                // SAME parser the builder uses, so the editor and the export can
                // never disagree about what the file says.
                var existing = new System.Collections.Generic.List<CommodityRate>();
                if (System.IO.File.Exists(target))
                {
                    existing = CommodityRateResolver.ParseCsv(
                        System.IO.File.ReadAllLines(target), out var skipped);
                    if (skipped.Count > 0)
                        TaskDialog.Show("STING — Price commodities",
                            $"{skipped.Count} row(s) in the existing commodity_rates.csv could not be "
                          + "read, so they are NOT shown in the editor and saving would drop them.\n\n"
                          + string.Join("\n", skipped.Take(5))
                          + "\n\nCancel and fix them by hand if they matter.");
                }

                // The same table the build used, so the editor's mapping preview
                // and the schedule can never disagree about what converts.
                var units = built.UnitsUsed;
                string patchPath = StingPaths.MetaFile(doc, "_BIM_COORD", "supplier_unit_patches.json");

                bool saved = StingTools.UI.CommodityRateEditor.ShowDialog(
                    doc, built.Document, existing, target, units, patchPath);

                return saved ? Result.Succeeded : Result.Cancelled;
            }
            catch (Exception ex)
            {
                StingLog.Error("MaterialSchedulePriceCommoditiesCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
