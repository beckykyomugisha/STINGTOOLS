// StingTools — Toggle + clear commands for the envelope-stale IUpdater.
//
// Hvac_EnvelopeStaleToggle    — enable / disable the IUpdater for this session.
// Hvac_EnvelopeStaleClear     — clear HVC_LOAD_STALE_BOOL on every Space.
//
// The IUpdater is registered at startup but disabled by default to keep
// the model edit-loop fast on projects that don't run block-loads.

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Hvac.Loads;
using StingTools.UI;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacEnvelopeStaleToggleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                bool nowOn = !HvacEnvelopeStaleUpdater.IsEnabled;
                HvacEnvelopeStaleUpdater.SetEnabled(nowOn);
                TaskDialog.Show("STING HVAC — Envelope-stale Updater",
                    $"Envelope-stale IUpdater is now {(nowOn ? "ENABLED" : "DISABLED")}.\n\n" +
                    "When enabled, geometry changes to exterior walls / windows / doors / curtain " +
                    "panels / roofs / floors stamp HVC_LOAD_STALE_BOOL=1 on the affected Spaces. " +
                    "Run Hvac_BlockLoad to recompute + clear the flag, or use Hvac_EnvelopeStaleClear.");
                try { StingHvacPanel.Instance?.PushRunRow($"Envelope-stale {(nowOn ? "on" : "off")}", "⬤"); }
                catch (Exception ex) { StingLog.Warn($"Panel push: {ex.Message}"); }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacEnvelopeStaleToggleCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacEnvelopeStaleClearCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                int cleared = 0, skipped = 0;
                var spaces = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MEPSpaces)
                    .WhereElementIsNotElementType()
                    .ToList();
                using (var tx = new Transaction(doc, "STING Clear HVAC load-stale flags"))
                {
                    tx.Start();
                    foreach (var sp in spaces)
                    {
                        try
                        {
                            int v = 0;
                            try { v = sp.LookupParameter("HVC_LOAD_STALE_BOOL")?.AsInteger() ?? 0; } catch { }
                            if (v == 0) { skipped++; continue; }
                            if (ParameterHelpers.SetInt(sp, "HVC_LOAD_STALE_BOOL", 0, overwrite: true))
                            {
                                ParameterHelpers.SetString(sp, "HVC_LOAD_STALE_REASON_TXT", "", overwrite: true);
                                cleared++;
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"StaleClear {sp.Id}: {ex.Message}"); }
                    }
                    tx.Commit();
                }
                TaskDialog.Show("STING HVAC", $"Cleared HVC_LOAD_STALE_BOOL on {cleared} spaces ({skipped} already clean).");
                try { StingHvacPanel.Instance?.PushRunRow($"Stale clear ({cleared} spaces)", "⬤"); }
                catch (Exception ex) { StingLog.Warn($"Panel push: {ex.Message}"); }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacEnvelopeStaleClearCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
