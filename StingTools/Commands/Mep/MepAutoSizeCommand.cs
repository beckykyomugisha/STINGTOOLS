// StingTools Phase 109 — MEP auto-size.
//
// Sets nominal size on selected pipes, ducts, conduits and cable
// trays from the flow / load / cable bundle data already present on
// the element:
//
//   Pipe    — target velocity ≤ 2.5 m/s (CIBSE Guide C 4.4.2);
//             q = flow (L/s), A = π·d²/4, round up to standard bore.
//   Duct    — target velocity ≤ 6 m/s (CIBSE Guide B3 low-vel
//             commercial), aspect ratio ≤ 3:1 where possible.
//   Conduit — fill ≤ 45% per BS 7671 522.8 using total cable cross
//             section from the circuit cable schedule.
//   Tray    — fill ≤ 50% standard industry rule of thumb, aggregate
//             cable cross section.
//
// Commands:
//   MepAutoSizePipe
//   MepAutoSizeDuct
//   MepAutoSizeConduit
//   MepAutoSizeAll   — dispatches all three in one pass.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Mep;
using StingTools.UI;

namespace StingTools.Commands.Mep
{
    internal static class MepSizeTables
    {
        // Hardcoded fallbacks — used only when STING_MEP_SIZING_RULES.json
        // is missing or fails to parse. Phase 181 routes every consumer
        // through MepSizingRegistry so projects can override per region.

        public static readonly double[] PipeStandardBoreMm =
        { 6, 8, 10, 15, 20, 25, 32, 40, 50, 65, 80, 100, 125, 150, 200, 250, 300, 350, 400, 450, 500, 600 };

        public static readonly double[] DuctStandardMm =
        { 100, 150, 200, 250, 300, 350, 400, 450, 500, 550, 600, 700, 800, 900, 1000, 1200, 1400, 1600, 1800, 2000 };

        public static readonly double[] ConduitStandardMm =
        { 16, 20, 25, 32, 40, 50, 63, 75, 100 };

        public static readonly double[] CableTrayStandardMm =
        { 50, 75, 100, 150, 200, 300, 450, 600, 750, 900 };

        public static double RoundUpTo(double targetMm, double[] table)
        {
            foreach (var t in table) if (t >= targetMm) return t;
            return table[table.Length - 1];
        }

        // ── Registry-aware lookups (Phase 181) ───────────────────────────
        // Each returns the project-scoped table from STING_MEP_SIZING_RULES.json
        // for the active region, falling back to the hardcoded array above
        // when the registry is unavailable or returns an empty list.

        public static double[] DuctSizesFor(Document doc)
        {
            try
            {
                var rules = MepSizingRegistry.Get(doc);
                var arr = rules.DuctSizesForRegion(rules.DuctDefaultRegion);
                if (arr != null && arr.Length > 0) return arr;
            }
            catch (Exception ex) { StingLog.Warn($"DuctSizesFor fallback: {ex.Message}"); }
            return DuctStandardMm;
        }

        public static double[] PipeBoresFor(Document doc)
        {
            try
            {
                var rules = MepSizingRegistry.Get(doc);
                var arr = rules.PipeBoresForRegion(rules.PipeDefaultRegion);
                if (arr != null && arr.Length > 0) return arr;
            }
            catch (Exception ex) { StingLog.Warn($"PipeBoresFor fallback: {ex.Message}"); }
            return PipeStandardBoreMm;
        }
    }

