// StingTools v4 MVP — Phase C calculation command surface.
//
// Three commands that expose the ConduitFillSolver / DuctFrictionSolver
// / SlopeAutoCorrector engines via the Routing tab. Each works as a
// selection-driven dry-run (compute + report) with the slope corrector
// offering a Fix mode that writes changes back through a
// TransactionGroup.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Calc;
using StingTools.UI;

// Autodesk.Revit.DB.Mechanical.DuctShape shadows our enum here because
// the Mechanical namespace is imported above. Alias the local one so
// the calc code keeps reading cleanly.
using DuctShape = StingTools.Core.Calc.DuctShape;

namespace StingTools.Commands.Routing
{
    /// <summary>
    /// BS 7671 App E conduit fill report for the selected conduits.
    /// For each conduit, reads cable manifest from parameters (cables
    /// written as "CSA:count,…" in ELC_CDT_CABLE_MANIFEST_TXT) and
    /// reports required vs actual bore.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CalcConduitFillCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc   = ctx.Doc;
            var uidoc = ctx.UIDoc;

            var conduits = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<Conduit>()
                .ToList();

            if (conduits.Count == 0)
            {
                // Fall back to view scope.
                conduits = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfClass(typeof(Conduit))
                    .WhereElementIsNotElementType()
                    .OfType<Conduit>()
                    .ToList();
            }

            var panel = StingResultPanel.Create("v4 Conduit Fill (BS 7671 App E)");
            panel.SetSubtitle($"{conduits.Count} conduit(s) analysed");

            int ok = 0, over = 0, under = 0, nocbl = 0;
            foreach (var c in conduits)
            {
                var cables = ReadManifest(c);
                if (cables.Count == 0) { nocbl++; continue; }

                double lengthFt = (c.Location as LocationCurve)?.Curve?.Length ?? 0;
                double lengthM  = lengthFt * 0.3048;
                // Bend count: Revit doesn't expose this cheaply; use 1
                // as a conservative default — long-run tables apply.
                int bendCount = 1;
                double bore   = c.Diameter * 304.8;

                var fill = ConduitFillSolver.FillRatio(bore, cables, lengthM, bendCount);
                string note = fill switch
                {
                    var r when double.IsNaN(r)     => "NO-TABLE",
                    var r when r > 1.0             => "OVERFILL",
                    var r when r < 0.30            => "UNDERUSED",
                    _                              => "OK",
                };
                if (note == "OVERFILL") over++;
                else if (note == "UNDERUSED") under++;
                else if (note == "OK") ok++;

                panel.Text($"#{c.Id.Value} bore {bore:F0}mm L={lengthM:F1}m cables=" +
                           string.Join(",", cables.Select(x => $"{x.CsaMm2}×{x.Count}")) +
                           $"  fill={(double.IsNaN(fill)?"–":fill.ToString("P0"))}  [{note}]");
            }

