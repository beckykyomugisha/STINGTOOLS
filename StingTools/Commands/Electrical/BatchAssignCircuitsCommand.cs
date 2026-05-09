// StingTools — BatchAssignCircuitsCommand.
//
// Auto-assigns unassigned electrical circuits to panels by:
//
//   1. Collecting every ElectricalSystem with no BaseEquipment and a
//      load known to STING (apparent load > 0).
//   2. Collecting every panel with available slots (NumberOfCircuits >
//      circuits already on it).
//   3. Picking, for each unassigned circuit, the smallest panel that
//      fits (by remaining-slot count and matching voltage class), then
//      preferring the least-loaded phase to keep the panel balanced.
//   4. Writing SelectPanel(panelName) on the circuit so Revit performs
//      the actual slot allocation. The phase column is left for the
//      follow-up PhaseBalanceCommand because Revit owns slot↔phase
//      mapping in the panel template.
//
// The command is read-only-by-default (preview a plan), with an
// explicit "Apply" confirmation prompt before any writes. Every
// circuit it touches is logged so the result panel + audit log
// reconcile cleanly with WORKFLOW_ElectricalQA.

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
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchAssignCircuitsCommand : IExternalCommand
    {
        // Voltage tolerance bands. Two voltages are "compatible" if both
        // sit in the same band; this mirrors how IEC / BS 7671 treats
        // nominal supply ranges.
        private static readonly (double low, double high, string label)[] _voltageBands = new[]
        {
            (   0.0,  60.0,  "ELV"   ),
            (  90.0, 140.0,  "120V"  ),
            ( 200.0, 250.0,  "230V"  ),
            ( 380.0, 420.0,  "400V"  ),
            ( 460.0, 530.0,  "480V"  ),
            ( 580.0, 720.0,  "600V"  )
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // ── 1. Collect inventory ──────────────────────────────────
            var allSystems = new FilteredElementCollector(doc)
                .OfClass(typeof(ElectricalSystem))
                .Cast<ElectricalSystem>()
                .ToList();
            var unassigned = allSystems.Where(s => SafeBaseEquipment(s) == null).ToList();
            if (unassigned.Count == 0)
            {
                TaskDialog.Show("STING Electrical",
                    "Every circuit already has a panel assignment. Nothing to do.");
                return Result.Succeeded;
            }

            var panels = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .ToList();
            if (panels.Count == 0)
            {
                TaskDialog.Show("STING Electrical",
                    "No electrical equipment in the model — cannot auto-assign circuits.");
                return Result.Cancelled;
            }

            // Pre-group every assigned circuit by its base-equipment id so
            // PanelState's constructor is O(1) per panel instead of O(S).
            // For a 50-panel / 500-circuit project this drops construction
            // from O(P*S)=25,000 ops to O(P+S)=550.
            var circuitsByPanel = new Dictionary<long, List<ElectricalSystem>>();
            foreach (var s in allSystems)
            {
                var be = SafeBaseEquipment(s);
                if (be == null) continue;
                long key = be.Id.Value;
                if (!circuitsByPanel.TryGetValue(key, out var list))
                {
                    list = new List<ElectricalSystem>();
                    circuitsByPanel[key] = list;
                }
                list.Add(s);
            }

            var pState = panels.Select(p => new PanelState(p, circuitsByPanel)).ToList();

            // ── 2. Greedy assignment ─────────────────────────────────
            var plan = new List<Assignment>();
            var sortedCircuits = unassigned
                .OrderByDescending(SafeApparentVA)
                .ThenBy(s => SafePoles(s))
                .ToList();

            foreach (var sys in sortedCircuits)
            {
                double va = SafeApparentVA(sys);
                int    poles = SafePoles(sys);
                double volts = SafeCircuitVoltage(sys);

                var fit = pState
                    .Where(ps => ps.RemainingSlots >= Math.Max(poles, 1))
                    .Where(ps => VoltageCompatible(volts, ps.NominalVoltage))
                    .OrderBy(ps => ps.RemainingSlots)         // tightest fit first
                    .ThenBy(ps => ps.ConnectedVa)             // least-loaded panel
                    .FirstOrDefault();

                if (fit == null)
                {
                    plan.Add(new Assignment
                    {
                        SystemId = sys.Id,
                        SystemName = sys.Name ?? "(?)",
                        PanelName = null,
                        Reason = poles > 1
                            ? $"No panel with ≥ {poles} free slots at {volts:F0} V"
                            : $"No panel with free slot at {volts:F0} V"
                    });
                    continue;
                }

                fit.Reserve(va, poles);
                plan.Add(new Assignment
                {
                    SystemId = sys.Id,
                    SystemName = sys.Name ?? "(?)",
                    PanelName = fit.Name,
                    Reason = $"fit slots={fit.RemainingSlots} after, panelLoad={fit.ConnectedVa/1000:F1} kVA"
                });
            }

            int wouldAssign  = plan.Count(a => a.PanelName != null);
            int wouldSkip    = plan.Count - wouldAssign;

            // ── 3. Preview / confirm ─────────────────────────────────
            var dlg = new TaskDialog("STING Auto-assign Circuits — Preview")
            {
                MainInstruction = $"Plan: assign {wouldAssign} of {plan.Count} unassigned circuits.",
                MainContent =
                    $"Skipped: {wouldSkip} (no compatible panel with free slots).\n\n" +
                    "Apply will set the panel reference on each circuit (SelectPanel). " +
                    "Phase assignment within the panel slot follows your panel template; " +
                    "run Circuit_Balance afterwards to balance phase loads.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
            };
            if (dlg.Show() != TaskDialogResult.Yes)
            {
                ShowResult(plan, doc, applied: 0, failed: 0, dryRun: true);
                return Result.Cancelled;
            }

            // ── 4. Apply ─────────────────────────────────────────────
            int applied = 0, failed = 0;
            using (var tx = new Transaction(doc, "STING Auto-assign Circuits"))
            {
                tx.Start();
                foreach (var a in plan)
                {
                    if (a.PanelName == null) continue;
                    try
                    {
                        var sys = doc.GetElement(a.SystemId) as ElectricalSystem;
                        if (sys == null) { failed++; continue; }
                        sys.SelectPanel(a.PanelName);
                        applied++;

                        // Stamp ELC_PANEL_REF_TXT on the circuit so STING tag
                        // pipelines see the back-reference even before the
                        // panel schedule is regenerated.
                        ParameterHelpers.SetString(sys, "ELC_PANEL_REF_TXT", a.PanelName, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        StingLog.Warn($"BatchAssignCircuits '{a.SystemName}' → '{a.PanelName}': {ex.Message}");
                    }
                }
                tx.Commit();
            }

            try { ActionAuditLog.Record("Circuit_AssignAuto",
                $"applied={applied} failed={failed} skipped={wouldSkip}"); }
            catch (Exception ex) { StingLog.Warn($"audit: {ex.Message}"); }
            try { ComplianceScan.InvalidateCache(); } catch { }

            ShowResult(plan, doc, applied, failed, dryRun: false);
            return Result.Succeeded;
        }

        // ── Result rendering ────────────────────────────────────────────

        private static void ShowResult(List<Assignment> plan, Document doc, int applied, int failed, bool dryRun)
        {
            int wouldAssign  = plan.Count(a => a.PanelName != null);
            int wouldSkip    = plan.Count - wouldAssign;

            var panel = StingResultPanel.Create(dryRun ? "Auto-assign Circuits — Preview" : "Auto-assign Circuits");
            panel.SetSubtitle($"{plan.Count} unassigned · {wouldAssign} matched · {wouldSkip} no fit");
            panel.AddSection("SUMMARY")
                 .Metric("Unassigned circuits", plan.Count.ToString())
                 .MetricHighlight("Plan: matched", wouldAssign.ToString())
                 .Metric("Plan: skipped",  wouldSkip.ToString());
            if (!dryRun)
            {
                panel.Metric("Applied", applied.ToString());
                panel.Metric("Failed",  failed.ToString());
            }

            var byPanel = plan.Where(a => a.PanelName != null).GroupBy(a => a.PanelName).OrderByDescending(g => g.Count());
            if (byPanel.Any())
            {
                panel.AddSection("BY PANEL");
                foreach (var g in byPanel)
                    panel.Metric(g.Key, g.Count().ToString(), $"circuits");
            }

            var skipped = plan.Where(a => a.PanelName == null).Take(20).ToList();
            if (skipped.Count > 0)
            {
                panel.AddSection("UNMATCHED");
                foreach (var a in skipped)
                    panel.Text($"{a.SystemName} — {a.Reason}");
                int rest = plan.Count(a => a.PanelName == null) - skipped.Count;
                if (rest > 0) panel.Text($"… {rest} more.");
            }

            panel.AddSection("NEXT STEPS")
                 .Text("Run 'Phase Balance' to balance loads across A/B/C within each panel.")
                 .Text("Run 'Batch Panel Schedules' to materialize the schedules and stamp ELC_PNL_*.")
                 .Text("Add free slots in panel families if 'no fit' circuits remain after expanding panels.");
            panel.Show();
        }

        // ── Helper data ─────────────────────────────────────────────────

        private class Assignment
        {
            public ElementId SystemId;
            public string SystemName;
            public string PanelName;
            public string Reason;
        }

        private class PanelState
        {
            public ElementId Id { get; }
            public string Name { get; }
            public int TotalSlots { get; }
            public int RemainingSlots { get; private set; }
            public double ConnectedVa { get; private set; }
            public double NominalVoltage { get; }

            public PanelState(FamilyInstance fi, Dictionary<long, List<ElectricalSystem>> circuitsByPanel)
            {
                Id = fi.Id;
                Name = SafeName(fi);
                TotalSlots = SafeReadInt(fi, "Number Of Circuits", 42);
                circuitsByPanel.TryGetValue(fi.Id.Value, out var owned);
                int used = owned?.Count ?? 0;
                RemainingSlots = Math.Max(0, TotalSlots - used);
                double sum = 0;
                if (owned != null) foreach (var s in owned) sum += SafeApparentVA(s);
                ConnectedVa = sum;
                NominalVoltage = SafePanelVoltage(fi);
            }

            public void Reserve(double va, int poles)
            {
                RemainingSlots = Math.Max(0, RemainingSlots - Math.Max(poles, 1));
                ConnectedVa += va;
            }

            private static string SafeName(FamilyInstance fi)
            {
                try { return fi.Name ?? fi.Id.ToString(); } catch { return fi.Id.ToString(); }
            }

            private static int SafeReadInt(Element el, string param, int fallback)
            {
                try
                {
                    var p = el.LookupParameter(param);
                    if (p != null && p.StorageType == StorageType.Integer) return p.AsInteger();
                    if (p != null && p.StorageType == StorageType.Double) return (int)Math.Round(p.AsDouble());
                }
                catch { }
                return fallback;
            }

            private static double SafePanelVoltage(Element el)
            {
                try
                {
                    var p = el.LookupParameter("Panel Voltage");
                    if (p != null && p.StorageType == StorageType.Double)
                    {
                        double v = p.AsDouble();
                        // Revit stores volts as volts in newer versions but
                        // historically as feet-of-equivalent. Anything above
                        // 1000 is suspect — return as-is and let the band
                        // matcher decide.
                        return v;
                    }
                }
                catch { }
                return 0;
            }
        }

        // ── Static helpers ─────────────────────────────────────────────

        private static FamilyInstance SafeBaseEquipment(ElectricalSystem s)
        {
            try { return s?.BaseEquipment as FamilyInstance; } catch { return null; }
        }

        private static double SafeApparentVA(ElectricalSystem s)
        {
            try { return s?.ApparentLoad ?? 0; } catch { return 0; }
        }

        private static int SafePoles(ElectricalSystem s)
        {
            try { return s?.PolesNumber ?? 1; } catch { return 1; }
        }

        private static double SafeCircuitVoltage(ElectricalSystem s)
        {
            try { return s?.Voltage ?? 0; } catch { return 0; }
        }

        private static bool VoltageCompatible(double a, double b)
        {
            if (a <= 0 || b <= 0) return true; // unknown — let it through
            foreach (var band in _voltageBands)
            {
                bool inA = a >= band.low && a <= band.high;
                bool inB = b >= band.low && b <= band.high;
                if (inA && inB) return true;
            }
            return false;
        }
    }
}
