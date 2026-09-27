using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Electrical;
using StingTools.UI;

namespace StingTools.Commands.Electrical.CableSizer
{
    /// <summary>
    /// Cable_ReloadTables: drops the cached BS 7671 tables and re-reads the corporate
    /// STING_WIRE_TABLES.json plus this project's override
    /// (<c>_BIM_COORD/bs7671_wire_tables.json</c>), then reports what is in force.
    /// An invalid override is reported as NOT IN FORCE and the command returns Failed:
    /// every sizer refuses until it is fixed, so saying "reloaded" would mislead.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WireTablesReloadCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData?.Application?.ActiveUIDocument?.Document;
            if (doc == null) { message = "No document open."; return Result.Failed; }

            CableSizerEngine.InvalidateCache();
            var data = CableSizerEngine.Bs7671Tables(doc);
            string report = Bs7671TableLayering.Describe(data);
            StingLog.Info("Cable_ReloadTables: " + report.Replace(Environment.NewLine, " | "));

            // The wire reference grid refreshes on the UI thread without a Document; point it
            // at this document's override so it shows the tables just loaded.
            StingTools.Commands.Electrical.ElectricalSnapshotBuilder.LastWireTableOverridePath =
                CableSizerEngine.OverridePath(doc);
            try { StingElectricalCommandHandler.Instance?.RefreshWireRefTable(null); }
            catch (Exception ex) { StingLog.Warn($"Cable_ReloadTables grid refresh: {ex.Message}"); }

            TaskDialog.Show("STING Wire Tables", report);
            if (!string.IsNullOrEmpty(data.LoadError)) { message = data.LoadError; return Result.Failed; }
            return Result.Succeeded;
        }
    }
}
