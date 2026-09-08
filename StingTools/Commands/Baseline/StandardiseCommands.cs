// ══════════════════════════════════════════════════════════════════════════
//  StandardiseCommands.cs — the two bulk operations the house standard needs.
//
//    Baseline_RenameTypes   propose house-standard names for existing host
//                           types, from the model's OWN materials
//    Materials_SetClass     fill Revit's Material Class where it is blank
//
//  Both are two-step by design: the audit form writes a CSV and changes
//  nothing; the apply form asks first and reports what it did. A rename
//  changes what schedules, filters and view templates key on, and a Class is
//  read by the carbon and cost engines — neither is a thing to do silently.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Baseline;
using StingTools.Core.Materials;

namespace StingTools.Commands.Baseline
{
    internal static class Standardise
    {
        internal static string Csv(string s)
        {
            s = s ?? "";
            return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        internal static string Write(Document doc, string stem, IEnumerable<string> rows)
        {
            try
            {
                string dir = StingPaths.Meta(doc, "_BIM_COORD");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"{stem}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                File.WriteAllLines(path, rows, Encoding.UTF8);
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"{stem} CSV: {ex.Message}"); return null; }
        }

        /// <summary>Every layered host type in the document, with its layers flattened
        /// and its instance count — the planner's input.</summary>
        internal static List<TypeRenameInput> ReadHostTypes(Document doc)
        {
            var counts = new Dictionary<long, int>();
            foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType()
                                      .OfClass(typeof(HostObject)))
            {
                var tid = el.GetTypeId();
                if (tid == null || tid.Value <= 0) continue;
                counts.TryGetValue(tid.Value, out int n);
                counts[tid.Value] = n + 1;
            }

