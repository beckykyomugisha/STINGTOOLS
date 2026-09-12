// ══════════════════════════════════════════════════════════════════════════
//  StampMaterialCodesCommand.cs — Materials_StampCodes.
//
//  Writes the governed register's MAT_CODE onto the materials that have none,
//  matching by exact MAT_NAME. Sits beside Materials_RegisterAudit on the SETUP
//  tab: the audit says what disagrees, this says what the register can name.
//
//  It exists because MAT_CODE is the key the BOQ's most specific rate lookup
//  uses — RateProviders Pass C, confidence 80 — and only one of the two paths
//  that mint materials ever wrote it. CompoundTypeCreator, which is what builds
//  a project's host-type catalogue, read the register row and discarded the
//  code; a model whose materials predate that work has none at all.
//
//  ── THE SHAPE, DELIBERATELY THE SAME AS Materials_SetClass ────────────────
//  Plan CSV FIRST, then a TaskDialog that defaults to No. The plan is written
//  whether or not the user says yes, so a decision to decline still leaves the
//  three counts on disk. Nothing about this is reversible by accident: it only
//  ever fills a blank.
//
//  The decision itself is in MaterialCodeStampPlanner — Revit-free, tested
//  against the shipped 1,279-row register and the 1,815-name corpus. This file
//  reads parameters and writes them and does no thinking of its own.
// ══════════════════════════════════════════════════════════════════════════
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
    public class StampMaterialCodesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                var registry = RegisterAuditCommand.LoadRegistry(out string loadNote);
                if (registry.Count == 0)
                {
                    // A register that loaded nothing must say so rather than report
                    // "0 materials could be coded", which is the same sentence a healthy
                    // run with nothing to do would produce.
                    TaskDialog.Show("Stamp Material Codes",
                        "The material register did not load, so there is nothing to stamp "
                      + "from:\n\n" + loadNote);
                    return Result.Failed;
                }

                var mats = new FilteredElementCollector(doc).OfClass(typeof(Material))
                              .Cast<Material>().OrderBy(m => m.Name).ToList();
                if (mats.Count == 0)
                {
                    TaskDialog.Show("Stamp Material Codes", "This model has no materials.");
                    return Result.Succeeded;
                }

                // MAT_CODE is bound to Materials and to nothing else. If the binding is
                // absent the read returns empty for every material and the plan would
                // propose stamping the whole model — a write that then silently does
                // nothing, because SetIfEmpty cannot find the parameter either. Say it.
                bool bound = mats.Any(m => SafeHasMatCode(m));
                if (!bound)
                {
                    TaskDialog.Show("Stamp Material Codes",
                        "MAT_CODE is not bound to Materials in this model, so there is nowhere "
                      + "to write the code.\n\nRun Load Shared Parameters first — MAT_CODE is "
                      + "declared in MR_PARAMETERS.txt and binds to the Materials category.");
                    return Result.Failed;
                }

                var byMat = new Dictionary<Material, MaterialCodeStampRow>();
                var input = new List<KeyValuePair<string, string>>();
                foreach (var m in mats)
                    input.Add(new KeyValuePair<string, string>(
                        m.Name, ParameterHelpers.GetString(m, "MAT_CODE") ?? ""));

                var rows = MaterialCodeStampPlanner.PlanAll(input, registry);
                for (int i = 0; i < mats.Count && i < rows.Count; i++) byMat[mats[i]] = rows[i];

                string path = StingTools.Commands.Baseline.Standardise.Write(
                                  doc, "material_code_plan", MaterialCodeStampPlanner.ToCsv(rows));

                var todo = byMat.Where(kv => kv.Value.WillWrite).ToList();
                var tally = MaterialCodeStampPlanner.Tally(rows);

                var sb = new StringBuilder();
                sb.AppendLine(MaterialCodeStampPlanner.Summary(rows));
                if (!string.IsNullOrEmpty(loadNote)) { sb.AppendLine(loadNote); sb.AppendLine(); }
                sb.AppendLine("MAT_CODE is what the BOQ's most specific rate lookup keys on. A "
                            + "material without one is priced by category and name instead.");
                foreach (var kv in todo.Take(12))
                    sb.AppendLine("  " + kv.Value.MaterialName + "  →  " + kv.Value.RegisterCode);
                if (todo.Count > 12) sb.AppendLine("  … and " + (todo.Count - 12) + " more, in the CSV");
                if (tally.Disagreements > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(tally.Disagreements + " material(s) already carry a code the "
                                + "register contradicts. They are in the CSV and this changes "
                                + "none of them.");
                }
                if (path != null) { sb.AppendLine(); sb.AppendLine("Plan: " + path); }

                if (todo.Count == 0)
                {
                    TaskDialog.Show("Stamp Material Codes", sb.ToString());
                    StingLog.Info("Materials_StampCodes: nothing to stamp; " + tally.AlreadyCoded
                                + " coded, " + tally.NoRegisterRow + " not in register -> " + path);
                    return Result.Succeeded;
                }

                var td = new TaskDialog("Stamp Material Codes")
                {
                    MainInstruction = todo.Count + " material(s) will be given their register code",
                    MainContent = sb.ToString() + Environment.NewLine + "Apply?",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.No,
                };
                if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;

                int done = 0;
                var failed = new List<string>();
                using (var t = new Transaction(doc, "STING Stamp Material Codes"))
                {
                    t.Start();
                    foreach (var kv in todo)
                    {
                        try
                        {
                            if (ParameterHelpers.SetIfEmpty(kv.Key, "MAT_CODE", kv.Value.RegisterCode))
                                done++;
                            else
                                failed.Add(kv.Value.MaterialName + " — MAT_CODE not writable");
                        }
                        catch (Exception ex) { failed.Add(kv.Value.MaterialName + " — " + ex.Message); }
                    }
                    t.Commit();
                }

                var res = new StringBuilder("Stamped " + done + " of " + todo.Count + " material(s).");
                res.AppendLine();
                res.AppendLine(tally.AlreadyCoded + " already had a code and were not touched.");
                res.AppendLine(tally.NoRegisterRow + " are not in the register — this project's own "
                             + "vocabulary, and what the next register revision should absorb.");
                if (failed.Count > 0)
                {
                    res.AppendLine();
                    res.AppendLine("Not stamped:");
                    foreach (string f in failed.Take(10)) res.AppendLine("  " + f);
                }
                TaskDialog.Show("Stamp Material Codes", res.ToString());
                StingLog.Info("Materials_StampCodes: " + done + "/" + todo.Count + " stamped, "
                            + tally.AlreadyCoded + " already coded, " + tally.NoRegisterRow
                            + " not in register, " + failed.Count + " failed -> " + path);
                return Result.Succeeded;
            }
            catch (OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("Materials_StampCodes", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>Is MAT_CODE actually bound here? A guarded LookupParameter, because
        /// an unbound parameter and an empty one read identically through GetString and
        /// the difference decides whether this command can do anything at all.</summary>
        private static bool SafeHasMatCode(Material m)
        {
            try { return m?.LookupParameter("MAT_CODE") != null; }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("StampCodes.Bind", "LookupParameter MAT_CODE: " + ex.Message);
                return false;
            }
        }
    }
}
