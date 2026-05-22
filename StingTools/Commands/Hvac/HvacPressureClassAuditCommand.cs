// StingTools Phase 183 — pressure-class audit.
//
// Closes gap A3 / D10: the header Pressure-class radio was previously
// decorative — sizing commands stamped it on each duct but nothing
// verified that the system's actual static pressure stayed within
// the chosen class limit (DW/144 A=500 Pa, B=1000 Pa, C=2500 Pa,
// D=7500 Pa).
//
// This command walks ducts in scope, estimates the system pressure
// drop using the existing DetailedPressureDropEngine (Darcy-Weisbach
// + SMACNA fitting losses already shipped in MEPIntelligenceEngine.cs),
// and emits a warning per duct whose estimated drop exceeds the active
// class. Results land in the HVAC panel's IssueGrid + a workflow row.
//
// Conservative-by-design: in absence of computed system ΔP we use the
// per-duct dynamic pressure (½ρv²) at the duct's actual size as a
// floor estimate. A duct exceeding the class on dynamic pressure
// alone is definitely over-class.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Calc;
using StingTools.Core.Mep;
using StingTools.UI;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacPressureClassAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                MepSizingRules rules = MepSizingRegistry.Get(doc);
                // Phase 187c — atomic snapshot of the header context so the
                // command never reads a torn (class, density, scope) tuple
                // if the user rapidly switches the header radios.
                var snap = StingHvacCommandHandler.Snapshot();
                string classId = snap.PressureClassId ?? "low";

                var pclass = rules.DuctPressureClasses
                    .FirstOrDefault(c => string.Equals(c.Id, classId, StringComparison.OrdinalIgnoreCase))
                    ?? rules.DuctPressureClasses.FirstOrDefault();
                if (pclass == null)
                {
                    TaskDialog.Show("STING HVAC — Pressure-class audit",
                        "No pressure classes configured in STING_MEP_SIZING_RULES.json.");
                    return Result.Cancelled;
                }
                double maxPa = pclass.MaxPa > 0 ? pclass.MaxPa : 500.0;
                // Air density: prefer the climate-registry value for the
                // project's location (elevation + cooling design temp), fall
                // back to the header radio, fall back to 1.20 kg/m³.
                double airDensity = 0;
                try
                {
                    var site = StingTools.Core.Climate.ClimateRegistry.ActiveSite(doc);
                    if (site != null) airDensity = site.AirDensityCoolingKgM3();
                }
                catch { }
                if (airDensity <= 0) airDensity = snap.AirDensityKgM3;
                if (airDensity <= 0) airDensity = 1.20;

                string scope = snap.Scope ?? "Project";

                List<Element> ducts = CollectDucts(ctx, scope);
                if (ducts.Count == 0)
                {
                    TaskDialog.Show("STING HVAC — Pressure-class audit",
                        $"No ducts in scope ({scope}).");
                    return Result.Cancelled;
                }

                int pass = 0, fail = 0, skipped = 0, stamped = 0, velFail = 0;
                double worstPa = 0;
                ElementId worstId = null;
                var details = new List<string>();
                var velocityDetails = new List<string>();

                // Phase 187b — per-duct role + role-velocity check, plus
                // adjacent-fitting ΔP contribution. Batch-detect roles once.
                var rules = StingTools.Core.Mep.MepSizingRegistry.Get(doc);
                var roleMap = StingTools.Core.Mep.HvacSegmentRoleDetector.DetectRolesBatch(doc, ducts);

                using var tx = new Transaction(doc, "STING HVAC Pressure-class Audit");
                tx.Start();
                foreach (var d in ducts)
                {
                    try
                    {
                        double flowLs = MepUnits.ReadAirFlowLs(d, "HVC_FLOW_LS");
                        if (flowLs <= 0)
                            flowLs = MepUnits.ReadBuiltInFlowLs(d, BuiltInParameter.RBS_DUCT_FLOW_PARAM);
                        if (flowLs <= 0) { skipped++; continue; }

                        // Get actual size → velocity → dynamic pressure (Pa).
                        double w = UnitUtils.ConvertFromInternalUnits(ReadDouble(d, "Width"),    UnitTypeId.Millimeters);
                        double h = UnitUtils.ConvertFromInternalUnits(ReadDouble(d, "Height"),   UnitTypeId.Millimeters);
                        double dia = UnitUtils.ConvertFromInternalUnits(ReadDouble(d, "Diameter"), UnitTypeId.Millimeters);
                        double areaMm2 = (w > 0 && h > 0) ? w * h
                                       : (dia > 0)         ? Math.PI * dia * dia * 0.25
                                       : 0;
                        if (areaMm2 <= 0) { skipped++; continue; }

                        double velMs = (flowLs * 1e-3) / (areaMm2 * 1e-6);  // m/s
                        double dynamicPa = 0.5 * airDensity * velMs * velMs;

                        // Role-based velocity check. Each role (main / branch /
                        // runout / outdoor_air / exhaust / kitchen / smoke) has
                        // its own velocity cap from STING_MEP_SIZING_RULES.json.
                        string roleId = roleMap.TryGetValue(d.Id, out var rid) ? rid : "branch";
                        var role = rules?.GetDuctRole(roleId);
                        double roleMaxVel = role?.MaxVelocityMs ?? 6.0;
                        if (velMs > roleMaxVel)
                        {
                            velFail++;
                            if (velocityDetails.Count < 40)
                                velocityDetails.Add(
                                    $"#{d.Id.Value} [{roleId}] v={velMs:F1} m/s > role max {roleMaxVel:F1} m/s");
                        }

                        // Length-based friction estimate. Use DuctFrictionSolver
                        // (Darcy-Weisbach + Swamee-Jain) so long high-velocity runs
                        // aren't quietly under-reporting friction by ~40%.
                        double lengthM = 0;
                        if (d is MEPCurve mc && mc.Location is LocationCurve lc && lc.Curve != null)
                            lengthM = UnitUtils.ConvertFromInternalUnits(lc.Curve.Length, UnitTypeId.Meters);

                        double frictionPa = 0;
                        if (lengthM > 0)
                        {
                            var shape = (w > 0 && h > 0) ? DuctShape.Rectangular : DuctShape.Round;
                            double a = shape == DuctShape.Round ? dia : w;
                            double b = shape == DuctShape.Round ? 0   : h;
                            var fr = DuctFrictionSolver.Solve(
                                shape, a, b, lengthM, flowLs * 1e-3, null,
                                DuctFrictionSolver.GalvRoughnessM);
                            frictionPa = fr.StraightDropPa;
                        }

                        // Adjacent fitting losses — walk each connector ONE step,
                        // sum the C of every directly-touching OST_DuctFitting +
                        // OST_DuctAccessory using the manufacturer-aware lookup.
                        // Half-credit avoids double counting (the same fitting
                        // would otherwise show up on both upstream + downstream
                        // ducts).
                        double fittingPa = AdjacentFittingLossPa(d, velMs, airDensity, rules) * 0.5;

                        double estimatedPa = dynamicPa + frictionPa + fittingPa;
                        if (estimatedPa > worstPa) { worstPa = estimatedPa; worstId = d.Id; }

                        // Close the calc → model loop: stamp HVC_PRESSURE_DROP_PA
                        // with the estimated ΔP, and HVC_PRESSURE_CLASS_TXT with
                        // the active class on ducts that haven't been sized yet
                        // (sized ducts keep their original class).
                        try
                        {
                            if (ParameterHelpers.SetString(d, "HVC_PRESSURE_DROP_PA",
                                    $"{estimatedPa:F0}", overwrite: true)) stamped++;
                            string existingClass = ParameterHelpers.GetString(d, "HVC_PRESSURE_CLASS_TXT");
                            if (string.IsNullOrEmpty(existingClass))
                                ParameterHelpers.SetString(d, "HVC_PRESSURE_CLASS_TXT",
                                    pclass.Id ?? "low", overwrite: true);
                        }
                        catch (Exception exS) { StingLog.Warn($"PressureAudit stamp {d.Id}: {exS.Message}"); }

                        if (estimatedPa > maxPa)
                        {
                            fail++;
                            if (details.Count < 40)
                                details.Add($"#{d.Id.Value} v={velMs:F1} m/s · est ΔP={estimatedPa:F0} Pa > {maxPa:F0} Pa");
                        }
                        else pass++;
                    }
                    catch (Exception ex) { skipped++; StingLog.Warn($"PressureAudit {d.Id}: {ex.Message}"); }
                }
                tx.Commit();

                var panel = StingResultPanel.Create("HVAC — Pressure-class Audit");
                panel.SetSubtitle($"class={pclass.Label} (≤ {maxPa:F0} Pa) · ρ={airDensity:F2} kg/m³ · scope={scope}");
                panel.AddSection("SUMMARY")
                     .Metric("Within class",        pass.ToString())
                     .Metric("Over class",          fail.ToString())
                     .Metric("Over role velocity",  velFail.ToString())
                     .Metric("Skipped",             skipped.ToString())
                     .Metric("HVC_PRESSURE_DROP_PA stamped", stamped.ToString())
                     .Metric("Worst ΔP",            $"{worstPa:F0} Pa" + (worstId != null ? $" (#{worstId.Value})" : ""));

                if (details.Count > 0)
                {
                    panel.AddSection("OVER CLASS (first 40)");
                    foreach (var s in details) panel.Text(s);
                }
                if (velocityDetails.Count > 0)
                {
                    panel.AddSection("ROLE-VELOCITY VIOLATIONS (first 40)");
                    foreach (var s in velocityDetails) panel.Text(s);
                }
                panel.Text("Estimate = dynamic pressure (½ρv²) + Darcy-Weisbach friction (Swamee-Jain f) " +
                           "over each duct's individual length + ½ of adjacent fitting C-losses (the other " +
                           "½ is attributed to the connecting duct on the far side). Role velocity comes " +
                           "from STING_MEP_SIZING_RULES.json duct.roles. Use Mep_PressureDrop for the full " +
                           "system-level report.");
                panel.Show();

                try
                {
                    StingHvacPanel.Instance?.PushRunRow(
                        $"Pressure-class audit ({fail} over class)",
                        fail > 0 ? "⬡" : "⬤");
                    if (fail > 0 && StingHvacPanel.Instance != null)
                    {
                        StingHvacPanel.Instance.IssueRows.Add(new HvacIssueRow
                        {
                            Severity   = "⚠",
                            Element    = worstId != null ? $"#{worstId.Value}" : "(see report)",
                            Issue      = $"{fail} ducts exceed {pclass.Label} (worst {worstPa:F0} Pa)",
                            Suggestion = "Raise pressure class in header, or re-size at lower velocity"
                        });
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Panel push: {ex.Message}"); }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacPressureClassAuditCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

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

        /// <summary>
        /// Sum the loss coefficient ΔP across every fitting / accessory
        /// directly connected to this duct. Each connector AllRefs is
        /// walked one step; the connected fitting's C is resolved via
        /// <see cref="StingTools.Model.FittingLossCalculator.ResolveFittingLoss"/>
        /// (which prefers manufacturer C when HVC_PROD_REF_TXT is set).
        /// The caller halves the result to share the loss between the
        /// upstream + downstream ducts that share the fitting.
        /// </summary>
        private static double AdjacentFittingLossPa(Element duct, double velMs,
            double airDensity, StingTools.Core.Mep.MepSizingRules rules)
        {
            double sumPa = 0;
            try
            {
                if (!(duct is MEPCurve mc)) return 0;
                var conns = mc.ConnectorManager?.Connectors;
                if (conns == null) return 0;
                double dynPa = 0.5 * airDensity * velMs * velMs;
                foreach (Connector c in conns)
                {
                    if (c?.AllRefs == null) continue;
                    foreach (Connector other in c.AllRefs)
                    {
                        var owner = other?.Owner;
                        if (owner == null || owner.Id == duct.Id) continue;
                        if (owner.Category == null) continue;
                        var bic = (BuiltInCategory)owner.Category.Id.Value;
                        if (bic != BuiltInCategory.OST_DuctFitting &&
                            bic != BuiltInCategory.OST_DuctAccessory) continue;
                        try
                        {
                            var rfl = StingTools.Model.FittingLossCalculator.ResolveFittingLoss(owner, rules);
                            if (rfl != null && rfl.C > 0) sumPa += rfl.C * dynPa;
                        }
                        catch (Exception ex) { StingLog.Warn($"AdjacentFittingLossPa {owner.Id}: {ex.Message}"); }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"AdjacentFittingLossPa {duct?.Id}: {ex.Message}"); }
            return sumPa;
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
