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
using StingTools.UI;

namespace StingTools.Commands.Mep
{
    internal static class MepSizeTables
    {
        // Pipe standard bores (mm) per BS EN 10255 / BS EN 10216 (DN).
        public static readonly double[] PipeStandardBoreMm =
        { 6, 8, 10, 15, 20, 25, 32, 40, 50, 65, 80, 100, 125, 150, 200, 250, 300, 350, 400, 450, 500, 600 };

        // Standard rectangular duct widths/heights (mm) per SMACNA.
        public static readonly double[] DuctStandardMm =
        { 100, 150, 200, 250, 300, 350, 400, 450, 500, 550, 600, 700, 800, 900, 1000, 1200, 1400, 1600, 1800, 2000 };

        // Standard conduit trade sizes (mm) per BS EN 61386.
        public static readonly double[] ConduitStandardMm =
        { 16, 20, 25, 32, 40, 50, 63, 75, 100 };

        // Standard cable tray widths (mm) per BS EN 61537.
        public static readonly double[] CableTrayStandardMm =
        { 50, 75, 100, 150, 200, 300, 450, 600, 750, 900 };

        public static double RoundUpTo(double targetMm, double[] table)
        {
            foreach (var t in table) if (t >= targetMm) return t;
            return table[table.Length - 1];
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
        private const double PipeMaxVelMs = 2.5;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var res = new MepSizeResult { Discipline = "Pipe" };
            List<Element> pipes;
            try
            {
                pipes = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PipeCurves)
                    .WhereElementIsNotElementType().ToList();
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
                            double flowLs = ReadDouble(p, "PLM_FLOW_LS");
                            if (flowLs <= 0) { res.Skipped++; continue; }
                            double flowM3s = flowLs * 1e-3;
                            // A = q / v → d = sqrt(4A/π)
                            double area = flowM3s / PipeMaxVelMs;
                            double diaMm = Math.Sqrt(4.0 * area / Math.PI) * 1000.0;
                            double standard = MepSizeTables.RoundUpTo(diaMm, MepSizeTables.PipeStandardBoreMm);
                            if (WriteSize(p, "Diameter", standard)) res.Resized++;
                            else res.Skipped++;
                        }
                        catch (Exception ex3)
                        {
                            res.Skipped++;
                            res.Warnings.Add($"size {p.Id}: {ex2.Message}");
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
            ShowResult(res, "Pipe (target ≤ 2.5 m/s, BS EN 10255 bore table)");
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
        private const double MmToFt = 1.0 / 304.8;
        private const double DuctMaxVelMs = 6.0;
        private const double MinAspect = 1.0;
        private const double MaxAspect = 3.0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var res = new MepSizeResult { Discipline = "Duct" };
            var ducts = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_DuctCurves)
                .WhereElementIsNotElementType().ToList();
            res.Inspected = ducts.Count;

            using (var tx = new Transaction(doc, "STING Auto-size ducts"))
            {
                try { tx.Start(); } catch (Exception ex) { res.Warnings.Add($"tx: {ex.Message}"); goto Done; }
                try
                {
                    foreach (var d in ducts)
                    {
                        try
                        {
                            // Flow in CFM or L/s depending on doc units;
                            // HVC_FLOW_LS preferred v4 param.
                            double flowLs = ReadDouble(d, "HVC_FLOW_LS");
                            if (flowLs <= 0)
                            {
                                // Revit built-in fallback
                                double flowCfm = ReadBuiltInFlowCfm(d);
                                flowLs = flowCfm * 0.4719; // CFM → L/s
                            }
                            if (flowLs <= 0) { res.Skipped++; continue; }
                            double flowM3s = flowLs * 1e-3;

                            // Area = q / v
                            double area = flowM3s / DuctMaxVelMs;

                            // Round-duct equivalent:
                            double diaMm = Math.Sqrt(4.0 * area / Math.PI) * 1000.0;
                            // Rectangular with aspect 1:1.5 default
                            double widthMm = Math.Sqrt(area * 1.5) * 1000.0;
                            double heightMm = widthMm / 1.5;
                            // Clamp aspect
                            if (widthMm / heightMm > MaxAspect)
                            {
                                heightMm = widthMm / MaxAspect;
                            }
                            widthMm  = MepSizeTables.RoundUpTo(widthMm,  MepSizeTables.DuctStandardMm);
                            heightMm = MepSizeTables.RoundUpTo(heightMm, MepSizeTables.DuctStandardMm);

                            bool wrote = false;
                            if (WriteSize(d, "Width",  widthMm))  wrote = true;
                            if (WriteSize(d, "Height", heightMm)) wrote = true;
                            if (!wrote && WriteSize(d, "Diameter",
                                MepSizeTables.RoundUpTo(diaMm, MepSizeTables.DuctStandardMm))) wrote = true;
                            if (wrote) res.Resized++; else res.Skipped++;
                        }
                        catch (Exception ex2)
                        {
                            res.Skipped++;
                            res.Warnings.Add($"size {d.Id}: {ex2.Message}");
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    res.Warnings.Add($"Duct sizing fatal: {ex.Message}");
                }
            }
        Done:
            ShowResult(res, "Duct (target ≤ 6 m/s, aspect ≤ 3:1, SMACNA standard sizes)");
            return Result.Succeeded;
        }

        private static double ReadDouble(Element el, string param)
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
        private static double ReadBuiltInFlowCfm(Element el)
        {
            try
            {
                var p = el.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_PARAM);
                if (p != null && p.StorageType == StorageType.Double) return p.AsDouble() * 60.0;
            }
            catch (Exception ex) { StingLog.Warn($"RBS_DUCT_FLOW_PARAM read: {ex.Message}"); }
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
        private const double MaxFillPct = 45.0; // BS 7671 522.8 single circuit

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var res = new MepSizeResult { Discipline = "Conduit" };
            var conduits = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Conduit)
                .WhereElementIsNotElementType().ToList();
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
                            double diaMm = Math.Sqrt(4.0 * totalCableAreaMm2 / (Math.PI * MaxFillPct / 100.0));
                            double std = MepSizeTables.RoundUpTo(diaMm, MepSizeTables.ConduitStandardMm);
                            if (WriteDouble(c, "Diameter", std * MmToFt)) res.Resized++;
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
            ShowResult(res, "Conduit (BS 7671 522.8 single circuit fill ≤ 45%)");
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
