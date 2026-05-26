// StingTools — Circuit-based view filter creation (I1).
//
// Creates a Revit ParameterFilterElement that highlights all electrical
// circuits belonging to a user-selected panel or phase in the active view.
//
// The user is presented a choice of filter scope (by panel or by phase).
// Panel names are built from ElectricalSystem.BaseEquipment names.
// Phase letters are derived from ELC_CIRCUIT_PHASE_TXT or from the circuit
// number suffix (a/b/c or 1/2/3).
//
// The created filter is applied to the active view with a blue projection-
// line and surface highlight.  The filter name is stamped on the view via
// the STING_CIRCUIT_FILTER_TXT shared parameter for round-trip awareness.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Electrical
{
    /// <summary>
    /// Creates a <see cref="ParameterFilterElement"/> for the selected
    /// circuit panel or phase and applies it to the active view with a
    /// blue highlight override.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CircuitViewFilterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc     = ctx.Doc;
            var uidoc   = ctx.UIDoc;
            var view    = doc.ActiveView;

            if (view == null)
            {
                TaskDialog.Show("STING Circuit Filter", "No active view.");
                return Result.Cancelled;
            }

            // ── Collect all circuits and build panel / phase lists ────────────
            var allCircuits = new FilteredElementCollector(doc)
                .OfClass(typeof(ElectricalSystem))
                .Cast<ElectricalSystem>()
                .ToList();

            if (allCircuits.Count == 0)
            {
                TaskDialog.Show("STING Circuit Filter",
                    "No electrical circuits found in the model.");
                return Result.Cancelled;
            }

            // Gather unique panel names.
            var panelNames = allCircuits
                .Select(es =>
                {
                    try { return es.BaseEquipment?.Name ?? ""; }
                    catch { return ""; }
                })
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();

            // Gather unique phase labels from parameter or circuit number.
            var phases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var es in allCircuits)
            {
                try
                {
                    string phase = es.LookupParameter("ELC_CIRCUIT_PHASE_TXT")?.AsString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(phase)) { phases.Add(phase.ToUpperInvariant()); continue; }
                    // Derive from circuit number suffix (e.g. "1A" → "A").
                    string circNum = es.CircuitNumber ?? "";
                    if (circNum.Length > 0)
                    {
                        char last = char.ToUpperInvariant(circNum[circNum.Length - 1]);
                        if (last == 'A' || last == 'B' || last == 'C') phases.Add(last.ToString());
                    }
                }
                catch { /* skip */ }
            }

            // ── Ask user to choose filter mode ─────────────────────────────────
            // TaskDialog with options: "By Panel" / "By Phase" / Cancel
            var modeDialog = new TaskDialog("STING Circuit Filter")
            {
                MainInstruction = "Select filter scope",
                MainContent     = $"Panels found: {panelNames.Count}   " +
                                  $"Phases found: {phases.Count}\n\n" +
                                  "Filter circuits by panel name or by phase?",
                CommonButtons   = TaskDialogCommonButtons.Cancel
            };
            modeDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "By Panel");
            modeDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "By Phase");
            var modeResult = modeDialog.Show();

            if (modeResult == TaskDialogResult.Cancel) return Result.Cancelled;

            bool byPanel = modeResult == TaskDialogResult.CommandLink1;
            string filterValue;
            string filterDesc;

            if (byPanel)
            {
                if (panelNames.Count == 0)
                {
                    TaskDialog.Show("STING Circuit Filter",
                        "No panels with connected circuits found.");
                    return Result.Cancelled;
                }
                // Let user pick a panel via a simple TaskDialog list.
                // For up to 4 panels use command links; otherwise prompt for text.
                filterValue = PickFromList("Select Panel", panelNames, 8);
                if (filterValue == null) return Result.Cancelled;
                filterDesc = $"Panel {filterValue}";
            }
            else
            {
                var phaseList = phases.OrderBy(p => p).ToList();
                if (phaseList.Count == 0)
                {
                    TaskDialog.Show("STING Circuit Filter",
                        "No phase data found. Set ELC_CIRCUIT_PHASE_TXT on circuits or use " +
                        "circuit numbers ending in A, B, or C.");
                    return Result.Cancelled;
                }
                filterValue = PickFromList("Select Phase", phaseList, 4);
                if (filterValue == null) return Result.Cancelled;
                filterDesc = $"Phase {filterValue}";
            }

            // ── Build the ParameterFilterElement ──────────────────────────────
            string filterName = $"STING - Circuit {filterDesc}";

            // Remove existing filter with the same name to allow re-run.
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement))
                .Cast<ParameterFilterElement>()
                .FirstOrDefault(f => string.Equals(f.Name, filterName, StringComparison.OrdinalIgnoreCase));

            int matchCount = 0;

            using (var tx = new Transaction(doc, "STING Circuit View Filter"))
            {
                tx.Start();

                if (existing != null)
                {
                    try { doc.Delete(existing.Id); } catch { /* might be in use on other views */ }
                }

                // Category: OST_ElectricalCircuit
                var cats = new List<ElementId>
                {
                    new ElementId(BuiltInCategory.OST_ElectricalCircuit)
                };

                ElementFilter elemFilter;

                if (byPanel)
                {
                    // Filter on RBS_ELEC_CIRCUIT_PANEL_PARAM equals filterValue.
                    var rule = ParameterFilterRuleFactory.CreateEqualsRule(
                        new ElementId(BuiltInParameter.RBS_ELEC_CIRCUIT_PANEL_PARAM),
                        filterValue);
                    elemFilter = new ElementParameterFilter(rule);
                }
                else
                {
                    // Filter on the ELC_CIRCUIT_PHASE_TXT shared parameter.
                    // Fall back to checking circuit number contains the phase letter.
                    // We use a contains rule on circuit number as a broad net.
                    var rule = ParameterFilterRuleFactory.CreateContainsRule(
                        new ElementId(BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER),
                        filterValue);
                    elemFilter = new ElementParameterFilter(rule);
                }

                ParameterFilterElement pfe;
                try
                {
                    pfe = ParameterFilterElement.Create(doc, filterName, cats, elemFilter);
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    message = $"Could not create filter: {ex.Message}";
                    return Result.Failed;
                }

                // Count matching circuits for report.
                matchCount = allCircuits.Count(es =>
                {
                    try
                    {
                        if (byPanel)
                        {
                            string pnl = es.BaseEquipment?.Name ?? "";
                            return pnl.Equals(filterValue, StringComparison.OrdinalIgnoreCase);
                        }
                        else
                        {
                            string phase = es.LookupParameter("ELC_CIRCUIT_PHASE_TXT")?.AsString() ?? "";
                            if (phase.Equals(filterValue, StringComparison.OrdinalIgnoreCase)) return true;
                            string circNum = es.CircuitNumber ?? "";
                            return circNum.EndsWith(filterValue, StringComparison.OrdinalIgnoreCase);
                        }
                    }
                    catch { return false; }
                });

                // Apply the filter to the active view.
                try
                {
                    if (!view.GetFilters().Contains(pfe.Id))
                        view.AddFilter(pfe.Id);

                    view.SetFilterVisibility(pfe.Id, true);

                    // Build blue highlight override.
                    var solidFill = new FilteredElementCollector(doc)
                        .OfClass(typeof(FillPatternElement))
                        .Cast<FillPatternElement>()
                        .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);

                    var ogs = new OverrideGraphicSettings();
                    ogs.SetProjectionLineColor(new Color(0, 100, 255));
                    ogs.SetProjectionLineWeight(4);
                    if (solidFill != null)
                    {
                        ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                        ogs.SetSurfaceForegroundPatternColor(new Color(0, 100, 255));
                        ogs.SetSurfaceTransparency(60);
                    }
                    view.SetFilterOverrides(pfe.Id, ogs);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"CircuitFilter apply: {ex.Message}");
                }

                // Stamp filter name on view.
                try { ParameterHelpers.SetString(view, "STING_CIRCUIT_FILTER_TXT", filterName, overwrite: true); }
                catch { /* shared param may not be bound */ }

                tx.Commit();
            }

            TaskDialog.Show("STING Circuit Filter",
                $"Filter '{filterName}' created and applied.\n" +
                $"Matching circuits: {matchCount}.");
            return Result.Succeeded;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Presents a task-dialog list picker for up to <paramref name="maxLinks"/> items.
        /// Returns the selected string or null if cancelled.
        /// </summary>
        private static string PickFromList(string title, List<string> items, int maxLinks)
        {
            if (items.Count == 1) return items[0];

            if (items.Count <= maxLinks)
            {
                var dlg = new TaskDialog("STING — " + title);
                dlg.MainInstruction = title;
                dlg.CommonButtons   = TaskDialogCommonButtons.Cancel;

                var linkIds = new[]
                {
                    TaskDialogCommandLinkId.CommandLink1,
                    TaskDialogCommandLinkId.CommandLink2,
                    TaskDialogCommandLinkId.CommandLink3,
                    TaskDialogCommandLinkId.CommandLink4
                };
                int count = Math.Min(items.Count, linkIds.Length);
                for (int i = 0; i < count; i++)
                    dlg.AddCommandLink(linkIds[i], items[i]);

                var res = dlg.Show();
                for (int i = 0; i < count; i++)
                    if (res == (TaskDialogResult)linkIds[i]) return items[i];
                return null;
            }
            else
            {
                // Too many items for command links — use first 8 in summary and ask for text.
                string preview = string.Join(", ", items.Take(8));
                string input = Microsoft.VisualBasic.Interaction.InputBox(
                    $"Available: {preview}{(items.Count > 8 ? ", …" : "")}\n\nType exact name:",
                    "STING — " + title, items[0]);
                if (string.IsNullOrEmpty(input)) return null;
                // Find best match (case-insensitive).
                return items.FirstOrDefault(i => i.Equals(input, StringComparison.OrdinalIgnoreCase))
                    ?? input;
            }
        }
    }
}