    public class MepSizeResult
    {
        public int Inspected { get; set; }
        public int Resized   { get; set; }
        public int Skipped   { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public string Discipline { get; set; } = "";
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepAutoSizePipeCommand : IExternalCommand
    {
        private const double MmToFt = 1.0 / 304.8;
        private const double PipeMaxVelMsFallback = 2.5;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // Phase 181 / 183 — pull velocity target + bore table from
            // STING_MEP_SIZING_RULES.json. Phase 183 (gap A2) detects the
            // pipe's service per-element via MEPSystem abbreviation →
            // STING_MEP_SERVICE_MAP.json and reads the velocity from the
            // matched PipeService entry rather than always using "chw".
            double[] boreTable = MepSizeTables.PipeStandardBoreMm;
            MepSizingRules rules = null;
            try
            {
                rules = MepSizingRegistry.Get(doc);
                boreTable = MepSizeTables.PipeBoresFor(doc);
            }
            catch (Exception ex) { StingLog.Warn($"PipeSize registry fallback: {ex.Message}"); }

            // Phase 182 — scope (gap D3).
            string scope = "Project";
            try { scope = StingTools.UI.StingHvacCommandHandler.CurrentScope ?? "Project"; } catch { }

            var res = new MepSizeResult { Discipline = "Pipe" };
            List<Element> pipes;
            try
            {
                if (scope == "Selection")
                {
                    var ids = ctx.UIDoc?.Selection?.GetElementIds();
                    pipes = (ids == null) ? new List<Element>() : ids
                        .Select(id => doc.GetElement(id))
                        .Where(e => e != null && e.Category != null
                                 && (BuiltInCategory)e.Category.Id.Value == BuiltInCategory.OST_PipeCurves)
                        .ToList();
                }
                else if (scope == "ActiveView" && doc.ActiveView != null)
                {
                    pipes = new FilteredElementCollector(doc, doc.ActiveView.Id)
                        .OfCategory(BuiltInCategory.OST_PipeCurves)
                        .WhereElementIsNotElementType().ToList();
                }
                else
                {
                    pipes = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PipeCurves)
                        .WhereElementIsNotElementType().ToList();
                }
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
            res.Inspected = pipes.Count;

            using (var tx = new Transaction(doc, "STING Auto-size pipes"))
            {
                try { tx.Start(); } catch (Exception ex2) { res.Warnings.Add($"tx: {ex2.Message}"); goto Done; }
                try
                {
                    foreach (var p in pipes)
                    {
                        try
                        {
                            // Per-pipe service lookup (Phase 183, gap A2).
                            string serviceId = StingTools.Core.Mep.PipeServiceDetector
                                .DetectServiceId(doc, p);
                            double maxVelMs = PipeMaxVelMsFallback;
                            string svcLabel = serviceId;
                            if (rules != null)
                            {
                                var svc = rules.GetPipeService(serviceId);
                                if (svc != null && svc.MaxVelocityMs > 0)
                                {
                                    maxVelMs = svc.MaxVelocityMs;
                                    svcLabel = string.IsNullOrEmpty(svc.Label) ? serviceId : svc.Label;
                                }
                            }

                            double flowLs = ReadDouble(p, "PLM_FLOW_LS");
                            if (flowLs <= 0) { res.Skipped++; continue; }
                            double flowM3s = flowLs * 1e-3;
                            // A = q / v → d = sqrt(4A/π)
                            double area = flowM3s / maxVelMs;
                            double diaMm = Math.Sqrt(4.0 * area / Math.PI) * 1000.0;
                            double standard = MepSizeTables.RoundUpTo(diaMm, boreTable);

                            // Audit (Phase 183) — stamp the detected service so
                            // the panel + drift detector can see what rule fired.
                            try
                            {
                                ParameterHelpers.SetString(p, ParamRegistry.HVC_PIPE_SERVICE_TXT,
                                    serviceId, overwrite: true);
                            }
                            catch (Exception exP) { StingLog.Warn($"HVC_PIPE_SERVICE stamp {p.Id}: {exP.Message}"); }

                            if (WriteSize(p, "Diameter", standard)) res.Resized++;
                            else res.Skipped++;
                        }
                        catch (Exception ex3)
                        {
                            res.Skipped++;
                            res.Warnings.Add($"size {p.Id}: {ex3.Message}");
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex3)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    res.Warnings.Add($"Pipe sizing fatal: {ex3.Message}");
                }
            }
        Done:
            ShowResult(res, $"Pipe · scope={scope} · per-service velocity (chw/hws/dcw/dhw/refrig/steam/gas) · {boreTable.Length} sizes");
            try
            {
                StingTools.UI.StingHvacPanel.Instance?.PushRunRow(
                    $"Auto-size Pipe ({scope})",
                    res.Resized > 0 ? "⬤" : (res.Skipped > 0 ? "⬡" : "✗"));
            }
            catch (Exception ex) { StingLog.Warn($"HvacPanel push: {ex.Message}"); }
            return Result.Succeeded;
        }

        private static double ReadDouble(Element el, string param)
        {
            try
            {
                var p = el?.LookupParameter(param);
                if (p == null) return 0;
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                if (p.StorageType == StorageType.String &&
                    double.TryParse(p.AsString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double v)) return v;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return 0;
        }

        private static bool WriteSize(Element el, string param, double mm)
        {
            try
            {
                var p = el.LookupParameter(param);
                if (p == null || p.IsReadOnly) return false;
                if (p.StorageType != StorageType.Double) return false;
                p.Set(mm * MmToFt);
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"WriteSize {param}={mm}: {ex.Message}"); return false; }
        }

        private static void ShowResult(MepSizeResult r, string subtitle)
        {
            var panel = StingResultPanel.Create($"MEP Auto-size — {r.Discipline}");
            panel.SetSubtitle(subtitle);
            panel.AddSection("SUMMARY")
                 .Metric("Inspected", r.Inspected.ToString())
                 .Metric("Resized",   r.Resized.ToString())
                 .Metric("Skipped",   r.Skipped.ToString());
            if (r.Warnings.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var w in r.Warnings.Take(20)) panel.Text(w);
                if (r.Warnings.Count > 20) panel.Text($"(+{r.Warnings.Count - 20} more — see StingLog)");
            }
            panel.Show();
        }
    }
}

namespace StingTools.Commands.Mep
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepAutoSizeDuctCommand : IExternalCommand
    {
        // Phase (dialog→engine) — this command is now a THIN UI wrapper over
        // DuctSizingApplyEngine (the single source of duct-sizing truth, dialog-free).
        // Button behaviour is unchanged: resolve scope + pressure class from the HVAC
        // panel header, call Apply(dryRun:false), then render the SAME StingResultPanel
        // and push the SAME HVAC-panel workflow row — all built FROM the engine result.
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // Header scope radio (Phase 182 gap D3) → engine scope PARAMETER. Selection is
            // resolved to element ids here (the command owns the UIDocument); the engine
            // stays Document-only. Unknown/empty → Project (historic behaviour).
            string scopeName = "Project";
            try { scopeName = StingTools.UI.StingHvacCommandHandler.CurrentScope ?? "Project"; } catch { }

            StingTools.Core.Mep.MepSizingScope scope;
            if (scopeName == "Selection")
            {
                var ids = ctx.UIDoc?.Selection?.GetElementIds() ?? new List<ElementId>();
                scope = StingTools.Core.Mep.MepSizingScope.ForIds(ids);
            }
            else if (scopeName == "ActiveView") scope = StingTools.Core.Mep.MepSizingScope.ActiveView();
            else scope = StingTools.Core.Mep.MepSizingScope.Project();

            // Pressure class (Phase 183 gap A3/D10) → engine PARAMETER (never read inside
            // the engine from the static UI field).
            string pclass = "low";
            try { pclass = StingTools.UI.StingHvacCommandHandler.CurrentPressureClassId ?? "low"; } catch { }

            StingTools.Core.Mep.DuctSizingApplyResult applied;
            try
            {
                applied = StingTools.Core.Mep.DuctSizingApplyEngine.Apply(doc, scope, dryRun: false, pressureClassId: pclass);
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }

            // Render the SAME result panel + HVAC-panel row, built from the engine result.
            var res = new MepSizeResult
            {
                Discipline = "Duct",
                Inspected = applied.Inspected,
                Resized = applied.Written,
                Skipped = applied.Skipped.Count,
            };
            foreach (var e in applied.Errors) res.Warnings.Add(e);
            foreach (var g in applied.RequiredBindingGaps) res.Warnings.Add(g);
            if (applied.NoWritesPersisted)
                res.Warnings.Add("Computed sizes but persisted 0 — every in-scope duct's size param was read-only (fitting-driven?).");

            var sample = applied.SampleChanges.FirstOrDefault();
            string roleBit = sample != null ? $"last role={sample.RoleId} ({sample.RoleSource})" : "no in-scope ducts sized";
            ShowResult(res, $"Duct · scope={scopeName} · pclass={pclass} · {roleBit}");
            try
            {
                StingTools.UI.StingHvacPanel.Instance?.PushRunRow(
                    $"Auto-size Duct ({scopeName})",
                    res.Resized > 0 ? "⬤" : (res.Skipped > 0 ? "⬡" : "✗"));
            }
            catch (Exception ex) { StingLog.Warn($"HvacPanel push: {ex.Message}"); }
            return Result.Succeeded;
        }

