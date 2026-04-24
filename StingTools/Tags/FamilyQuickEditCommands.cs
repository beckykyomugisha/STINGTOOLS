using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using StingTools.Core;
using StingTools.Select;
using StingTools.UI;

namespace StingTools.Tags
{
    // ════════════════════════════════════════════════════════════════════════════
    //  FAMILY QUICK-EDIT COMMANDS
    //
    //  Four commands that sit next to the model-creation tools so a user can fix
    //  family problems without leaving the modelling flow:
    //    - OpenFamilyQuickEditCommand : launches the dialog that dispatches below
    //    - ChangeHostCommand          : instance-level rehost (delete + recreate)
    //    - SwapCategoryCommand        : EditFamily + change FamilyCategory + reload
    //    - InjectAutomationPackCommand: injects automation/presentation param pack
    //
    //  All four respect the existing transaction + IFamilyLoadOptions pattern used
    //  by FamilyParamCreatorCommand.
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>Compatibility matrix for <see cref="SwapCategoryCommand"/>.
    /// Revit's family-category swap only succeeds between categories that share a
    /// family-template lineage. System families and hosted-template families
    /// (Doors / Windows) are immutable. Model-family categories (Generic Models,
    /// Furniture, Casework, Specialty Equipment, MEP equipment &amp; fixtures) all
    /// share the Metric Generic Model lineage and can freely interchange.</summary>
    internal static class FamilyCategoryCompatibility
    {
        /// <summary>One interchangeable group covering the common model-family
        /// categories. Every BIC in this set can be swapped to every other BIC
        /// in the set (Revit's template compatibility is symmetric for these).</summary>
        public static readonly HashSet<BuiltInCategory> ModelFamilyGroup = new()
        {
            BuiltInCategory.OST_GenericModel,
            BuiltInCategory.OST_Furniture,
            BuiltInCategory.OST_FurnitureSystems,
            BuiltInCategory.OST_Casework,
            BuiltInCategory.OST_SpecialtyEquipment,
            BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_LightingDevices,
            BuiltInCategory.OST_PlumbingFixtures,
            BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_SecurityDevices,
            BuiltInCategory.OST_NurseCallDevices,
            BuiltInCategory.OST_DataDevices,
            BuiltInCategory.OST_CommunicationDevices,
            BuiltInCategory.OST_TelephoneDevices,
            BuiltInCategory.OST_Sprinklers,
        };

        public static IEnumerable<BuiltInCategory> GetCompatibleFor(BuiltInCategory current)
        {
            if (ModelFamilyGroup.Contains(current))
                return ModelFamilyGroup.Where(b => b != current);
            return Array.Empty<BuiltInCategory>();
        }

