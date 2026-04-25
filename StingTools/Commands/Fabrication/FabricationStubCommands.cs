// StingTools v4 MVP — Fabrication Workspace stub commands.
//
// These commands back the new buttons added by the Fabrication
// Workspace dialog (Smart group / Clash pre-flight / Incremental
// rebuild / BOM roll-up / Doc Register link). Full implementations
// land later — for now they surface a clear roadmap so the workspace
// can dispatch them without "command not wired" errors.
//
// UndoFabPackageCommand already exists in FabricationUndoManager.cs,
// it just needed a tag in StingCommandHandler.

using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

namespace StingTools.Commands.Fabrication
{
    /// <summary>
    /// Smart group — re-clusters the spool grouping for the current
    /// scope using STING_FAB_RULES.json transport / lift / weight
    /// limits before assemblies are emitted. Stub for v4 MVP.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SmartGroupCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, Autodesk.Revit.DB.ElementSet els)
        {
            TaskDialog.Show("STING v4 — Smart Group",
                "Smart Group will re-cluster spools by transport / lift / weight " +
                "limits from STING_FAB_RULES.json before Generate Package runs.\n\n" +
                "Roadmap entry — wiring lands alongside the rule-driven AssemblyGrouper update.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Clash pre-flight — runs Navisworks-style hard-clash detection
    /// against the current selection BEFORE assemblies are created so
    /// the operator sees and fixes interferences first. Stub for v4 MVP.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ClashPreflightCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, Autodesk.Revit.DB.ElementSet els)
        {
            TaskDialog.Show("STING v4 — Clash Pre-flight",
                "Pre-flight will run STING_CLASH against the workspace scope and " +
                "block Generate Package until the clash count drops to zero.\n\n" +
                "Roadmap entry — surfaces the existing ClashManager pipeline through " +
                "the Fabrication workspace.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Incremental rebuild — re-runs Generate Package only for spools
    /// whose member elements have changed since the last successful
    /// run. Stub for v4 MVP.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class IncrementalRebuildCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, Autodesk.Revit.DB.ElementSet els)
        {
            TaskDialog.Show("STING v4 — Incremental Rebuild",
                "Incremental Rebuild compares the current workspace scope against the " +
                "last FabricationResult on the undo stack and only regenerates affected " +
                "spools — saves a full project sweep.\n\n" +
                "Roadmap entry — landing alongside the FabricationUndoManager hash diff.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// BOM roll-up — aggregates per-discipline cut list / weld map /
    /// PCF data into a single multi-sheet XLSX bill of materials.
    /// Stub for v4 MVP.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BomRollupCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, Autodesk.Revit.DB.ElementSet els)
        {
            TaskDialog.Show("STING v4 — BOM Roll-up",
                "BOM Roll-up will combine cut list, weld map and PCF outputs into " +
                "STING_v4_bom_rollup.xlsx (one sheet per discipline + a project total).\n\n" +
                "Roadmap entry — uses the existing ClosedXML pipeline.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Link → Doc Register — registers all generated shop drawings
    /// with the BIM Manager document register for ISO 19650 compliance.
    /// Stub for v4 MVP.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LinkDocRegisterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, Autodesk.Revit.DB.ElementSet els)
        {
            TaskDialog.Show("STING v4 — Link to Doc Register",
                "Link → Doc Register will push every SP-… sheet emitted on the last " +
                "successful Generate Package into the project document register " +
                "(STATUS = WIP, REV = P01) so it shows up in the next transmittal.\n\n" +
                "Roadmap entry — bridges FabricationResult.SheetIds to the existing " +
                "document register pipeline.");
            return Result.Succeeded;
        }
    }
}
