using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════════════════
    //  ScheduledExportRunner — unattended driver for ExportCenterState.ScheduledExports.
    //
    //  A scheduled job names a saved profile + saved set and a NextRunUtc. When the
    //  job is due, the engine runs it headlessly (no UI), records the result, and
    //  re-schedules the next occurrence per its Repeat rule. Invoked manually via
    //  ExportCenterRunSchedulesCommand, or automatically on DocumentSaved when the
    //  user has opted in (EnableSaveTriggeredSchedules).
    //
    //  Headless by design — ExportCenterEngine.Run does no WPF / TaskDialog work, so
    //  this is safe to call from an event handler on the Revit API thread.
    //
    //  Salvaged from claude/dreamy-maxwell-prlg44 and re-targeted at main's current
    //  ExportCenterEngine API (LoadState / SaveState / ResolveSet / Run — all
    //  signature-compatible; main already carried the ScheduledExport model + the
    //  ExportCenterState.ScheduledExports list, but no execution layer).
    // ════════════════════════════════════════════════════════════════════════════

    public static class ScheduledExportRunner
    {
        /// <summary>
        /// Run every enabled schedule whose NextRunUtc is in the past. Returns the
        /// number of jobs run. <paramref name="fromSave"/> gates the save-triggered
        /// path behind the opt-in flag so files never appear unexpectedly.
        /// </summary>
        public static int RunDue(Document doc, bool fromSave)
        {
            if (doc == null) return 0;

            ExportCenterState state;
            try { state = ExportCenterEngine.LoadState(); }
            catch (Exception ex) { StingLog.Warn($"ScheduledExportRunner load: {ex.Message}"); return 0; }

            if (fromSave && !state.EnableSaveTriggeredSchedules) return 0;
            if (state.ScheduledExports == null || state.ScheduledExports.Count == 0) return 0;

            var now = DateTime.UtcNow;
            int ran = 0;
            bool dirty = false;

            foreach (var sch in state.ScheduledExports)
            {
                if (sch == null || !sch.Enabled || sch.NextRunUtc > now) continue;

                var profile = state.Profiles.FirstOrDefault(p =>
                    string.Equals(p.Name, sch.ProfileName, StringComparison.OrdinalIgnoreCase));
                if (profile == null) { sch.LastResult = "Profile not found"; dirty = true; continue; }

                string setName = string.IsNullOrEmpty(sch.SetName) ? profile.DefaultSetName : sch.SetName;
                var set = state.SavedSets.FirstOrDefault(s =>
                              string.Equals(s.Name, setName, StringComparison.OrdinalIgnoreCase))
                          ?? state.SavedSets.FirstOrDefault(s => s.Name == "All Sheets");

                try
                {
                    var ids = ExportCenterEngine.ResolveSet(doc, set, out _);
                    if (ids.Count == 0)
                    {
                        sch.LastResult = "No sheets resolved";
                    }
                    else
                    {
                        var res = ExportCenterEngine.Run(doc, profile, ids);
                        sch.LastResult = $"{res.Success} ok / {res.Failed} failed"
                            + (res.Warnings.Count > 0 ? $" ({res.Warnings.Count} warning(s))" : "");
                        ran++;
                        StingLog.Info($"Scheduled export '{sch.ProfileName}' [{setName}]: {sch.LastResult}");
                    }
                }
                catch (Exception ex)
                {
                    sch.LastResult = "Error: " + ex.Message;
                    StingLog.Error($"Scheduled export '{sch.ProfileName}' failed", ex);
                }

                sch.LastRunUtc = now;
                Reschedule(sch, now);
                dirty = true;
            }

            if (dirty)
                try { ExportCenterEngine.SaveState(state); }
                catch (Exception ex) { StingLog.Warn($"ScheduledExportRunner save: {ex.Message}"); }

            return ran;
        }

        private static void Reschedule(ScheduledExport sch, DateTime now)
        {
            switch ((sch.Repeat ?? "Once").Trim().ToLowerInvariant())
            {
                case "daily":   sch.NextRunUtc = now.AddDays(1);   break;
                case "weekly":  sch.NextRunUtc = now.AddDays(7);   break;
                case "monthly": sch.NextRunUtc = now.AddMonths(1); break;
                default:        sch.Enabled = false;               break; // "Once" — disable after running
            }
        }
    }

    /// <summary>Manually run any due scheduled-export jobs from a button / workflow step.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportCenterRunSchedulesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                var doc = data?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }

                int ran = ScheduledExportRunner.RunDue(doc, fromSave: false);
                TaskDialog.Show("STING Export Centre",
                    ran == 0
                        ? "No scheduled export jobs were due."
                        : $"Ran {ran} scheduled export job(s).\nSee the export folder and the STING_Export_Report CSV for details.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ExportCenterRunSchedulesCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