        private static void ShowResult(MepSizeResult r, string subtitle)
        {
            var panel = StingResultPanel.Create($"MEP Auto-size — {r.Discipline}");
            panel.SetSubtitle(subtitle);
            panel.AddSection("SUMMARY")
                 .Metric("Inspected", r.Inspected.ToString())
                 .Metric("Resized",   r.Resized.ToString())
                 .Metric("Skipped",   r.Skipped.ToString());
            if (r.Warnings.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var w in r.Warnings.Take(20)) panel.Text(w);
                if (r.Warnings.Count > 20) panel.Text($"(+{r.Warnings.Count - 20} more — see StingLog)");
            }
            panel.Show();
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepAutoSizeConduitCommand : IExternalCommand
    {
        private const double MmToFt = 1.0 / 304.8;
        private const double MaxFillPctFallback = 45.0; // BS 7671 522.8 single circuit

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // Phase 181 — fill limit from STING_MEP_SIZING_RULES.json (conduit.maxFillPct).
            double maxFillPct = MaxFillPctFallback;
            try
            {
                var rules = MepSizingRegistry.Get(doc);
                if (rules.ConduitMaxFillPct > 0) maxFillPct = rules.ConduitMaxFillPct;
            }
            catch (Exception ex) { StingLog.Warn($"ConduitSize registry fallback: {ex.Message}"); }

            // Phase 182 — scope (gap D3).
            string scope = "Project";
            try { scope = StingTools.UI.StingHvacCommandHandler.CurrentScope ?? "Project"; } catch { }

            var res = new MepSizeResult { Discipline = "Conduit" };
            List<Element> conduits;
            try
            {
                if (scope == "Selection")
                {
                    var ids = ctx.UIDoc?.Selection?.GetElementIds();
                    conduits = (ids == null) ? new List<Element>() : ids
                        .Select(id => doc.GetElement(id))
                        .Where(e => e != null && e.Category != null
                                 && (BuiltInCategory)e.Category.Id.Value == BuiltInCategory.OST_Conduit)
                        .ToList();
                }
                else if (scope == "ActiveView" && doc.ActiveView != null)
                {
                    conduits = new FilteredElementCollector(doc, doc.ActiveView.Id)
                        .OfCategory(BuiltInCategory.OST_Conduit)
                        .WhereElementIsNotElementType().ToList();
                }
                else
                {
                    conduits = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Conduit)
                        .WhereElementIsNotElementType().ToList();
                }
            }
            catch (Exception ex) { StingLog.Warn($"Conduit scope filter ({scope}) fallback: {ex.Message}");
                conduits = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Conduit)
                    .WhereElementIsNotElementType().ToList(); }
            res.Inspected = conduits.Count;

            using (var tx = new Transaction(doc, "STING Auto-size conduits"))
            {
                try { tx.Start(); } catch (Exception ex) { res.Warnings.Add($"tx: {ex.Message}"); goto Done; }
                try
                {
                    foreach (var c in conduits)
                    {
                        try
                        {
                            double totalCableAreaMm2 = Read(c, "ELC_CBL_TOTAL_AREA_MM2");
                            if (totalCableAreaMm2 <= 0) { res.Skipped++; continue; }
                            // Required bore = sqrt(4 * A / (π * fill))
                            double diaMm = Math.Sqrt(4.0 * totalCableAreaMm2 / (Math.PI * maxFillPct / 100.0));
                            double std = MepSizeTables.RoundUpTo(diaMm, MepSizeTables.ConduitStandardMm);
                            if (WriteDouble(c, "Diameter", std * MmToFt))
                            {
                                res.Resized++;
                                // Close the calc → model loop: stamp the
                                // resulting fill % to ELC_CDT_CBL_FILL_PCT
                                // (existing param) so schedules / warning
                                // checkers and downstream conduit-bend audits
                                // can read it without re-running this command.
                                double conduitAreaMm2 = Math.PI * std * std * 0.25;
                                double actualFillPct  = conduitAreaMm2 > 0
                                    ? totalCableAreaMm2 / conduitAreaMm2 * 100.0 : 0;
                                try
                                {
                                    StingTools.Core.ParameterHelpers.SetString(c,
                                        "ELC_CDT_CBL_FILL_PCT",
                                        $"{actualFillPct:F1}", overwrite: true);
                                }
                                catch (Exception exF) { StingLog.Warn($"Conduit fill stamp {c.Id}: {exF.Message}"); }
                            }
                            else res.Skipped++;
                        }
                        catch (Exception ex2)
                        {
                            res.Skipped++;
                            res.Warnings.Add($"size {c.Id}: {ex2.Message}");
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    res.Warnings.Add($"Conduit sizing fatal: {ex.Message}");
                }
            }
        Done:
            ShowResult(res, $"Conduit · scope={scope} · fill ≤ {maxFillPct:F0}% per BS 7671 522.8 / NEC Ch 9 Table 1");
            try
            {
                StingTools.UI.StingHvacPanel.Instance?.PushRunRow(
                    $"Auto-size Conduit ({scope})",
                    res.Resized > 0 ? "⬤" : (res.Skipped > 0 ? "⬡" : "✗"));
            }
            catch (Exception ex) { StingLog.Warn($"HvacPanel push: {ex.Message}"); }
            return Result.Succeeded;
        }
        private static double Read(Element el, string param)
        {
            try { var p = el?.LookupParameter(param);
                  if (p == null) return 0;
                  if (p.StorageType == StorageType.Double) return p.AsDouble();
                  if (p.StorageType == StorageType.Integer) return p.AsInteger();
                  if (p.StorageType == StorageType.String &&
                      double.TryParse(p.AsString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double v)) return v; }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return 0;
        }
        private static bool WriteDouble(Element el, string param, double valueInternal)
        {
            try { var p = el.LookupParameter(param);
                  if (p == null || p.IsReadOnly || p.StorageType != StorageType.Double) return false;
                  p.Set(valueInternal); return true; }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }
        private static void ShowResult(MepSizeResult r, string subtitle)
        {
            var panel = StingResultPanel.Create($"MEP Auto-size — {r.Discipline}");
            panel.SetSubtitle(subtitle);
            panel.AddSection("SUMMARY")
                 .Metric("Inspected", r.Inspected.ToString())
                 .Metric("Resized",   r.Resized.ToString())
                 .Metric("Skipped",   r.Skipped.ToString());
            if (r.Warnings.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var w in r.Warnings.Take(20)) panel.Text(w);
            }
            panel.Show();
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepAutoSizeAllCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Run all three in sequence. Each opens its own transaction.
            new MepAutoSizePipeCommand()   .Execute(commandData, ref message, elements);
            new MepAutoSizeDuctCommand()   .Execute(commandData, ref message, elements);
            new MepAutoSizeConduitCommand().Execute(commandData, ref message, elements);
            TaskDialog.Show("STING — MEP Auto-size All",
                "Pipe + Duct + Conduit auto-size passes complete. Each reported its own panel.");
            return Result.Succeeded;
        }
    }
}
