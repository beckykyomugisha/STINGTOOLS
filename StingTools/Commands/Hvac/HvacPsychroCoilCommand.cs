// Hvac_PsychroCoil — psychrometric mix + cooling-coil check.
//
// Outdoor air (defaulted from the project's climate design day) mixes with
// return air at an outdoor-air fraction; the mixed air is cooled to a
// leaving condition. Reports every state, coil total / sensible / latent
// load, apparatus dew point, bypass factor, sensible heat ratio and
// condensate, and — when a room sensible load is given — the supply airflow
// that load needs at the leaving temperature. Pressure follows the site
// elevation. Nothing is written to the model.

using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Climate;
using StingTools.Core.Hvac;
using StingTools.UI;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacPsychroCoilCommand : IExternalCommand
    {
        private const string Title = "STING HVAC — Psychrometric Coil Check";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }

                ClimateSite site = null;
                try { site = ClimateRegistry.ActiveSite(ctx.Doc); }
                catch (Exception ex) { StingLog.Warn($"Psychro: climate site: {ex.Message}"); }
                double oaDb = site?.Cooling996DbC ?? 28;
                double oaWb = site?.Cooling996McwbC ?? 20;
                double elev = site?.ElevationM ?? 0;

                var form = new StingFormDialog(Title, "Air conditions",
                        site != null
                            ? $"Outdoor defaults: {site.Label} cooling design day ({oaDb:0.#} °C db / {oaWb:0.#} °C mcwb), elevation {elev:0} m."
                            : "No climate site set — outdoor values are placeholders; enter the design day.")
                    .Number("oaDb", "Outdoor dry bulb (°C)", oaDb, -40, 60)
                    .Number("oaWb", "Outdoor wet bulb (°C)", oaWb, -40, 60)
                    .Number("raDb", "Return dry bulb (°C)", 24, 5, 40)
                    .Number("raRh", "Return RH (%)", 50, 1, 100)
                    .Number("oaFrac", "Outdoor-air fraction (0–1)", 0.3, 0, 1)
                    .Number("flow", "Coil airflow at entering state (m³/s)", 1.0, 0.001, 1000)
                    .Number("offDb", "Leaving dry bulb (°C)", 13, -10, 40)
                    .Number("offRh", "Leaving RH (%)", 95, 1, 100)
                    .Number("elev", "Site elevation (m)", elev, -500, 5000)
                    .Number("roomQs", "Room sensible load for a supply check (kW, 0 = skip)", 0, 0, 100000)
                    .Number("roomDb", "Room design dry bulb (°C)", 24, 5, 40);
                if (form.ShowDialog() != true) return Result.Cancelled;

                double p = Psychrometrics.PressureAtElevation(form.Get("elev"));
                if (form.Get("oaWb") > form.Get("oaDb"))
                {
                    TaskDialog.Show(Title, "Outdoor wet bulb cannot exceed dry bulb.");
                    return Result.Cancelled;
                }
                var oa = Psychrometrics.FromDryBulbWetBulb(form.Get("oaDb"), form.Get("oaWb"), p);
                var ra = Psychrometrics.FromDryBulbRh(form.Get("raDb"), form.Get("raRh") / 100.0, p);
                var mix = Psychrometrics.Mix(oa, ra, form.Get("oaFrac"));
                var coil = Psychrometrics.CoolingCoil(mix, form.Get("offDb"), form.Get("offRh") / 100.0, form.Get("flow"));

                var warnings = new List<string>();
                if (coil.Leaving.DryBulbC >= mix.DryBulbC) warnings.Add("Leaving dry bulb is not below the entering dry bulb — this is not a cooling process.");
                if (coil.Leaving.HumidityRatio >= mix.HumidityRatio - 1e-9) warnings.Add("No moisture removed: the leaving state is at or above the entering humidity ratio (sensible cooling only).");
                if (!double.IsNaN(coil.ApparatusDewPointC) && coil.ApparatusDewPointC < 2) warnings.Add($"Apparatus dew point {coil.ApparatusDewPointC:F1} °C is near freezing — check the coil selection.");

                var panel = StingResultPanel.Create("Psychrometric Coil Check");
                panel.SetSubtitle($"p = {p:F2} kPa at {form.Get("elev"):0} m · ASHRAE Fundamentals Ch.1 · OA fraction {form.Get("oaFrac"):0.##}");
                panel.AddSection("STATES").Table(
                    new[] { "State", "db °C", "wb °C", "RH %", "g/kg", "h kJ/kg", "dp °C" },
                    new List<string[]> { Row("Outdoor", oa), Row("Return", ra), Row("Mixed (on coil)", mix), Row("Off coil", coil.Leaving) });
                var c = panel.AddSection("COIL");
                c.MetricHighlight("Total load", $"{coil.TotalKw:F1} kW");
                c.Metric("Sensible / latent", $"{coil.SensibleKw:F1} / {coil.LatentKw:F1} kW");
                c.Metric("Sensible heat ratio", double.IsNaN(coil.Shr) ? "—" : $"{coil.Shr:F2}");
                c.Metric("Apparatus dew point", double.IsNaN(coil.ApparatusDewPointC) ? "— (no dehumidification)" : $"{coil.ApparatusDewPointC:F1} °C");
                c.Metric("Bypass factor", double.IsNaN(coil.BypassFactor) ? "—" : $"{coil.BypassFactor:F2}");
                c.Metric("Condensate", $"{coil.CondensateLh:F1} L/h");
                c.Metric("Dry-air mass flow", $"{coil.DryAirMassKgS:F3} kg/s");

                double roomQs = form.Get("roomQs");
                if (roomQs > 0)
                {
                    double dT = form.Get("roomDb") - coil.Leaving.DryBulbC;
                    var s = panel.AddSection("ROOM SUPPLY CHECK");
                    if (dT <= 0) s.MetricError("Supply ΔT", $"{dT:F1} K — leaving air is not below the room");
                    else
                    {
                        // Moist-air specific heat at the supply humidity ratio.
                        double cp = 1.006 + 1.86 * coil.Leaving.HumidityRatio;
                        double m = roomQs / (cp * dT);
                        double vol = m * coil.Leaving.SpecificVolume;
                        s.Metric("Supply ΔT", $"{dT:F1} K");
                        s.MetricHighlight("Airflow for the room", $"{vol:F3} m³/s ({vol * 1000:F0} L/s at supply state)");
                        double flowAtSupply = coil.DryAirMassKgS * coil.Leaving.SpecificVolume;
                        s.Metric("Coil airflow at supply state", $"{flowAtSupply:F3} m³/s — {(flowAtSupply + 1e-9 >= vol ? "enough" : "SHORT")}");
                        s.Text("Fan and duct heat gains are not added; include them in the room load or reduce ΔT.");
                    }
                }
                if (coil.LeavingClamped) warnings.Add("Leaving RH above 100 % was clamped to saturation.");
                if (warnings.Count > 0)
                {
                    var w = panel.AddSection("WARNINGS");
                    foreach (var line in warnings) w.Text(line);
                }
                panel.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacPsychroCoilCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string[] Row(string name, AirState s) => new[]
        {
            name, $"{s.DryBulbC:F1}", $"{s.WetBulbC:F1}", $"{s.RelativeHumidity * 100:F0}",
            $"{s.HumidityRatio * 1000:F2}", $"{s.EnthalpyKJkg:F1}", double.IsNaN(s.DewPointC) ? "—" : $"{s.DewPointC:F1}"
        };
    }
}
