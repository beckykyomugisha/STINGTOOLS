// StingTools — Create Per-Option 3D Isolation View.
//
// For each option in a chosen set (or all sets), mints a 3D view named
// "STING - Option <set> · <option>" with VIEWER_OPTION_VISIBILITY locked
// to that option. Used for QA, presentation packages, and per-option
// renders. Idempotent — re-runs replace existing isolation views.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.DesignOptions;
using StingTools.UI;

namespace StingTools.Commands.DesignOptions
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateIsolationViewCommand : IExternalCommand
    {
        public const string ViewNamePrefix = "STING - Option";

        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            var doc = ctx.Doc;

            var sets = DesignOptionRegistry.Snapshot(doc);
            if (sets.Count == 0)
            {
                TaskDialog.Show("STING", "No design option sets in this document.");
                return Result.Cancelled;
            }

            var setLabels = new List<string> { "<all sets>" };
            setLabels.AddRange(sets.Select(s => s.Name));
            var picked = StingListPicker.Show("STING — Per-Option Isolation Views",
                "Generate isolation 3D views for which set?", setLabels);
            if (picked == null) return Result.Cancelled;

            var targetSets = picked == "<all sets>"
                ? sets
                : sets.Where(s => s.Name == picked).ToList();

            var threeDType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);
            if (threeDType == null)
            {
                TaskDialog.Show("STING", "No 3D view family type available.");
                return Result.Failed;
            }

            int made = 0, replaced = 0, failed = 0;
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .ToList();

            using (var tg = new TransactionGroup(doc, "STING Per-Option Isolation Views"))
            {
                tg.Start();
                using (var t = new Transaction(doc, "STING Mint Isolation Views"))
                {
                    t.Start();
                    foreach (var s in targetSets)
                    foreach (var opt in s.Options)
                    {
                        string name = $"{ViewNamePrefix} {s.Name} · {opt.Name}";
                        try
                        {
                            // Replace if it already exists
                            var stale = existing.FirstOrDefault(v => v.Name == name);
                            if (stale != null)
                            {
                                doc.Delete(stale.Id);
                                replaced++;
                            }

                            var v3 = View3D.CreateIsometric(doc, threeDType.Id);
                            v3.Name = name;
                            var p = v3.get_Parameter(BuiltInParameter.VIEWER_OPTION_VISIBILITY);
                            if (p != null) p.Set(opt.OptionId);
                            made++;
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            StingLog.Warn($"CreateIsolationView '{name}': {ex.Message}");
                        }
                    }
                    t.Commit();
                }
                tg.Assimilate();
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Sets processed   : {targetSets.Count}");
            sb.AppendLine($"Views created    : {made}");
            sb.AppendLine($"Stale replaced   : {replaced}");
            sb.AppendLine($"Failed           : {failed}");
            TaskDialog.Show("STING — Isolation Views", sb.ToString());
            return made > 0 ? Result.Succeeded : Result.Failed;
        }
    }
}
