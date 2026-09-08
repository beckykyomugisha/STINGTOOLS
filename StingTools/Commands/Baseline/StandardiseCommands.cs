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

                // FamilyName is half of what the PROD rules match against ("Basic Wall
                // Exterior_CreamWhite_230"), so without it the planner cannot ask what the
                // type resolves to today and the code gate cannot fire.
                string fam = null;
                try { fam = t.FamilyName; }
                catch (Exception ex) { StingLog.WarnRateLimited("Std.Fam", $"FamilyName {t.Id}: {ex.Message}"); }

                outList.Add(new TypeRenameInput
                {
                    Category = cat, FamilyName = fam ?? "", CurrentName = t.Name,
                    Layers = layers, InstanceCount = used,
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

                // The resolver is passed IN, so TypeRenamePlanner stays Revit-free and the
                // rename asks the same question ProdResolver answers for every tag.
                var plans = TypeRenamePlanner.PlanAll(
                    Standardise.ReadHostTypes(doc),
                    i =>
                    {
                        string code = TagConfig.ResolveProdForNames(
                            doc, i.FamilyName, i.CurrentName, i.Category, out string src);
                        return new ExistingProdCode
                        {
                            Code = code, Source = src, IsSpecific = ProdResolver.IsSpecific(src),
                        };
                    });
                if (plans.Count == 0)
                {
                    TaskDialog.Show("Rename Types", "No layered host types found in this model.");
                    return Result.Succeeded;
                }

                var rows = new List<string>
                { "Category,CurrentName,ProposedName,Instances,Outcome,ExistingCode,ExistingSource,DeclaredCode,Reason" };
                foreach (var p in plans.OrderBy(x => x.Category).ThenBy(x => x.CurrentName))
                    rows.Add(string.Join(",", Standardise.Csv(p.Category), Standardise.Csv(p.CurrentName),
                        Standardise.Csv(p.ProposedName ?? ""), p.InstanceCount,
                        p.AlreadyConforms ? "conforms"
                            : p.IsProposal ? "rename"
                            : p.RefusedToProtectCode ? "refused - would change the product code"
                            : "cannot name",
                        Standardise.Csv(p.Existing?.Code ?? ""), Standardise.Csv(p.Existing?.Source ?? ""),
                        Standardise.Csv(p.DeclaredCode ?? ""),
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

        /// <summary>The same guarded read, for the revert command next door.</summary>
        internal static string ReadClassOf(Material m) => SafeClass(m);
    }

    /// <summary>
    /// Materials_RevertClassPlan — put back the classes a material_class_plan set.
    ///
    /// Materials_SetClass never overwrites a class somebody already chose. That rule is
    /// right, and on 2026-09-08 it is what left 41 wrong classes stuck in a delivered
    /// model: once the tool had written them, the tool's own guard protected them, and
    /// re-running the corrected planner changed nothing.
    ///
    /// So this reads the plan CSV that run wrote — the provenance of exactly what was set
    /// and to what — and reverts only where the material still carries what that plan
    /// proposed. Anything changed since belongs to whoever changed it and is reported,
    /// not overwritten.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RevertMaterialClassCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                string startIn = null;
                try { startIn = StingPaths.Meta(doc, "_BIM_COORD"); }
                catch (Exception ex) { StingLog.Warn("Revert: coord folder: " + ex.Message); }

                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Pick the material_class_plan CSV to undo",
                    Filter = "Material class plan (material_class_plan_*.csv)|material_class_plan_*.csv"
                           + "|CSV (*.csv)|*.csv",
                    DefaultExt = ".csv",
                };
                if (!string.IsNullOrEmpty(startIn) && Directory.Exists(startIn)) dlg.InitialDirectory = startIn;
                if (dlg.ShowDialog() != true) return Result.Cancelled;

                var planRows = MaterialClassRevertPlanner.ReadPlan(
                    File.ReadAllLines(dlg.FileName, Encoding.UTF8), out string readError);
                if (readError != null)
                {
                    // A wrong file must say so. "0 to revert" would read as "already clean".
                    TaskDialog.Show("Revert Material Class",
                        $"Cannot read {Path.GetFileName(dlg.FileName)} — {readError}");
                    return Result.Failed;
                }

                // Current class per material name. Duplicate names cannot be told apart by
                // name alone, so the first wins and the rest are reported as ambiguous
                // rather than reverted on a coin toss.
                var current = new Dictionary<string, string>(StringComparer.Ordinal);
                var ambiguous = new HashSet<string>(StringComparer.Ordinal);
                var byName = new Dictionary<string, Material>(StringComparer.Ordinal);
                foreach (Material m in new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>())
                {
                    string n = m.Name ?? "";
                    if (current.ContainsKey(n)) { ambiguous.Add(n); continue; }
                    current[n] = SetMaterialClassCommand.ReadClassOf(m);
                    byName[n] = m;
                }

                var ps = MaterialClassRevertPlanner.PlanAll(planRows, current);
                var todo = ps.Where(x => x.WillRevert && !ambiguous.Contains(x.MaterialName)).ToList();

                var rows = new List<string> { "Material,PlannedClass,CurrentClass,RestoreTo,WillRevert,Reason" };
                foreach (var p in ps)
                    rows.Add(string.Join(",", Standardise.Csv(p.MaterialName), Standardise.Csv(p.PlannedClass),
                        Standardise.Csv(p.CurrentClass ?? "(not in model)"), Standardise.Csv(p.RestoreTo),
                        p.WillRevert && !ambiguous.Contains(p.MaterialName) ? "yes" : "no",
                        Standardise.Csv(ambiguous.Contains(p.MaterialName)
                            ? "more than one material has this name — cannot tell them apart" : p.Reason)));
                string path = Standardise.Write(doc, "material_class_revert", rows);

                var sb = new StringBuilder();
                sb.AppendLine("Plan read: " + Path.GetFileName(dlg.FileName));
                sb.AppendLine();
                sb.AppendLine(MaterialClassRevertPlanner.Summary(ps));
                if (ambiguous.Count > 0)
                    sb.AppendLine($"{ambiguous.Count} name(s) belong to more than one material and are "
                                + "left alone — they cannot be told apart by name.");
                foreach (var p in todo.Take(12))
                    sb.AppendLine($"  {p.MaterialName}  {p.PlannedClass}  →  "
                                + (p.RestoreTo.Length == 0 ? "(no class)" : p.RestoreTo));
                if (todo.Count > 12) sb.AppendLine($"  … and {todo.Count - 12} more, in the CSV");
                if (path != null) { sb.AppendLine(); sb.AppendLine("Revert plan: " + path); }

                if (todo.Count == 0)
                {
                    TaskDialog.Show("Revert Material Class", sb.ToString());
                    return Result.Succeeded;
                }

                var td = new TaskDialog("Revert Material Class")
                {
                    MainInstruction = $"{todo.Count} material(s) will go back to how the plan found them",
                    MainContent = sb.ToString() + Environment.NewLine + "Apply?",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.No,
                };
                if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;

                int done = 0; var failed = new List<string>();
                using (var t = new Transaction(doc, "STING Revert Material Class"))
                {
                    t.Start();
                    foreach (var p in todo)
                    {
                        if (!byName.TryGetValue(p.MaterialName, out Material m) || m == null)
                        { failed.Add($"{p.MaterialName} — no longer present"); continue; }

                        // Revit spells "no class" two ways and which one the API accepts is
                        // a version question this code cannot answer for itself. Try the
                        // value the plan recorded, then the other spelling, then give up
                        // NOISILY — a swallowed failure here looks exactly like a success.
                        if (TrySetClass(m, p.RestoreTo, out string e1)) { done++; continue; }
                        string alt = p.RestoreTo.Length == 0 ? "Unassigned" : "";
                        if (TrySetClass(m, alt, out string e2)) { done++; continue; }
                        failed.Add($"{p.MaterialName} — {e1}; and as '{Describe(alt)}': {e2}");
                    }
                    t.Commit();
                }

                var res = new StringBuilder($"Reverted {done} of {todo.Count} material(s).");
                if (failed.Count > 0)
                {
                    res.AppendLine().AppendLine().AppendLine("Not reverted:");
                    foreach (string f in failed.Take(10)) res.AppendLine("  " + f);
                    if (failed.Count > 10) res.AppendLine($"  … and {failed.Count - 10} more, in the log");
                }
                if (path != null) { res.AppendLine().AppendLine("Revert plan: " + path); }
                TaskDialog.Show("Revert Material Class", res.ToString());
                StingLog.Info($"Materials_RevertClassPlan: {done}/{todo.Count} reverted, "
                            + $"{failed.Count} failed, from {dlg.FileName} -> {path}");
                foreach (string f in failed) StingLog.Warn("Materials_RevertClassPlan: " + f);
                return Result.Succeeded;
            }
            catch (OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("Materials_RevertClassPlan", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static bool TrySetClass(Material m, string value, out string error)
        {
            error = null;
            try { m.MaterialClass = value; return true; }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private static string Describe(string cls) => cls.Length == 0 ? "(blank)" : cls;
    }
}