            var outList = new List<TypeRenameInput>();
            foreach (var t in new FilteredElementCollector(doc).OfClass(typeof(HostObjAttributes))
                                 .Cast<HostObjAttributes>())
            {
                string cat = t.Category?.Name;
                if (string.IsNullOrEmpty(cat)) continue;

                CompoundStructure cs = null;
                try { cs = t.GetCompoundStructure(); }
                catch (Exception ex) { StingLog.WarnRateLimited("Std.CS", $"GetCompoundStructure {t.Id}: {ex.Message}"); }
                if (cs == null) continue;

                var layers = new List<MaterialLayer>();
                var cls = cs.GetLayers();
                for (int i = 0; i < cls.Count; i++)
                {
                    var cl = cls[i];
                    string name = null;
                    if (cl.MaterialId != null && cl.MaterialId.Value > 0)
                        name = doc.GetElement(cl.MaterialId)?.Name;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    layers.Add(new MaterialLayer
                    {
                        Index = i,
                        MaterialName = name,
                        ThicknessMm = cl.Width * 304.8,
                        IsStructure = cl.Function == MaterialFunctionAssignment.Structure,
                    });
                }

                counts.TryGetValue(t.Id.Value, out int used);
                outList.Add(new TypeRenameInput
                {
                    Category = cat, CurrentName = t.Name, Layers = layers, InstanceCount = used,
                });
            }
            return outList;
        }
    }

    /// <summary>
    /// Baseline_RenameTypes — propose house-standard names for the host types this
    /// model already has, reading the substance off each type's own core material.
    /// Writes a CSV and, on confirmation, applies the renames.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RenameTypesToStandardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                var plans = TypeRenamePlanner.PlanAll(Standardise.ReadHostTypes(doc));
                if (plans.Count == 0)
                {
                    TaskDialog.Show("Rename Types", "No layered host types found in this model.");
                    return Result.Succeeded;
                }

                var rows = new List<string> { "Category,CurrentName,ProposedName,Instances,Outcome,Reason" };
                foreach (var p in plans.OrderBy(x => x.Category).ThenBy(x => x.CurrentName))
                    rows.Add(string.Join(",", Standardise.Csv(p.Category), Standardise.Csv(p.CurrentName),
                        Standardise.Csv(p.ProposedName ?? ""), p.InstanceCount,
                        p.AlreadyConforms ? "conforms" : p.IsProposal ? "rename" : "cannot name",
                        Standardise.Csv(p.Reason)));
                string path = Standardise.Write(doc, "type_rename_plan", rows);

                var todo = plans.Where(p => p.IsProposal && !p.AlreadyConforms).ToList();
                var sb = new StringBuilder();
                sb.AppendLine(TypeRenamePlanner.Summary(plans));
                sb.AppendLine();
                foreach (var p in todo.OrderByDescending(p => p.InstanceCount).Take(12))
                    sb.AppendLine($"  {p.InstanceCount,4} x  {p.CurrentName}  →  {p.ProposedName}");
                if (todo.Count > 12) sb.AppendLine($"  … and {todo.Count - 12} more, in the CSV");
                if (path != null) { sb.AppendLine(); sb.AppendLine("Plan: " + path); }

                if (todo.Count == 0)
                {
                    TaskDialog.Show("Rename Types", sb.ToString());
                    return Result.Succeeded;
                }

                sb.AppendLine();
                sb.AppendLine("A rename changes what schedules, filters and view templates key on. "
                            + "Apply these now?");
                var td = new TaskDialog("Rename Types to House Standard")
                {
                    MainInstruction = $"{todo.Count} type(s) can be renamed",
                    MainContent = sb.ToString(),
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.No,
                };
                if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;

                int done = 0; var failed = new List<string>();
                using (var t = new Transaction(doc, "STING Rename Types to Standard"))
                {
                    t.Start();
                    // Name by name, so one clash does not lose the rest.
                    foreach (var p in todo)
                    {
                        var type = new FilteredElementCollector(doc).OfClass(typeof(HostObjAttributes))
                            .FirstOrDefault(e => string.Equals(e.Name, p.CurrentName, StringComparison.Ordinal)
                                              && string.Equals(e.Category?.Name, p.Category, StringComparison.OrdinalIgnoreCase));
                        if (type == null) { failed.Add($"{p.CurrentName} — no longer present"); continue; }
                        try { type.Name = p.ProposedName; done++; }
                        catch (Exception ex) { failed.Add($"{p.CurrentName} — {ex.Message}"); }
                    }
                    t.Commit();
                }

                var res = new StringBuilder($"Renamed {done} of {todo.Count} type(s).");
                if (failed.Count > 0)
                {
                    res.AppendLine().AppendLine();
                    res.AppendLine("Not renamed — most often a name already in use:");
                    foreach (string f in failed.Take(10)) res.AppendLine("  " + f);
                }
                TaskDialog.Show("Rename Types", res.ToString());
                StingLog.Info($"Baseline_RenameTypes: {done}/{todo.Count} renamed, {failed.Count} failed -> {path}");
                return Result.Succeeded;
            }
            catch (OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("Baseline_RenameTypes", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Materials_SetClass — fill Revit's Material Class where it is blank, from the
    /// material's own name. Never overwrites a class somebody already chose, and never
    /// guesses one for a name that says nothing.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetMaterialClassCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                var mats = new FilteredElementCollector(doc).OfClass(typeof(Material))
                              .Cast<Material>().OrderBy(m => m.Name).ToList();
                if (mats.Count == 0)
                {
                    TaskDialog.Show("Material Class", "This model has no materials.");
                    return Result.Succeeded;
                }

                var plans = mats.Select(m => new
                {
                    Mat = m,
                    Plan = MaterialClassPlanner.Plan(m.Name, SafeClass(m)),
                }).ToList();

                var rows = new List<string> { "Material,ExistingClass,ProposedClass,Reason" };
                foreach (var x in plans)
                    rows.Add(string.Join(",", Standardise.Csv(x.Plan.MaterialName),
                        Standardise.Csv(x.Plan.ExistingClass), Standardise.Csv(x.Plan.ProposedClass ?? ""),
                        Standardise.Csv(x.Plan.Reason)));
                string path = Standardise.Write(doc, "material_class_plan", rows);

                var todo = plans.Where(x => x.Plan.WillWrite).ToList();
                var sb = new StringBuilder();
                sb.AppendLine(MaterialClassPlanner.Summary(plans.Select(x => x.Plan).ToList()));
                sb.AppendLine();
                sb.AppendLine("Class is read by the embodied-carbon and BOQ cost engines. A material "
                            + "with none is answered by nothing at all.");
                foreach (var x in todo.Take(12))
                    sb.AppendLine($"  {x.Plan.MaterialName}  →  {x.Plan.ProposedClass}");
                if (todo.Count > 12) sb.AppendLine($"  … and {todo.Count - 12} more, in the CSV");
                if (path != null) { sb.AppendLine(); sb.AppendLine("Plan: " + path); }

                if (todo.Count == 0)
                {
                    TaskDialog.Show("Material Class", sb.ToString());
                    return Result.Succeeded;
                }

                var td = new TaskDialog("Set Material Class")
                {
                    MainInstruction = $"{todo.Count} material(s) will be classified",
                    MainContent = sb.ToString() + Environment.NewLine + "Apply?",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.No,
                };
                if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;

                int done = 0; var failed = new List<string>();
                using (var t = new Transaction(doc, "STING Set Material Class"))
                {
                    t.Start();
                    foreach (var x in todo)
                    {
                        try { x.Mat.MaterialClass = x.Plan.ProposedClass; done++; }
                        catch (Exception ex) { failed.Add($"{x.Plan.MaterialName} — {ex.Message}"); }
                    }
                    t.Commit();
                }

                var res = new StringBuilder($"Classified {done} of {todo.Count} material(s).");
                if (failed.Count > 0)
                {
                    res.AppendLine().AppendLine("Not set:");
                    foreach (string f in failed.Take(10)) res.AppendLine("  " + f);
                }
                TaskDialog.Show("Material Class", res.ToString());
                StingLog.Info($"Materials_SetClass: {done}/{todo.Count} set, {failed.Count} failed -> {path}");
                return Result.Succeeded;
            }
            catch (OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("Materials_SetClass", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string SafeClass(Material m)
        {
            try { return m?.MaterialClass ?? ""; }
            catch (Exception ex) { StingLog.WarnRateLimited("Std.MatCls", $"MaterialClass: {ex.Message}"); return ""; }
        }
    }
}
