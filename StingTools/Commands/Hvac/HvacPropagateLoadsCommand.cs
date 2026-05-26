// StingTools — Block-load → AutoSize bridge.
//
// Closes the loop between BlockLoadEngine and MepAutoSizeDuctCommand:
//
//   1. BlockLoad stamps HVC_PEAK_SENS_W and HVC_OA_LS on every Space.
//   2. PropagateLoads walks ducts in scope, identifies the served Space
//      via the downstream terminal (or upstream equipment, falling back
//      to the duct's location-room), and stamps HVC_FLOW_LS from the
//      larger of (peak sensible / ρ·cp·ΔT) and the design OA.
//   3. AutoSize reads HVC_FLOW_LS exactly as it already does, picks
//      the standard size that meets the role-velocity target.
//
// Without this bridge the two engines don't talk to each other —
// BlockLoad becomes a report-only feature and AutoSize relies on
// HVC_FLOW_LS that someone else had to compute.
//
// Algorithm per duct:
//   serving-space ← walk downstream connectors → AirTerminal → host Space
//                  ↳ else walk upstream connectors → Equipment → served Spaces
//                  ↳ else use the room at the duct's mid-point
//   if found and space has HVC_PEAK_SENS_W:
//       q_sens = peak / (ρ · cp · ΔT)     ΔT = 11 K cooling supply default
//       q_oa   = HVC_OA_LS                (already L/s)
//       flow   = max(q_sens, q_oa)
//       stamp HVC_FLOW_LS = flow
//       stamp HVC_LOAD_SOURCE_TXT = "<space-name>:peak={peak}|oa={oa}"
//
// Honours the standard HVAC panel scope radio.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Climate;
using StingTools.UI;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacPropagateLoadsCommand : IExternalCommand
    {
        // 11 K is the canonical CIBSE Guide B3 / ASHRAE supply-air ΔT
        // for cooling-dominated commercial systems. Heating uses a larger
        // ΔT (~20 K) but is plant-side limited; treat 11 K as conservative.
        private const double DefaultSupplyDtK = 11.0;
        private const double AirCpJperKgK = 1005.0;

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                string scope = "Project";
                try { scope = StingHvacCommandHandler.CurrentScope ?? "Project"; } catch { }

                // Air density at the cooling design dry-bulb — location-aware
                // via the climate registry, falls back to 1.20 kg/m³.
                double rho = 1.20;
                try
                {
                    var site = ClimateRegistry.ActiveSite(doc);
                    if (site != null && site.AirDensityCoolingKgM3() > 0)
                        rho = site.AirDensityCoolingKgM3();
                }
                catch { }

                var ducts = CollectDucts(ctx, scope);
                if (ducts.Count == 0)
                {
                    TaskDialog.Show("STING HVAC — Propagate Loads",
                        $"No ducts in scope ({scope}).");
                    return Result.Cancelled;
                }

                int stamped = 0, alreadyOk = 0, noSpace = 0, noPeak = 0;
                double totalFlowLs = 0;
                var details = new List<string>();

                using (var tx = new Transaction(doc, "STING Propagate Loads"))
                {
                    tx.Start();
                    foreach (var d in ducts)
                    {
                        try
                        {
                            var served = FindServedSpace(d);
                            if (served == null) { noSpace++; continue; }

                            double peakW = ReadDouble(served, "HVC_PEAK_SENS_W");
                            double oaLs  = ReadDouble(served, "HVC_OA_LS");
                            if (peakW <= 0 && oaLs <= 0) { noPeak++; continue; }

                            // q (m³/s) = Q_sens / (ρ · cp · ΔT). Convert to L/s.
                            double qSensLs = peakW > 0
                                ? (peakW / (rho * AirCpJperKgK * DefaultSupplyDtK)) * 1000.0
                                : 0;
                            double flowLs = Math.Max(qSensLs, oaLs);
                            if (flowLs <= 0) { noPeak++; continue; }

                            // Skip ducts that already have a fresh HVC_FLOW_LS
                            // matching what we'd write (within 2% — re-runs are no-ops).
                            double existing = ReadDouble(d, "HVC_FLOW_LS");
                            if (existing > 0 &&
                                Math.Abs(existing - flowLs) / flowLs < 0.02)
                            { alreadyOk++; continue; }

                            ParameterHelpers.SetString(d, "HVC_FLOW_LS",
                                $"{flowLs:F1}", overwrite: true);
                            ParameterHelpers.SetString(d, "HVC_LOAD_SOURCE_TXT",
                                $"{served.Name}: peak={peakW:F0}W / OA={oaLs:F0}L/s → flow={flowLs:F0}L/s",
                                overwrite: true);
                            stamped++;
                            totalFlowLs += flowLs;
                            if (details.Count < 40)
                                details.Add($"#{d.Id.Value} ← '{served.Name}' → {flowLs:F0} L/s (sens {qSensLs:F0}, OA {oaLs:F0})");
                        }
                        catch (Exception ex) { StingLog.Warn($"PropagateLoads {d.Id}: {ex.Message}"); }
                    }
                    tx.Commit();
                }

                var panel = StingResultPanel.Create("HVAC — Propagate Loads");
                panel.SetSubtitle($"ρ={rho:F3} kg/m³ · ΔT={DefaultSupplyDtK:F0} K · scope={scope}");
                panel.AddSection("SUMMARY")
                     .Metric("HVC_FLOW_LS stamped",   stamped.ToString())
                     .Metric("Already current",       alreadyOk.ToString())
                     .Metric("No served space",       noSpace.ToString())
                     .Metric("Space had no peak/OA",  noPeak.ToString())
                     .Metric("Total flow propagated", $"{totalFlowLs / 1000:F2} m³/s");

                if (details.Count > 0)
                {
                    panel.AddSection("DETAIL (first 40)");
                    foreach (var s in details) panel.Text(s);
                }
                panel.Text("Run order: Hvac_BlockLoad → Hvac_PropagateLoads → Hvac_AutoSizeDuct. " +
                           "PropagateLoads uses the served space's HVC_PEAK_SENS_W + HVC_OA_LS " +
                           "(stamped by BlockLoad) and writes the bigger of (sensible-derived) and OA " +
                           "as HVC_FLOW_LS for AutoSize to consume.");
                panel.Show();

                try
                {
                    StingHvacPanel.Instance?.PushRunRow(
                        $"Propagate loads ({stamped} ducts stamped, {noSpace + noPeak} skipped)",
                        stamped > 0 ? "⬤" : "⬡");
                }
                catch (Exception ex) { StingLog.Warn($"Panel push: {ex.Message}"); }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacPropagateLoadsCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Served-space resolution ─────────────────────────────────

        /// <summary>
        /// Walk the duct's connector graph to find the air terminal it
        /// serves, then return that terminal's host Space. Falls back to
        /// the Space at the duct's mid-point. Returns null when no Space
        /// can be associated.
        /// </summary>
        private static Space FindServedSpace(Element duct)
        {
            try
            {
                // 1. Downstream terminal via connector walk (max depth 12).
                var visited = new HashSet<long>();
                var found = WalkForTerminal(duct, visited, 0);
                if (found != null)
                {
                    var sp = SpaceAtElement(found);
                    if (sp != null) return sp;
                }

                // 2. Fall back to the Space at the duct mid-point.
                if (duct is MEPCurve mc && mc.Location is LocationCurve lc && lc.Curve != null)
                {
                    var p = lc.Curve.Evaluate(0.5, true);
                    return duct.Document.GetSpaceAtPoint(p);
                }
            }
            catch (Exception ex) { StingLog.Warn($"FindServedSpace {duct?.Id}: {ex.Message}"); }
            return null;
        }

        private const int MaxWalk = 12;

        private static Element WalkForTerminal(Element el, HashSet<long> seen, int depth)
        {
            if (el == null || depth > MaxWalk) return null;
            if (!seen.Add(el.Id.Value)) return null;
            try
            {
                if (el.Category != null &&
                    (BuiltInCategory)el.Category.Id.Value == BuiltInCategory.OST_DuctTerminal)
                    return el;

                ConnectorSet set = null;
                if (el is MEPCurve mc) set = mc.ConnectorManager?.Connectors;
                else if (el is FamilyInstance fi) set = fi.MEPModel?.ConnectorManager?.Connectors;
                if (set == null) return null;

                foreach (Connector c in set)
                {
                    if (c?.AllRefs == null) continue;
                    foreach (Connector other in c.AllRefs)
                    {
                        if (other?.Owner == null) continue;
                        var next = WalkForTerminal(other.Owner, seen, depth + 1);
                        if (next != null) return next;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"WalkForTerminal: {ex.Message}"); }
            return null;
        }

        private static Space SpaceAtElement(Element el)
        {
            try
            {
                if (el is FamilyInstance fi)
                {
                    return fi.Space;
                }
                if (el.Location is LocationPoint lp)
                    return el.Document.GetSpaceAtPoint(lp.Point);
            }
            catch { }
            return null;
        }

        // ── Helpers ─────────────────────────────────────────────────

        private static List<Element> CollectDucts(StingCommandContext ctx, string scope)
        {
            var doc = ctx.Doc;
            if (scope == "Selection")
            {
                var ids = ctx.UIDoc?.Selection?.GetElementIds();
                if (ids == null) return new List<Element>();
                return ids.Select(id => doc.GetElement(id))
                    .Where(e => e != null && e.Category != null
                             && (BuiltInCategory)e.Category.Id.Value == BuiltInCategory.OST_DuctCurves)
                    .ToList();
            }
            if (scope == "ActiveView" && doc.ActiveView != null)
            {
                return new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfCategory(BuiltInCategory.OST_DuctCurves)
                    .WhereElementIsNotElementType().ToList();
            }
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_DuctCurves)
                .WhereElementIsNotElementType().ToList();
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
            catch (Exception ex) { StingLog.Warn($"ReadDouble: {ex.Message}"); }
            return 0;
        }
    }
}
