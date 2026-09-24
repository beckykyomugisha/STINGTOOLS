// StingTools — Materials_SyncIdentity
//
// Gives every coded material one identity a Material Tag can read without any
// shared parameter: Mark = code, Keynote = code, Description = the short name,
// with STING's long "enriched" paragraph moved to MAT_SPECIFICATIONS. Fills the
// shared MAT_CODE from the register where it is empty (the StampCodes rule).
// The decisions are MaterialIdentityPlanner's (Revit-free, tested against the
// shipped register); this file reads, asks, writes and reports.
//
// Never clobbers a value someone set unless the user picks "Overwrite" — and
// the plan CSV lists every such field before anything is written.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Materials;

namespace StingTools.Commands.Materials
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SyncMaterialIdentityCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                var registry = RegisterAuditCommand.LoadRegistry(out string loadNote);
                var mats = new FilteredElementCollector(doc).OfClass(typeof(Material))
                              .Cast<Material>().OrderBy(m => m.Name).ToList();
                if (mats.Count == 0) { TaskDialog.Show("Sync Material Identity", "This model has no materials."); return Result.Succeeded; }

                var inputs = mats.Select(Read).ToList();
                var plan = MaterialIdentityPlanner.PlanAll(inputs, registry, force: false);
                string path = StingTools.Commands.Baseline.Standardise.Write(
                                  doc, "material_identity_plan", MaterialIdentityPlanner.ToCsv(plan));

                var sb = new StringBuilder(MaterialIdentityPlanner.Summary(plan, applied: false));
                if (registry.Count == 0)
                    sb.AppendLine("\nThe material register did not load, so only materials that already carry MAT_CODE can be synced."
                                  + (string.IsNullOrEmpty(loadNote) ? "" : "\n" + loadNote));
                if (!inputs.Any(i => i.HasSharedCodeParam))
                    sb.AppendLine("\nMAT_CODE is not bound to Materials here — codes come from the register by name only. "
                                  + "Run Load Shared Parameters to bind it.");
                if (path != null) sb.AppendLine("\nPlan: " + path);

                int conflicts = plan.Count(r => r.Conflicts.Count > 0);
                if (!plan.Any(r => r.WillWrite) && conflicts == 0)
                {
                    TaskDialog.Show("Sync Material Identity", sb.ToString());
                    return Result.Succeeded;
                }

                var td = new TaskDialog("Sync Material Identity")
                {
                    MainInstruction = "Give each coded material Mark = code, Keynote = code, Description = short name",
                    MainContent = sb.ToString(),
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Fill empty and STING-written fields",
                    "Leaves every value someone typed. Recommended.");
                if (conflicts > 0)
                    td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, $"Overwrite — also replace the {conflicts} material(s) someone set",
                        "Mark, Keynote and Description become the code and short name everywhere.");
                var pick = td.Show();
                bool force;
                if (pick == TaskDialogResult.CommandLink1) force = false;
                else if (pick == TaskDialogResult.CommandLink2) force = true;
                else return Result.Cancelled;

                if (force) plan = MaterialIdentityPlanner.PlanAll(inputs, registry, force: true);

                int fieldsDone = 0; var failed = new List<string>();
                using (var t = new Transaction(doc, "STING Sync Material Identity"))
                {
                    t.Start();
                    for (int i = 0; i < mats.Count && i < plan.Count; i++)
                        foreach (var w in plan[i].Writes)
                        {
                            try
                            {
                                if (WriteField(mats[i], w)) fieldsDone++;
                                else failed.Add($"{plan[i].MaterialName}: {w.Field} not writable");
                            }
                            catch (Exception ex) { failed.Add($"{plan[i].MaterialName}: {w.Field} — {ex.Message}"); }
                        }
                    t.Commit();
                }

                var res = new StringBuilder(MaterialIdentityPlanner.Summary(plan, applied: true));
                res.AppendLine($"\n{fieldsDone} field(s) written.");
                if (failed.Count > 0) { res.AppendLine("Not written:"); foreach (var f in failed.Take(10)) res.AppendLine("  " + f); }
                TaskDialog.Show("Sync Material Identity", res.ToString());
                StingLog.Info($"Materials_SyncIdentity: {fieldsDone} field(s), force={force}, {failed.Count} failed -> {path}");
                return Result.Succeeded;
            }
            catch (OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("Materials_SyncIdentity", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static MaterialIdentityInput Read(Material m) => new MaterialIdentityInput
        {
            Name = m.Name,
            SharedCode = ParameterHelpers.GetString(m, "MAT_CODE") ?? "",
            Mark = BuiltIn(m, BuiltInParameter.ALL_MODEL_MARK),
            Description = BuiltIn(m, BuiltInParameter.ALL_MODEL_DESCRIPTION),
            Keynote = BuiltIn(m, BuiltInParameter.KEYNOTE_PARAM),
            Specifications = ParameterHelpers.GetString(m, "MAT_SPECIFICATIONS") ?? "",
            HasSharedCodeParam = Has(m, "MAT_CODE"),
            HasSpecificationsParam = Has(m, "MAT_SPECIFICATIONS"),
        };

        private static bool WriteField(Material m, MaterialIdentityWrite w)
        {
            Parameter p;
            switch (w.Field)
            {
                case "Mark":        p = m.get_Parameter(BuiltInParameter.ALL_MODEL_MARK); break;
                case "Description": p = m.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION); break;
                case "Keynote":     p = m.get_Parameter(BuiltInParameter.KEYNOTE_PARAM); break;
                default:            p = m.LookupParameter(w.Field); break;   // MAT_CODE, MAT_SPECIFICATIONS
            }
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) return false;
            return p.Set(w.To ?? "");
        }

        private static string BuiltIn(Material m, BuiltInParameter bip)
        {
            try { return m.get_Parameter(bip)?.AsString() ?? ""; }
            catch (Exception ex) { StingLog.WarnRateLimited("SyncIdentity.Read", $"{m.Name} {bip}: {ex.Message}"); return ""; }
        }

        private static bool Has(Material m, string name)
        {
            try { return m.LookupParameter(name) != null; }
            catch (Exception ex) { StingLog.WarnRateLimited("SyncIdentity.Has", $"{name}: {ex.Message}"); return false; }
        }
    }
}