        public static string FriendlyName(BuiltInCategory bic)
        {
            // Strip "OST_" prefix and camel-split for display
            string n = bic.ToString();
            if (n.StartsWith("OST_")) n = n.Substring(4);
            var sb = new StringBuilder();
            for (int i = 0; i < n.Length; i++)
            {
                char c = n[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(n[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }
    }

    /// <summary>Shared helpers for the four family-quick-edit commands.</summary>
    internal static class FamilyQuickEditHelpers
    {
        /// <summary>Resolve the FamilyInstance the user is working on.
        /// Preference order: current selection → ask user to pick.
        /// Returns null and shows a TaskDialog if the user cancels or the
        /// picked element is not a FamilyInstance.</summary>
        public static FamilyInstance ResolveTargetInstance(UIDocument uidoc, string promptTitle)
        {
            Document doc = uidoc.Document;
            ICollection<ElementId> sel = uidoc.Selection.GetElementIds();
            if (sel != null && sel.Count == 1)
            {
                var fi = doc.GetElement(sel.First()) as FamilyInstance;
                if (fi != null) return fi;
            }

            try
            {
                var picked = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new FamilyInstanceFilter(),
                    "Pick a family instance");
                return doc.GetElement(picked.ElementId) as FamilyInstance;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return null; }
            catch (Exception ex)
            {
                StingLog.Warn($"{promptTitle}: pick failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Snapshot all writable instance parameters so they can be
        /// copied onto a replacement instance after a delete-and-recreate rehost.
        /// Built-in parameters are included when they are writable — this
        /// preserves Comments, Mark, phase, workset, etc.</summary>
        public static Dictionary<string, object> SnapshotInstanceParams(FamilyInstance inst)
        {
            var snap = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (inst == null) return snap;

            foreach (Parameter p in inst.Parameters)
            {
                if (p == null || p.IsReadOnly) continue;
                try
                {
                    string name = p.Definition?.Name;
                    if (string.IsNullOrEmpty(name)) continue;
                    switch (p.StorageType)
                    {
                        case StorageType.String:  snap[name] = p.AsString(); break;
                        case StorageType.Integer: snap[name] = p.AsInteger(); break;
                        case StorageType.Double:  snap[name] = p.AsDouble(); break;
                        case StorageType.ElementId: snap[name] = p.AsElementId(); break;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"SnapshotInstanceParams '{p?.Definition?.Name}': {ex.Message}"); }
            }
            return snap;
        }

        /// <summary>Write back the snapshot onto a new instance. Missing or
        /// read-only parameters on the new instance are silently skipped — the
        /// two instances may not have identical schemas (category change etc.).</summary>
        public static int RestoreInstanceParams(FamilyInstance newInst, Dictionary<string, object> snap)
        {
            int restored = 0;
            if (newInst == null || snap == null) return 0;
            foreach (var kv in snap)
            {
                try
                {
                    Parameter p = newInst.LookupParameter(kv.Key);
                    if (p == null || p.IsReadOnly) continue;
                    switch (p.StorageType)
                    {
                        case StorageType.String when kv.Value is string s:
                            p.Set(s); restored++; break;
                        case StorageType.Integer when kv.Value is int i:
                            p.Set(i); restored++; break;
                        case StorageType.Double when kv.Value is double d:
                            p.Set(d); restored++; break;
                        case StorageType.ElementId when kv.Value is ElementId eid && eid != ElementId.InvalidElementId:
                            p.Set(eid); restored++; break;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"RestoreInstanceParams '{kv.Key}': {ex.Message}"); }
            }
            return restored;
        }

        /// <summary>Describe the current host in human terms for UI display.</summary>
        public static string DescribeHost(FamilyInstance inst)
        {
            if (inst == null) return "(no instance)";
            Element host = inst.Host;
            if (host == null)
            {
                var fam = inst.Symbol?.Family;
                return fam != null
                    ? $"(free-standing — placement: {fam.FamilyPlacementType})"
                    : "(free-standing)";
            }
            string cat = host.Category?.Name ?? host.GetType().Name;
            return $"{cat} [id {host.Id.Value}] '{host.Name}'";
        }
    }

    /// <summary>ISelectionFilter restricting picks to FamilyInstance elements.</summary>
    internal class FamilyInstanceFilter : ISelectionFilter
    {
        public bool AllowElement(Element e) => e is FamilyInstance;
        public bool AllowReference(Reference r, XYZ p) => true;
    }

    /// <summary>ISelectionFilter for walls only — used by ChangeHostCommand
    /// when rehosting a wall-based family to a different wall.</summary>
    internal class WallSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element e) => e is Wall;
        public bool AllowReference(Reference r, XYZ p) => true;
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  CHANGE HOST COMMAND
    //
    //  Rehost a wall-based family instance to a different wall. Preserves the
    //  instance's symbol, location, and all writable parameters. Detach (convert
    //  to free-standing) is intentionally NOT offered — it requires rebuilding
    //  the family against a non-hosted template, which the EditFamily API cannot
    //  do cleanly. Users who need that should use Swap Category → open in editor
    //  → save as new .rfa against a non-hosted template.
    // ════════════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ChangeHostCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null)
            {
                TaskDialog.Show("STING — Change Host", "Open a Revit project first.");
                return Result.Failed;
            }
            Document doc = ctx.Doc;
            UIDocument uidoc = ctx.UIDoc;

            FamilyInstance inst = FamilyQuickEditHelpers.ResolveTargetInstance(uidoc, "Change Host");
            if (inst == null) return Result.Cancelled;

            // Only wall-hosted instances are supported in v1. Face-based and
            // free-standing placement types are filtered out so the recreate
            // logic below doesn't have to branch on host geometry type.
            Wall currentWall = inst.Host as Wall;
            if (currentWall == null)
            {
                TaskDialog.Show("STING — Change Host",
                    $"This command only rehosts wall-based families. The selected family " +
                    $"is hosted on: {FamilyQuickEditHelpers.DescribeHost(inst)}.\n\n" +
                    "For face-hosted, free-standing, or level-based families, use Swap " +
                    "Category or open the family editor.");
                return Result.Cancelled;
            }

            // Ask the user to pick a destination wall. This replaces the host —
            // the instance's location point is preserved and projected onto the
            // new wall during re-creation.
            Wall newWall;
            try
            {
                var picked = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new WallSelectionFilter(),
                    "Pick the destination wall (ESC to cancel)");
                newWall = doc.GetElement(picked.ElementId) as Wall;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

            if (newWall == null)
            {
                TaskDialog.Show("STING — Change Host", "Destination must be a wall.");
                return Result.Failed;
            }
            if (newWall.Id == currentWall.Id)
            {
                TaskDialog.Show("STING — Change Host", "Destination wall is the same as the current host — nothing to do.");
                return Result.Cancelled;
            }

            // Snapshot everything we need to recreate the instance on the new host.
            var snap = FamilyQuickEditHelpers.SnapshotInstanceParams(inst);
            FamilySymbol sym = inst.Symbol;
            Level level = doc.GetElement(inst.LevelId) as Level;
            // Fall back to the wall's base-constraint level — wall-hosted instances
            // sometimes carry Invalid LevelId when the host wall is multi-story.
            if (level == null)
            {
                try
                {
                    var wallLvlParam = newWall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                    if (wallLvlParam != null && wallLvlParam.StorageType == StorageType.ElementId)
                        level = doc.GetElement(wallLvlParam.AsElementId()) as Level;
                }
                catch (Exception ex) { StingLog.Warn($"ChangeHost level fallback: {ex.Message}"); }
            }
            XYZ locationPoint = null;
            double rotation = 0;
            if (inst.Location is LocationPoint lp) { locationPoint = lp.Point; rotation = lp.Rotation; }
            if (locationPoint == null)
            {
                // Use the bounding-box centre as a fallback — better than aborting.
                BoundingBoxXYZ bb = inst.get_BoundingBox(null);
                if (bb != null) locationPoint = (bb.Min + bb.Max) * 0.5;
            }
            if (locationPoint == null)
            {
                TaskDialog.Show("STING — Change Host", "Could not determine the current instance location.");
                return Result.Failed;
            }

            int restored = 0;
            ElementId newInstId = ElementId.InvalidElementId;
            try
            {
                using (Transaction t = new Transaction(doc, "STING Change Host"))
                {
                    t.Start();
                    if (!sym.IsActive) sym.Activate();
                    doc.Delete(inst.Id);
                    FamilyInstance replacement = doc.Create.NewFamilyInstance(
                        locationPoint, sym, newWall, level, StructuralType.NonStructural);
                    if (replacement == null)
                    {
                        t.RollBack();
                        TaskDialog.Show("STING — Change Host",
                            "Revit refused to create the replacement on the destination wall. " +
                            "This usually means the family's template isn't compatible with " +
                            "that wall kind (e.g., curtain vs basic). The original instance " +
                            "has been restored.");
                        return Result.Failed;
                    }
                    newInstId = replacement.Id;
                    restored = FamilyQuickEditHelpers.RestoreInstanceParams(replacement, snap);

                    // Restore rotation by rotating around vertical axis through the point.
                    try
                    {
                        if (Math.Abs(rotation) > 1e-9 && replacement.Location is LocationPoint newLp)
                        {
                            Line axis = Line.CreateBound(locationPoint, locationPoint + XYZ.BasisZ);
                            ElementTransformUtils.RotateElement(doc, replacement.Id, axis, rotation);
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"ChangeHost rotation restore: {ex.Message}"); }

                    t.Commit();
                }

                if (newInstId != ElementId.InvalidElementId)
                    uidoc.Selection.SetElementIds(new[] { newInstId });

                TaskDialog.Show("STING — Change Host",
                    $"Rehosted successfully.\n\n" +
                    $"New host: Wall [id {newWall.Id.Value}] '{newWall.Name}'\n" +
                    $"Parameters restored: {restored}");
                StingLog.Info($"ChangeHost: moved instance to wall {newWall.Id.Value}, restored {restored} params");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ChangeHostCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  SWAP CATEGORY COMMAND
    //
    //  Change a family's category via Document.EditFamily() → modify →
    //  LoadFamily. Only permitted within the interchangeable "model family"
    //  group (see FamilyCategoryCompatibility). System families, hosted
    //  (doors / windows) and annotation families are refused with a clear
    //  message up-front so the user isn't left with a half-edited family doc.
    // ════════════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SwapCategoryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null)
            {
                TaskDialog.Show("STING — Swap Category", "Open a Revit project first.");
                return Result.Failed;
            }
            Document doc = ctx.Doc;

            // Resolve the target family — either from a selected instance or
            // by asking the user to pick a family from the FamilyManager.
            Family family = ResolveFamily(ctx);
            if (family == null) return Result.Cancelled;

            Category currentCat = family.FamilyCategory;
            if (currentCat == null)
            {
                TaskDialog.Show("STING — Swap Category", "Could not read the family's current category.");
                return Result.Failed;
            }
            BuiltInCategory currentBic = (BuiltInCategory)currentCat.Id.Value;

            // Compatibility gate — reject everything outside the model-family group.
            if (!FamilyCategoryCompatibility.ModelFamilyGroup.Contains(currentBic))
            {
                TaskDialog.Show("STING — Swap Category",
                    $"The family '{family.Name}' is in '{currentCat.Name}', which is NOT in the " +
                    "STING interchangeable-model-family group.\n\n" +
                    "Safe swaps are supported only between: Generic Models, Furniture, Casework, " +
                    "Specialty Equipment, Mechanical/Electrical/Plumbing Equipment & Fixtures, " +
                    "Lighting, and Device families.\n\n" +
                    "Doors, Windows, System families, Annotation families and Tags cannot have " +
                    "their category changed — rebuild the family against the desired template.");
                return Result.Cancelled;
            }

            // Pick destination category from the compatible list.
            var options = FamilyCategoryCompatibility.GetCompatibleFor(currentBic)
                .Select(bic => new StingListPicker.ListItem
                {
                    Label = FamilyCategoryCompatibility.FriendlyName(bic),
                    Detail = bic.ToString(),
                    Tag = bic
                })
                .OrderBy(i => i.Label)
                .ToList();

            if (options.Count == 0)
            {
                TaskDialog.Show("STING — Swap Category", "No compatible destination categories for this family.");
                return Result.Cancelled;
            }

            var selected = StingListPicker.Show(
                "STING — Swap Category",
                $"Family '{family.Name}' — currently {currentCat.Name}. Pick a new category:",
                options);
            if (selected == null || selected.Count == 0) return Result.Cancelled;
            if (!(selected[0].Tag is BuiltInCategory newBic)) return Result.Cancelled;

            // Warn about consequences before editing. Tag / schedule / filter
            // rebinding is the user's responsibility after the swap.
            var confirm = new TaskDialog("STING — Confirm Swap")
            {
                MainInstruction = $"Change '{family.Name}' from {currentCat.Name} to {FamilyCategoryCompatibility.FriendlyName(newBic)}?",
                MainContent =
                    "Impact:\n" +
                    "• Any tags bound to the OLD category will stop picking up this family.\n" +
                    "• Schedules filtering by the OLD category will lose these rows.\n" +
                    "• View filters scoped to the OLD category will drop this family.\n" +
                    "• Placed instance parameter values are preserved where parameter schemas match.\n\n" +
                    "Run this on a committed / backed-up model.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
            };
            if (confirm.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            return ExecuteSwap(doc, family, newBic, ref message);
        }

        private static Family ResolveFamily(StingCommandContext ctx)
        {
            Document doc = ctx.Doc;
            var sel = ctx.UIDoc.Selection.GetElementIds();
            if (sel != null && sel.Count == 1)
            {
                Element el = doc.GetElement(sel.First());
                if (el is FamilyInstance fi && fi.Symbol?.Family != null) return fi.Symbol.Family;
                if (el is Family fam) return fam;
                if (el is FamilySymbol fs && fs.Family != null) return fs.Family;
            }

            // No usable selection — ask user to pick an instance of the family
            // they want to swap, since that's the most common "I'm looking at this
            // thing and want to fix it" entry point.
            try
            {
                var picked = ctx.UIDoc.Selection.PickObject(
                    ObjectType.Element,
                    new FamilyInstanceFilter(),
                    "Pick a family instance whose family you want to re-categorise");
                var fi = doc.GetElement(picked.ElementId) as FamilyInstance;
                return fi?.Symbol?.Family;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return null; }
        }

        private static Result ExecuteSwap(Document doc, Family family, BuiltInCategory newBic, ref string message)
        {
            Document famDoc = null;
            try
            {
                famDoc = doc.EditFamily(family);
                if (famDoc == null || !famDoc.IsFamilyDocument)
                {
                    message = "EditFamily returned no family document.";
                    return Result.Failed;
                }

                using (Transaction t = new Transaction(famDoc, "STING Swap Family Category"))
                {
                    t.Start();
                    try
                    {
                        Category newCat = famDoc.Settings.Categories.get_Item(newBic);
                        if (newCat == null)
                        {
                            t.RollBack();
                            message = $"Category {newBic} is not available in this document.";
                            return Result.Failed;
                        }
                        famDoc.OwnerFamily.FamilyCategory = newCat;
                        t.Commit();
                    }
                    catch (Exception inner)
                    {
                        t.RollBack();
                        StingLog.Error("SwapCategoryCommand inner", inner);
                        message = $"Revit refused the category change: {inner.Message}";
                        return Result.Failed;
                    }
                }

                // Load the modified family back into the project with parameter
                // overwrite on — new schema takes precedence where it differs.
                var loadOpts = new StingFamilyLoadOptions(true);
                Family reloaded = famDoc.LoadFamily(doc, loadOpts);
                bool loaded = reloaded != null;

                try { famDoc.Close(false); famDoc = null; }
                catch (Exception ex) { StingLog.Warn($"SwapCategory close: {ex.Message}"); }

                if (!loaded)
                {
                    message = "LoadFamily returned null — the edited family did not reload.";
                    return Result.Failed;
                }

                TaskDialog.Show("STING — Swap Category",
                    $"'{family.Name}' is now in category {FamilyCategoryCompatibility.FriendlyName(newBic)}.\n\n" +
                    "Check tags, schedules, and filters scoped to the old category.");
                StingLog.Info($"SwapCategory: '{family.Name}' → {newBic}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SwapCategoryCommand", ex);
                message = ex.Message;
                try { famDoc?.Close(false); } catch { }
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  INJECT AUTOMATION PACK COMMAND
    //
    //  Thin wrapper that runs FamilyParamEngine.InjectAutomationPresentationPack
    //  on the family of a selected instance. Uses the same EditFamily →
    //  transaction → LoadFamily pattern as FamilyParamCreatorCommand.
    // ════════════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class InjectAutomationPackCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null)
            {
                TaskDialog.Show("STING — Automation Pack", "Open a Revit project first.");
                return Result.Failed;
            }
            Document doc = ctx.Doc;

            Family family = ResolveFamilyFromSelection(ctx);
            if (family == null) return Result.Cancelled;

            Document famDoc = null;
            try
            {
                famDoc = doc.EditFamily(family);
                if (famDoc == null || !famDoc.IsFamilyDocument)
                {
                    message = "EditFamily returned no family document.";
                    return Result.Failed;
                }

                int added, skipped;
                using (Transaction t = new Transaction(famDoc, "STING Inject Automation Pack"))
                {
                    t.Start();
                    var (a, s) = FamilyParamEngine.InjectAutomationPresentationPack(famDoc);
                    added = a; skipped = s;
                    t.Commit();
                }

                var loadOpts = new StingFamilyLoadOptions(false); // preserve instance values
                Family reloaded = famDoc.LoadFamily(doc, loadOpts);
                bool loaded = reloaded != null;

                try { famDoc.Close(false); famDoc = null; }
                catch (Exception ex) { StingLog.Warn($"AutomationPack close: {ex.Message}"); }

                TaskDialog.Show("STING — Automation Pack",
                    $"Family '{family.Name}'\n\n" +
                    $"Added:   {added} parameters\n" +
                    $"Skipped: {skipped} (already present)\n" +
                    (loaded ? "Loaded back into project." : "Load failed — see log."));
                StingLog.Info($"InjectAutomationPack '{family.Name}': +{added} / skip {skipped}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("InjectAutomationPackCommand", ex);
                message = ex.Message;
                try { famDoc?.Close(false); } catch { }
                return Result.Failed;
            }
        }

        private static Family ResolveFamilyFromSelection(StingCommandContext ctx)
        {
            Document doc = ctx.Doc;
            var sel = ctx.UIDoc.Selection.GetElementIds();
            if (sel != null && sel.Count == 1)
            {
                Element el = doc.GetElement(sel.First());
                if (el is FamilyInstance fi && fi.Symbol?.Family != null) return fi.Symbol.Family;
                if (el is Family fam) return fam;
                if (el is FamilySymbol fs && fs.Family != null) return fs.Family;
            }
            try
            {
                var picked = ctx.UIDoc.Selection.PickObject(
                    ObjectType.Element,
                    new FamilyInstanceFilter(),
                    "Pick a family instance to inject the automation pack into");
                var fi = doc.GetElement(picked.ElementId) as FamilyInstance;
                return fi?.Symbol?.Family;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return null; }
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  OPEN FAMILY QUICK EDIT COMMAND
    //
    //  Entry point from the dockable panel. Opens FamilyQuickEditDialog for the
    //  selected instance, then dispatches to the chosen sub-command.
    // ════════════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class OpenFamilyQuickEditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null)
            {
                TaskDialog.Show("STING — Family Quick Edit", "Open a Revit project first.");
                return Result.Failed;
            }

            FamilyInstance inst = FamilyQuickEditHelpers.ResolveTargetInstance(ctx.UIDoc, "Family Quick Edit");
            if (inst == null) return Result.Cancelled;

            var dlg = new FamilyQuickEditDialog(inst);
            FamilyQuickEditDialog.ActionChoice choice = dlg.ShowAndGetChoice();

            switch (choice)
            {
                case FamilyQuickEditDialog.ActionChoice.None:
                    return Result.Cancelled;
                case FamilyQuickEditDialog.ActionChoice.ChangeHost:
                    return new ChangeHostCommand().Execute(commandData, ref message, elements);
                case FamilyQuickEditDialog.ActionChoice.SwapCategory:
                    return new SwapCategoryCommand().Execute(commandData, ref message, elements);
                case FamilyQuickEditDialog.ActionChoice.InjectAutomationPack:
                    return new InjectAutomationPackCommand().Execute(commandData, ref message, elements);
                case FamilyQuickEditDialog.ActionChoice.InjectStingParamPack:
                    return new FamilyParamCreatorCommand().Execute(commandData, ref message, elements);
                case FamilyQuickEditDialog.ActionChoice.OpenInFamilyEditor:
                    return OpenInFamilyEditor(ctx, inst, ref message);
                case FamilyQuickEditDialog.ActionChoice.ShowTypeProperties:
                    return ShowTypeProperties(ctx, inst);
                default:
                    return Result.Cancelled;
            }
        }

        private static Result OpenInFamilyEditor(StingCommandContext ctx, FamilyInstance inst, ref string message)
        {
            try
            {
                Family fam = inst.Symbol?.Family;
                if (fam == null) { message = "No family on selected instance."; return Result.Failed; }
                if (!fam.IsEditable)
                {
                    TaskDialog.Show("STING — Open in Editor",
                        $"'{fam.Name}' is not editable (likely a system family or in-place element).");
                    return Result.Cancelled;
                }
                // Post Revit's built-in Edit Family command with the instance
                // selected. More reliable than EditFamily() + UI activation —
                // Revit handles the tab switch and window focus itself.
                ctx.UIDoc.Selection.SetElementIds(new[] { inst.Id });
                var cmdId = RevitCommandId.LookupPostableCommandId(PostableCommand.EditFamily);
                ctx.UIDoc.Application.PostCommand(cmdId);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("OpenInFamilyEditor", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static Result ShowTypeProperties(StingCommandContext ctx, FamilyInstance inst)
        {
            // There is no public API to open Revit's Type Properties dialog.
            // Instead, render the type parameters as read-only text — still a
            // fast one-look answer to "what's on this type?"
            FamilySymbol sym = inst.Symbol;
            if (sym == null) return Result.Cancelled;

            var sb = new StringBuilder();
            sb.AppendLine($"Family:  {sym.Family?.Name}");
            sb.AppendLine($"Type:    {sym.Name}");
            sb.AppendLine();
            sb.AppendLine("Type parameters:");
            foreach (Parameter p in sym.Parameters.Cast<Parameter>().OrderBy(p => p.Definition?.Name))
            {
                if (p?.Definition == null) continue;
                string v;
                try { v = p.AsValueString() ?? p.AsString() ?? (p.StorageType == StorageType.Integer ? p.AsInteger().ToString() : ""); }
                catch { v = ""; }
                sb.AppendLine($"  {p.Definition.Name,-40}  {v}");
            }

            TaskDialog.Show("STING — Type Properties", sb.Length > 4000 ? sb.ToString(0, 4000) + "\n…(truncated)" : sb.ToString());
            return Result.Succeeded;
        }
    }
}
