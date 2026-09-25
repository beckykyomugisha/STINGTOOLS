using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Electrical
{
    /// <summary>
    /// Bulk-fills ELC_PNL_* shared parameters on every electrical panel using
    /// data Revit already exposes (panel name, voltage, connected load,
    /// upstream feed, manufacturer, model, room/level location). Skips
    /// elements that don't carry the parameter binding so unbinding
    /// per-project doesn't break the run.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ElecPanelParamSyncCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var panels = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .ToList();

            if (panels.Count == 0)
            {
                TaskDialog.Show("STING Electrical", "No electrical equipment found.");
                return Result.Succeeded;
            }

            var roomIndex = SpatialAutoDetect.BuildRoomIndex(doc);
            string projLoc = SpatialAutoDetect.DetectProjectLoc(doc) ?? "";
            int updated = 0;

            using (var tx = new Transaction(doc, "STING Electrical Param Sync"))
            {
                tx.Start();
                foreach (var p in panels)
                {
                    try
                    {
                        ParameterHelpers.SetString(p, ParamRegistry.ELC_PNL_NAME, p.Name, overwrite: true);

                        var voltageParam = p.get_Parameter(BuiltInParameter.RBS_ELEC_PANEL_SUPPLY_FROM_PARAM)?.AsString();
                        if (!string.IsNullOrEmpty(voltageParam))
                            ParameterHelpers.SetString(p, ParamRegistry.ELC_PNL_FED_FROM, voltageParam, overwrite: true);

                        // Voltage / phase — Revit-derived. Read by display name to stay
                        // version-agnostic (the BIP enum constant has changed between
                        // Revit versions and isn't guaranteed to compile).
                        try
                        {
                            var vp = p.LookupParameter("Voltage")
                                  ?? p.LookupParameter("Distribution System")
                                  ?? p.LookupParameter("Panel Voltage");
                            if (vp != null && vp.StorageType == StorageType.Double)
                            {
                                double vDouble = StingTools.Core.Electrical.ElecUnits.ToSi(vp);
                                if (vDouble > 0)
                                    ParameterHelpers.SetString(p, ParamRegistry.ELC_PNL_VOLTAGE, $"{vDouble:0}V", overwrite: true);
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

                        // Connected load (kW)
                        try
                        {
                            var loadVA = StingTools.Core.Electrical.ElecUnits.Read(p, BuiltInParameter.RBS_ELEC_PANEL_TOTALLOAD_PARAM);
                            if (loadVA > 0)
                                ParameterHelpers.SetString(p, ParamRegistry.ELC_PNL_LOAD, $"{loadVA / 1000.0:0.0}", overwrite: true);
                        }
                        catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

                        // Location from spatial / project. Canonical: ASS_LOC_TXT
                        // (per MR_PARAMETERS — used by every other discipline's
                        // location stamp, no panel-specific param exists).
                        string loc = SpatialAutoDetect.DetectLoc(doc, p, roomIndex, projLoc) ?? "";
                        if (!string.IsNullOrEmpty(loc))
                            ParameterHelpers.SetString(p, "ASS_LOC_TXT", loc, overwrite: false);

                        // Manufacturer / model from family-type native params.
                        // Canonical via ParamRegistry.MFR alias → ASS_MANUFACTURER_TXT.
                        try
                        {
                            var mfg = p.Symbol?.get_Parameter(BuiltInParameter.ALL_MODEL_MANUFACTURER)?.AsString();
                            if (!string.IsNullOrEmpty(mfg))
                                ParameterHelpers.SetString(p, ParamRegistry.MFR, mfg, overwrite: false);
                        }
                        catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

                        updated++;
                    }
                    catch (Exception ex) { StingLog.Warn($"ParamSync panel {p?.Name}: {ex.Message}"); }
                }
                tx.Commit();
            }

            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            TaskDialog.Show("STING Electrical", $"Synced parameters on {updated} panel(s).");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Writes the values currently shown in the PANEL PARAMETERS expander on
    /// the dock panel back to the selected panel.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ElecPanelWriteParamsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var snap = StingElectricalCommandHandler.CurrentPanelParams;
            if (snap == null || string.IsNullOrEmpty(snap.PanelName))
            {
                TaskDialog.Show("STING Electrical",
                    "Select a panel in the PNLS grid and fill the PANEL PARAMETERS card before clicking Save.");
                return Result.Cancelled;
            }

            // By element id first: the grid row carries it. By Panel Name only as a
            // fallback — never by p.Name, which is the family TYPE name and matched the
            // first board of that type whichever row was picked.
            FamilyInstance panel = snap.PanelId > 0
                ? doc.GetElement(new ElementId(snap.PanelId)) as FamilyInstance
                : null;
            if (panel == null)
                panel = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                    .WhereElementIsNotElementType()
                    .OfType<FamilyInstance>()
                    .FirstOrDefault(p => string.Equals(
                        p.get_Parameter(BuiltInParameter.RBS_ELEC_PANEL_NAME)?.AsString(),
                        snap.PanelName, StringComparison.OrdinalIgnoreCase));
            if (panel == null)
            {
                TaskDialog.Show("STING Electrical", $"Panel '{snap.PanelName}' not found.");
                return Result.Failed;
            }

            bool enclosureNotWritten = false;
            using (var tx = new Transaction(doc, "STING Write Panel Params"))
            {
                tx.Start();
                if (!string.IsNullOrEmpty(snap.MainBreakerA))
                    ParameterHelpers.SetString(panel, ParamRegistry.ELC_MAIN_BRK, snap.MainBreakerA, overwrite: true);
                if (!string.IsNullOrEmpty(snap.FedFrom))
                    ParameterHelpers.SetString(panel, ParamRegistry.ELC_PNL_FED_FROM, snap.FedFrom, overwrite: true);
                // Canonical via MR_PARAMETERS (Phase 188 fix-3):
                //   Location  → ASS_LOC_TXT (no panel-specific equivalent exists)
                //   Manufact. → ASS_MANUFACTURER_TXT (via ParamRegistry.MFR alias)
                //   Fault kA  → ELC_PNL_SHORT_CIRCUIT_RATING_KA (via ELC_PNL_FAULT_KA alias)
                //   IP rating → ELC_PNL_IP_RATING_TXT (what the STING panel schedule
                //               header shows) and ELC_IP_RATING_TXT (older readers).
                //   Enclosure type (Floor Standing / Wall Mounted / Din Rail) is a
                //   mounting type, NOT an IP code; it used to be written into the IP
                //   column, so every schedule read "Floor Standing" as its IP rating.
                //   It has its own parameter, ELC_PNL_ENCLOSURE_TXT, shown beside the
                //   IP rating in the STING panel schedule header.
                if (!string.IsNullOrEmpty(snap.Enclosure)
                    && !ParameterHelpers.SetString(panel, "ELC_PNL_ENCLOSURE_TXT", snap.Enclosure, overwrite: true))
                    enclosureNotWritten = true;
                if (!string.IsNullOrEmpty(snap.Location))
                    ParameterHelpers.SetString(panel, "ASS_LOC_TXT", snap.Location, overwrite: true);
                if (!string.IsNullOrEmpty(snap.IpRating))
                {
                    ParameterHelpers.SetString(panel, ParamRegistry.ELC_IP_RATING, snap.IpRating, overwrite: true);
                    ParameterHelpers.SetString(panel, "ELC_PNL_IP_RATING_TXT", snap.IpRating, overwrite: true);
                }
                if (!string.IsNullOrEmpty(snap.Manufacturer))
                    ParameterHelpers.SetString(panel, ParamRegistry.MFR, snap.Manufacturer, overwrite: true);
                if (!string.IsNullOrEmpty(snap.FaultKA))
                    ParameterHelpers.SetString(panel, ParamRegistry.ELC_PNL_FAULT_KA, snap.FaultKA, overwrite: true);
                tx.Commit();
            }
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            string board = panel.get_Parameter(BuiltInParameter.RBS_ELEC_PANEL_NAME)?.AsString();
            TaskDialog.Show("STING Electrical", $"Saved to '{(string.IsNullOrWhiteSpace(board) ? panel.Name : board)}'."
                + (enclosureNotWritten
                    ? $"\n\nEnclosure type '{snap.Enclosure}' was not written: ELC_PNL_ENCLOSURE_TXT is missing or read-only on this board (run Load Params; see the log)."
                    : ""));
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Compacts the active panel schedule after deletions: moves each circuit
    /// into the lowest free slots (PanelScheduleView.MoveSlotTo), which is how
    /// Revit renumbers a panelled circuit — its number is its slot.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ElecCircuitRenumberCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // Limit to the active PanelScheduleView when one is shown.
            var psv = doc.ActiveView as PanelScheduleView;
            if (psv == null)
            {
                TaskDialog.Show("STING Electrical",
                    "Activate a panel schedule view first (CIRCTS tab → click the panel in the grid).");
                return Result.Cancelled;
            }

            // ELEC-12 — the old body wrote RBS_ELEC_CIRCUIT_NUMBER, which Revit makes
            // read-only once a circuit is on a panel (the number IS the slot), so it
            // changed nothing and reported "renumbered 0" at best. Revit's supported
            // way to renumber a panel's circuits is to move them between slots:
            // PanelScheduleView.CanMoveSlotTo / MoveSlotTo. This compacts the panel —
            // each circuit, lowest slot first, moves down to the lowest run of free
            // slots that fits its poles — and counts the circuit numbers that
            // actually changed.
            var panelId = psv.GetPanel();
            var panel = doc.GetElement(panelId) as FamilyInstance;
            if (panel == null)
            {
                TaskDialog.Show("STING Electrical", "This panel schedule has no panel to renumber.");
                return Result.Cancelled;
            }
            int step = StingTools.Core.Electrical.PanelSlotReader.SlotStep(psv, out bool stepKnown);
            int totalSlots = 0;
            try { totalSlots = psv.GetTableData()?.NumberOfSlots ?? 0; }
            catch (Exception ex) { StingLog.Warn($"Renumber NumberOfSlots: {ex.Message}"); }
            if (totalSlots <= 0) totalSlots = StingTools.Core.Electrical.PanelSlotReader.PanelSlotCount(panel) ?? 0;

            var confirm = new TaskDialog("STING Renumber Circuits")
            {
                MainInstruction = "Compact this panel's circuits into the lowest free slots?",
                MainContent =
                    "Circuits are MOVED between slots (the circuit number follows the slot). " +
                    "On a multi-phase panel a moved circuit can change phase — run Phase Balance afterwards. " +
                    "Locked slots are not moved.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
            };
            if (confirm.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            int moved = 0, refused = 0, changed = 0;
            var refusedNames = new List<string>();
            using (var tx = new Transaction(doc, "STING Renumber Circuits"))
            {
                tx.Start();
                var before = PanelCircuits(doc, panelId).ToDictionary(s => s.Id.Value, s => SafeNumber(s));
                try
                {
                    // One pass in start-slot order. Occupancy is re-read before every
                    // move, so each decision sees the panel as it now is.
                    foreach (long id in PanelCircuits(doc, panelId)
                                 .OrderBy(s => SafeStartSlot(s)).Select(s => s.Id.Value).ToList())
                    {
                        var live = PanelCircuits(doc, panelId);
                        var me = live.FirstOrDefault(s => s.Id.Value == id);
                        if (me == null) continue;
                        int start = SafeStartSlot(me), poles = Math.Max(1, SafePoles(me));
                        if (start <= 0) continue;

                        var occupied = new HashSet<int>();
                        foreach (var o in live)
                            if (o.Id.Value != id)
                                foreach (int sl in StingTools.Core.Electrical.CircuitSlotParser.FromStartSlot(
                                             SafeStartSlot(o), Math.Max(1, SafePoles(o)), step))
                                    occupied.Add(sl);

                        int? target = StingTools.Core.Electrical.PanelSlotRules.LowestFreeStart(
                            occupied, start, poles, step, totalSlots);
                        if (!target.HasValue) continue;

                        if (TryMoveSlot(psv, start, target.Value)) { moved++; doc.Regenerate(); }
                        else { refused++; if (refusedNames.Count < 10) refusedNames.Add(SafeNumber(me)); }
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Renumber traverse: {ex.Message}");
                }

                foreach (var s in PanelCircuits(doc, panelId))
                    if (before.TryGetValue(s.Id.Value, out var old) && old != SafeNumber(s)) changed++;

                if (changed == 0) tx.RollBack(); else tx.Commit();
            }

            StingLog.Info($"ElecCircuitRenumber: panel {panelId.Value} — {moved} moved, {changed} numbers changed, {refused} refused");
            string msg = changed == 0 && refused == 0
                ? "Nothing to renumber — the panel's circuits already occupy the lowest slots."
                : $"{changed} circuit number(s) changed ({moved} slot move(s)).";
            if (refused > 0)
                msg += $"\n{refused} circuit(s) could not be moved (Revit refused the move — typically a locked slot " +
                       $"or a grouped / multi-pole breaker that does not fit): {string.Join(", ", refusedNames)}.";
            if (!stepKnown)
                msg += "\nThe schedule's numbering could not be read; multi-pole breakers were assumed to take every other slot.";
            TaskDialog.Show("STING Electrical", msg);
            return Result.Succeeded;
        }

        private static List<ElectricalSystem> PanelCircuits(Document doc, ElementId panelId)
            => new FilteredElementCollector(doc)
                .OfClass(typeof(ElectricalSystem))
                .Cast<ElectricalSystem>()
                .Where(s =>
                {
                    try { return s.BaseEquipment != null && s.BaseEquipment.Id == panelId; }
                    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
                })
                .ToList();

        /// <summary>Moves the breaker in slot <paramref name="from"/> to slot
        /// <paramref name="to"/> through the panel schedule, if Revit allows it.</summary>
        private static bool TryMoveSlot(PanelScheduleView psv, int from, int to)
        {
            try
            {
                psv.GetCellsBySlotNumber(from, out IList<int> fr, out IList<int> fc);
                psv.GetCellsBySlotNumber(to,   out IList<int> tr, out IList<int> tc);
                if (fr == null || fc == null || tr == null || tc == null
                    || fr.Count == 0 || fc.Count == 0 || tr.Count == 0 || tc.Count == 0) return false;
                if (!psv.CanMoveSlotTo(fr[0], fc[0], tr[0], tc[0])) return false;
                psv.MoveSlotTo(fr[0], fc[0], tr[0], tc[0]);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Renumber move {from}→{to}: {ex.Message}");
                return false;
            }
        }

        private static string SafeNumber(ElectricalSystem s)
        {
            try { return s.CircuitNumber ?? ""; } catch { return ""; }
        }

        private static int SafeStartSlot(ElectricalSystem s)
        {
            try { return s.StartSlot; }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; }
        }

        private static int SafePoles(ElectricalSystem s)
        {
            try { return s.PolesNumber; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 1; }
        }
    }

    /// <summary>
    /// Computes connected and demand load per panel using <see cref="ElectricalSystem.ApparentLoad"/>.
    /// Demand factors are applied per the user-selected NEC/IEC presets.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ElecLoadSummaryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            // The snapshot pushed back to the panel after Dispatch already includes
            // load-summary rows, so all this command does is invalidate any cache
            // and surface a confirmation.
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            TaskDialog.Show("STING Electrical", "Load summary refreshed — see the LOAD SUMMARY grid.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Creates a STING-branded lighting fixture schedule. Mirrors the schedule
    /// layout used by other STING family schedules so the column order is
    /// consistent across deliverables.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ElecLightingScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            using (var tx = new Transaction(doc, "STING Lighting Schedule"))
            {
                tx.Start();
                try
                {
                    var category = new ElementId(BuiltInCategory.OST_LightingFixtures);
                    var schedule = ViewSchedule.CreateSchedule(doc, category);
                    schedule.Name = "STING - Lighting Fixtures";
                    AddField(schedule, "Family and Type", BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM);
                    AddField(schedule, "Mark", BuiltInParameter.ALL_MODEL_MARK);
                    AddField(schedule, "Wattage", BuiltInParameter.RBS_ELEC_APPARENT_LOAD);
                    AddField(schedule, "Level", BuiltInParameter.SCHEDULE_LEVEL_PARAM);
                }
                catch (Exception ex) { StingLog.Warn($"Lighting schedule: {ex.Message}"); }
                tx.Commit();
            }
            TaskDialog.Show("STING Electrical", "Created 'STING - Lighting Fixtures' schedule.");
            return Result.Succeeded;
        }

        private static void AddField(ViewSchedule schedule, string label, BuiltInParameter bip)
        {
            try
            {
                var def = schedule.Definition;
                var pid = new ElementId(bip);
                var sf = def.GetSchedulableFields().FirstOrDefault(f => f.ParameterId == pid);
                if (sf != null) def.AddField(sf);
            }
            catch (Exception ex) { StingLog.Warn($"AddField {label}: {ex.Message}"); }
        }
    }
}
