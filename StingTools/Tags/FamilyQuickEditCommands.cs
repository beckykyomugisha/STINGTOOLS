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
            BuiltInCategory.OST_SpecialityEquipment,
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

    /// <summary>ISelectionFilter for floor / ceiling / roof slab picks — the
    /// three horizontal host kinds that share the single-host NewFamilyInstance
    /// overload with a Wall.</summary>
    internal class FloorCeilingRoofFilter : ISelectionFilter
    {
        public bool AllowElement(Element e) =>
            e is Floor || e is Ceiling || e is RoofBase;
        public bool AllowReference(Reference r, XYZ p) => true;
    }

    /// <summary>ISelectionFilter for work-plane targets — reference planes and
    /// levels. Both can host a WorkPlaneBased or OneLevelBased family when
    /// passed via <c>NewFamilyInstance(Reference, XYZ, XYZ, FamilySymbol)</c>
    /// or the level-based overload.</summary>
    internal class WorkPlaneFilter : ISelectionFilter
    {
        public bool AllowElement(Element e) => e is ReferencePlane || e is Level;
        public bool AllowReference(Reference r, XYZ p) => true;
    }

    /// <summary>ISelectionFilter that accepts any face reference — used when
    /// rehosting face-based families. The underlying element category is not
    /// constrained because face-based families can sit on any solid surface.</summary>
    internal class AnyFaceFilter : ISelectionFilter
    {
        public bool AllowElement(Element e) => true;
        public bool AllowReference(Reference r, XYZ p) => true;
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  CHANGE HOST COMMAND
    //
    //  Change the host of a family instance — or remove the instance entirely.
    //  The command opens a mode picker covering every rehost target the Revit
    //  API can satisfy without a family-template rebuild:
    //    1. Wall               → NewFamilyInstance(XYZ, sym, Wall, Level, …)
    //    2. Floor / Ceiling / Roof → NewFamilyInstance(XYZ, sym, Element, Level, …)
    //    3. Face (any solid)   → NewFamilyInstance(Reference, XYZ, XYZ, sym)
    //    4. Work Plane (ref plane / level) → NewFamilyInstance(Reference, XYZ, XYZ, sym)
    //    5. Detach → free-standing → NewFamilyInstance(XYZ, sym, Level?, …) —
    //       only succeeds when the family's FamilyPlacementType permits non-
    //       hosted placement. WorkPlaneBased and OneLevelBased families are
    //       typically eligible; hosted-template families are refused with a
    //       clear "requires family rebuild" message.
    //    6. Delete Instance    → doc.Delete(inst.Id), with a confirmation.
    //
    //  All rehost modes 1–5 snapshot writable parameters on the original
    //  instance and replay them onto the replacement, so Mark / Comments /
    //  phase / workset / STING tokens survive the delete+recreate cycle.
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>Snapshot of a family instance captured immediately before a
    /// delete-and-recreate rehost. Carries everything the six
    /// <see cref="ChangeHostCommand"/> modes need to reconstruct the instance
    /// on a new host while preserving parameter values and orientation.</summary>
    internal class InstanceRehostSnapshot
    {
        public ElementId OriginalId { get; set; }
        public FamilySymbol Symbol { get; set; }
        public Level Level { get; set; }
        public XYZ LocationPoint { get; set; }
        public double Rotation { get; set; }
        public Dictionary<string, object> Params { get; set; }
        public FamilyPlacementType PlacementType { get; set; }
        public string OriginalHostDescription { get; set; }

        /// <summary>Capture a snapshot from the live instance. Returns null only
        /// when the location cannot be resolved even from the bounding box —
        /// every rehost mode needs a location point to place the replacement.</summary>
        public static InstanceRehostSnapshot Take(FamilyInstance inst)
        {
            if (inst == null) return null;
            var snap = new InstanceRehostSnapshot
            {
                OriginalId = inst.Id,
                Symbol = inst.Symbol,
                Params = FamilyQuickEditHelpers.SnapshotInstanceParams(inst),
                PlacementType = inst.Symbol?.Family?.FamilyPlacementType ?? FamilyPlacementType.Invalid,
                OriginalHostDescription = FamilyQuickEditHelpers.DescribeHost(inst),
            };

            Document doc = inst.Document;
            snap.Level = doc.GetElement(inst.LevelId) as Level;

            if (inst.Location is LocationPoint lp)
            {
                snap.LocationPoint = lp.Point;
                snap.Rotation = lp.Rotation;
            }
            else
            {
                BoundingBoxXYZ bb = inst.get_BoundingBox(null);
                if (bb != null) snap.LocationPoint = (bb.Min + bb.Max) * 0.5;
            }

            return snap.LocationPoint == null ? null : snap;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ChangeHostCommand : IExternalCommand
    {
        private enum Mode { Wall, FloorCeilingRoof, Face, WorkPlane, Detach, Delete }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null)
            {
                TaskDialog.Show("STING — Change Host", "Open a Revit project first.");
                return Result.Failed;
            }

            FamilyInstance inst = FamilyQuickEditHelpers.ResolveTargetInstance(ctx.UIDoc, "Change Host");
            if (inst == null) return Result.Cancelled;

            // Present the six modes. Each row carries a (Mode, bool enabled, detail)
            // tuple in .Tag so the list picker itself stays agnostic about what
            // the choices mean. The Wall and FCR rows are enabled iff the
            // current host is one of those kinds (so the picker nudges users
            // toward sensible swaps); Face and WorkPlane and Detach always show.
            Element currentHost = inst.Host;
            bool isWallHosted = currentHost is Wall;
            bool isFCRHosted = currentHost is Floor || currentHost is Ceiling || currentHost is RoofBase;
            var fpt = inst.Symbol?.Family?.FamilyPlacementType ?? FamilyPlacementType.Invalid;
            bool detachEligible = fpt == FamilyPlacementType.WorkPlaneBased
                               || fpt == FamilyPlacementType.OneLevelBased
                               || fpt == FamilyPlacementType.TwoLevelsBased;

            var modeItems = new List<StingListPicker.ListItem>
            {
                new StingListPicker.ListItem {
                    Label = "Rehost to Wall",
                    Detail = isWallHosted ? "Pick a new wall. Preserves location + parameters." : "Best for wall-hosted families. May fail if the family's template isn't wall-based.",
                    Tag = Mode.Wall },
                new StingListPicker.ListItem {
                    Label = "Rehost to Floor / Ceiling / Roof",
                    Detail = isFCRHosted ? "Pick a new floor, ceiling, or roof." : "For horizontal-slab-hosted families (sprinklers, ceiling fixtures, etc.).",
                    Tag = Mode.FloorCeilingRoof },
                new StingListPicker.ListItem {
                    Label = "Rehost to a Face",
                    Detail = "Pick any face on any solid element. Best for face-based families.",
                    Tag = Mode.Face },
                new StingListPicker.ListItem {
                    Label = "Rehost to a Work Plane",
                    Detail = "Pick a reference plane or level datum. Best for work-plane-based families.",
                    Tag = Mode.WorkPlane },
                new StingListPicker.ListItem {
                    Label = "Detach from Host — make free-standing",
                    Detail = detachEligible
                        ? $"Family placement type is {fpt} — can be placed without a host."
                        : $"Family placement type is {fpt} — likely hosted-template only. Detach may fail (rebuild required).",
                    Tag = Mode.Detach,
                    IsInvalid = !detachEligible },
                new StingListPicker.ListItem {
                    Label = "Delete Instance (remove completely)",
                    Detail = "Destructive — deletes the family instance from the model. Asks for confirmation.",
                    Tag = Mode.Delete },
            };

            var chosen = StingListPicker.Show(
                "STING — Change Host",
                $"Selected: {inst.Symbol?.Family?.Name} — {inst.Symbol?.Name}   |   Current host: {FamilyQuickEditHelpers.DescribeHost(inst)}",
                modeItems);
            if (chosen == null || chosen.Count == 0) return Result.Cancelled;
            if (!(chosen[0].Tag is Mode mode)) return Result.Cancelled;

            // Delete is the only path that doesn't need a snapshot — skip the
            // snapshot for that mode so we don't do unnecessary work.
            if (mode == Mode.Delete) return ExecuteDelete(ctx, inst, ref message);

            var snap = InstanceRehostSnapshot.Take(inst);
            if (snap == null)
            {
                TaskDialog.Show("STING — Change Host", "Could not determine the current instance location.");
                return Result.Failed;
            }

            switch (mode)
            {
                case Mode.Wall:              return ExecuteRehostToWall(ctx, snap, ref message);
                case Mode.FloorCeilingRoof:  return ExecuteRehostToFloorCeilingRoof(ctx, snap, ref message);
                case Mode.Face:              return ExecuteRehostToFace(ctx, snap, ref message);
                case Mode.WorkPlane:         return ExecuteRehostToWorkPlane(ctx, snap, ref message);
                case Mode.Detach:            return ExecuteDetach(ctx, snap, ref message);
                default:                     return Result.Cancelled;
            }
        }

        // ── Mode: Wall ──────────────────────────────────────────────────────
        private static Result ExecuteRehostToWall(StingCommandContext ctx, InstanceRehostSnapshot snap, ref string message)
        {
            Wall newWall;
            try
            {
                var picked = ctx.UIDoc.Selection.PickObject(
                    ObjectType.Element, new WallSelectionFilter(),
                    "Pick the destination wall (ESC to cancel)");
                newWall = ctx.Doc.GetElement(picked.ElementId) as Wall;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

            if (newWall == null) { TaskDialog.Show("STING — Change Host", "Destination must be a wall."); return Result.Failed; }

            // Level fallback from the destination wall — wall-hosted instances
            // sometimes carry Invalid LevelId when the host wall spans stories.
            Level level = snap.Level ?? ResolveLevelFromWall(ctx.Doc, newWall);

            return CommitRehost(ctx, snap, "Wall",
                $"Wall [id {newWall.Id.Value}] '{newWall.Name}'",
                ref message,
                (doc) => doc.Create.NewFamilyInstance(
                    snap.LocationPoint, snap.Symbol, newWall, level, StructuralType.NonStructural));
        }

        // ── Mode: Floor / Ceiling / Roof ───────────────────────────────────
        private static Result ExecuteRehostToFloorCeilingRoof(StingCommandContext ctx, InstanceRehostSnapshot snap, ref string message)
        {
            Element host;
            try
            {
                var picked = ctx.UIDoc.Selection.PickObject(
                    ObjectType.Element, new FloorCeilingRoofFilter(),
                    "Pick the destination floor, ceiling, or roof (ESC to cancel)");
                host = ctx.Doc.GetElement(picked.ElementId);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

            if (host == null) { TaskDialog.Show("STING — Change Host", "Pick aborted."); return Result.Failed; }

            Level level = snap.Level ?? ResolveLevelFromHost(ctx.Doc, host);

            string hostKind = host.GetType().Name;
            return CommitRehost(ctx, snap, hostKind,
                $"{hostKind} [id {host.Id.Value}] '{host.Name}'",
                ref message,
                (doc) => doc.Create.NewFamilyInstance(
                    snap.LocationPoint, snap.Symbol, host, level, StructuralType.NonStructural));
        }

        // ── Mode: Face ──────────────────────────────────────────────────────
        private static Result ExecuteRehostToFace(StingCommandContext ctx, InstanceRehostSnapshot snap, ref string message)
        {
            Reference faceRef;
            XYZ facePoint;
            try
            {
                faceRef = ctx.UIDoc.Selection.PickObject(
                    ObjectType.Face, new AnyFaceFilter(),
                    "Pick the destination face (ESC to cancel)");
                facePoint = faceRef.GlobalPoint;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

            if (faceRef == null || facePoint == null)
            {
                TaskDialog.Show("STING — Change Host", "Could not resolve the picked face.");
                return Result.Failed;
            }

            // Build a reference direction. For face-based families, the second XYZ
            // argument controls the family's X axis orientation on the face. A
            // safe default is world X projected onto the face; otherwise fall
            // back to Y or Z to avoid passing an axis perpendicular to the face.
            XYZ refDir = ProjectNonParallelAxis(ctx.Doc, faceRef);

            string targetName = "Face";
            try
            {
                var hostEl = ctx.Doc.GetElement(faceRef.ElementId);
                if (hostEl != null) targetName = $"{hostEl.Category?.Name ?? hostEl.GetType().Name} face [id {hostEl.Id.Value}]";
            }
            catch (Exception ex) { StingLog.Warn($"ChangeHost face element name: {ex.Message}"); }

            return CommitRehost(ctx, snap, "Face", targetName, ref message,
                (doc) => doc.Create.NewFamilyInstance(faceRef, facePoint, refDir, snap.Symbol));
        }

        // ── Mode: Work Plane ────────────────────────────────────────────────
        private static Result ExecuteRehostToWorkPlane(StingCommandContext ctx, InstanceRehostSnapshot snap, ref string message)
        {
            // Pick a reference plane or level. For reference planes we use the
            // Reference-based NewFamilyInstance overload; for levels we use the
            // level-based overload (cleaner for OneLevelBased families).
            Element picked;
            Reference pickedRef;
            try
            {
                pickedRef = ctx.UIDoc.Selection.PickObject(
                    ObjectType.Element, new WorkPlaneFilter(),
                    "Pick a reference plane or level (ESC to cancel)");
                picked = ctx.Doc.GetElement(pickedRef.ElementId);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

            if (picked == null) { TaskDialog.Show("STING — Change Host", "Pick aborted."); return Result.Failed; }

            if (picked is Level newLevel)
            {
                return CommitRehost(ctx, snap, "Level",
                    $"Level '{newLevel.Name}' [id {newLevel.Id.Value}]",
                    ref message,
                    (doc) => doc.Create.NewFamilyInstance(
                        snap.LocationPoint, snap.Symbol, newLevel, StructuralType.NonStructural));
            }

            if (picked is ReferencePlane refPlane)
            {
                // Revit 2025 dropped ReferencePlane.Reference (the property);
                // construct the Reference from the Element directly. The
                // resulting Reference is valid for the 4-arg
                // NewFamilyInstance overload and travels with the plane.
                Reference planeRef = new Reference(refPlane);
                if (planeRef == null)
                {
                    TaskDialog.Show("STING — Change Host", "Could not resolve a Reference from the picked reference plane.");
                    return Result.Failed;
                }
                XYZ refDir = ProjectNonParallelAxis(ctx.Doc, planeRef);
                return CommitRehost(ctx, snap, "Reference Plane",
                    $"Reference Plane '{refPlane.Name}' [id {refPlane.Id.Value}]",
                    ref message,
                    (doc) => doc.Create.NewFamilyInstance(planeRef, snap.LocationPoint, refDir, snap.Symbol));
            }

            TaskDialog.Show("STING — Change Host", "Pick was not a Level or Reference Plane.");
            return Result.Failed;
        }

        // ── Mode: Detach (make free-standing) ───────────────────────────────
        private static Result ExecuteDetach(StingCommandContext ctx, InstanceRehostSnapshot snap, ref string message)
        {
            // Only eligible for placement types that support placing without
            // a host. Hosted-template families cannot be detached — they need
            // a family rebuild against a non-hosted template, which is out of
            // scope for instance-level operations.
            bool eligible = snap.PlacementType == FamilyPlacementType.WorkPlaneBased
                         || snap.PlacementType == FamilyPlacementType.OneLevelBased
                         || snap.PlacementType == FamilyPlacementType.TwoLevelsBased;
            if (!eligible)
            {
                TaskDialog.Show("STING — Change Host",
                    $"Cannot detach: family placement type is {snap.PlacementType}, which is hosted-template only.\n\n" +
                    "To convert this family to free-standing, open it in the family editor and save a copy against " +
                    "a non-hosted template (e.g., Metric Generic Model.rft), or use Swap Category to move to an " +
                    "already-free-standing category.");
                return Result.Cancelled;
            }

            var confirm = new TaskDialog("STING — Detach from Host")
            {
                MainInstruction = "Detach this instance from its host and place it free-standing?",
                MainContent = $"Current host: {snap.OriginalHostDescription}\n" +
                              $"Placement type: {snap.PlacementType}\n\n" +
                              "The instance will be re-created at the same location without a host. " +
                              "If Revit rejects free placement, the original is restored via rollback.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
            };
            if (confirm.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            Level level = snap.Level;
            return CommitRehost(ctx, snap, "Free-standing",
                level != null ? $"Free-standing on level '{level.Name}'" : "Free-standing (no level)",
                ref message,
                (doc) =>
                {
                    // Prefer the level-based overload when we have a level —
                    // gives the free instance a sensible host level for schedules.
                    if (level != null)
                        return doc.Create.NewFamilyInstance(snap.LocationPoint, snap.Symbol, level, StructuralType.NonStructural);
                    return doc.Create.NewFamilyInstance(snap.LocationPoint, snap.Symbol, StructuralType.NonStructural);
                });
        }

        // ── Mode: Delete ────────────────────────────────────────────────────
        private static Result ExecuteDelete(StingCommandContext ctx, FamilyInstance inst, ref string message)
        {
            var confirm = new TaskDialog("STING — Delete Instance")
            {
                MainInstruction = "Delete this family instance?",
                MainContent = $"Family: {inst.Symbol?.Family?.Name}\n" +
                              $"Type: {inst.Symbol?.Name}\n" +
                              $"Host: {FamilyQuickEditHelpers.DescribeHost(inst)}\n\n" +
                              "The instance is removed from the model. The family itself is NOT deleted.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
            };
            if (confirm.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            try
            {
                long instId = inst.Id.Value;
                using (Transaction t = new Transaction(ctx.Doc, "STING Delete Family Instance"))
                {
                    t.Start();
                    ctx.Doc.Delete(inst.Id);
                    t.Commit();
                }
                TaskDialog.Show("STING — Delete Instance", $"Deleted instance [id {instId}].");
                StingLog.Info($"ChangeHost(Delete): removed instance {instId}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ChangeHostCommand Delete", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Shared commit path used by every rehost mode ────────────────────
        private static Result CommitRehost(
            StingCommandContext ctx,
            InstanceRehostSnapshot snap,
            string modeLabel,
            string newHostLabel,
            ref string message,
            Func<Document, FamilyInstance> createReplacement)
        {
            int restored = 0;
            ElementId newInstId = ElementId.InvalidElementId;
            try
            {
                using (Transaction t = new Transaction(ctx.Doc, $"STING Change Host — {modeLabel}"))
                {
                    t.Start();
                    if (!snap.Symbol.IsActive) snap.Symbol.Activate();
                    ctx.Doc.Delete(snap.OriginalId);

                    FamilyInstance replacement = null;
                    try { replacement = createReplacement(ctx.Doc); }
                    catch (Exception createEx)
                    {
                        t.RollBack();
                        StingLog.Warn($"ChangeHost {modeLabel} create: {createEx.Message}");
                        TaskDialog.Show("STING — Change Host",
                            $"Revit refused to create the replacement ({modeLabel}).\n\n{createEx.Message}\n\n" +
                            "The original instance has been restored.");
                        return Result.Failed;
                    }

                    if (replacement == null)
                    {
                        t.RollBack();
                        TaskDialog.Show("STING — Change Host",
                            $"Revit returned no replacement instance ({modeLabel}). " +
                            "This usually means the family's template is incompatible with the chosen host kind. " +
                            "The original instance has been restored.");
                        return Result.Failed;
                    }

                    newInstId = replacement.Id;
                    restored = FamilyQuickEditHelpers.RestoreInstanceParams(replacement, snap.Params);

                    // Restore rotation around the vertical axis through the
                    // location point. Skipped for near-zero rotations to avoid
                    // adding tiny rounding errors.
                    try
                    {
                        if (Math.Abs(snap.Rotation) > 1e-9 && replacement.Location is LocationPoint)
                        {
                            Line axis = Line.CreateBound(snap.LocationPoint, snap.LocationPoint + XYZ.BasisZ);
                            ElementTransformUtils.RotateElement(ctx.Doc, replacement.Id, axis, snap.Rotation);
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"ChangeHost rotation restore ({modeLabel}): {ex.Message}"); }

                    t.Commit();
                }

                if (newInstId != ElementId.InvalidElementId)
                    ctx.UIDoc.Selection.SetElementIds(new[] { newInstId });

                TaskDialog.Show("STING — Change Host",
                    $"Rehosted successfully ({modeLabel}).\n\n" +
                    $"New host:   {newHostLabel}\n" +
                    $"Parameters restored: {restored}");
                StingLog.Info($"ChangeHost({modeLabel}): new id {newInstId.Value}, restored {restored} params");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error($"ChangeHostCommand {modeLabel}", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Small helpers shared by the mode handlers ──────────────────────
        private static Level ResolveLevelFromWall(Document doc, Wall wall)
        {
            try
            {
                var p = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                if (p != null && p.StorageType == StorageType.ElementId)
                    return doc.GetElement(p.AsElementId()) as Level;
            }
            catch (Exception ex) { StingLog.Warn($"ResolveLevelFromWall: {ex.Message}"); }
            return null;
        }

        private static Level ResolveLevelFromHost(Document doc, Element host)
        {
            // Floors, ceilings, roofs all carry LEVEL_PARAM on at least one
            // of: the instance, the type, or the SketchPlan. Best-effort only.
            try
            {
                foreach (BuiltInParameter bip in new[] {
                    BuiltInParameter.LEVEL_PARAM,
                    BuiltInParameter.SCHEDULE_LEVEL_PARAM,
                    BuiltInParameter.FAMILY_LEVEL_PARAM,
                    BuiltInParameter.ROOF_BASE_LEVEL_PARAM })
                {
                    var p = host.get_Parameter(bip);
                    if (p != null && p.StorageType == StorageType.ElementId)
                    {
                        var lvl = doc.GetElement(p.AsElementId()) as Level;
                        if (lvl != null) return lvl;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveLevelFromHost: {ex.Message}"); }
            return null;
        }

        /// <summary>Return a world axis (X, then Y, then Z) that is reasonably
        /// non-parallel to the picked face / reference plane. Used as the
        /// <c>referenceDirection</c> argument in the Reference-based
        /// NewFamilyInstance overload so face-based / work-plane families
        /// orient predictably without requiring the caller to do geometry.</summary>
        private static XYZ ProjectNonParallelAxis(Document doc, Reference r)
        {
            XYZ[] candidates = { XYZ.BasisX, XYZ.BasisY, XYZ.BasisZ };
            try
            {
                // Try to sniff the face / plane normal to pick a non-parallel axis.
                XYZ normal = null;
                var el = doc.GetElement(r.ElementId);
                if (el != null)
                {
                    var geomObj = el.GetGeometryObjectFromReference(r);
                    if (geomObj is Face face)
                    {
                        // Sample the normal at the UV centre of the face domain.
                        BoundingBoxUV bb = face.GetBoundingBox();
                        if (bb != null)
                        {
                            UV mid = (bb.Min + bb.Max) * 0.5;
                            normal = face.ComputeNormal(mid);
                        }
                    }
                    else if (el is ReferencePlane rp)
                    {
                        normal = rp.Normal;
                    }
                }
                if (normal != null)
                {
                    foreach (var axis in candidates)
                    {
                        if (Math.Abs(normal.DotProduct(axis)) < 0.98)
                            return axis;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ProjectNonParallelAxis: {ex.Message}"); }
            return XYZ.BasisX;
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
                // Revit 2025 removed PostableCommand.EditFamily; fall back to
                // the API method Document.EditFamily(family) which opens the
                // family for editing in a new tab. STING owns no UI focus
                // logic so we trust Revit to bring the editor forward.
                ctx.UIDoc.Selection.SetElementIds(new[] { inst.Id });
                ctx.Doc.EditFamily(fam);
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
