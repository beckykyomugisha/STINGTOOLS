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
    // ── Circuit_Create ────────────────────────────────────────────────────
    /// <summary>
    /// Adds a spare slot to the panel schedule of a user-picked panel. This
    /// is the safest programmatic path — full element-based circuit
    /// creation requires a user-picked connectable element, which is what
    /// the Phase 178 CircuitWizardDialog handles.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CircuitCreateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No document open."; return Result.Failed; }
            var doc = ctx.Doc;

            var panels = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .Cast<FamilyInstance>().ToList();
            if (panels.Count == 0)
            { TaskDialog.Show("STING Circuit", "No electrical panels found in the model."); return Result.Cancelled; }

            string panelName = StingListPicker.Show("Select Panel",
                "Choose a panel to add a spare slot to:",
                panels.Select(p => p.Name).OrderBy(n => n).ToList());
            if (string.IsNullOrEmpty(panelName)) return Result.Cancelled;
            var panel = panels.FirstOrDefault(p => p.Name == panelName);
            if (panel == null) return Result.Cancelled;

            var psv = new FilteredElementCollector(doc)
                .OfClass(typeof(PanelScheduleView)).Cast<PanelScheduleView>()
                .FirstOrDefault(v => v.GetPanel()?.Value == panel.Id.Value);
            if (psv == null)
            {
                TaskDialog.Show("STING Circuit",
                    $"No panel schedule exists for '{panelName}'. Run PNLS → Batch Schedules first.");
                return Result.Cancelled;
            }

            using (var tx = new Transaction(doc, "STING Create Spare Circuit"))
            {
                tx.Start();
                int added = 0;
                try
                {
                    var body = psv.GetTableData()?.GetSectionData(SectionType.Body);
                    if (body == null) { tx.RollBack(); msg = "Schedule has no body section."; return Result.Failed; }
                    int rows = body.NumberOfRows;
                    int cols = body.NumberOfColumns;
                    for (int r = 0; r < rows && added == 0; r++)
                    {
                        for (int c = 0; c < cols && added == 0; c++)
                        {
                            try
                            {
                                bool occupied = psv.IsSpare(r, c) || psv.IsSpace(r, c)
                                    || (psv.GetCircuitByCell(r, c) != null);
                                if (!occupied) { psv.AddSpare(r, c); added = 1; }
                            }
                            catch (Exception ex) { StingLog.Info($"AddSpare probe [{r},{c}] on '{psv.Name}': {ex.Message}"); }
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack(); msg = ex.Message;
                    StingLog.Error($"CircuitCreate on {panelName}", ex);
                    return Result.Failed;
                }
                try { ComplianceScan.InvalidateCache(); } catch { }
                if (added == 0)
                {
                    TaskDialog.Show("STING Circuit",
                        $"All slots on '{panelName}' are already occupied. Use 'Convert Spaces → Spares' or " +
                        "expand the schedule before adding more.");
                    return Result.Cancelled;
                }
                TaskDialog.Show("STING Circuit",
                    $"Added a spare slot to panel '{panelName}'. To assign a real load: connect a fixture/" +
                    "device to the panel in the model, then run Circuit Sort to organise the sequence.");
            }
            return Result.Succeeded;
        }
    }

    // ── Circuit_Delete ────────────────────────────────────────────────────
    /// <summary>
    /// Deletes every spare/space slot across all panel schedules. Active
    /// circuits (carrying real loads) are skipped — Revit raises on those
    /// and we surface a count rather than aborting the batch.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CircuitDeleteCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No document open."; return Result.Failed; }
            var doc = ctx.Doc;

            var psvList = new FilteredElementCollector(doc)
                .OfClass(typeof(PanelScheduleView)).Cast<PanelScheduleView>().ToList();
            if (psvList.Count == 0)
            { TaskDialog.Show("STING Circuit", "No panel schedules in the project."); return Result.Cancelled; }

            int spareCount = 0, spaceCount = 0;
            using (var tx = new Transaction(doc, "STING Delete Spare/Space Slots"))
            {
                tx.Start();
                foreach (var psv in psvList)
                {
                    try
                    {
                        var body = psv.GetTableData()?.GetSectionData(SectionType.Body);
                        if (body == null) continue;
                        int rows = body.NumberOfRows, cols = body.NumberOfColumns;
                        for (int r = 0; r < rows; r++)
                        for (int c = 0; c < cols; c++)
                        {
                            try
                            {
                                if (psv.IsSpare(r, c)) { psv.RemoveSpare(r, c); spareCount++; }
                                else if (psv.IsSpace(r, c)) { psv.RemoveSpace(r, c); spaceCount++; }
                            }
                            catch (Exception ex) { StingLog.Info($"Remove [{r},{c}] on {psv.Name}: {ex.Message}"); }
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"CircuitDelete scan {psv.Name}: {ex.Message}"); }
                }
                tx.Commit();
            }
            try { ComplianceScan.InvalidateCache(); } catch { }
            TaskDialog.Show("STING Circuit",
                $"Removed {spareCount} spare(s) and {spaceCount} space(s) across {psvList.Count} schedule(s).\n" +
                "Active circuits (with real loads) were not touched.");
            return Result.Succeeded;
        }
    }

    // ── Circuit_Move ──────────────────────────────────────────────────────
    /// <summary>
    /// Moves the user-selected ElectricalSystem to a different panel via
    /// <c>ElectricalSystem.SelectPanel(panel)</c>. This is the Revit
    /// 2024+ stable API for circuit re-parenting; the older "disconnect
    /// + reconnect" pattern conflicts with the API surface we already hit
    /// in Phase 178 / 179 (ElectricalSystem.Create takes a single
    /// Connector, not a List&lt;ElementId&gt;).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CircuitMoveCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No document open."; return Result.Failed; }
            var doc = ctx.Doc;
            var uidoc = ctx.UIDoc;

            var sel = uidoc.Selection.GetElementIds();
            var srcSystem = sel.Select(id => doc.GetElement(id)).OfType<ElectricalSystem>().FirstOrDefault();
            if (srcSystem == null)
            {
                TaskDialog.Show("STING Circuit",
                    "Select an ElectricalSystem (circuit) before running Circuit Move.\n\n" +
                    "Tip: select an electrical device in the model, click 'Select Circuit' in Revit's ribbon, " +
                    "then return to STING and run this command.");
                return Result.Cancelled;
            }
            string srcPanelName = "";
            try { srcPanelName = srcSystem.PanelName ?? ""; } catch { }

            var panels = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .Cast<FamilyInstance>()
                .Where(p => !string.Equals(p.Name, srcPanelName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (panels.Count == 0)
            { TaskDialog.Show("STING Circuit", "No other panels available to move this circuit to."); return Result.Cancelled; }

            string srcLabel = "";
            try
            {
                string num = srcSystem.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER)?.AsString() ?? "?";
                srcLabel = $"#{num} on '{srcPanelName}'";
            }
            catch { srcLabel = $"id {srcSystem.Id?.Value}"; }

            string destName = StingListPicker.Show("Move Circuit",
                $"Move circuit {srcLabel} to:",
                panels.Select(p => p.Name).OrderBy(n => n).ToList());
            if (string.IsNullOrEmpty(destName)) return Result.Cancelled;
            var destPanel = panels.FirstOrDefault(p => p.Name == destName);
            if (destPanel == null) return Result.Cancelled;

            using (var tx = new Transaction(doc, "STING Move Circuit"))
            {
                tx.Start();
                try
                {
                    srcSystem.SelectPanel(destPanel);
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    msg = ex.Message;
                    StingLog.Error("CircuitMove", ex);
                    TaskDialog.Show("STING Circuit",
                        $"Move failed: {ex.Message}\n\n" +
                        "Revit API limit: SelectPanel rejects voltage / phase / pole mismatches. " +
                        "Verify the destination panel matches the circuit's voltage profile.");
                    return Result.Failed;
                }
            }
            try { ComplianceScan.InvalidateCache(); } catch { }
            TaskDialog.Show("STING Circuit",
                $"Moved circuit {srcLabel} to '{destName}'. Verify the new circuit number in the panel schedule.");
            return Result.Succeeded;
        }
    }

    // ── Circuit_Sort ──────────────────────────────────────────────────────
    /// <summary>
    /// Re-sequences circuit numbers within each panel by load (largest
    /// first) / load name (A→Z) / current order. Best-effort writes via
    /// <c>RBS_ELEC_CIRCUIT_NUMBER</c>; Revit rejects writes on circuits
    /// where the panel's slot-style numbering is auto-managed — those
    /// are reported in the result message.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CircuitSortCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No document open."; return Result.Failed; }
            var doc = ctx.Doc;

            var td = new TaskDialog("STING Sort Circuits")
            {
                MainInstruction = "Sort circuit numbers within each panel",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "By load (largest first)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "By load name (A → Z)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Compact gaps (re-sequence in current order)");
            var choice = td.Show();
            if (choice == TaskDialogResult.Cancel) return Result.Cancelled;

            var circuits = new FilteredElementCollector(doc)
                .OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>()
                .Where(s =>
                {
                    try { return !string.IsNullOrEmpty(s.PanelName); }
                    catch { return false; }
                })
                .ToList();
            if (circuits.Count == 0)
            { TaskDialog.Show("STING Sort Circuits", "No panel circuits found."); return Result.Cancelled; }

            var byPanel = circuits.GroupBy(c =>
                {
                    try { return c.PanelName ?? ""; } catch { return ""; }
                })
                .ToList();
            int updated = 0, readOnly = 0;
            using (var tx = new Transaction(doc, "STING Sort Circuits"))
            {
                tx.Start();
                foreach (var group in byPanel)
                {
                    var sorted = choice == TaskDialogResult.CommandLink1
                        ? group.OrderByDescending(SafeApparentLoad).ToList()
                        : choice == TaskDialogResult.CommandLink2
                            ? group.OrderBy(SafeLoadName, StringComparer.OrdinalIgnoreCase).ToList()
                            : group.OrderBy(c => SafeCircuitNumber(c)).ToList();
                    for (int i = 0; i < sorted.Count; i++)
                    {
                        try
                        {
                            var p = sorted[i].get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER);
                            if (p == null) continue;
                            if (p.IsReadOnly) { readOnly++; continue; }
                            p.Set((i + 1).ToString());
                            updated++;
                        }
                        catch (Exception ex) { StingLog.Info($"CircuitSort {sorted[i].Id?.Value}: {ex.Message}"); }
                    }
                }
                tx.Commit();
            }
            try { ComplianceScan.InvalidateCache(); } catch { }
            TaskDialog.Show("STING Sort Circuits",
                $"Re-sequenced {updated} circuit number(s) across {byPanel.Count} panel(s).\n" +
                (readOnly == 0 ? ""
                  : $"{readOnly} circuit(s) had a read-only RBS_ELEC_CIRCUIT_NUMBER (Revit auto-managed) and were skipped.\n"));
            return Result.Succeeded;
        }

        private static double SafeApparentLoad(ElectricalSystem s)
        { try { return s.ApparentLoad; } catch { return 0; } }

        private static string SafeLoadName(ElectricalSystem s)
        { try { return s.LoadName ?? ""; } catch { return ""; } }

        private static int SafeCircuitNumber(ElectricalSystem s)
        {
            try
            {
                string raw = s.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER)?.AsString() ?? "0";
                int.TryParse(new string(raw.TakeWhile(char.IsDigit).ToArray()), out int n);
                return n;
            }
            catch { return 0; }
        }
    }
}
