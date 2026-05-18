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

            // Phase 181 — pull velocity target + bore table from STING_MEP_SIZING_RULES.json.
            // Per-service velocity (chw / hws / dcw / dhw / refrigerant / steam / gas) reads
            // from rules.GetPipeService(serviceId); for now we default to the "chw" entry
            // which carries the lowest velocity, matching the historic 2.5 m/s safety margin.
            double maxVelMs = PipeMaxVelMsFallback;
            double[] boreTable = MepSizeTables.PipeStandardBoreMm;
            try
            {
                var rules = MepSizingRegistry.Get(doc);
                var svc = rules.GetPipeService("chw");
                if (svc != null && svc.MaxVelocityMs > 0) maxVelMs = svc.MaxVelocityMs;
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
                            double flowLs = ReadDouble(p, "PLM_FLOW_LS");
                            if (flowLs <= 0) { res.Skipped++; continue; }
                            double flowM3s = flowLs * 1e-3;
                            // A = q / v → d = sqrt(4A/π)
                            double area = flowM3s / maxVelMs;
                            double diaMm = Math.Sqrt(4.0 * area / Math.PI) * 1000.0;
                            double standard = MepSizeTables.RoundUpTo(diaMm, boreTable);
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
            ShowResult(res, $"Pipe · scope={scope} · target ≤ {maxVelMs:F2} m/s · {boreTable.Length} sizes");
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
        private const double MmToFt = 1.0 / 304.8;
        private const double DuctMaxVelMsFallback = 6.0;
        private const double MaxAspectFallback = 3.0;
        private const double DefaultAspectFallback = 1.5;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // Phase 181/182 — read targets from STING_MEP_SIZING_RULES.json
            // and resolve role per-element via HvacSegmentRoleDetector.
            // The role registry carries main / branch / runout / OA / exhaust /
            // kitchen / smoke; we detect each duct's role on first encounter,
            // cache it back on HVC_SEGMENT_ROLE_TXT, and pick targets per duct.
            double[] sizeTable    = MepSizeTables.DuctStandardMm;
            double defaultAspect  = DefaultAspectFallback;
            MepSizingRules rules  = null;
            try
            {
                rules = MepSizingRegistry.Get(doc);
                if (rules.DuctDefaultAspect > 0) defaultAspect = rules.DuctDefaultAspect;
                sizeTable = MepSizeTables.DuctSizesFor(doc);
            }
            catch (Exception ex) { StingLog.Warn($"DuctSize registry fallback: {ex.Message}"); }

            // Phase 182 — scope enforcement (gap D3). Header radio drives:
            //   "Selection"   → currently-selected duct ids only
            //   "ActiveView"  → ducts owned by ctx.UIDoc.ActiveView
            //   "Project"     → everything (historic behaviour)
            string scope = "Project";
            try { scope = StingTools.UI.StingHvacCommandHandler.CurrentScope ?? "Project"; } catch { }

            var res = new MepSizeResult { Discipline = "Duct" };
            List<Element> ducts;
            try
            {
                if (scope == "Selection")
                {
                    var ids = ctx.UIDoc?.Selection?.GetElementIds();
                    ducts = (ids == null) ? new List<Element>() : ids
                        .Select(id => doc.GetElement(id))
                        .Where(e => e != null && e.Category != null
                                 && (BuiltInCategory)e.Category.Id.Value == BuiltInCategory.OST_DuctCurves)
                        .ToList();
                }
                else if (scope == "ActiveView" && doc.ActiveView != null)
                {
                    ducts = new FilteredElementCollector(doc, doc.ActiveView.Id)
                        .OfCategory(BuiltInCategory.OST_DuctCurves)
                        .WhereElementIsNotElementType().ToList();
                }
                else
                {
                    ducts = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_DuctCurves)
                        .WhereElementIsNotElementType().ToList();
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DuctSize scope filter ({scope}) fallback to Project: {ex.Message}");
                ducts = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_DuctCurves)
                    .WhereElementIsNotElementType().ToList();
            }
            res.Inspected = ducts.Count;

            // For the result panel — most-recent role lookup keeps the
            // subtitle informative without enumerating every role.
            string lastRoleId = "branch";
            string lastRoleSrc = "registry";
            double lastMaxVel = DuctMaxVelMsFallback;
            double lastMaxAsp = MaxAspectFallback;

            using (var tx = new Transaction(doc, "STING Auto-size ducts"))
            {
                try { tx.Start(); } catch (Exception ex) { res.Warnings.Add($"tx: {ex.Message}"); goto Done; }
                try
                {
                    foreach (var d in ducts)
                    {
                        try
                        {
                            // Per-element role lookup (Phase 182, gap D5).
                            string roleId = StingTools.Core.Mep.HvacSegmentRoleDetector.DetectRole(doc, d);
                            double maxVelMs  = DuctMaxVelMsFallback;
                            double maxAspect = MaxAspectFallback;
                            string roleSrc   = "fallback";
                            if (rules != null)
                            {
                                var role = rules.GetDuctRole(roleId);
                                if (role != null && role.MaxVelocityMs > 0)
                                {
                                    maxVelMs  = role.MaxVelocityMs;
                                    maxAspect = role.AspectMax > 0 ? role.AspectMax : MaxAspectFallback;
                                    roleSrc   = string.IsNullOrEmpty(role.Source) ? "registry" : role.Source;
                                }
                            }
                            lastRoleId = roleId; lastRoleSrc = roleSrc;
                            lastMaxVel = maxVelMs; lastMaxAsp = maxAspect;

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
                            double area = flowM3s / maxVelMs;

                            // Round-duct equivalent:
                            double diaMm = Math.Sqrt(4.0 * area / Math.PI) * 1000.0;
                            // Rectangular with configured aspect default
                            double widthMm = Math.Sqrt(area * defaultAspect) * 1000.0;
                            double heightMm = widthMm / defaultAspect;
                            // Clamp aspect
                            if (widthMm / heightMm > maxAspect)
                            {
                                heightMm = widthMm / maxAspect;
                            }
                            widthMm  = MepSizeTables.RoundUpTo(widthMm,  sizeTable);
                            heightMm = MepSizeTables.RoundUpTo(heightMm, sizeTable);

                            // Audit trail (Phase 182, gap D4) — capture previous
                            // dims before write so the panel + drift detector
                            // can show what changed.
                            string prevSize = SnapshotDuctSize(d);

                            bool wrote = false;
                            if (WriteSize(d, "Width",  widthMm))  wrote = true;
                            if (WriteSize(d, "Height", heightMm)) wrote = true;
                            if (!wrote && WriteSize(d, "Diameter",
                                MepSizeTables.RoundUpTo(diaMm, sizeTable))) wrote = true;
                            if (wrote)
                            {
                                res.Resized++;
                                StampSizingAudit(d, prevSize, $"{widthMm:F0}x{heightMm:F0}", roleId, roleSrc);
                            }
                            else res.Skipped++;
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
            ShowResult(res, $"Duct · scope={scope} · last role={lastRoleId} (≤ {lastMaxVel:F1} m/s, aspect ≤ {lastMaxAsp:F1}:1) · {sizeTable.Length} sizes · {lastRoleSrc}");
            // Phase 182 — push a workflow row to the HVAC panel (gap D9).
            try
            {
                StingTools.UI.StingHvacPanel.Instance?.PushRunRow(
                    $"Auto-size Duct ({scope})",
                    res.Resized > 0 ? "⬤" : (res.Skipped > 0 ? "⬡" : "✗"));
            }
            catch (Exception ex) { StingLog.Warn($"HvacPanel push: {ex.Message}"); }
            return Result.Succeeded;
        }

        // ── Phase 182 audit-trail helpers (gap D4) ──────────────────────
        // Best-effort: shared parameter bindings may not exist on every
        // project. SetString is no-op if read-only / unbound, so callers
        // never fail because of missing params.

        private static string SnapshotDuctSize(Element d)
        {
            try
            {
                double w = ReadDouble(d, "Width")  * 304.8;
                double h = ReadDouble(d, "Height") * 304.8;
                double dia = ReadDouble(d, "Diameter") * 304.8;
                if (w > 0 && h > 0) return $"{w:F0}x{h:F0}";
                if (dia > 0)        return $"Ø{dia:F0}";
            }
            catch (Exception ex) { StingLog.Warn($"SnapshotDuctSize {d.Id}: {ex.Message}"); }
            return "";
        }

        private static void StampSizingAudit(Element el, string previous, string current, string roleId, string ruleSrc)
        {
            try
            {
                if (!string.IsNullOrEmpty(previous))
                    ParameterHelpers.SetString(el, "HVC_SIZE_PREV_TXT", previous, overwrite: true);
                ParameterHelpers.SetString(el, "HVC_SIZE_MODIFIED_DT",
                    DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), overwrite: true);
                ParameterHelpers.SetString(el, "HVC_SIZE_RULE_ID_TXT",
                    string.IsNullOrEmpty(roleId) ? ruleSrc : $"{roleId}|{ruleSrc}", overwrite: true);
            }
            catch (Exception ex) { StingLog.Warn($"StampSizingAudit {el.Id}: {ex.Message}"); }
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
