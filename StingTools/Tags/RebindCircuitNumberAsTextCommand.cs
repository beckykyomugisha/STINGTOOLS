// RebindCircuitNumberAsTextCommand — PARAM-5.
//
// ELC_CKT_NR became TEXT on 2026-09-24 so it can hold a multi-pole number such
// as "1,3,5". Revit keys a shared parameter by GUID and the GUID did not change,
// so Load Params on a project bound before then leaves the old NUMBER binding in
// place, and every writer's "1,3,5" is refused. The only fix is to remove the
// parameter and bind it again — which, done by hand, throws away every value on
// it. This command does it in one transaction and carries the values across:
//
//   1. read each element's current value as text ("3", not "3.00");
//   2. confirm, showing the count;
//   3. remove the binding and the parameter element;
//   4. re-bind ELC_CKT_NR from MR_PARAMETERS.txt as an instance binding on the
//      RESOLVED_BINDINGS.csv categories, UNIONED with the old binding's
//      categories (the Load Params rule: a rebind never takes a home away, or
//      the values there would have nowhere to go);
//   5. write the values back as text, and report before/after counts and every
//      element whose value did not come back.
//
// Any failure before commit rolls the whole transaction back, so the project is
// either fully converted or untouched.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Electrical;

namespace StingTools.Tags
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RebindCircuitNumberAsTextCommand : IExternalCommand
    {
        private const string Title = "STING - Rebind Circuit Number as Text";
        private const string ParamName = "ELC_CKT_NR";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                return ExecuteCore(commandData);
            }
            catch (Exception ex)
            {
                StingLog.Error("RebindCircuitNumberAsTextCommand crashed", ex);
                TaskDialog.Show(Title, $"Command failed:\n\n{ex.Message}\n\nNothing was changed. See the STING log.");
                return Result.Failed;
            }
        }

        private Result ExecuteCore(ExternalCommandData commandData)
        {
            var uiApp = ParameterHelpers.GetApp(commandData);
            Document doc = uiApp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                TaskDialog.Show(Title, "No document is open.");
                return Result.Failed;
            }
            if (doc.IsFamilyDocument)
            {
                TaskDialog.Show(Title, "This command works on a project, not a family.");
                return Result.Failed;
            }

            // ── 1. Find the current binding ─────────────────────────────────
            InternalDefinition oldDef = null;
            ElementBinding oldBinding = null;
            var iter = doc.ParameterBindings.ForwardIterator();
            while (iter.MoveNext())
            {
                if (iter.Key is InternalDefinition d
                    && string.Equals(d.Name, ParamName, StringComparison.Ordinal))
                {
                    oldDef = d;
                    oldBinding = iter.Current as ElementBinding;
                    break;
                }
            }
            if (oldDef == null)
            {
                TaskDialog.Show(Title,
                    $"{ParamName} is not bound in this project, so there is nothing to convert.\n\n" +
                    "Run Load Params: it binds ELC_CKT_NR as TEXT.");
                return Result.Cancelled;
            }

            ForgeTypeId oldType = oldDef.GetDataType();
            if (oldType == SpecTypeId.String.Text)
            {
                TaskDialog.Show(Title, $"{ParamName} is already bound as TEXT. Nothing to do.");
                return Result.Succeeded;
            }

            // ── 2. The replacement definition, from MR_PARAMETERS.txt ───────
            //    Resolved BEFORE anything is touched: without it there is nothing
            //    to rebind to, and deleting first would lose the values.
            ExternalDefinition newDef = ResolveSharedDefinition(uiApp.Application, out string defError);
            if (newDef == null)
            {
                TaskDialog.Show(Title, defError + "\n\nNothing was changed.");
                return Result.Failed;
            }
            if (newDef.GetDataType() != SpecTypeId.String.Text)
            {
                TaskDialog.Show(Title,
                    $"MR_PARAMETERS.txt defines {ParamName} as {SafeTypeLabel(newDef.GetDataType())}, not TEXT, " +
                    "so re-binding from it would not help.\n\nNothing was changed.");
                return Result.Failed;
            }

            // ── 3. Read every current value as text ─────────────────────────
            var oldCatIds = new List<ElementId>();
            if (oldBinding?.Categories != null)
                foreach (Category c in oldBinding.Categories) oldCatIds.Add(c.Id);

            var values = new List<(ElementId Id, string Text)>();
            int withParam = 0;
            if (oldCatIds.Count > 0)
            {
                var collector = new FilteredElementCollector(doc)
                    .WherePasses(new ElementMulticategoryFilter(oldCatIds));
                foreach (Element el in collector)
                {
                    Parameter p = el.get_Parameter(oldDef);
                    if (p == null) continue;
                    withParam++;
                    string text = ReadAsText(p);
                    if (!string.IsNullOrEmpty(text)) values.Add((el.Id, text));
                }
            }

            string oldCatNames = oldBinding?.Categories == null ? "(none)"
                : string.Join(", ", oldBinding.Categories.Cast<Category>().Select(c => c.Name).OrderBy(n => n));

            // ── 4. Confirm ──────────────────────────────────────────────────
            var confirm = new TaskDialog(Title)
            {
                MainInstruction = $"Re-bind {ParamName} as TEXT?",
                MainContent =
                    $"{ParamName} is bound as {SafeTypeLabel(oldType)} " +
                    $"({(oldBinding is TypeBinding ? "type" : "instance")} binding).\n" +
                    $"{withParam} element(s) carry it; {values.Count} have a value.\n\n" +
                    "The parameter will be removed and bound again as TEXT from MR_PARAMETERS.txt, " +
                    "and every value will be written back as text (3.00 becomes 3). " +
                    "It runs as one transaction: if anything fails, nothing changes.\n\n" +
                    $"Current categories: {oldCatNames}",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };
            if (confirm.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            // ── 5. Remove, re-bind, restore ─────────────────────────────────
            CategorySet newCats = BuildCategorySet(doc, oldBinding, out List<string> specMissing);
            if (newCats.IsEmpty)
            {
                TaskDialog.Show(Title,
                    $"No category to bind {ParamName} to (RESOLVED_BINDINGS.csv row missing and no old binding).\n\nNothing was changed.");
                return Result.Failed;
            }

            var notRestored = new List<string>();
            int restored = 0;
            ForgeTypeId group = SafeGroup(oldDef);

            using (var tx = new Transaction(doc, "STING Rebind ELC_CKT_NR as Text"))
            {
                var fho = tx.GetFailureHandlingOptions();
                fho.SetFailuresPreprocessor(new BindingWarningSwallower());
                tx.SetFailureHandlingOptions(fho);
                tx.Start();

                doc.ParameterBindings.Remove(oldDef);
                Element paramElem = doc.GetElement(oldDef.Id);
                if (paramElem != null) doc.Delete(paramElem.Id);
                doc.Regenerate();

                bool inserted = doc.ParameterBindings.Insert(
                    newDef, doc.Application.Create.NewInstanceBinding(newCats), group);
                if (!inserted)
                {
                    tx.RollBack();
                    TaskDialog.Show(Title,
                        $"Revit refused to bind {ParamName} as TEXT. The change was rolled back; nothing was changed.");
                    return Result.Failed;
                }
                doc.Regenerate();

                foreach (var (id, text) in values)
                {
                    Element el = doc.GetElement(id);
                    Parameter p = el?.get_Parameter(newDef.GUID);
                    if (p == null)
                    {
                        notRestored.Add($"{id.Value} ('{text}'): parameter not on the element after re-binding");
                        continue;
                    }
                    if (p.IsReadOnly)
                    {
                        notRestored.Add($"{id.Value} ('{text}'): parameter is read-only");
                        continue;
                    }
                    try
                    {
                        if (p.Set(text) && p.AsString() == text) restored++;
                        else notRestored.Add($"{id.Value} ('{text}'): value did not stick");
                    }
                    catch (Exception ex)
                    {
                        notRestored.Add($"{id.Value} ('{text}'): {ex.Message}");
                    }
                }

                if (tx.Commit() != TransactionStatus.Committed)
                {
                    TaskDialog.Show(Title, "The transaction did not commit; nothing was changed.");
                    return Result.Failed;
                }
            }

            // ── 6. After-count ──────────────────────────────────────────────
            int afterWith = 0, afterValues = 0;
            ForgeTypeId afterType = null;
            var afterIter = doc.ParameterBindings.ForwardIterator();
            while (afterIter.MoveNext())
                if (afterIter.Key is InternalDefinition d && d.Name == ParamName) { afterType = d.GetDataType(); break; }
            foreach (Element el in new FilteredElementCollector(doc)
                         .WherePasses(new ElementMulticategoryFilter(newCats.Cast<Category>().Select(c => c.Id).ToList())))
            {
                Parameter p = el.get_Parameter(newDef.GUID);
                if (p == null) continue;
                afterWith++;
                if (!string.IsNullOrEmpty(p.AsString())) afterValues++;
            }

            foreach (var line in notRestored) StingLog.Warn($"Rebind {ParamName}: not restored — {line}");
            StingLog.Info($"Rebind {ParamName}: {SafeTypeLabel(oldType)} -> {SafeTypeLabel(afterType)}; " +
                          $"before {withParam} carrying / {values.Count} valued; after {afterWith} carrying / {afterValues} valued; " +
                          $"restored {restored}, not restored {notRestored.Count}");

            var sb = new StringBuilder();
            sb.AppendLine($"{ParamName}: {SafeTypeLabel(oldType)} → {SafeTypeLabel(afterType)}");
            sb.AppendLine();
            sb.AppendLine($"Before: {withParam} element(s) carried it, {values.Count} with a value.");
            sb.AppendLine($"After:  {afterWith} element(s) carry it, {afterValues} with a value.");
            sb.AppendLine($"Values restored: {restored} of {values.Count}.");
            if (specMissing.Count > 0)
                sb.AppendLine($"\nRESOLVED_BINDINGS.csv categories not in this project: {string.Join(", ", specMissing)}");
            if (notRestored.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"NOT RESTORED ({notRestored.Count}) — element id ('old value'): reason");
                foreach (var line in notRestored.Take(15)) sb.AppendLine("  " + line);
                if (notRestored.Count > 15) sb.AppendLine($"  … {notRestored.Count - 15} more in the STING log");
            }
            TaskDialog.Show(Title, sb.ToString().TrimEnd());
            return notRestored.Count == 0 ? Result.Succeeded : Result.Failed;
        }

        /// <summary>Current value as text; a NUMBER reads "3", not "3.00".</summary>
        private static string ReadAsText(Parameter p)
        {
            if (p == null || !p.HasValue) return string.Empty;
            switch (p.StorageType)
            {
                case StorageType.String:  return p.AsString() ?? string.Empty;
                case StorageType.Double:  return CircuitNumberText.FromNumber(p.AsDouble(), p.AsValueString());
                case StorageType.Integer: return p.AsInteger().ToString(System.Globalization.CultureInfo.InvariantCulture);
                default:                  return p.AsValueString() ?? string.Empty;
            }
        }

        /// <summary>
        /// ELC_CKT_NR from MR_PARAMETERS.txt. The Revit shared-parameter file setting
        /// is restored afterwards, as the user had it.
        /// </summary>
        private static ExternalDefinition ResolveSharedDefinition(
            Autodesk.Revit.ApplicationServices.Application app, out string error)
        {
            error = null;
            string previous = app.SharedParametersFilename;
            string mrPath = LoadSharedParamsCommand.FindMrParametersFile(previous);
            if (string.IsNullOrEmpty(mrPath))
            {
                error = "Could not find MR_PARAMETERS.txt.";
                return null;
            }
            try
            {
                app.SharedParametersFilename = mrPath;
                DefinitionFile file = app.OpenSharedParameterFile();
                if (file == null)
                {
                    error = "Could not open the shared parameter file:\n" + mrPath;
                    return null;
                }
                foreach (DefinitionGroup g in file.Groups)
                    foreach (Definition d in g.Definitions)
                        if (d is ExternalDefinition ext && string.Equals(ext.Name, ParamName, StringComparison.Ordinal))
                            return ext;
                error = $"{ParamName} is not defined in {mrPath}.";
                return null;
            }
            finally
            {
                try
                {
                    if (!string.IsNullOrEmpty(previous) && previous != mrPath)
                        app.SharedParametersFilename = previous;
                }
                catch (Exception ex) { StingLog.Warn($"Rebind {ParamName}: restoring shared parameter file setting failed — {ex.Message}"); }
            }
        }

        /// <summary>
        /// RESOLVED_BINDINGS.csv categories for ELC_CKT_NR, unioned with the old
        /// binding's categories so no value loses its home.
        /// </summary>
        private static CategorySet BuildCategorySet(Document doc, ElementBinding oldBinding, out List<string> specMissing)
        {
            specMissing = new List<string>();
            var set = new CategorySet();
            var seen = new HashSet<long>();

            if (SharedParamGuids.ResolvedScopedBindings.TryGetValue(ParamName, out BuiltInCategory[] bics))
            {
                foreach (var bic in bics)
                {
                    Category c = null;
                    try { c = Category.GetCategory(doc, bic); }
                    catch (Exception ex) { StingLog.Warn($"Rebind {ParamName}: category {bic} — {ex.Message}"); }
                    if (c == null || !c.AllowsBoundParameters) { specMissing.Add(bic.ToString()); continue; }
                    if (seen.Add(c.Id.Value)) set.Insert(c);
                }
            }
            else
            {
                StingLog.Warn($"Rebind {ParamName}: no RESOLVED_BINDINGS.csv row; keeping the old binding's categories only.");
            }

            if (oldBinding?.Categories != null)
                foreach (Category c in oldBinding.Categories)
                    if (c != null && c.AllowsBoundParameters && seen.Add(c.Id.Value)) set.Insert(c);

            return set;
        }

        private static ForgeTypeId SafeGroup(InternalDefinition def)
        {
            try { return def.GetGroupTypeId() ?? GroupTypeId.General; }
            catch (Exception ex)
            {
                StingLog.Warn($"Rebind {ParamName}: reading the old parameter group failed — {ex.Message}; using General.");
                return GroupTypeId.General;
            }
        }

        private static string SafeTypeLabel(ForgeTypeId t)
        {
            if (t == null) return "(unbound)";
            try { return LabelUtils.GetLabelForSpec(t); }
            catch (Exception ex)
            {
                StingLog.Warn($"Rebind {ParamName}: spec label for {t.TypeId} — {ex.Message}");
                return t.TypeId;
            }
        }
    }
}