            panel.AddSection("SUMMARY")
                 .Metric("OK",          ok.ToString())
                 .Metric("Over-fill",   over.ToString())
                 .Metric("Under-used",  under.ToString())
                 .Metric("No manifest", nocbl.ToString());
            panel.Show();
            return Result.Succeeded;
        }

        /// <summary>
        /// Parse ELC_CDT_CABLE_MANIFEST_TXT content like
        /// "1.5:3,2.5:2,4.0:1" into a ConduitCableEntry list. Assumes
        /// STRANDED copper unless a trailing /S flags solid.
        /// </summary>
        private static List<ConduitCableEntry> ReadManifest(Element conduit)
        {
            var list = new List<ConduitCableEntry>();
            try
            {
                var p = conduit.LookupParameter("ELC_CDT_CABLE_MANIFEST_TXT");
                var s = p?.AsString() ?? "";
                if (string.IsNullOrEmpty(s)) return list;
                foreach (var token in s.Split(','))
                {
                    var t = token.Trim();
                    if (string.IsNullOrEmpty(t)) continue;
                    var parts = t.Split(':');
                    if (parts.Length < 2) continue;
                    bool isSolid = parts[0].EndsWith("/S", StringComparison.OrdinalIgnoreCase);
                    string csaStr = isSolid ? parts[0].Substring(0, parts[0].Length - 2) : parts[0];
                    if (!double.TryParse(csaStr, System.Globalization.NumberStyles.Any,
                                         System.Globalization.CultureInfo.InvariantCulture,
                                         out double csa)) continue;
                    if (!int.TryParse(parts[1], out int count)) continue;
                    list.Add(new ConduitCableEntry
                    {
                        CsaMm2 = csa,
                        Count  = count,
                        ConductorType = isSolid ? "SOLID" : "STRANDED"
                    });
                }
            }
            catch (Exception ex)
            { StingLog.Warn($"CalcConduitFillCommand.ReadManifest: {ex.Message}"); }
            return list;
        }
    }

    /// <summary>
    /// Darcy-Weisbach + SMACNA fitting-loss report for selected ducts.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CalcDuctFrictionCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc   = ctx.Doc;
            var uidoc = ctx.UIDoc;

            var ducts = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<Duct>()
                .ToList();

            if (ducts.Count == 0)
            {
                TaskDialog.Show("STING v4 — Duct Friction",
                    "Select one or more ducts and re-run. The calc uses:\n\n" +
                    "  • Duct.Diameter (round) or Width/Height (rectangular)\n" +
                    "  • Duct.FlowParam (system-calculated flow)\n" +
                    "  • LocationCurve length\n" +
                    "and reports velocity, Reynolds, friction factor,\n" +
                    "Darcy-Weisbach pressure drop, CIBSE B3 velocity check.");
                return Result.Cancelled;
            }

            var panel = StingResultPanel.Create("v4 Duct Friction (Darcy-Weisbach + SMACNA)");
            panel.SetSubtitle($"{ducts.Count} duct(s) analysed");

            foreach (var d in ducts)
            {
                DuctShape shape;
                double sideA, sideB;
                try
                {
                    var shapeParam = d.DuctType?.Shape ?? ConnectorProfileType.Invalid;
                    if (shapeParam == ConnectorProfileType.Round)
                    {
                        shape = DuctShape.Round;
                        sideA = d.Diameter * 304.8;
                        sideB = 0;
                    }
                    else
                    {
                        shape = DuctShape.Rectangular;
                        sideA = d.Width  * 304.8;
                        sideB = d.Height * 304.8;
                    }
                }
                catch
                {
                    panel.Text($"#{d.Id.Value} — shape read failed");
                    continue;
                }

                double lengthM = ((d.Location as LocationCurve)?.Curve?.Length ?? 0) * 0.3048;
                double flowM3S = ReadFlowM3S(d);

                var r = DuctFrictionSolver.Solve(shape, sideA, sideB, lengthM, flowM3S, null);
                var issues = DuctFrictionSolver.CibseB3VelocityCheck(r, "branch");

                string tag = shape == DuctShape.Round
                    ? $"Ø{sideA:F0}"
                    : $"{sideA:F0}×{sideB:F0}";
                panel.Text($"#{d.Id.Value} {tag} L={lengthM:F1}m Q={flowM3S:F2}m³/s " +
                           $"v={r.VelocityMs:F1}m/s ΔP={r.TotalDropPa:F1}Pa" +
                           (issues.Count > 0 ? "  ⚠ " + issues[0] : ""));
            }
            panel.Show();
            return Result.Succeeded;
        }

        private static double ReadFlowM3S(Duct d)
        {
            try
            {
                // Duct.FlowParam is volumetric flow in Revit's internal
                // unit (ft³/s). Convert to m³/s.
                var p = d.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_PARAM);
                if (p != null) return p.AsDouble() * 0.028316846592;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return 0;
        }
    }

    /// <summary>
    /// BS EN 12056 slope report + auto-corrector. Dry-run by default.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CalcSlopeCorrectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var td = new TaskDialog("STING v4 — Slope Auto-Correct")
            {
                MainInstruction = "Preview or apply?",
                MainContent = "Walks every drainage pipe in the project and:\n" +
                              "  • FLIPS pipes sloping the wrong way\n" +
                              "  • DEPRESSES downstream ends to reach 1.0% (BS EN 12056-2)\n" +
                              "  • Leaves vertical stacks unchanged",
                CommonButtons = TaskDialogCommonButtons.Cancel,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Preview (dry run)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Apply fixes");
            var choice = td.Show();
            if (choice != TaskDialogResult.CommandLink1 && choice != TaskDialogResult.CommandLink2)
                return Result.Cancelled;

            bool dryRun = choice == TaskDialogResult.CommandLink1;
            SlopeAutoCorrectionResult res;
            try
            {
                res = SlopeAutoCorrector.RunFix(doc, dryRun);
            }
            catch (Exception ex)
            {
                StingLog.Error("CalcSlopeCorrectCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }

            var panel = StingResultPanel.Create("v4 Slope Auto-Correct");
            panel.SetSubtitle(dryRun ? "DRY RUN" : "Applied");
            panel.AddSection("SUMMARY")
                 .Metric("Scanned",   res.PipesScanned.ToString())
                 .Metric("Flipped",   res.PipesFlipped.ToString())
                 .Metric("Depressed", res.PipesDepressed.ToString())
                 .Metric("Unchanged", res.PipesUnchanged.ToString())
                 .Metric("Failed",    res.PipesFailed.ToString());

            if (res.Warnings.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var w in res.Warnings.Take(40)) panel.Text(w);
                if (res.Warnings.Count > 40) panel.Text($"(+{res.Warnings.Count - 40} more — see StingLog)");
            }

            if (res.Fixes.Count > 0)
            {
                panel.AddSection("FIXES");
                foreach (var f in res.Fixes.Take(40))
                    panel.Text($"#{f.PipeId.Value} {f.Action} {f.OriginalPct:F2}% → {f.AppliedPct:F2}%");
                if (res.Fixes.Count > 40) panel.Text($"(+{res.Fixes.Count - 40} more)");
            }
            panel.Show();
            return Result.Succeeded;
        }
    }
}
